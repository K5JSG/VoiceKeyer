using System.Text;
using System.Text.RegularExpressions;

namespace VoiceKeyer.Macros;

public enum OperatingMode
{
    Run,
    SearchAndPounce,
}

/// <summary>One function-key line of a message (.mc) file.</summary>
public sealed record MacroEntry(int Key, string Label, string Message)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Message);

    /// <summary>The label as shown on the button: "&amp;&amp;" becomes "&amp;", a lone "&amp;" is dropped.</summary>
    public string DisplayLabel
    {
        get
        {
            string text = Label.Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&");
            return text.Trim();
        }
    }
}

/// <summary>
/// A function-key message file (.mc). Lines are "F&lt;n&gt; label,message"; the first F1..F12 block is
/// the Run set and the second is the Search &amp; Pounce set. Comments and layout are preserved on save.
/// </summary>
public sealed partial class MacroFile
{
    public const int KeyCount = 12;

    [GeneratedRegex(@"^\s*F(?<key>\d{1,2})\s(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyLine();

    private readonly List<string> _lines;
    private readonly Dictionary<(OperatingMode Mode, int Key), int> _lineIndex = new();

    private MacroFile(List<string> lines)
    {
        _lines = lines;
        Index();
    }

    public string? Path { get; private set; }

    public static MacroFile Parse(string text) =>
        new(text.Replace("\r\n", "\n").Split('\n').ToList().TrimTrailingBlank());

    public static MacroFile Load(string path)
    {
        var file = Parse(File.ReadAllText(path));
        file.Path = path;
        return file;
    }

    public static MacroFile CreateDefault()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Voice Keyer function key messages");
        builder.AppendLine("# F<n> label,message   Messages: {TTS text}  {TTSParms 0.8}  folder\\file.wav  {WIPE}");
        foreach (string section in new[] { "RUN", "S&P" })
        {
            builder.AppendLine($"# {section} messages");
            for (int key = 1; key <= KeyCount; key++)
            {
                builder.AppendLine($"F{key} Spare,");
            }
        }

        return Parse(builder.ToString());
    }

    public MacroEntry Get(OperatingMode mode, int key)
    {
        if (_lineIndex.TryGetValue((mode, key), out int line))
        {
            return ParseLine(_lines[line])!;
        }

        return new MacroEntry(key, string.Empty, string.Empty);
    }

    public IEnumerable<MacroEntry> Entries(OperatingMode mode) =>
        Enumerable.Range(1, KeyCount).Select(key => Get(mode, key));

    /// <summary>Replaces (or appends) one key's line, leaving every other line exactly as it was.</summary>
    public void Set(OperatingMode mode, int key, string label, string message)
    {
        string line = $"F{key} {label},{message}";
        if (_lineIndex.TryGetValue((mode, key), out int index))
        {
            _lines[index] = line;
            return;
        }

        // Missing key: put it at the end of its section (or the file for the Run set).
        int insertAt = _lines.Count;
        if (mode == OperatingMode.Run)
        {
            int firstSp = _lineIndex.Where(pair => pair.Key.Mode == OperatingMode.SearchAndPounce)
                .Select(pair => pair.Value).DefaultIfEmpty(_lines.Count).Min();
            insertAt = firstSp;
        }

        _lines.Insert(insertAt, line);
        Index();
    }

    public void Save(string? path = null)
    {
        path ??= Path ?? throw new InvalidOperationException("No file path.");
        File.WriteAllLines(path, _lines, new UTF8Encoding(false));
        Path = path;
    }

    private static MacroEntry? ParseLine(string line)
    {
        var match = KeyLine().Match(line);
        if (!match.Success)
        {
            return null;
        }

        string rest = match.Groups["rest"].Value;
        int comma = rest.IndexOf(',');
        string label = comma < 0 ? rest : rest[..comma];
        string message = comma < 0 ? string.Empty : rest[(comma + 1)..];
        return new MacroEntry(int.Parse(match.Groups["key"].Value), label.Trim(), message.Trim());
    }

    private void Index()
    {
        _lineIndex.Clear();
        var mode = OperatingMode.Run;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].TrimStart().StartsWith('#'))
            {
                continue;
            }

            var entry = ParseLine(_lines[i]);
            if (entry is null || entry.Key is < 1 or > KeyCount)
            {
                continue;
            }

            // A repeated key means the next (S&P) block has started.
            if (_lineIndex.ContainsKey((mode, entry.Key)) && mode == OperatingMode.Run)
            {
                mode = OperatingMode.SearchAndPounce;
            }

            _lineIndex[(mode, entry.Key)] = i;
        }
    }
}

internal static class ListExtensions
{
    public static List<string> TrimTrailingBlank(this List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
