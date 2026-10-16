using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using VoiceKeyer.Audio;
using VoiceKeyer.Services;

namespace VoiceKeyer.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly TextBox _macroFile = new();
    private readonly TextBox _voiceFolder = new();
    private readonly ComboBox _voice = new();
    private readonly TextBox _speed = new();
    private readonly ComboBox _outputDevice = new() { IsEditable = true };
    private readonly ComboBox _ptt = new();
    private readonly ComboBox _port = new() { IsEditable = true };
    private readonly TextBox _lead = new();
    private readonly TextBox _tail = new();
    private readonly TextBox _catOn = new();
    private readonly TextBox _catOff = new();
    private readonly TextBox _myCall = new();
    private readonly TextBox _roverQth = new();
    private readonly TextBox _operator = new();
    private readonly TextBox _wavFolder = new();
    private int _row;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        Section("Messages");
        Row("Message file (.mc)", _macroFile, BrowseFile(_macroFile, "Function key message files (*.mc)|*.mc|All files|*.*"),
            hint: "Leave blank to use Voice Keyer's own file (messages.mc in %AppData%\\VoiceKeyer).");
        Row("Wav folder", _wavFolder, BrowseFolder(_wavFolder), hint: "Recordings in a message ({OPERATOR}\\Cq.wav) are found under this folder.");
        Row("My callsign ({MYCALL})", _myCall);
        Row("Operator ({OPERATOR})", _operator);
        Row("Rover QTH ({ROVERQTH})", _roverQth);

        Section("Voice (Piper text-to-speech)");
        Row("Voice folder", _voiceFolder, BrowseFolder(_voiceFolder));
        var voiceButtons = new StackPanel { Orientation = Orientation.Horizontal };
        voiceButtons.Children.Add(Small("Import voice...", ImportVoice_Click));
        voiceButtons.Children.Add(Small("Export voice...", ExportVoice_Click));
        voiceButtons.Children.Add(Small("Refresh", (_, _) => LoadVoices()));
        Row("Voice", _voice, voiceButtons);
        Row("Default speed", _speed, hint: "Piper length scale: 1.0 = normal, lower = faster (0.7 is typical for contests).");

        Section("Radio audio and PTT");
        Row("Output sound card", _outputDevice);
        Row("PTT method", _ptt);
        Row("PTT COM port", _port, hint: "A port can only be open in one program; pick one no other program (logger, CAT software) is using.");
        Row("Lead / tail delay (ms)", _lead, extra: _tail);
        Row("CAT PTT on / off", _catOn, extra: _catOff);

        _ptt.ItemsSource = new[] { "None (the radio's VOX keys it)", "Serial port RTS", "Serial port DTR", "CAT command" };
        _outputDevice.ItemsSource = ClipPlayer.OutputDevices();
        _port.ItemsSource = SerialPort.GetPortNames();
        Load();
    }

    private void Load()
    {
        _macroFile.Text = _settings.MessageFilePath;
        _wavFolder.Text = _settings.WavFolder;
        _voiceFolder.Text = _settings.VoiceFolder;
        LoadVoices();
        _speed.Text = _settings.DefaultLengthScale.ToString("0.##", CultureInfo.InvariantCulture);
        _outputDevice.Text = _settings.OutputDevice;
        _ptt.SelectedIndex = (int)_settings.Ptt;
        _port.Text = _settings.PttPort;
        _lead.Text = _settings.PttLeadMs.ToString(CultureInfo.InvariantCulture);
        _tail.Text = _settings.PttTailMs.ToString(CultureInfo.InvariantCulture);
        _catOn.Text = _settings.CatOn;
        _catOff.Text = _settings.CatOff;
        _myCall.Text = _settings.MyCall;
        _roverQth.Text = _settings.RoverQth;
        _operator.Text = _settings.Operator;
    }

    private void LoadVoices()
    {
        string selected = _voice.Text.Length > 0 ? _voice.Text : _settings.VoiceModel;
        var temp = new AppSettings { VoiceFolder = _voiceFolder.Text };
        _voice.ItemsSource = temp.AvailableVoices().ToList();
        _voice.Text = selected;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(_speed.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed) || speed is < 0.2 or > 3)
        {
            MessageBox.Show(this, "Default speed must be a number between 0.2 and 3 (for example 0.8).", "Voice Keyer");
            return;
        }

        _settings.MessageFilePath = _macroFile.Text.Trim();
        _settings.WavFolder = _wavFolder.Text.Trim();
        _settings.VoiceFolder = _voiceFolder.Text.Trim();
        _settings.VoiceModel = _voice.Text.Trim();
        _settings.DefaultLengthScale = speed;
        _settings.OutputDevice = _outputDevice.Text.Trim();
        _settings.Ptt = (PttMethod)Math.Max(0, _ptt.SelectedIndex);
        _settings.PttPort = _port.Text.Trim();
        _settings.PttLeadMs = int.TryParse(_lead.Text, out int lead) ? Math.Clamp(lead, 0, 2000) : 150;
        _settings.PttTailMs = int.TryParse(_tail.Text, out int tail) ? Math.Clamp(tail, 0, 2000) : 150;
        _settings.CatOn = _catOn.Text;
        _settings.CatOff = _catOff.Text;
        _settings.MyCall = _myCall.Text.Trim().ToUpperInvariant();
        _settings.RoverQth = _roverQth.Text.Trim().ToUpperInvariant();
        _settings.Operator = _operator.Text.Trim().ToUpperInvariant();
        DialogResult = true;
    }

    /// <summary>Imports a voice from a zip (the trainer's download or an exported voice) or from a .onnx with its .onnx.json.</summary>
    private void ImportVoice_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Piper voice (*.zip;*.onnx)|*.zip;*.onnx",
            Title = "Choose a voice zip, or the voice .onnx",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var added = VoicePackage.Import(dialog.FileName, _voiceFolder.Text.Trim());
            LoadVoices();
            _voice.Text = added[0];
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Voice Keyer");
        }
    }

    /// <summary>Writes the selected voice to a single zip that can be imported on any PC running Voice Keyer.</summary>
    private void ExportVoice_Click(object sender, RoutedEventArgs e)
    {
        string name = _voice.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Pick a voice to export first.", "Voice Keyer");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Voice zip (*.zip)|*.zip",
            FileName = name + "_voice.zip",
            Title = "Export voice",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            VoicePackage.Export(_voiceFolder.Text.Trim(), name, dialog.FileName);
            MessageBox.Show(this, $"Saved {dialog.FileName}.\n\nOn the other PC: Settings > Import voice... and pick this zip.", "Voice Keyer");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Voice Keyer");
        }
    }

    private void Section(string title)
    {
        AddRow();
        var text = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) };
        Grid.SetRow(text, _row - 1);
        Grid.SetColumnSpan(text, 3);
        Form.Children.Add(text);
    }

    private void Row(string label, Control input, UIElement? button = null, string? hint = null, TextBox? extra = null)
    {
        AddRow();
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
        input.Margin = new Thickness(0, 3, 0, 3);
        input.Padding = new Thickness(3);
        input.ToolTip = hint;
        Grid.SetRow(caption, _row - 1);
        Grid.SetRow(input, _row - 1);
        Grid.SetColumn(input, 1);
        Form.Children.Add(caption);

        if (extra is not null)
        {
            // Two small boxes share the input column.
            var pair = new UniformGrid { Columns = 2 };
            extra.Margin = new Thickness(6, 3, 0, 3);
            extra.Padding = new Thickness(3);
            pair.Children.Add(input);
            pair.Children.Add(extra);
            Grid.SetRow(pair, _row - 1);
            Grid.SetColumn(pair, 1);
            Form.Children.Add(pair);
        }
        else
        {
            Form.Children.Add(input);
        }

        if (button is not null)
        {
            Grid.SetRow(button, _row - 1);
            Grid.SetColumn(button, 2);
            Form.Children.Add(button);
        }

        if (hint is not null)
        {
            AddRow();
            var note = new TextBlock { Text = hint, FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("DimBrush"), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(note, _row - 1);
            Grid.SetColumn(note, 1);
            Form.Children.Add(note);
        }
    }

    private void AddRow()
    {
        Form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _row++;
    }

    private static Button Small(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 3, 0, 3) };
        button.Click += handler;
        return button;
    }

    private Button BrowseFile(TextBox target, string filter) => Small("Browse...", (_, _) =>
    {
        var dialog = new OpenFileDialog { Filter = filter, FileName = target.Text };
        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FileName;
        }
    });

    private Button BrowseFolder(TextBox target) => Small("Browse...", (_, _) =>
    {
        var dialog = new OpenFolderDialog { InitialDirectory = target.Text };
        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FolderName;
        }
    });
}
