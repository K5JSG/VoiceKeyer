using VoiceKeyer.Macros;

namespace VoiceKeyer.Trainer;

/// <summary>
/// The sentences a speaker reads to train a voice: general English for natural prosody, plus the phonetics,
/// numbers and contest phrases a radio operator's macros actually use. The list is built around the speaker's own
/// call sign (from Settings) so anyone can train a voice that says their own call and other people's calls well.
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

    /// <summary>
    /// Other stations' calls, each spelled in phonetics on its own line and used in reports. Between them every letter
    /// and digit is covered several times, in the prefix, the middle and the suffix, and "Papa" is spelled in many
    /// positions: a voice trained with a call sign only ever ending in "stroke Papa" squashes a Papa inside a call to "pa".
    /// </summary>
    public static readonly string[] SampleCalls =
    [
        "K1AB", "W2CD", "N3EF", "K4GH", "W5IJ", "N6KL", "K7MN", "W8OP", "N9QR", "K0ST", "WA1UV", "KB2WX",
        "N4YZ", "AA5BC", "KD6DE", "WB7FG", "KC8HI", "N0JK", "W3LM", "K9NO", "K5X", "WA9Y", "N2XJ", "KJ3ZW",
        "W4JQ", "K8UQ", "W6VT", "N1ZX", "KF0YV", "AB4CS",
        "KD2PQP", "W0PE", "K4PQE", "N8PAP", "WP4PA", "K6QPP", "W1PPA", "AA7P", "N5P", "K2PW", "W9QPR",
        "KP4PX", "W3PG", "N7PD", "K1PA", "WB5PEP",
    ];

    /// <summary>Contest and activity names for the "CQ x, CQ x, CQ x" loops. Spell any acronym the way it is said aloud.</summary>
    private static readonly string[] CqNames =
    [
        "contest", "DX", "POTA", "Sweepstakes", "Field Day", "test", "QRP", "state QSO party",
    ];

    private static readonly string[] States =
    [
        "Alabama, Alaska, Arizona, Arkansas, California.",
        "Colorado, Connecticut, Delaware, Florida, Georgia.",
        "Hawaii, Idaho, Illinois, Indiana, Iowa.",
        "Kansas, Kentucky, Louisiana, Maine, Maryland.",
        "Massachusetts, Michigan, Minnesota, Mississippi, Missouri.",
        "Montana, Nebraska, Nevada, New Hampshire, New Jersey.",
        "New Mexico, New York, North Carolina, North Dakota, Ohio.",
        "Oklahoma, Oregon, Pennsylvania, Rhode Island, South Carolina.",
        "South Dakota, Tennessee, Texas, Utah, Vermont.",
        "Virginia, Washington, West Virginia, Wisconsin, Wyoming.",
    ];

    private static readonly string[] Reports =
    [
        "Five nine plus ten.", "Five nine plus twenty.", "Five nine, thank you.", "You are five seven.",
        "You are five nine plus.", "Your report is five nine, my report is five nine.", "Five by nine.", "Four and five.",
    ];

    private static readonly string[] ParkLines =
    [
        "You are five nine into park K one two three four.",
        "You are five nine into park K five six seven eight.",
        "You are five nine into park K nine nine zero zero.",
        "Park to park, you are five nine from park K two four six eight.",
        "Thank you for the park, seventy three.",
        "Activating park U S two zero six eight today.",
        "Activating park U S eight five four eight today.",
        "I am in park K zero zero one three, with fifty watts.",
        "Thanks for the park to park contact, good luck with the hunters.",
        "Hunters, please stand by, I am calling CQ POTA.",
    ];

    private static string Spelled(string call) =>
        NatoPhonetic.Spell(call, respellPapa: false).Replace(",", string.Empty);

    /// <summary>
    /// Builds the full reading list (about 300 lines, 18 to 22 minutes of speech). <paramref name="myCall"/> is the
    /// speaker's own call (blank gives a list without it); <paramref name="extraPhrases"/> are the speaker's own lines,
    /// such as a local contest, county names or a town that is hard to pronounce, written the way they are said aloud.
    /// The most useful lines come first, so a short session still gives a usable voice.
    /// </summary>
    public static IReadOnlyList<string> Build(string myCall = "", IEnumerable<string>? extraPhrases = null)
    {
        var prompts = new List<string>();
        string call = (myCall ?? string.Empty).Trim().ToUpperInvariant();
        string[] spelledCalls = SampleCalls.Select(Spelled).ToArray();

        // 1. The speaker's own call, which the keyer sends most.
        if (call.Length > 0)
        {
            string me = Spelled(call);
            string Other(int i) => spelledCalls[(i * 7 + 3) % spelledCalls.Length];
            prompts.Add(me + ".");
            prompts.Add($"This is {me}.");
            prompts.Add($"{me}, {me}.");
            prompts.Add($"{me}, calling CQ.");
            prompts.Add($"QRZ, this is {me}.");
            prompts.Add($"{me}, QRZ?");
            if (!call.Contains('/'))
            {
                prompts.Add($"{me} stroke Papa.");
                prompts.Add($"{me} stroke Mike.");
                prompts.Add($"{me} stroke Quebec Romeo Papa.");
                prompts.Add($"This is {me} stroke Papa.");
                prompts.Add($"{me} stroke Papa, {me} stroke Papa.");
            }

            for (int i = 0; i < CqNames.Length; i++)
            {
                string name = CqNames[i];
                string sign = call.Contains('/') || i % 2 == 0 ? me : $"{me} stroke Papa";
                prompts.Add($"CQ {name}. CQ {name}. CQ {name}. {sign}.");
                prompts.Add($"CQ {name}, CQ {name}, this is {sign}, {sign}.");
            }

            for (int i = 0; i < 6; i++)
            {
                prompts.Add($"{Other(i)}, you are five nine, thank you, {me}.");
                prompts.Add($"Thank you {Other(i + 10)}, seventy three, {me}, QRZ?");
            }

            prompts.Add($"{me}, five nine, five nine.");
            prompts.Add($"{me}, you are five nine plus ten, thank you.");
        }

        // 2. Other stations' calls: alone, then inside sentences, so every letter works at the start, middle and end.
        prompts.AddRange(spelledCalls.Select(c => c + "."));
        for (int i = 0; i < spelledCalls.Length; i++)
        {
            prompts.Add($"{spelledCalls[i]}, you are five nine {NumberWords(i * 37 + 12)}.");
            if (SampleCalls[i].Contains('P'))
            {
                prompts.Add($"Thank you {spelledCalls[i]}, seventy three.");
                prompts.Add($"{spelledCalls[i]}, go ahead, I hear you well.");
            }
        }

        // 3. Contest phrases, reports and numbers.
        prompts.AddRange(Phrases.Select((p, i) => p.Replace("{C}", spelledCalls[i % spelledCalls.Length])));
        prompts.AddRange(Reports);
        prompts.AddRange(ParkLines);

        // 4. The alphabet and digits in groups of five.
        string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        for (int i = 0; i < 26; i += 5)
        {
            prompts.Add(Spelled(letters.Substring(i, Math.Min(5, 26 - i))) + ".");
        }

        for (int i = 0; i < 10; i += 5)
        {
            prompts.Add(Spelled("0123456789".Substring(i, 5)) + ".");
        }

        prompts.AddRange(States);

        // 5. The speaker's own lines, then ordinary English for natural rhythm.
        if (extraPhrases is not null)
        {
            prompts.AddRange(extraPhrases.Select(l => l.Trim()).Where(l => l.Length > 0));
        }

        prompts.AddRange(General);
        return prompts;
    }

    private static string NumberWords(int number)
    {
        string[] digits = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
        return string.Join(' ', number.ToString().Select(c => digits[c - '0']));
    }
}
