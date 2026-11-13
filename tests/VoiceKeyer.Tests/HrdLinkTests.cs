using VoiceKeyer.Services;

namespace VoiceKeyer.Tests;

public sealed class HrdLinkTests
{
    [Fact]
    public void ReadQso_NeverThrows_WhetherOrNotHrdIsRunning()
    {
        HrdQso? qso = HrdLink.ReadQso();

        // null = no HRD Add/Edit window open; otherwise the call box text (possibly blank), upper case.
        Assert.True(qso is null || qso.Call == qso.Call.Trim().ToUpperInvariant());
    }
}
