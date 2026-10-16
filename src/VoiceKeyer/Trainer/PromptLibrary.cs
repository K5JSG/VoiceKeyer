using VoiceKeyer.Macros;

namespace VoiceKeyer.Trainer;

/// <summary>
/// The sentences a speaker reads to train a voice: general English for natural prosody, plus the phonetics,
/// numbers and contest phrases a radio operator's macros actually use.
/// </summary>
public static class PromptLibrary
{
    private static readonly string[] General =
    [
        "The quick brown fox jumps over the lazy dog near the old barn.",
        "Please speak clearly and keep a steady pace for the whole recording.",
        "A good signal depends on a good antenna and a solid ground.",
        "Thunderstorms were forecast for the afternoon, so we took the antenna down early.",
        "She sells seashells by the seashore, and the shells she sells are surely seashells.",
        "The band was wide open and signals were booming in from across the ocean.",
        "We set up the tent before dark and ran the coax back to the truck.",
        "Could you repeat that one more time? The noise is rather bad tonight.",
        "I will be operating from the mountain top this weekend if the weather holds.",
        "Every contact counts, but a clean log counts even more.",
        "The antenna tuner found a match on the first try, which never happens.",
        "Keep your answers short, give the exchange once, and listen for the next call.",
        "He picked up the microphone, checked the meter, and called again.",
        "Propagation changes quickly at sunrise, so watch the higher bands first.",
        "A steady rain fell on the roof while the generator hummed outside.",
        "Nobody expected the contest to be this busy so early in the morning.",
        "The operator wrote the report on a paper log and entered it later.",
        "Turn the power down a little, the amplifier is running very warm.",
        "Our club is holding a field day on the second weekend of June.",
        "Please stand by while I check the frequency before I call.",
        "What time does the contest end, and when are the logs due?",
        "The signal faded in and out as the aurora moved across the northern sky.",
        "Whenever the wind blows hard, the long wire sways between the two trees.",
        "Three tall towers stood at the edge of the field behind the farmhouse.",
        "I think the problem is in the connector, not in the cable itself.",
        "Please move the microphone a little closer and try the check again.",
        "Good morning, and thank you for joining the net this week.",
        "It took almost an hour to find the right frequency, but it was worth it.",
        "They drove two hundred miles to operate from a county nobody had worked.",
        "Once the sun goes down, the lower bands come alive with distant stations.",
        "Do you have a spare battery, or should we shut down the radio now?",
        "The new voice keyer plays my message and keys the radio automatically.",
        "Before you transmit, always listen to make sure the frequency is clear.",
        "Quiet nights in winter are the best time to work weak signals.",
        "Over the years the hobby has taught me patience, curiosity, and humility.",
        "We worked every state in the union before lunch on Saturday.",
        "Fifty watts and a wire can make surprising contacts when the band cooperates.",
        "I heard you calling, but you were barely above the noise.",
        "The weather station reported light winds and clear skies all night.",
        "Please read back the number once more so I can write it down.",
        "A little patience goes a long way when the pileup is this deep.",
        "He walked the length of the field, checking every guy wire twice.",
        "The older rig had a warm sound, and its dial glowed softly in the dark.",
        "If you can hear me, please give me a quick call on the repeater.",
        "Many hands make light work when it is time to raise the mast.",
        "The county line is just beyond the bridge, about a mile down the road.",
        "Use the headphones, because the speaker will feed back into the microphone.",
        "Please call again with your full callsign, I only caught part of it.",
        "Seven days of rain left the whole campsite soaked and muddy.",
        "Her voice was calm and clear, even with a hundred stations calling at once.",
    ];

    private static readonly string[] Phrases =
    [
        "CQ contest, CQ contest, this is {C} calling CQ.",
        "QRZ, this is {C}.",
        "Thank you, seventy three, {C}, QRZ?",
        "Roger, thank you, QRZ.",
        "Again, again.",
        "Your call again?",
        "Say again your exchange.",
        "You are five nine, thank you.",
        "Five nine, five nine, thank you very much.",
        "Please repeat the number.",
        "Serial, serial.",
        "Exchange, exchange.",
        "State, state.",
        "Section, section.",
        "County, county.",
        "Station calling, please go ahead.",
        "Only the last two letters of the call, please.",
        "Is this frequency in use?",
        "Sorry, the frequency is busy.",
        "Good luck in the contest, seventy three.",
        "Q S L, thank you.",
        "Standing by.",
        "Break, break.",
        "Kilo One Alpha Bravo Charlie, you are five nine.",
        "Whiskey Two Romeo, Whiskey Two Romeo, calling CQ New York.",
        "New York, New York, Whiskey Two Romeo.",
        "Frequency fourteen point two five zero megahertz.",
        "Operating on seven point two zero zero megahertz.",
        "Listening on three point eight five zero.",
        "CQ on twenty one point three zero zero megahertz.",
        "Serial number one hundred and twenty three.",
        "Serial number two hundred and forty five.",
        "Serial number three hundred and sixty seven.",
        "Serial number four hundred and eighty nine.",
        "Serial number five hundred and one.",
        "Serial number six hundred and twelve.",
        "Serial number seven hundred and thirty four.",
        "Serial number eight hundred and fifty six.",
        "Serial number nine hundred and ninety nine.",
        "Kilo Two Alpha Bravo, stroke portable.",
        "Whiskey One Alpha, slash mobile.",
        "November Three Charlie Delta, stroke Papa.",
        "Alpha Four Bravo Charlie, slash Quebec Romeo Papa.",
        "Five nine zero zero one.",
        "Five nine zero one two.",
        "Five nine one two three.",
        "Five nine two three four.",
        "Five nine three four five.",
        "Five nine four five six.",
        "Five nine five six seven.",
        "Five nine six seven eight.",
        "Five nine seven eight nine.",
        "Five nine eight nine zero.",
    ];

    private static readonly string[] SampleCalls =
    [
        "K5JSG", "W2R", "N1MM", "K2AB", "W3CD", "N1EF", "K8GH", "W9IJ", "KA1BC", "WB2DE", "N4FG", "AA5HI",
        "K7JK", "W6LM", "N0NO", "KD9PQ", "WA3RS", "K1TU", "N5VW", "W8XY", "KB6ZA", "AB4CD", "K9EF", "W0GH",
    ];

    /// <summary>Builds the full reading list (about 250 lines, 15 to 20 minutes of speech).</summary>
    public static IReadOnlyList<string> Build()
    {
        var prompts = new List<string>();
        prompts.AddRange(General);

        string[] spelledCalls = SampleCalls.Select(NatoPhonetic.Spell).Select(s => s.Replace(",", string.Empty)).ToArray();
        prompts.AddRange(Phrases.Select((p, i) => p.Replace("{C}", spelledCalls[i % spelledCalls.Length])));

        // Callsigns in phonetics, one per line, rotated so every letter and digit is covered several times.
        foreach (string call in SampleCalls)
        {
            prompts.Add(NatoPhonetic.Spell(call).Replace(",", string.Empty) + ".");
        }

        string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        for (int i = 0; i < 26; i += 5)
        {
            prompts.Add(NatoPhonetic.Spell(letters.Substring(i, Math.Min(5, 26 - i))).Replace(",", string.Empty) + ".");
        }

        for (int i = 0; i < 10; i += 5)
        {
            prompts.Add(NatoPhonetic.Spell("0123456789".Substring(i, 5)).Replace(",", string.Empty) + ".");
        }

        // Callsigns with report numbers, as spoken in a contest.
        for (int i = 0; i < spelledCalls.Length; i++)
        {
            prompts.Add($"{spelledCalls[i]}, you are five nine {NumberWords(i * 37 + 12)}.");
        }

        return prompts;
    }

    private static string NumberWords(int number)
    {
        string[] digits = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
        return string.Join(' ', number.ToString().Select(c => digits[c - '0']));
    }
}
