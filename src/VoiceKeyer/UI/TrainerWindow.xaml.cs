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
    private IReadOnlyList<string> _prompts = PromptLibrary.Build();
    private DatasetRecorder _dataset;
    private WaveIn? _capture;
    private MemoryStream? _buffer;
    private int _index;
    private string _warning = string.Empty;

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
        _index = FirstUnrecorded();
        ShowPrompt();
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
        // Resume with the same prompt order the earlier session used.
        if (File.Exists(dataset.PromptsPath))
        {
            var saved = File.ReadAllLines(dataset.PromptsPath);
            if (saved.Length > 0)
            {
                _prompts = saved;
            }
        }
        else
        {
            _prompts = PromptLibrary.Build();
        }

        return dataset;
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
        _index = FirstUnrecorded();
        ShowPrompt();
    }

    private void ShowPrompt()
    {
        PromptText.Text = _prompts[_index];
        int done = Enumerable.Range(0, _prompts.Count).Count(_dataset.HasClip);
        double minutes = Enumerable.Range(0, _prompts.Count).Where(_dataset.HasClip)
            .Sum(i => new FileInfo(_dataset.ClipPath(i)).Length / 2.0 / DatasetRecorder.SampleRate) / 60;
        string mark = _dataset.HasClip(_index) ? "  (recorded)" : string.Empty;
        Progress.Text = $"Prompt {_index + 1} of {_prompts.Count}{mark}   -   {done} recorded, {minutes:0.0} minutes{_warning}";
        PrevButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index < _prompts.Count - 1;
        PlayButton.IsEnabled = _dataset.HasClip(_index);
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

        var quality = _dataset.Save(_index, _buffer!.ToArray(), CaptureRate);
        _buffer = null;
        if (quality.IsGood)
        {
            _warning = string.Empty;
            if (_index < _prompts.Count - 1)
            {
                _index++;
            }
        }
        else
        {
            _warning = "   -   " + string.Join(" ", quality.Warnings) + " Saved anyway; press Record to redo it.";
        }

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
        _index = Math.Max(0, _index - 1);
        ShowPrompt();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _warning = string.Empty;
        _index = Math.Min(_prompts.Count - 1, _index + 1);
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
        MessageBox.Show(this,
            $"Exported {clips} clips ({duration.TotalMinutes:0.0} minutes) to:\n{zip}\n\nNext: open the Colab notebook in the trainer folder, upload this zip and run it.",
            "Voice trainer");
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
