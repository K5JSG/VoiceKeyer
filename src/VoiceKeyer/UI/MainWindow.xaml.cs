using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VoiceKeyer.Macros;
using VoiceKeyer.Services;
using VoiceKeyer.Tts;

namespace VoiceKeyer.UI;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly PiperSynthesizer _synthesizer = new(AppSettings.CacheFolder);
    private readonly MessageSender _sender;
    private readonly Button[] _keyButtons = new Button[MacroFile.KeyCount];
    private readonly DispatcherTimer _reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private FileSystemWatcher? _watcher;
    private MacroFile _macros = MacroFile.CreateDefault();
    private CancellationTokenSource? _prerender;

    public MainWindow()
    {
        InitializeComponent();
        _sender = new MessageSender(_settings, _synthesizer, () => CallBox.Text, () => CallBox.Clear());
        _sender.Status += text => Dispatcher.Invoke(() => StatusText.Text = text);

        for (int i = 0; i < _keyButtons.Length; i++)
        {
            int key = i + 1;
            if (i > 0 && i % 4 == 0)
            {
                // Groups of four (F1-F4, F5-F8, F9-F12), like the key groups on a keyboard.
                KeyPanel.Children.Add(new Border
                {
                    Width = 1,
                    Margin = new Thickness(7, 5, 7, 5),
                    Background = (Brush)FindResource("BarBorder"),
                });
            }

            var button = new Button { Style = (Style)FindResource("KeyButton"), Width = 74, Height = 44 };
            button.Click += (_, _) => SendKey(key);
            button.MouseRightButtonUp += (_, args) => ShowKeyMenu(button, key, args);
            _keyButtons[i] = button;
            KeyPanel.Children.Add(button);
        }

        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            LoadMacros();
        };

        _sender.SendingChanged += OnSendingChanged;

        Loaded += (_, _) =>
        {
            VolumeSlider.Value = _settings.OutputVolumePercent;
            ShowVolume();
            ShowMode();
            ApplySettings();
            ApplyHotKeys();
            ApplyHrd();
        };
        ContentRendered += (_, _) => RestoreDock();
        PlaceWindow();
    }

    private const int StopHotKeyId = 100;
    private const int VkEscape = 0x1B;
    private const int VkF1 = 0x70;

    private GlobalHotKeys? _hotKeys;

    /// <summary>Registers (or releases) F1 to F12 system-wide according to the setting.</summary>
    private void ApplyHotKeys()
    {
        GlobalKeysItem.IsChecked = _settings.GlobalHotKeys;

        // Created here, not on SourceInitialized: that event had not run yet when the window loaded.
        _hotKeys ??= new GlobalHotKeys(new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle());
        _hotKeys.UnregisterAll();
        if (!_settings.GlobalHotKeys)
        {
            return;
        }

        var taken = new List<string>();
        for (int key = 1; key <= MacroFile.KeyCount; key++)
        {
            int number = key;
            if (!_hotKeys.Register(number, VkF1 + number - 1, () => SendKey(number)))
            {
                taken.Add($"F{number}");
            }
        }

        if (taken.Count > 0)
        {
            StatusText.Text = $"{string.Join(", ", taken)} cannot be global (Windows reserves F12; another program may hold the others). They only work when Voice Keyer is focused.";
        }
    }

    /// <summary>Esc stops a message from any program, but only while one is playing so Esc is not taken from everything else.</summary>
    private void OnSendingChanged(bool sending)
    {
        if (_hotKeys is null || !_settings.GlobalHotKeys)
        {
            return;
        }

        if (sending)
        {
            _hotKeys.Register(StopHotKeyId, VkEscape, _sender.Stop);
        }
        else
        {
            _hotKeys.Unregister(StopHotKeyId);
        }
    }

    private readonly DispatcherTimer _hrdTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _hrdReading;
    private string? _lastHrdCall;

    /// <summary>Starts (or stops) following the callsign in HRD Logbook's open Add/Edit window.</summary>
    private void ApplyHrd()
    {
        HrdCallItem.IsChecked = _settings.HrdCallsign;
        _lastHrdCall = null;
        if (_settings.HrdCallsign)
        {
            _hrdTimer.Tick -= HrdTimer_Tick;
            _hrdTimer.Tick += HrdTimer_Tick;
            _hrdTimer.Start();
        }
        else
        {
            _hrdTimer.Stop();
        }
    }

    /// <summary>
    /// Copies HRD's callsign into the CALL box whenever HRD's value changes. A call typed by hand stays until HRD's changes again;
    /// when HRD's box goes blank (a new QSO) the CALL box is cleared, but only if it still holds what HRD put there.
    /// </summary>
    private async void HrdTimer_Tick(object? sender, EventArgs e)
    {
        if (_hrdReading)
        {
            return;
        }

        _hrdReading = true;
        try
        {
            HrdQso? qso = await Task.Run(HrdLink.ReadQso);
            if (qso is null || !_settings.HrdCallsign)
            {
                return;
            }

            _sender.SentRst = qso.RstSent;
            string call = qso.Call;
            if (call == _lastHrdCall)
            {
                return;
            }

            string previous = _lastHrdCall ?? string.Empty;
            _lastHrdCall = call;
            if (call.Length > 0)
            {
                CallBox.Text = call;
                StatusText.Text = $"Call from HRD: {call}";
            }
            else if (CallBox.Text.Trim().Equals(previous, StringComparison.OrdinalIgnoreCase))
            {
                CallBox.Clear();
            }
        }
        finally
        {
            _hrdReading = false;
        }
    }

    private void HrdCall_Click(object sender, RoutedEventArgs e)
    {
        _settings.HrdCallsign = HrdCallItem.IsChecked;
        _settings.Save();
        ApplyHrd();
    }

    private void ShowVolume() => VolumeText.Text = $"{_settings.OutputVolumePercent}%";

    private void Volume_Click(object sender, RoutedEventArgs e) => VolumePopup.IsOpen = !VolumePopup.IsOpen;

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.OutputVolumePercent = (int)Math.Round(e.NewValue);
        ShowVolume();
    }

    private void VolumePopup_Closed(object? sender, EventArgs e) => _settings.Save();

    /// <summary>Mouse wheel over the VOL button or the slider nudges the volume by 5%.</summary>
    private void Volume_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (e.Delta > 0 ? 5 : -5), 0, 100);
        e.Handled = true;
    }

    private void GlobalKeys_Click(object sender, RoutedEventArgs e)
    {
        _settings.GlobalHotKeys = GlobalKeysItem.IsChecked;
        _settings.Save();
        ApplyHotKeys();
    }

    /// <summary>Run uses the first F1 to F12 block of the message file, Search and Pounce the second.</summary>
    private OperatingMode Mode => _settings.Mode;

    private void ParkBox_LostFocus(object sender, RoutedEventArgs e)
    {
        string parks = ParkBox.Text.Trim();
        if (parks != _settings.Parks)
        {
            _settings.Parks = parks;
            _settings.Save();
            LoadMacros();
        }
    }

    private void ShowMode() =>
        ModeText.Text = Mode == OperatingMode.Run ? "RUN" : "S&P";

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        _settings.Mode = Mode == OperatingMode.Run ? OperatingMode.SearchAndPounce : OperatingMode.Run;
        _settings.Save();
        ShowMode();
        LoadMacros();
        StatusText.Text = Mode == OperatingMode.Run ? "RUN messages" : "Search & Pounce messages";
    }

    private void PlaceWindow()
    {
        if (_settings.WindowLeft is double left && _settings.WindowTop is double top &&
            left > SystemParameters.VirtualScreenLeft - 50 && left < SystemParameters.VirtualScreenWidth + SystemParameters.VirtualScreenLeft - 100 &&
            top > SystemParameters.VirtualScreenTop - 20 && top < SystemParameters.VirtualScreenHeight + SystemParameters.VirtualScreenTop - 50)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>Pushes the current settings into the services and reloads the macro file.</summary>
    private void ApplySettings()
    {
        _synthesizer.PiperExe = AppSettings.PiperExe;
        _synthesizer.ModelPath = _settings.VoiceModelPath;
        ParkBox.Text = _settings.Parks;
        WatchMacroFile();
        LoadMacros();
        if (!File.Exists(_synthesizer.PiperExe))
        {
            StatusText.Text = "Piper is missing from the Voice Keyer folder; reinstall Voice Keyer.";
        }
        else if (_synthesizer.ModelPath is null || !File.Exists(_synthesizer.ModelPath))
        {
            StatusText.Text = "Open the gear menu > Settings to choose a voice.";
        }
    }

    private void WatchMacroFile()
    {
        _watcher?.Dispose();
        _watcher = null;
        string path = _settings.EffectiveMessageFile;
        if (!File.Exists(path))
        {
            return;
        }

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => Dispatcher.Invoke(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
    }

    private void LoadMacros()
    {
        try
        {
            _macros = File.Exists(_settings.EffectiveMessageFile) ? MacroFile.Load(_settings.EffectiveMessageFile) : MacroFile.CreateDefault();
        }
        catch (IOException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        foreach (var entry in _macros.Entries(Mode))
        {
            var button = _keyButtons[entry.Key - 1];
            button.IsEnabled = !entry.IsEmpty;
            button.ToolTip = entry.IsEmpty ? "Right-click to assign a message" : entry.Message;
            button.Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = $"F{entry.Key}", FontSize = 10, Foreground = (Brush)FindResource("DimBrush"), HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = LabelText(entry), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 70, HorizontalAlignment = HorizontalAlignment.Center },
                },
            };
            // Disabled buttons still need the right-click menu so a Spare key can be filled in.
            button.IsEnabled = true;
            button.Opacity = entry.IsEmpty ? 0.4 : 1;
        }

        _prerender?.Cancel();
        _prerender = new CancellationTokenSource();
        _ = _sender.PrerenderAsync(_macros.Entries(Mode).Where(e => !e.IsEmpty).Select(e => e.Message), _prerender.Token);
    }

    /// <summary>Button caption, with the {MYCALL} and {OPERATOR} placeholders filled in.</summary>
    private string LabelText(MacroEntry entry) =>
        entry.DisplayLabel
            .Replace("{MYCALL}", _settings.MyCall.Length > 0 ? _settings.MyCall : "My call", StringComparison.OrdinalIgnoreCase)
            .Replace("{OPERATOR}", _settings.Operator, StringComparison.OrdinalIgnoreCase);

    private void SendKey(int key)
    {
        var entry = _macros.Get(Mode, key);
        if (!entry.IsEmpty)
        {
            _ = _sender.SendAsync(entry.Message);
        }
    }

    private void ShowKeyMenu(Button button, int key, MouseButtonEventArgs args)
    {
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = $"Edit F{key} message..." };
        edit.Click += (_, _) => EditKey(key);
        var preview = new MenuItem { Header = "Preview (play without keying the radio)" };
        preview.Click += (_, _) =>
        {
            var entry = _macros.Get(Mode, key);
            if (!entry.IsEmpty)
            {
                _ = _sender.SendAsync(entry.Message, key: false);
            }
        };
        var open = new MenuItem { Header = "Open message file in editor" };
        open.Click += OpenMacroFile_Click;
        menu.Items.Add(edit);
        menu.Items.Add(preview);
        menu.Items.Add(open);
        button.ContextMenu = menu;
        menu.IsOpen = true;
        args.Handled = true;
    }

    private void EditKey(int key)
    {
        var entry = _macros.Get(Mode, key);
        var dialog = new EditMacroWindow(entry, _sender) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string path = _settings.EffectiveMessageFile;
        if (!File.Exists(path))
        {
            // First edit with no message file yet: start Voice Keyer's own file from the blank template.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            MacroFile.CreateDefault().Save(path);
        }

        // Re-read so edits made in an external editor since the last load are not overwritten.
        var file = MacroFile.Load(path);
        file.Set(Mode, key, dialog.Label, dialog.Message);
        file.Save();
        LoadMacros();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _sender.Stop();
            e.Handled = true;
        }
        else if (key is >= Key.F1 and <= Key.F12)
        {
            SendKey(key - Key.F1 + 1);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Drag the bar by its grip. Dropped near the top or bottom (just above the taskbar) it docks there and maximized
    /// windows make room for it; dropped near a side or the middle it snaps there.
    /// </summary>
    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dock ??= new BarDock(this);
        _dock.Undock();
        DragMove();
        _dock.SnapAfterDrag();
        _settings.DockEdge = _dock.Edge;
        _settings.DockAlign = _dock.Align;
        StatusText.Text = _dock.Edge switch
        {
            DockEdge.Bottom => "Docked above the taskbar (drag the grip to undock)",
            DockEdge.Top => "Docked at the top of the screen (drag the grip to undock)",
            _ => StatusText.Text,
        };
    }

    private BarDock? _dock;

    /// <summary>Docks the bar again where it was last time Voice Keyer ran (needs the window's final size).</summary>
    private void RestoreDock()
    {
        if (_settings.DockEdge != DockEdge.None)
        {
            _dock ??= new BarDock(this);
            _dock.Dock(_settings.DockEdge, _settings.DockAlign);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _sender.Stop();

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        MenuButton.ContextMenu.PlacementTarget = MenuButton;
        MenuButton.ContextMenu.IsOpen = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings.Save();
            ApplySettings();
        }
    }

    /// <summary>Switches to another message (.mc) file; the bar reloads at once.</summary>
    private void LoadMessageFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Function key message files (*.mc)|*.mc|All files|*.*",
            Title = "Choose a message file",
            FileName = _settings.EffectiveMessageFile,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _settings.MessageFilePath = dialog.FileName;
        _settings.Save();
        ApplySettings();
        StatusText.Text = $"Messages: {Path.GetFileName(dialog.FileName)}";
    }

    private void UseOwnMessageFile_Click(object sender, RoutedEventArgs e)
    {
        _settings.MessageFilePath = string.Empty;
        _settings.Save();
        ApplySettings();
        StatusText.Text = "Messages: Voice Keyer's own file";
    }

    private void OpenMacroFile_Click(object sender, RoutedEventArgs e)
    {
        string path = _settings.EffectiveMessageFile;
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else
        {
            StatusText.Text = "No message file yet: right-click a button to write your first message.";
        }
    }

    private void Trainer_Click(object sender, RoutedEventArgs e) =>
        new TrainerWindow(_settings) { Owner = this }.Show();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _sender.Stop();
        _hrdTimer.Stop();
        _hotKeys?.Dispose();
        _watcher?.Dispose();

        // Give the reserved strip back; the saved DockEdge docks the bar again at the next start.
        _dock?.Dispose();
        if (IsLoaded && double.IsFinite(Left) && double.IsFinite(Top))
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }

        _settings.Save();
    }
}
