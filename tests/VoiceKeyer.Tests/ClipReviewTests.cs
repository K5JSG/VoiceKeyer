using VoiceKeyer.Trainer;

namespace VoiceKeyer.Tests;

public sealed class ClipReviewTests
{
    private const int Rate = DatasetRecorder.SampleRate;

    // A voiced-sounding clip: a 200 Hz tone with some 800 Hz, two seconds long, at the given amplitude.
    private static float[] Speech(double amplitude, double seconds = 2)
    {
        var s = new float[(int)(Rate * seconds)];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate;
            s[i] = (float)(amplitude * (0.7 * Math.Sin(2 * Math.PI * 200 * t) + 0.3 * Math.Sin(2 * Math.PI * 800 * t)));
        }

        return s;
    }

    [Fact]
    public void Measure_FlagsALowFrequencyThumpButNotPlainSpeech()
    {
        float[] clean = Speech(0.3);
        float[] thump = Speech(0.05);
        for (int i = Rate / 2; i < Rate / 2 + Rate / 20; i++)
        {
            thump[i] += (float)(0.5 * Math.Sin(2 * Math.PI * 35 * i / Rate));
        }

        Assert.True(ClipReview.Measure(clean).PopRatio < ClipReview.PopThreshold);
        Assert.True(ClipReview.Measure(thump).PopRatio > ClipReview.PopThreshold);
    }

    [Fact]
    public void Judge_SuggestsLoudQuietNoisyAndOddlyPacedClipsOnly()
    {
        string[] prompts = Enumerable.Repeat("Kilo Five Juliet Sierra Golf", 12).ToArray();
        var clips = new List<(int, ClipMetrics)>();
        for (int i = 0; i < 12; i++)
        {
            clips.Add((i, new ClipMetrics(3.0, -10, -30, -75, 0.2)));
        }

        clips[2] = (2, new ClipMetrics(3.0, -10, -22, -75, 0.2));   // louder
        clips[4] = (4, new ClipMetrics(3.0, -10, -38, -75, 0.2));   // quieter
        clips[6] = (6, new ClipMetrics(3.0, -10, -30, -45, 0.2));   // noisy
        clips[8] = (8, new ClipMetrics(7.0, -10, -30, -75, 0.2));   // far slower
        clips[10] = (10, new ClipMetrics(3.0, -10, -30, -75, 0.9)); // thump

        var findings = ClipReview.Judge(clips, prompts);

        Assert.Equal(new[] { 2, 4, 6, 8, 10 }, findings.Select(f => f.Index));
        Assert.Contains("louder", findings[0].Reason);
        Assert.Contains("quieter", findings[1].Reason);
        Assert.Contains("noise", findings[2].Reason);
        Assert.Contains("slower", findings[3].Reason);
        Assert.Contains("pop", findings[4].Reason);
    }

    [Fact]
    public void WriteRedoList_UsesTheNumberBarReasonFormatRedoModeReads()
    {
        string path = Path.Combine(Path.GetTempPath(), "redo-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            ClipReview.WriteRedoList(path, [new ReviewFinding(241, "too quiet")]);

            Assert.Equal("0242|too quiet", File.ReadAllText(path).Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
