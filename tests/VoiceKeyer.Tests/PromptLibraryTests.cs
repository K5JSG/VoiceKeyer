using VoiceKeyer.Macros;
using VoiceKeyer.Trainer;

namespace VoiceKeyer.Tests;

public sealed class PromptLibraryTests
{
    [Theory]
    [InlineData("K5JSG")]
    [InlineData("w1aw")]
    [InlineData("VE3XYZ/P")]
    public void Build_UsesTheSpeakersOwnCallAndNobodyElses(string call)
    {
        var prompts = PromptLibrary.Build(call);

        string spelled = NatoPhonetic.Spell(call.ToUpperInvariant(), respellPapa: false).Replace(",", string.Empty);
        Assert.Contains(prompts, p => p == spelled + ".");
        Assert.True(prompts.Count(p => p.Contains(spelled)) >= 20);
    }

    [Fact]
    public void Build_WithoutACall_HasNoOwnCallLinesButStillCoversOtherCalls()
    {
        var prompts = PromptLibrary.Build();

        Assert.DoesNotContain(prompts, p => p.Contains("Juliet Sierra Golf"));
        Assert.True(prompts.Count > 200);
    }

    [Fact]
    public void Build_SpellsPapaPlainlyForTheSpeakerToRead()
    {
        Assert.DoesNotContain(PromptLibrary.Build("K5JSG"), p => p.Contains("Pop-paw"));
    }

    [Fact]
    public void Build_CoversEveryLetterAndDigitAndPapaInsideCalls()
    {
        var calls = PromptLibrary.SampleCalls;

        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
        {
            Assert.True(calls.Count(x => x.Contains(c)) >= 2, $"{c} appears in fewer than two sample calls");
        }

        Assert.True(calls.Count(x => x.IndexOf('P') is > 0 && x.IndexOf('P') < x.Length - 1) >= 8, "Papa in the middle of a call");
        Assert.True(calls.Count(x => x.EndsWith('P')) >= 4, "Papa at the end of a call");
    }

    [Fact]
    public void Build_AddsTheSpeakersOwnPhrasesBeforeGeneralEnglish()
    {
        var prompts = PromptLibrary.Build("K5JSG", ["  CQ, en why queue pee.  ", ""]);

        int mine = prompts.ToList().IndexOf("CQ, en why queue pee.");
        Assert.True(mine > 0);
        Assert.True(mine < prompts.ToList().IndexOf("The quick brown fox jumps over the lazy dog near the old barn."));
    }
}
