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
            zip.CreateEntryFromFile(wav, "wavs/" + Path.GetFileName(wav));
        }

        return zipPath;
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
