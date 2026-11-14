using NAudio.Wave;

namespace VoiceKeyer.Trainer;

/// <summary>What was measured on one recorded clip.</summary>
public sealed record ClipMetrics(double Seconds, double PeakDb, double SpeechDb, double NoiseDb, double PopRatio);

/// <summary>A clip the review thinks should be recorded again, and why (plain words for the speaker).</summary>
public sealed record ReviewFinding(int Index, string Reason);

/// <summary>
/// Looks over every recorded clip and suggests the ones to record again: a "P" pop (a thump of very low
/// frequencies from a puff of air), a level far from the rest, background noise, clipping, or a clip whose length is
/// far out of line with the words read (a stumble or a skipped word).
/// </summary>
public static class ClipReview
{
    /// <summary>Corner of the low-pass that isolates the thump of a plosive, well below speech.</summary>
    public const double PopBandHz = 70;

    /// <summary>A pop is flagged when, in any loud 20 ms, this share of the energy lies below <see cref="PopBandHz"/>.</summary>
    public const double PopThreshold = 0.7;

    /// <summary>A clip is flagged when its speech level is this many dB away from the median clip.</summary>
    public const double LevelToleranceDb = 5;

    public static ClipMetrics Measure(float[] samples)
    {
        const int window = DatasetRecorder.SampleRate / 50; // 20 ms
        int count = samples.Length / window;
        if (count == 0)
        {
            return new ClipMetrics((double)samples.Length / DatasetRecorder.SampleRate, -90, -90, -90, 0);
        }

        double peak = samples.Max(Math.Abs);
        float[] low = LowPass(samples, PopBandHz);
        var rms = new double[count];
        var lowRms = new double[count];
        for (int w = 0; w < count; w++)
        {
            rms[w] = Rms(samples, w * window, window);
            lowRms[w] = Rms(low, w * window, window);
        }

        // Speech level: only the windows that carry speech, not the pauses.
        double loudest = rms.Max();
        double[] active = rms.Where(r => r > 0.2 * loudest).ToArray();
        double speech = active.Length == 0 ? 0 : Math.Sqrt(active.Average(r => r * r));

        // A pop is a window dominated by the very low band: voiced speech always has plenty above 70 Hz, a thump does not.
        double pop = 0;
        for (int w = 0; w < count; w++)
        {
            if (rms[w] > 0.25 * speech)
            {
                pop = Math.Max(pop, lowRms[w] / rms[w]);
            }
        }

        return new ClipMetrics(
            (double)samples.Length / DatasetRecorder.SampleRate, ToDb(peak), ToDb(speech), ToDb(rms.Min()), pop);
    }

    /// <summary>Reviews every clip that has a recording and returns the ones worth recording again.</summary>
    public static IReadOnlyList<ReviewFinding> Review(DatasetRecorder dataset, IReadOnlyList<string> prompts)
    {
        var measured = new List<(int Index, ClipMetrics Metrics)>();
        for (int i = 0; i < prompts.Count; i++)
        {
            if (dataset.HasClip(i))
            {
                measured.Add((i, Measure(DatasetRecorder.ReadSamples(dataset.ClipPath(i)))));
            }
        }

        return Judge(measured, prompts);
    }

    /// <summary>The judging rules, separate from reading files so they can be tested.</summary>
    public static IReadOnlyList<ReviewFinding> Judge(IReadOnlyList<(int Index, ClipMetrics Metrics)> clips, IReadOnlyList<string> prompts)
    {
        var findings = new List<ReviewFinding>();
        if (clips.Count == 0)
        {
            return findings;
        }

        double medianLevel = Median(clips.Select(c => c.Metrics.SpeechDb));
        double medianNoise = Median(clips.Select(c => c.Metrics.NoiseDb));
        double medianPace = Median(clips.Where(c => Letters(prompts[c.Index]) >= 8)
            .Select(c => c.Metrics.Seconds / Letters(prompts[c.Index])).DefaultIfEmpty(0));

        foreach (var (index, m) in clips)
        {
            var reasons = new List<string>();
            if (m.PopRatio > PopThreshold)
            {
                reasons.Add("possible pop or thump (often a puff of air on a P or B): listen to it, and if it is there, move back a little or say it softer");
            }

            if (m.PeakDb >= -1)
            {
                reasons.Add("too loud, it may clip");
            }
            else if (m.SpeechDb > medianLevel + LevelToleranceDb)
            {
                reasons.Add("much louder than your other clips");
            }
            else if (m.SpeechDb < medianLevel - LevelToleranceDb)
            {
                reasons.Add("much quieter than your other clips");
            }

            if (m.NoiseDb > -50 || m.NoiseDb > medianNoise + 10)
            {
                reasons.Add("background noise in the gaps");
            }

            if (m.Seconds < 0.8)
            {
                reasons.Add("very short, did the recording start late?");
            }

            int letters = Letters(prompts[index]);
            if (medianPace > 0 && letters >= 8)
            {
                double pace = m.Seconds / letters / medianPace;
                if (pace > 1.8)
                {
                    reasons.Add("much slower than your other clips (a stumble or a long pause?)");
                }
                else if (pace < 0.55)
                {
                    reasons.Add("much faster than your other clips (a word skipped?)");
                }
            }

            if (reasons.Count > 0)
            {
                findings.Add(new ReviewFinding(index, string.Join("; ", reasons)));
            }
        }

        return findings;
    }

    /// <summary>Writes redo.txt ("0242|reason" per line) so the trainer's redo mode steps through these clips.</summary>
    public static void WriteRedoList(string path, IReadOnlyList<ReviewFinding> findings) =>
        File.WriteAllLines(path, findings.Select(f => $"{f.Index + 1:0000}|{f.Reason}"));

    private static int Letters(string text) => text.Count(char.IsLetter);

    private static double Rms(float[] samples, int start, int length)
    {
        double sum = 0;
        for (int i = start; i < start + length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return Math.Sqrt(sum / length);
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }

    private static double ToDb(double linear) => linear <= 1e-9 ? -90 : 20 * Math.Log10(linear);

    /// <summary>Second-order Butterworth low-pass (RBJ biquad).</summary>
    private static float[] LowPass(float[] samples, double hz)
    {
        double w0 = 2 * Math.PI * hz / DatasetRecorder.SampleRate;
        double alpha = Math.Sin(w0) / (2 * Math.Sqrt(0.5));
        double cos = Math.Cos(w0);
        double a0 = 1 + alpha;
        double b0 = (1 - cos) / 2 / a0;
        double b1 = (1 - cos) / a0;
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
}
