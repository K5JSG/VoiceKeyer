using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NAudio.Wave;
using VoiceKeyer.Audio;
using VoiceKeyer.Services;
using VoiceKeyer.Trainer;

namespace VoiceKeyer.UI;

/// <summary>Guided recorder that builds a Piper training dataset from the user's own voice.</summary>
public partial class TrainerWindow : Window
{
    private const int CaptureRate = 48000;

    private readonly AppSettings _settings;
    private IReadOnlyList<string> _prompts = [];
    private DatasetRecorder _dataset;
    private WaveIn? _capture;
    private MemoryStream? _buffer;
    private int _index;
    private string _warning = string.Empty;

    // Redo mode: when the voice folder holds a redo.txt ("0242|too quiet" per line), the window steps through just
    // those clips, keeps the old take in redo_backup and judges each new take against the normal level range.
    private List<(int Index, string Note)>? _redo;
    private int _redoPos;
    private const double RedoMinPeakDb = -15;
    private const double RedoMaxPeakDb = -6;

    public TrainerWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        VoiceName.Text = "MyVoice";

        var devices = ClipPlayer.InputDevices();
        Microphone.ItemsSource = devices;
        int selected = ClipPlayer.ResolveDevice(devices, settings.TrainerInputDevice);
        Microphone.SelectedIndex = selected >= 0 ? selected : (devices.Count > 0 ? 0 : -1);

        _dataset = OpenDataset();
        _index = StartIndex();
        ShowPrompt();
    }

    private int StartIndex()
    {
        _redoPos = 0;
        return _redo is { Count: > 0 } ? _redo[0].Index : FirstUnrecorded();
    }

    private string RedoPath => Path.Combine(DatasetFolder, "redo.txt");

    private void LoadRedoList()
    {
        _redo = null;
        if (!File.Exists(RedoPath))
        {
            return;
        }

        var list = new List<(int Index, string Note)>();
        foreach (string line in File.ReadAllLines(RedoPath))
        {
            string[] parts = line.Split('|', 2);
            if (int.TryParse(parts[0].Trim(), out int number) && number >= 1 && number <= _prompts.Count)
            {
                list.Add((number - 1, parts.Length > 1 ? parts[1].Trim() : string.Empty));
            }
        }

        _redo = list.Count > 0 ? list : null;
    }

    private string DatasetFolder => Path.Combine(_settings.TrainerFolder, SafeName(VoiceName.Text));

    private static string SafeName(string text)
    {
        string clean = Regex.Replace(text.Trim(), @"[^A-Za-z0-9_]", "_");
        return clean.Length == 0 ? "MyVoice" : clean;
    }

    private DatasetRecorder OpenDataset()
    {
        var dataset = new DatasetRecorder(DatasetFolder);
        _prompts = [];
        // Resume with the same prompt order the earlier session used.
        if (File.Exists(dataset.PromptsPath))
        {
            var saved = File.ReadAllLines(dataset.PromptsPath);
            if (saved.Length > 0)
            {
                _prompts = saved;
            }
        }

        if (_prompts.Count == 0)
        {
            _prompts = PromptLibrary.Build(_settings.MyCall, ReadOwnPhrases(dataset));
        }

        LoadRedoList();
        return dataset;
    }

    /// <summary>Optional my_phrases.txt in the voice folder: the speaker's own lines (local contest, county names, hard words), one per line.</summary>
    private static IEnumerable<string> ReadOwnPhrases(DatasetRecorder dataset)
    {
        string path = Path.Combine(Path.GetDirectoryName(dataset.WavFolder)!, "my_phrases.txt");
        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

    private int FirstUnrecorded()
    {
        for (int i = 0; i < _prompts.Count; i++)
        {
            if (!_dataset.HasClip(i))
            {
                return i;
            }
        }

        return 0;
    }

    private void VoiceName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_capture is not null)
        {
            return;
        }

        VoiceName.Text = SafeName(VoiceName.Text);
        _dataset = OpenDataset();
        _index = StartIndex();
        ShowPrompt();
    }

    private void ShowPrompt()
    {
        PromptText.Text = _prompts[_index];
        int done = Enumerable.Range(0, _prompts.Count).Count(_dataset.HasClip);
        double minutes = Enumerable.Range(0, _prompts.Count).Where(_dataset.HasClip)
            .Sum(i => new FileInfo(_dataset.ClipPath(i)).Length / 2.0 / DatasetRecorder.SampleRate) / 60;
        string mark = _dataset.HasClip(_index) ? "  (recorded)" : string.Empty;
        if (_redo is not null)
        {
            string note = _redo[_redoPos].Note.Length > 0 ? $"   -   why: {_redo[_redoPos].Note}" : string.Empty;
            Progress.Text = $"REDO {_redoPos + 1} of {_redo.Count}   -   clip {_index + 1:0000}{note}{_warning}";
            PrevButton.IsEnabled = _redoPos > 0;
            NextButton.IsEnabled = _redoPos < _redo.Count - 1;
            PlayButton.IsEnabled = _dataset.HasClip(_index);
            return;
        }

        string noCall = done == 0 && string.IsNullOrWhiteSpace(_settings.MyCall)
            ? "   -   Your call sign is not set: close this window, enter it in Settings (My call), then reopen to get prompts with your own call."
            : string.Empty;
        Progress.Text = $"Prompt {_index + 1} of {_prompts.Count}{mark}   -   {done} recorded, {minutes:0.0} minutes{noCall}{_warning}";
        PrevButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index < _prompts.Count - 1;
        PlayButton.IsEnabled = _dataset.HasClip(_index);
    }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        if (_capture is not null)
        {
            return;
        }

        var findings = ClipReview.Review(_dataset, _prompts);
        if (findings.Count == 0)
        {
            MessageBox.Show(this, "No clip stands out. The recordings look consistent in level, noise and timing.", "Voice trainer");
            return;
        }

        string preview = string.Join(Environment.NewLine, findings.Take(8)
            .Select(f => $"{f.Index + 1:0000}  {_prompts[f.Index]}  -  {f.Reason}"));
        string more = findings.Count > 8 ? $"{Environment.NewLine}... and {findings.Count - 8} more." : string.Empty;
        var answer = MessageBox.Show(this,
            $"{findings.Count} clip(s) are worth recording again:{Environment.NewLine}{Environment.NewLine}{preview}{more}" +
            $"{Environment.NewLine}{Environment.NewLine}Start the redo list now? Your old takes are kept in redo_backup.",
            "Voice trainer", MessageBoxButton.YesNo);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        ClipReview.WriteRedoList(RedoPath, findings);
        LoadRedoList();
        _index = StartIndex();
        _warning = string.Empty;
        ShowPrompt();
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_capture is null)
        {
            StartRecording();
        }
        else
        {
            StopRecording();
        }
    }

    private void StartRecording()
    {
        if (Microphone.SelectedIndex < 0)
        {
            MessageBox.Show(this, "No microphone found.", "Voice trainer");
            return;
        }

        _warning = string.Empty;
        _buffer = new MemoryStream();
        _capture = new WaveIn
        {
            DeviceNumber = Microphone.SelectedIndex,
            WaveFormat = new WaveFormat(CaptureRate, 16, 1),
            BufferMilliseconds = 50,
        };
        _capture.DataAvailable += (_, args) =>
        {
            _buffer?.Write(args.Buffer, 0, args.BytesRecorded);
            double peak = 0;
            for (int i = 0; i + 1 < args.BytesRecorded; i += 2)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(args.Buffer, i)) / 32768.0);
            }

            Dispatcher.BeginInvoke(() => Level.Value = peak);
        };
        _capture.StartRecording();
        RecordButton.Content = "■ Stop (Space)";
        Microphone.IsEnabled = false;
        _settings.TrainerInputDevice = (string)Microphone.SelectedItem;
    }

    private void StopRecording()
    {
        var capture = _capture!;
        _capture = null;
        capture.StopRecording();
        capture.Dispose();
        RecordButton.Content = "● Record (Space)";
        Microphone.IsEnabled = true;
        Level.Value = 0;

        if (_redo is not null)
        {
            BackUpOldTake(_index);
        }

        var quality = _dataset.Save(_index, _buffer!.ToArray(), CaptureRate);
        _buffer = null;
        var warnings = quality.Warnings.ToList();
        if (_redo is not null)
        {
            if (quality.PeakDb < RedoMinPeakDb)
            {
                warnings.Add($"Too quiet (peak {quality.PeakDb:0} dB, aim for {RedoMinPeakDb:0} to {RedoMaxPeakDb:0}): speak up or move the boom closer.");
            }
            else if (quality.PeakDb > RedoMaxPeakDb)
            {
                warnings.Add($"Too loud (peak {quality.PeakDb:0} dB, aim for {RedoMinPeakDb:0} to {RedoMaxPeakDb:0}): a little softer or a bit further away.");
            }
        }

        if (warnings.Count == 0)
        {
            _warning = _redo is null ? string.Empty : $"   -   last take OK (peak {quality.PeakDb:0} dB)";
            if (_redo is not null)
            {
                if (_redoPos < _redo.Count - 1)
                {
                    _redoPos++;
                    _index = _redo[_redoPos].Index;
                }
                else
                {
                    FinishRedo();
                    return;
                }
            }
            else if (_index < _prompts.Count - 1)
            {
                _index++;
            }
        }
        else if (_redo is not null)
        {
            _warning = "   -   " + string.Join(" ", warnings) + " Press Record to redo it.";
        }
        else
        {
            _warning = "   -   " + string.Join(" ", warnings) + " Saved anyway; press Record to redo it.";
        }

        ShowPrompt();
    }

    private void BackUpOldTake(int index)
    {
        string old = _dataset.ClipPath(index);
        if (!File.Exists(old))
        {
            return;
        }

        string folder = Path.Combine(DatasetFolder, "redo_backup");
        Directory.CreateDirectory(folder);
        File.Copy(old, Path.Combine(folder, $"{index + 1:0000}_{DateTime.Now:HHmmss}.wav"), overwrite: true);
    }

    private void FinishRedo()
    {
        _warning = string.Empty;
        _dataset.WriteMetadata(_prompts);
        File.Move(RedoPath, Path.Combine(DatasetFolder, "redo.done.txt"), overwrite: true);
        _redo = null;
        MessageBox.Show(this, "Every clip on the redo list is re-recorded. The old takes are in the redo_backup folder.", "Voice trainer");
        _index = FirstUnrecorded();
        ShowPrompt();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.OriginalSource is not (TextBox or ComboBox))
        {
            Record_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        _warning = string.Empty;
        if (_redo is not null)
        {
            _redoPos = Math.Max(0, _redoPos - 1);
            _index = _redo[_redoPos].Index;
        }
        else
        {
            _index = Math.Max(0, _index - 1);
        }

        ShowPrompt();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _warning = string.Empty;
        if (_redo is not null)
        {
            _redoPos = Math.Min(_redo.Count - 1, _redoPos + 1);
            _index = _redo[_redoPos].Index;
        }
        else
        {
            _index = Math.Min(_prompts.Count - 1, _index + 1);
        }

        ShowPrompt();
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_dataset.HasClip(_index))
        {
            await ClipPlayer.PlayAsync([_dataset.ClipPath(_index)], string.Empty, CancellationToken.None);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var (clips, duration) = _dataset.WriteMetadata(_prompts);
        if (clips < 50)
        {
            MessageBox.Show(this, $"Only {clips} clips recorded. Record at least 150 (about 15 minutes) before training.", "Voice trainer");
            return;
        }

        string zip = _dataset.Export(_prompts, SafeName(VoiceName.Text));
        // The notebook ships with the app; put a copy beside the zip so both are in the folder that opens.
        string notebook = Path.Combine(AppContext.BaseDirectory, "trainer", "Piper_Voice_Trainer.ipynb");
        string notebookCopy = Path.Combine(Path.GetDirectoryName(zip)!, "Piper_Voice_Trainer.ipynb");
        bool hasNotebook = false;
        try
        {
            if (File.Exists(notebook))
            {
                File.Copy(notebook, notebookCopy, overwrite: true);
                hasNotebook = true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        string next = hasNotebook
            ? "Next: open Piper_Voice_Trainer.ipynb (in the same folder) in Google Colab (colab.research.google.com > Upload), upload this zip and run it."
            : "Next: get Piper_Voice_Trainer.ipynb from github.com/K5JSG/VoiceKeyer (trainer folder), open it in Google Colab, upload this zip and run it.";
        MessageBox.Show(this, $"Exported {clips} clips ({duration.TotalMinutes:0.0} minutes) to:\n{zip}\n\n{next}", "Voice trainer");
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(DatasetFolder) { UseShellExecute = true });

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_capture is not null)
        {
            StopRecording();
        }

        _dataset.WriteMetadata(_prompts);
        _settings.Save();
    }
}
