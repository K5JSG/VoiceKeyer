using NAudio.Wave;

namespace VoiceKeyer.Audio;

/// <summary>Removes leading and trailing silence from 16-bit PCM wav files.</summary>
public static class WavTrimmer
{
    /// <param name="threshold">Absolute sample value (of 32768) below which audio counts as silence.</param>
    /// <param name="padMilliseconds">Silence kept on each side so words are not clipped.</param>
    public static void TrimSilence(string inputPath, string outputPath, short threshold = 250, int padMilliseconds = 60)
    {
        using var reader = new WaveFileReader(inputPath);
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16)
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            return;
        }

        byte[] bytes = new byte[reader.Length];
        int read = reader.Read(bytes, 0, bytes.Length);
        int channels = reader.WaveFormat.Channels;
        int frames = read / (2 * channels);

        int first = -1;
        int last = -1;
        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                short sample = BitConverter.ToInt16(bytes, (frame * channels + channel) * 2);
                if (Math.Abs((int)sample) > threshold)
                {
                    if (first < 0)
                    {
                        first = frame;
                    }

                    last = frame;
                    break;
                }
            }
        }

        if (first < 0)
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            return;
        }

        int pad = reader.WaveFormat.SampleRate * padMilliseconds / 1000;
        first = Math.Max(0, first - pad);
        last = Math.Min(frames - 1, last + pad);

        using var writer = new WaveFileWriter(outputPath, reader.WaveFormat);
        int blockAlign = reader.WaveFormat.BlockAlign;
        writer.Write(bytes, first * blockAlign, (last - first + 1) * blockAlign);
    }
}
