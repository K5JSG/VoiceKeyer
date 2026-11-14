using System.Diagnostics;
using NAudio.Wave;
using VoiceKeyer.Macros;
using VoiceKeyer.Services;
using VoiceKeyer.Tts;

namespace VoiceKeyer.Tests;

public sealed class PiperTests
{
    [Fact]
    public async Task RenderAsync_WithRealVoice_ProducesTrimmedWavAndCachesIt()
    {
        // The Piper engine and the stock voices are bundled with the app and copied beside the test assembly.
        string exe = AppSettings.PiperExe;
        string model = Path.Combine(AppContext.BaseDirectory, "voices", "en_US-joe-medium.onnx");
        Assert.True(File.Exists(exe), "bundled piper.exe missing");
        Assert.True(File.Exists(model), "bundled voice missing");

        string cache = Path.Combine(Path.GetTempPath(), "ssb-piper-test-" + Guid.NewGuid().ToString("N"));
        var synth = new PiperSynthesizer(cache) { PiperExe = exe, ModelPath = model };
        var action = new SpeakAction("Whiskey, Two, Romeo", 0.8);

        var watch = Stopwatch.StartNew();
        string path = await synth.RenderAsync(action, CancellationToken.None);
        long first = watch.ElapsedMilliseconds;
        watch.Restart();
        string again = await synth.RenderAsync(action, CancellationToken.None);
        long second = watch.ElapsedMilliseconds;

        double seconds;
        using (var reader = new WaveFileReader(path))
        {
            seconds = reader.TotalTime.TotalSeconds;
        }

        Assert.Equal(path, again);
        Assert.InRange(seconds, 0.8, 6);
        Assert.True(second < first / 5 + 20, $"cache hit not fast: first {first} ms, second {second} ms");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "ssb-piper-timing.txt"), $"first={first}ms second={second}ms dur={seconds:0.00}s");
        synth.Dispose();
        Directory.Delete(cache, recursive: true);
    }

    [Fact]
    public async Task RenderAsync_ResidentPiperRendersNewPhrasesQuickly()
    {
        string model = Path.Combine(AppContext.BaseDirectory, "voices", "en_US-joe-medium.onnx");
        string cache = Path.Combine(Path.GetTempPath(), "ssb-piper-warm-" + Guid.NewGuid().ToString("N"));
        using var synth = new PiperSynthesizer(cache) { PiperExe = AppSettings.PiperExe, ModelPath = model };

        await synth.RenderAsync(new SpeakAction("Warm up the voice", 0.8), CancellationToken.None);
        var watch = Stopwatch.StartNew();
        foreach (string call in new[] { "Kilo Five Juliet Sierra Golf", "Whiskey Two Romeo", "November One Mike Mike" })
        {
            await synth.RenderAsync(new SpeakAction(call + ", you are five nine", 0.8), CancellationToken.None);
        }

        long perPhrase = watch.ElapsedMilliseconds / 3;
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "ssb-piper-warm-timing.txt"), $"per phrase once loaded={perPhrase}ms");
        Assert.True(perPhrase < 800, $"resident piper too slow: {perPhrase} ms per phrase");
        synth.Dispose();
        Directory.Delete(cache, recursive: true);
    }
}
