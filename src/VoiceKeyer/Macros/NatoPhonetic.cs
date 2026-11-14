using System.Text;

namespace VoiceKeyer.Macros;

/// <summary>Spells text out with the NATO phonetic alphabet, as the {ICAO} macro does.</summary>
public static class NatoPhonetic
{
    private static readonly Dictionary<char, string> Words = new()
    {
        ['A'] = "Alpha", ['B'] = "Bravo", ['C'] = "Charlie", ['D'] = "Delta", ['E'] = "Echo",
        ['F'] = "Foxtrot", ['G'] = "Golf", ['H'] = "Hotel", ['I'] = "India", ['J'] = "Juliet",
        ['K'] = "Kilo", ['L'] = "Lima", ['M'] = "Mike", ['N'] = "November", ['O'] = "Oscar",
        ['P'] = "Papa", ['Q'] = "Quebec", ['R'] = "Romeo", ['S'] = "Sierra", ['T'] = "Tango",
        ['U'] = "Uniform", ['V'] = "Victor", ['W'] = "Whiskey", ['X'] = "X-ray", ['Y'] = "Yankee",
        ['Z'] = "Zulu",
        ['0'] = "Zero", ['1'] = "One", ['2'] = "Two", ['3'] = "Three", ['4'] = "Four",
        ['5'] = "Five", ['6'] = "Six", ['7'] = "Seven", ['8'] = "Eight", ['9'] = "Nine",
        ['/'] = "Stroke", ['.'] = "Point",
    };

    /// <summary>
    /// <paramref name="respellPapa"/> is true for speech and false when the text is a script for a person to read aloud.
    /// Returns e.g. "Kilo Two Alpha Bravo" for "K2AB". Unknown characters are skipped. The words are joined with plain
    /// spaces: commas make Piper pause and lift its pitch at every word, which sounds choppy and runs about 25% longer.
    /// </summary>
    public static string Spell(string text, bool respellPapa = true)
    {
        var builder = new StringBuilder();
        bool pause = false;
        char previous = ' ';
        foreach (char c in text.ToUpperInvariant())
        {
            if (!Words.TryGetValue(c, out string? word))
            {
                // A comma or space between items (e.g. two park references) is a real pause.
                pause |= builder.Length > 0 && (c == ',' || c == ';' || char.IsWhiteSpace(c));
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(pause ? ", " : " ");
            }

            pause = false;

            // The trained headset voice squashes "Papa" to "pa" inside a call sign, but says "Pop-paw" cleanly. The
            // "stroke Papa" at the end of a /P call was recorded and is fine as it is.
            builder.Append(respellPapa && c == 'P' && previous != '/' ? "Pop-paw" : word);
            previous = c;
        }

        return builder.ToString();
    }
}
