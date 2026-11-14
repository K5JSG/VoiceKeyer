using System.IO.Compression;
using NAudio.Wave;
using VoiceKeyer.Trainer;

namespace VoiceKeyer.Tests;

public sealed class DatasetRecorderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "keyer-dataset-test-" + Guid.NewGuid().ToString("N"));

    public DatasetRecorderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void LevelUp_RaisesAQuietClipToTheTargetPeak()
    {
        float[] quiet = [0.1f, -0.29f, 0.2f, 0f];

        float[] result = DatasetRecorder.LevelUp(quiet);

        Assert.Equal(DatasetRecorder.TargetPeak, result.Max(Math.Abs), 3);
        Assert.Equal(0.1f * (float)(DatasetRecorder.TargetPeak / 0.29), result[0], 3);
    }

    [Fact]
    public void LevelUp_LeavesLoudAndSilentClipsAlone()
    {
        float[] loud = [0.8f, -0.9f];
        float[] silent = new float[100];

        Assert.Same(loud, DatasetRecorder.LevelUp(loud));
        Assert.Same(silent, DatasetRecorder.LevelUp(silent));
    }

    [Fact]
    public void LevelUp_NeverAddsMoreThanTheGainLimit()
    {
        float[] nearlyEmpty = [0.001f, -0.002f];

        float[] result = DatasetRecorder.LevelUp(nearlyEmpty);

        double limit = Math.Pow(10, DatasetRecorder.MaxGainDb / 20);
        Assert.Equal(0.002 * limit, result.Max(Math.Abs), 3);
    }

    [Fact]
    public void HighPass_RemovesRumbleButKeepsSpeechRange()
    {
        float[] rumble = Tone(40, 0.5);
        float[] voice = Tone(500, 0.5);

        double rumbleLeft = Rms(DatasetRecorder.HighPass(rumble)) / Rms(rumble);
        double voiceLeft = Rms(DatasetRecorder.HighPass(voice)) / Rms(voice);

        Assert.True(rumbleLeft < 0.4, $"40 Hz left {rumbleLeft:0.00}");
        Assert.InRange(voiceLeft, 0.97, 1.03);
    }

    private static float[] Tone(double hz, double amplitude)
    {
        var samples = new float[DatasetRecorder.SampleRate * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / DatasetRecorder.SampleRate));
        }

        return samples;
    }

    private static double Rms(float[] samples)
    {
        // Skip the first half second so the filter's start-up settles.
        int skip = DatasetRecorder.SampleRate / 2;
        return Math.Sqrt(samples.Skip(skip).Average(s => (double)s * s));
    }

    [Fact]
    public void Export_RaisesTheCopiesInTheZipButNotTheRecordings()
    {
        var recorder = new DatasetRecorder(_root);
        string clip = recorder.ClipPath(0);
        WriteQuietClip(clip, peak: 0.25);
        byte[] original = File.ReadAllBytes(clip);

        string zipPath = recorder.Export(["Kilo Five Juliet Sierra Golf stroke Papa."], "Test");

        Assert.Equal(original, File.ReadAllBytes(clip));
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("wavs/0001.wav");
        Assert.NotNull(entry);
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        using var reader = new WaveFileReader(memory);
        Assert.Equal(DatasetRecorder.SampleRate, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        var samples = reader.ToSampleProvider();
        float peak = 0;
        var buffer = new float[1024];
        int read;
        while ((read = samples.Read(buffer)) > 0)
        {
            peak = Math.Max(peak, buffer.Take(read).Max(Math.Abs));
        }

        Assert.InRange(peak, 0.69, 0.71);
        Assert.NotNull(zip.GetEntry("metadata.csv"));
    }

    private static void WriteQuietClip(string path, double peak)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(DatasetRecorder.SampleRate, 16, 1));
        var samples = new float[DatasetRecorder.SampleRate];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(peak * Math.Sin(2 * Math.PI * 220 * i / DatasetRecorder.SampleRate));
        }

        writer.WriteSamples(samples, 0, samples.Length);
    }
}
