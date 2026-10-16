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
        ['/'] = "Stroke", ['-'] = "Dash", ['.'] = "Point",
    };

    /// <summary>
    /// Returns e.g. "Kilo Two Alpha Bravo" for "K2AB". Unknown characters are skipped. The words are joined with plain
    /// spaces: commas make Piper pause and lift its pitch at every word, which sounds choppy and runs about 25% longer.
    /// </summary>
    public static string Spell(string text)
    {
        var builder = new StringBuilder();
        foreach (char c in text.ToUpperInvariant())
        {
            if (!Words.TryGetValue(c, out string? word))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(word);
        }

        return builder.ToString();
    }
}
