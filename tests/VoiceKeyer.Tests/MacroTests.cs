using VoiceKeyer.Macros;

namespace VoiceKeyer.Tests;

public sealed class MacroTests
{
    private sealed class FakeContext : IMacroContext
    {
        public string MyCall { get; init; } = "W2R";
        public string Operator { get; init; } = "K5JSG";
        public string EntryCall { get; init; } = "K2AB";
        public string RoverQth { get; init; } = "MON";
        public IReadOnlyList<string> Parks { get; init; } = ["K-1234", "K-5678"];
        public string SentRst { get; init; } = string.Empty;
        public string WavFolder { get; init; } = @"C:\Keyer\Wav";
        public double DefaultLengthScale { get; init; } = 1.0;
    }

    private const string Sample = """
        # header
        F1 CQ,{TTSParms 0.68}{TTS C-Q {MYCALL}}
        F2 Exch,{OPERATOR}\Cq.wav
        F3 Spare,
        # S&P
        F1 S&&P,{TTS hello, there}
        F2 Wipe,{WIPE}
        """;

    [Fact]
    public void Parse_SplitsRunAndSearchAndPounceBlocks()
    {
        var file = MacroFile.Parse(Sample);

        Assert.Equal("CQ", file.Get(OperatingMode.Run, 1).Label);
        Assert.Equal(@"{OPERATOR}\Cq.wav", file.Get(OperatingMode.Run, 2).Message);
        Assert.True(file.Get(OperatingMode.Run, 3).IsEmpty);
        Assert.Equal("{TTS hello, there}", file.Get(OperatingMode.SearchAndPounce, 1).Message);
    }

    [Fact]
    public void DisplayLabel_CollapsesDoubleAmpersand()
    {
        var file = MacroFile.Parse(Sample);

        Assert.Equal("S&P", file.Get(OperatingMode.SearchAndPounce, 1).DisplayLabel);
    }

    [Fact]
    public void Set_ReplacesOnlyTheTargetLine()
    {
        var file = MacroFile.Parse(Sample);

        file.Set(OperatingMode.Run, 3, "TU", "{TTS Thanks}");

        Assert.Equal("TU", file.Get(OperatingMode.Run, 3).Label);
        Assert.Equal("{TTS Thanks}", file.Get(OperatingMode.Run, 3).Message);
        Assert.Equal("CQ", file.Get(OperatingMode.Run, 1).Label);
        Assert.Equal("Wipe", file.Get(OperatingMode.SearchAndPounce, 2).Label);
    }

    [Fact]
    public void Expand_TtsWithParmsAndVariables()
    {
        var actions = MacroEngine.Expand("{TTSParms 0.68}{TTS C-Q {MYCALL}}", new FakeContext());

        var speak = Assert.IsType<SpeakAction>(Assert.Single(actions));
        Assert.Equal("C-Q W2R", speak.Text);
        Assert.Equal(0.68, speak.LengthScale);
    }

    [Fact]
    public void Expand_IcaoOfEntryCallAndLiteral()
    {
        var actions = MacroEngine.Expand("{TTS{ICAO !}}{TTS {ICAO ALL} & {ICAO CAT}}", new FakeContext());

        Assert.Equal("Kilo Two Alpha Bravo", ((SpeakAction)actions[0]).Text);
        Assert.Equal("Alpha Lima Lima & Charlie Alpha Tango", ((SpeakAction)actions[1]).Text);
    }

    [Fact]
    public void Expand_ParkMacros()
    {
        var actions = MacroEngine.Expand("{TTS into park {ICAO {PARK}} and {PARK2}, all {PARKS}}", new FakeContext());

        Assert.Equal("into park Kilo One Two Three Four and K five six seven eight, all K one two three four, K five six seven eight",
            ((SpeakAction)Assert.Single(actions)).Text);
    }

    [Fact]
    public void Expand_ParkIsSpokenDigitByDigit()
    {
        var actions = MacroEngine.Expand("{TTS park number {PARK}}", new FakeContext { Parks = ["US-2038"] });

        Assert.Equal("park number U S two zero three eight", ((SpeakAction)Assert.Single(actions)).Text);
    }

    [Fact]
    public void Expand_SentRstFromHrdOrDefault59()
    {
        Assert.Equal("You are five seven",
            ((SpeakAction)Assert.Single(MacroEngine.Expand("{TTS You are {SENTRST}}", new FakeContext { SentRst = "57" }))).Text);
        Assert.Equal("You are five nine",
            ((SpeakAction)Assert.Single(MacroEngine.Expand("{TTS You are {SENTRST}}", new FakeContext()))).Text);
        Assert.Equal("You are five nine plus 10",
            ((SpeakAction)Assert.Single(MacroEngine.Expand("{TTS You are {SENTRST}}", new FakeContext { SentRst = "59+10dB" }))).Text);
        Assert.Equal("You are five nine plus 20",
            ((SpeakAction)Assert.Single(MacroEngine.Expand("{TTS You are {SENTRST}}", new FakeContext { SentRst = "59 +20 DB" }))).Text);
        Assert.Equal("You are five nine plus",
            ((SpeakAction)Assert.Single(MacroEngine.Expand("{TTS You are {SENTRST}}", new FakeContext { SentRst = "59+" }))).Text);
    }

    [Fact]
    public void Icao_PausesBetweenSeparatedItems()
    {
        Assert.Equal("Kilo One, Kilo Two", NatoPhonetic.Spell("K1, K2"));
    }

    [Fact]
    public void Icao_SaysPopPawForPapaInsideACallButNotAfterStroke()
    {
        Assert.Equal("Kilo Delta Two Pop-paw Quebec Pop-paw", NatoPhonetic.Spell("KD2PQP"));
        Assert.Equal("Whiskey Zero Pop-paw Echo", NatoPhonetic.Spell("W0PE"));
        Assert.Equal("Kilo Five Juliet Sierra Golf Stroke Papa", NatoPhonetic.Spell("K5JSG/P"));
    }

    [Fact]
    public void Expand_WavPathResolvesAgainstWavFolder()
    {
        var actions = MacroEngine.Expand(@"{OPERATOR}\Cq.wav", new FakeContext());

        var wav = Assert.IsType<WavAction>(Assert.Single(actions));
        Assert.Equal(@"C:\Keyer\Wav\K5JSG\Cq.wav", wav.Path);
    }

    [Fact]
    public void Expand_WipeIsACommandAndUnknownMacroWarns()
    {
        var actions = MacroEngine.Expand("{WIPE}{UNKNOWN}", new FakeContext());

        Assert.Equal("WIPE", Assert.IsType<CommandAction>(actions[0]).Name);
        Assert.IsType<WarningAction>(actions[1]);
    }

    [Fact]
    public void Expand_MissingClosingBraceStillSpeaksAndWarns()
    {
        var actions = MacroEngine.Expand("{TTSParms 0.68}{TTS C-Q {MYCALL} STANDING BY", new FakeContext());

        Assert.IsType<WarningAction>(actions[0]);
        var speak = Assert.IsType<SpeakAction>(Assert.Single(actions.OfType<SpeakAction>()));
        Assert.Equal("C-Q W2R STANDING BY", speak.Text);
    }

    [Fact]
    public void Expand_TtsParmsAppliesToLaterSpeechOnly()
    {
        var actions = MacroEngine.Expand("{TTS one}{TTSParms 0.5}{TTS two}", new FakeContext { DefaultLengthScale = 0.9 });

        Assert.Equal(0.9, ((SpeakAction)actions[0]).LengthScale);
        Assert.Equal(0.5, ((SpeakAction)actions[1]).LengthScale);
    }
}
