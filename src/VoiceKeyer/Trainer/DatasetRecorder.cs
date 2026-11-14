using System.IO.Compression;
using System.Text;
using NAudio.Wave;

namespace VoiceKeyer.Trainer;

public sealed record ClipQuality(double Seconds, double PeakDb, double NoiseFloorDb, IReadOnlyList<string> Warnings)
{
    public bool IsGood => Warnings.Count == 0;
}

/// <summary>Stores recorded prompts as a Piper training dataset: wavs/NNNN.wav plus metadata.csv ("file|text").</summary>
public sealed class DatasetRecorder
{
    public const int SampleRate = 22050;

    private readonly string _folder;

    public DatasetRecorder(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(WavFolder);
    }

    public string WavFolder => Path.Combine(_folder, "wavs");

    public string MetadataPath => Path.Combine(_folder, "metadata.csv");

    public string PromptsPath => Path.Combine(_folder, "prompts.txt");

    public string ClipPath(int index) => Path.Combine(WavFolder, $"{index + 1:0000}.wav");

    public bool HasClip(int index) => File.Exists(ClipPath(index));

    /// <summary>Converts raw 16-bit mono PCM at <paramref name="sourceRate"/> to 22.05 kHz, trims silence and saves it.</summary>
    public ClipQuality Save(int index, byte[] pcm16Mono, int sourceRate)
    {
        float[] samples = ToFloat(pcm16Mono);
        samples = Resample(samples, sourceRate);
        samples = TrimSilence(samples);

        double peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
        double seconds = (double)samples.Length / SampleRate;
        double noise = NoiseFloor(samples);
        var warnings = new List<string>();
        if (seconds < 0.8)
        {
            warnings.Add("Too short - did the recording start late?");
        }

        if (seconds > 15)
        {
            warnings.Add("Too long (over 15 s) - read one prompt at a time.");
        }

        if (peak >= 0.99)
        {
            warnings.Add("Clipping - lower the microphone level.");
        }
        else if (ToDb(peak) < -30)
        {
            warnings.Add("Very quiet - raise the microphone level or move closer.");
        }

        if (ToDb(noise) > -50)
        {
            warnings.Add("Noisy background - find a quieter spot.");
        }

        using (var writer = new WaveFileWriter(ClipPath(index), new WaveFormat(SampleRate, 16, 1)))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return new ClipQuality(seconds, ToDb(peak), ToDb(noise), warnings);
    }

    public void Delete(int index)
    {
        if (HasClip(index))
        {
            File.Delete(ClipPath(index));
        }
    }

    /// <summary>Writes metadata.csv for every prompt that has a recording, and the prompt list itself.</summary>
    public (int Clips, TimeSpan Duration) WriteMetadata(IReadOnlyList<string> prompts)
    {
        var lines = new List<string>();
        var total = TimeSpan.Zero;
        for (int i = 0; i < prompts.Count; i++)
        {
            if (!HasClip(i))
            {
                continue;
            }

            using var reader = new WaveFileReader(ClipPath(i));
            total += reader.TotalTime;
            lines.Add($"{Path.GetFileName(ClipPath(i))}|{prompts[i].Replace('|', ' ')}");
        }

        File.WriteAllLines(MetadataPath, lines, new UTF8Encoding(false));
        File.WriteAllLines(PromptsPath, prompts, new UTF8Encoding(false));
        return (lines.Count, total);
    }

    /// <summary>Zips wavs/ and metadata.csv for upload to the Colab trainer.</summary>
    public string Export(IReadOnlyList<string> prompts, string voiceName)
    {
        WriteMetadata(prompts);
        string zipPath = Path.Combine(_folder, $"{voiceName}_dataset.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(MetadataPath, "metadata.csv");
        foreach (string wav in Directory.EnumerateFiles(WavFolder, "*.wav"))
        {
            // The recordings on disk stay as they were; only the copies in the zip are raised to a healthy level.
            var entry = zip.CreateEntry("wavs/" + Path.GetFileName(wav), CompressionLevel.Optimal);
            using var stream = entry.Open();
            byte[] bytes = LevelledWav(wav);
            stream.Write(bytes, 0, bytes.Length);
        }

        return zipPath;
    }

    /// <summary>Peak the exported clips are raised to (about -3 dBFS); clips already at or above it are left alone.</summary>
    public const double TargetPeak = 0.7;

    /// <summary>Most gain a clip may get (18 dB), so a nearly empty clip is not turned into loud noise.</summary>
    public const double MaxGainDb = 18;

    /// <summary>
    /// Scales a quiet clip up so its loudest sample reaches <see cref="TargetPeak"/>. Nothing else is changed:
    /// no EQ, no noise removal. A headset on a plain USB sound adapter records low, and Piper trains better on
    /// clips at a normal level.
    /// </summary>
    public static float[] LevelUp(float[] samples)
    {
        double peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
        if (peak <= 1e-4 || peak >= TargetPeak)
        {
            return samples;
        }

        double gain = Math.Min(TargetPeak / peak, Math.Pow(10, MaxGainDb / 20));
        var scaled = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            scaled[i] = (float)Math.Clamp(samples[i] * gain, -1.0, 1.0);
        }

        return scaled;
    }

    /// <summary>Corner of the gentle rumble filter applied to exported clips.</summary>
    public const double HighPassHz = 75;

    /// <summary>
    /// Second-order Butterworth high-pass (12 dB/octave) that removes desk and mouth-pop rumble below speech,
    /// so it neither wastes level headroom nor gets learned by the voice. Applied before the peak is measured.
    /// </summary>
    public static float[] HighPass(float[] samples)
    {
        double w0 = 2 * Math.PI * HighPassHz / SampleRate;
        double alpha = Math.Sin(w0) / (2 * Math.Sqrt(0.5));
        double cos = Math.Cos(w0);
        double a0 = 1 + alpha;
        double b0 = (1 + cos) / 2 / a0;
        double b1 = -(1 + cos) / a0;
        double b2 = b0;
        double a1 = -2 * cos / a0;
        double a2 = (1 - alpha) / a0;

        var output = new float[samples.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double x = samples[i];
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            output[i] = (float)y;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
        }

        return output;
    }

    private static byte[] LevelledWav(string path)
    {
        float[] samples;
        using (var reader = new WaveFileReader(path))
        {
            var provider = reader.ToSampleProvider();
            var all = new List<float>((int)(reader.Length / 2));
            var buffer = new float[4096];
            int read;
            while ((read = provider.Read(buffer)) > 0)
            {
                all.AddRange(buffer.AsSpan(0, read).ToArray());
            }

            samples = all.ToArray();
        }

        samples = LevelUp(HighPass(samples));
        using var memory = new MemoryStream(44 + samples.Length * 2);
        using (var writer = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true))
        {
            int dataBytes = samples.Length * 2;
            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataBytes);
            foreach (float sample in samples)
            {
                writer.Write((short)Math.Clamp(Math.Round(sample * 32767.0), -32768, 32767));
            }
        }

        return memory.ToArray();
    }

    private static float[] ToFloat(byte[] pcm)
    {
        var samples = new float[pcm.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        }

        return samples;
    }

    /// <summary>Windowed-sinc resampler (Hann window, 16 zero crossings) - low-passes below the new Nyquist to avoid aliasing.</summary>
    private static float[] Resample(float[] samples, int sourceRate)
    {
        if (sourceRate == SampleRate)
        {
            return samples;
        }

        const int zeroCrossings = 16;
        double ratio = (double)sourceRate / SampleRate;
        double cutoff = Math.Min(1.0, 1.0 / ratio) * 0.97;
        double halfWidth = zeroCrossings / cutoff;
        var output = new float[(int)(samples.Length / ratio)];
        for (int n = 0; n < output.Length; n++)
        {
            double center = n * ratio;
            int from = Math.Max(0, (int)Math.Ceiling(center - halfWidth));
            int to = Math.Min(samples.Length - 1, (int)Math.Floor(center + halfWidth));
            double sum = 0;
            for (int i = from; i <= to; i++)
            {
                double x = i - center;
                double sinc = Math.Abs(x) < 1e-9 ? 1 : Math.Sin(Math.PI * cutoff * x) / (Math.PI * cutoff * x);
                double window = 0.5 * (1 + Math.Cos(Math.PI * x / halfWidth));
                sum += samples[i] * sinc * window;
            }

            output[n] = (float)(sum * cutoff);
        }

        return output;
    }

    /// <summary>Keeps 150 ms of room tone either side of the speech, as training prefers a little silence.</summary>
    private static float[] TrimSilence(float[] samples)
    {
        const float threshold = 0.012f;
        int first = Array.FindIndex(samples, s => Math.Abs(s) > threshold);
        int last = Array.FindLastIndex(samples, s => Math.Abs(s) > threshold);
        if (first < 0)
        {
            return samples;
        }

        int pad = SampleRate * 150 / 1000;
        first = Math.Max(0, first - pad);
        last = Math.Min(samples.Length - 1, last + pad);
        return samples[first..(last + 1)];
    }

    /// <summary>RMS of the quietest 100 ms window - a cheap estimate of background noise.</summary>
    private static double NoiseFloor(float[] samples)
    {
        int window = SampleRate / 10;
        if (samples.Length < window)
        {
            return 0;
        }

        double quietest = double.MaxValue;
        for (int start = 0; start + window <= samples.Length; start += window)
        {
            double sum = 0;
            for (int i = start; i < start + window; i++)
            {
                sum += samples[i] * samples[i];
            }

            quietest = Math.Min(quietest, Math.Sqrt(sum / window));
        }

        return quietest;
    }

    private static double ToDb(double linear) => linear <= 1e-9 ? -90 : 20 * Math.Log10(linear);
}
