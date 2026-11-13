using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceKeyer.Services;

public enum PttMethod
{
    None,
    SerialRts,
    SerialDtr,
    CatCommand,
}

/// <summary>User settings, stored as JSON in %AppData%\VoiceKeyer\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The function-key message file (.mc). Blank = Voice Keyer's own messages.mc in %AppData%\VoiceKeyer.</summary>
    public string MessageFilePath { get; set; } = string.Empty;

    /// <summary>F1 to F12 work in any program (they are taken from the other program while Voice Keyer runs).</summary>
    public bool GlobalHotKeys { get; set; } = true;

    /// <summary>Put the callsign from Ham Radio Deluxe Logbook's open Add/Edit window into the CALL box (read-only).</summary>
    public bool HrdCallsign { get; set; } = true;

    /// <summary>Folder holding the Piper voices (.onnx plus .onnx.json). Voices bundled with Voice Keyer are copied here.</summary>
    public string VoiceFolder { get; set; } = string.Empty;

    public string VoiceModel { get; set; } = string.Empty;

    public double DefaultLengthScale { get; set; } = 1.0;

    public string OutputDevice { get; set; } = string.Empty;

    /// <summary>Sending volume, 0 to 100 (software gain on the clips; the sound card setting is untouched).</summary>
    public int OutputVolumePercent { get; set; } = 100;

    /// <summary>Linear gain for <see cref="OutputVolumePercent"/>. Squared so the slider feels even: 50% is about -12 dB.</summary>
    [JsonIgnore]
    public float VolumeGain => (float)Math.Pow(Math.Clamp(OutputVolumePercent, 0, 100) / 100.0, 2);

    public PttMethod Ptt { get; set; } = PttMethod.None;

    public string PttPort { get; set; } = "COM1";

    public int PttLeadMs { get; set; } = 150;

    public int PttTailMs { get; set; } = 150;

    public string CatOn { get; set; } = "TX1;";

    public string CatOff { get; set; } = "TX0;";

    public string MyCall { get; set; } = string.Empty;

    public string Operator { get; set; } = string.Empty;

    public string RoverQth { get; set; } = string.Empty;

    /// <summary>Parks On The Air reference(s) being activated, separated by commas or spaces (e.g. "K-1234, K-5678").</summary>
    public string Parks { get; set; } = string.Empty;

    [JsonIgnore]
    public IReadOnlyList<string> ParkList =>
        Parks.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(park => park.ToUpperInvariant()).ToList();

    /// <summary>Which message set the F-keys send: Run (calling CQ) or Search and Pounce (hunting).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Macros.OperatingMode Mode { get; set; } = Macros.OperatingMode.Run;

    /// <summary>Folder that relative .wav paths in messages are resolved against.</summary>
    public string WavFolder { get; set; } = string.Empty;

    public string TrainerInputDevice { get; set; } = string.Empty;

    public string TrainerFolder { get; set; } = string.Empty;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    /// <summary>Screen edge the bar is docked to (space reserved so maximized windows stay clear of it), or None.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UI.DockEdge DockEdge { get; set; } = UI.DockEdge.None;

    /// <summary>Where along the edge the bar sits (left, centre, right, or wherever it was dropped).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UI.DockAlign DockAlign { get; set; } = UI.DockAlign.Free;

    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceKeyer");

    public static string SettingsPath { get; } = Path.Combine(DataFolder, "settings.json");

    public static string DefaultMessageFile { get; } = Path.Combine(DataFolder, "messages.mc");

    public static string CacheFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceKeyer", "cache");

    public static string DefaultTrainerFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Voice Keyer Training");

    /// <summary>The Piper engine that ships beside VoiceKeyer.exe.</summary>
    public static string PiperExe { get; } = Path.Combine(AppContext.BaseDirectory, "piper", "piper.exe");

    private static string BundledVoiceFolder { get; } = Path.Combine(AppContext.BaseDirectory, "voices");

    /// <summary>The message file in use: the chosen one, or Voice Keyer's own default.</summary>
    public string EffectiveMessageFile =>
        string.IsNullOrWhiteSpace(MessageFilePath) ? DefaultMessageFile : MessageFilePath;

    public string? VoiceModelPath =>
        string.IsNullOrWhiteSpace(VoiceModel) || string.IsNullOrWhiteSpace(VoiceFolder)
            ? null
            : Path.Combine(VoiceFolder, VoiceModel + ".onnx");

    public static AppSettings Load()
    {
        LegacyData.MigrateAll();
        AppSettings settings;
        try
        {
            settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            settings = new AppSettings();
        }

        settings.RewriteLegacyPaths();
        settings.FillDefaults();
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Points saved folders that were under the program's old name ("Keyer") at their new location.</summary>
    private void RewriteLegacyPaths()
    {
        VoiceFolder = LegacyData.RewritePrefix(VoiceFolder, LegacyData.OldDataFolder, DataFolder);
        WavFolder = LegacyData.RewritePrefix(WavFolder, LegacyData.OldDataFolder, DataFolder);
        MessageFilePath = LegacyData.RewritePrefix(MessageFilePath, LegacyData.OldDataFolder, DataFolder);
        TrainerFolder = LegacyData.RewritePrefix(TrainerFolder, LegacyData.OldTrainerFolder, DefaultTrainerFolder);
    }

    /// <summary>Fills blank folders with Voice Keyer's own, so a fresh install works with no setup.</summary>
    public void FillDefaults()
    {
        if (string.IsNullOrWhiteSpace(VoiceFolder))
        {
            VoiceFolder = Path.Combine(DataFolder, "voices");
        }

        if (string.IsNullOrWhiteSpace(WavFolder))
        {
            WavFolder = Path.Combine(DataFolder, "wav");
        }

        if (string.IsNullOrWhiteSpace(TrainerFolder))
        {
            TrainerFolder = DefaultTrainerFolder;
        }

        InstallBundledVoices();

        if (VoiceModelPath is null || !File.Exists(VoiceModelPath))
        {
            // Prefer a user-trained voice (no "en_US-style" dash in the name) over the stock Piper voices.
            var voices = AvailableVoices().ToList();
            VoiceModel = voices.FirstOrDefault(name => !name.Contains('-')) ?? voices.FirstOrDefault() ?? string.Empty;
        }
    }

    public IEnumerable<string> AvailableVoices() =>
        Directory.Exists(VoiceFolder)
            ? Directory.EnumerateFiles(VoiceFolder, "*.onnx")
                .Select(path => Path.GetFileNameWithoutExtension(path)!)
                .Order(StringComparer.OrdinalIgnoreCase)
            : [];

    /// <summary>Copies the voices shipped with Voice Keyer into the voice folder (never overwriting an existing file).</summary>
    private void InstallBundledVoices()
    {
        if (!Directory.Exists(BundledVoiceFolder))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(VoiceFolder);
            foreach (string source in Directory.EnumerateFiles(BundledVoiceFolder))
            {
                string target = Path.Combine(VoiceFolder, Path.GetFileName(source));
                if (!File.Exists(target))
                {
                    File.Copy(source, target);
                }
            }
        }
        catch (IOException)
        {
            // A voice folder that cannot be written to is reported later, when a voice is needed.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
