using System.Globalization;
using System.Text;

namespace VoiceKeyer.Macros;

/// <summary>Live values the macros can refer to. Implemented by the message sender (settings plus the callsign box on the bar).</summary>
public interface IMacroContext
{
    string MyCall { get; }

    string Operator { get; }

    /// <summary>The callsign currently typed in the callsign box on the bar.</summary>
    string EntryCall { get; }

    string RoverQth { get; }

    /// <summary>The Parks On The Air reference(s) being activated, e.g. "K-1234" (more than one for a two-fer).</summary>
    IReadOnlyList<string> Parks { get; }

    /// <summary>The signal report being sent, e.g. "59" (read from HRD's Add/Edit window when available).</summary>
    string SentRst { get; }

    /// <summary>Folder that relative .wav paths in macros are resolved against (the Wav folder in Settings).</summary>
    string WavFolder { get; }

    double DefaultLengthScale { get; }
}

public abstract record MacroAction;

public sealed record SpeakAction(string Text, double LengthScale, double? NoiseScale = null, double? NoiseW = null) : MacroAction;

public sealed record WavAction(string Path) : MacroAction;

/// <summary>A command that acts on the bar rather than the radio, e.g. WIPE (clears the callsign box).</summary>
public sealed record CommandAction(string Name) : MacroAction;

public sealed record WarningAction(string Message) : MacroAction;

/// <summary>
/// Turns a message such as <c>{TTSParms 0.68}{TTS C-Q {MYCALL}}</c> or <c>{OPERATOR}\Cq.wav</c> into
/// an ordered list of actions. Variables ({MYCALL}, {ICAO x}, ...) are substituted; commands ({TTS}, {WIPE}) become actions.
/// </summary>
public static class MacroEngine
{
    private static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase) { "WIPE" };

    public static List<MacroAction> Expand(string message, IMacroContext context)
    {
        var actions = new List<MacroAction>();
        double length = context.DefaultLengthScale;
        double? noise = null;
        double? noiseW = null;
        var literal = new StringBuilder();

        if (HasUnclosedBrace(message))
        {
            actions.Add(new WarningAction("Message is missing a closing } (sending it anyway)"));
        }

        void FlushLiteral()
        {
            string text = literal.ToString().Trim();
            literal.Clear();
            if (text.Length == 0)
            {
                return;
            }

            if (text.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                actions.Add(new WavAction(ResolveWav(text, context.WavFolder)));
            }
            else
            {
                actions.Add(new WarningAction($"Ignored plain text \"{text}\" (use {{TTS ...}} to speak it)"));
            }
        }

        foreach (var (isMacro, content) in Tokenize(message))
        {
            if (!isMacro)
            {
                literal.Append(content);
                continue;
            }

            var (name, argument) = SplitMacro(content);
            switch (name.ToUpperInvariant())
            {
                case "TTS":
                    FlushLiteral();
                    string text = ExpandText(argument, context).Trim();
                    if (text.Length > 0)
                    {
                        actions.Add(new SpeakAction(text, length, noise, noiseW));
                    }

                    break;
                case "TTSPARMS":
                    FlushLiteral();
                    double?[] numbers = ExpandText(argument, context)
                        .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : (double?)null)
                        .ToArray();
                    if (numbers.Length > 0 && numbers[0] is > 0 and double scale)
                    {
                        length = scale;
                    }

                    noise = numbers.Length > 1 ? numbers[1] : noise;
                    noiseW = numbers.Length > 2 ? numbers[2] : noiseW;
                    break;
                case "TTSTRIM":
                    // Silence is always trimmed from the ends of spoken clips.
                    break;
                default:
                    if (KnownCommands.Contains(name))
                    {
                        FlushLiteral();
                        actions.Add(new CommandAction(name.ToUpperInvariant()));
                    }
                    else if (TryVariable(content, context, out string value))
                    {
                        literal.Append(value);
                    }
                    else
                    {
                        FlushLiteral();
                        actions.Add(new WarningAction($"Unsupported macro {{{content}}}"));
                    }

                    break;
            }
        }

        FlushLiteral();
        return actions;
    }

    /// <summary>Substitutes variables in a text argument (the part inside {TTS ...}).</summary>
    /// <param name="spoken">
    /// True (the default) gives park references and reports in a form Piper reads digit by digit ("U S two zero three eight");
    /// false gives the raw value, which {ICAO} needs so it can spell it.
    /// </param>
    public static string ExpandText(string text, IMacroContext context, bool spoken = true)
    {
        var builder = new StringBuilder();
        foreach (var (isMacro, content) in Tokenize(text))
        {
            if (!isMacro)
            {
                builder.Append(content);
            }
            else if (TryVariable(content, context, out string value, spoken))
            {
                builder.Append(value);
            }
            else
            {
                builder.Append('{').Append(content).Append('}');
            }
        }

        return builder.ToString();
    }

    private static bool TryVariable(string content, IMacroContext context, out string value, bool spoken = true)
    {
        var (name, argument) = SplitMacro(content);
        switch (name.ToUpperInvariant())
        {
            case "MYCALL":
                value = context.MyCall;
                return true;
            case "OPERATOR":
                value = context.Operator;
                return true;
            case "CALL":
            case "CALLSIGN":
                value = context.EntryCall;
                return true;
            case "ROVERQTH":
                value = context.RoverQth;
                return true;
            case "PARK":
            case "PARKREF":
                value = Say(context.Parks.FirstOrDefault() ?? string.Empty, spoken);
                return true;
            case "PARK2":
                value = Say(context.Parks.Skip(1).FirstOrDefault() ?? string.Empty, spoken);
                return true;
            case "PARKS":
                value = string.Join(", ", context.Parks.Select(park => Say(park, spoken)));
                return true;
            case "SENTRST":
            case "RST":
                string rst = CleanReport(context.SentRst);
                value = spoken ? SayReport(rst.Length > 0 ? rst : "59") : rst.Length > 0 ? rst : "59";
                return true;
            case "ICAO":
                string source = ExpandText(argument.Trim(), context, spoken: false);
                value = NatoPhonetic.Spell(source == "!" ? context.EntryCall : source);
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static string Say(string value, bool spoken) => spoken ? SpellOut(value) : value;

    /// <summary>HRD's RST Sent as sent on the air: "59+10dB" becomes "59+10" (spaces and the dB unit dropped).</summary>
    private static string CleanReport(string report) =>
        report.Replace("dB", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty);

    /// <summary>
    /// "59" becomes "five nine", "59+" becomes "five nine plus" and "59+10" becomes "five nine plus 10": the report digit
    /// by digit, the amount over S9 as a number ("plus ten", as it is said on the air).
    /// </summary>
    public static string SayReport(string report)
    {
        int plus = report.IndexOf('+');
        if (plus < 0)
        {
            return SpellOut(report);
        }

        string over = new(report[(plus + 1)..].Where(char.IsAsciiDigit).ToArray());
        string spoken = SpellOut(report[..plus]);
        // A bare "59+" is said as "five nine plus".
        return over.Length > 0 ? $"{spoken} plus {int.Parse(over, CultureInfo.InvariantCulture)}" : $"{spoken} plus";
    }

    private static readonly string[] DigitWords = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    /// <summary>
    /// "US-2038" becomes "U S two zero three eight" and "59" becomes "five nine": Piper would otherwise read the digits as a
    /// number ("two thousand thirty eight") and say "dash". Letters are spaced out so they are read one at a time.
    /// </summary>
    public static string SpellOut(string text)
    {
        var words = new List<string>();
        foreach (char c in text.ToUpperInvariant())
        {
            if (char.IsAsciiDigit(c))
            {
                words.Add(DigitWords[c - '0']);
            }
            else if (char.IsAsciiLetter(c))
            {
                words.Add(c.ToString());
            }
        }

        return string.Join(' ', words);
    }

    private static string ResolveWav(string path, string wavFolder) =>
        Path.IsPathRooted(path) ? path : Path.Combine(wavFolder, path);

    /// <summary>Macro name is the leading run of non-space, non-brace characters; everything after is the argument.</summary>
    private static (string Name, string Argument) SplitMacro(string content)
    {
        int end = 0;
        while (end < content.Length && content[end] != ' ' && content[end] != '{')
        {
            end++;
        }

        string name = content[..end];
        string argument = content[end..];
        return (name, argument.StartsWith(' ') ? argument[1..] : argument);
    }

    /// <summary>Splits text into plain runs and top-level {...} macros (braces nest).</summary>
    private static IEnumerable<(bool IsMacro, string Content)> Tokenize(string text)
    {
        var plain = new StringBuilder();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{')
            {
                if (depth == 0)
                {
                    if (plain.Length > 0)
                    {
                        yield return (false, plain.ToString());
                        plain.Clear();
                    }

                    start = i + 1;
                }

                depth++;
            }
            else if (c == '}' && depth > 0)
            {
                depth--;
                if (depth == 0)
                {
                    yield return (true, text[start..i]);
                }
            }
            else if (depth == 0)
            {
                plain.Append(c);
            }
        }

        if (depth > 0)
        {
            // Missing closing brace: treat the rest as the macro so the message still works (Expand warns about it).
            yield return (true, text[start..]);
        }
        else if (plain.Length > 0)
        {
            yield return (false, plain.ToString());
        }
    }

    private static bool HasUnclosedBrace(string text)
    {
        int depth = 0;
        foreach (char c in text)
        {
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && depth > 0)
            {
                depth--;
            }
        }

        return depth > 0;
    }
}
