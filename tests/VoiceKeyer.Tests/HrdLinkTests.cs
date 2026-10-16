using VoiceKeyer.Services;

namespace VoiceKeyer.Tests;

public sealed class HrdLinkTests
{
    [Fact]
    public void ReadCallsign_NeverThrows_WhetherOrNotHrdIsRunning()
    {
        string? call = HrdLink.ReadCallsign();

        // null = no HRD Add/Edit window open; otherwise the call box text (possibly blank), upper case.
        Assert.True(call is null || call == call.Trim().ToUpperInvariant());
    }
}
