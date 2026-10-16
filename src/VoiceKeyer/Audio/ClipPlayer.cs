using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceKeyer.Audio;

/// <summary>Plays wav files in order to a chosen output device.</summary>
public static class ClipPlayer
{
    /// <summary>Lists output device names as Windows reports them (MME names are truncated to 31 characters).</summary>
    public static IReadOnlyList<string> OutputDevices() =>
        Enumerable.Range(0, WaveOut.DeviceCount).Select(i => WaveOut.GetCapabilities(i).ProductName).ToList();

    public static IReadOnlyList<string> InputDevices() =>
        Enumerable.Range(0, WaveIn.DeviceCount).Select(i => WaveIn.GetCapabilities(i).ProductName).ToList();

    /// <summary>Finds a device by name (exact, then prefix, since saved names may be truncated). -1 = system default.</summary>
    public static int ResolveDevice(IReadOnlyList<string> devices, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return -1;
        }

        int exact = devices.ToList().FindIndex(d => d.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0)
        {
            return exact;
        }

        return devices.ToList().FindIndex(d =>
            d.StartsWith(name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Common format every clip is converted to, so a whole message plays as one continuous stream.</summary>
    private const int OutputRate = 48000;

    /// <param name="volume">Gain from 0 to 1, read continuously so a change applies while playing. Null = full volume.</param>
    /// <remarks>
    /// All clips go through ONE output device and stream. Opening the device again for each clip leaves an audible gap
    /// (and a click) between phrases, which made multi-part messages sound choppy.
    /// </remarks>
    public static async Task PlayAsync(IEnumerable<string> files, string deviceName, CancellationToken cancellationToken, Func<float>? volume = null)
    {
        int device = ResolveDevice(OutputDevices(), deviceName);
        var readers = new List<AudioFileReader>();
        try
        {
            var parts = new List<ISampleProvider>();
            foreach (string file in files)
            {
                var reader = new AudioFileReader(file);
                readers.Add(reader);
                parts.Add(ToOutputFormat(reader));
            }

            if (parts.Count == 0)
            {
                return;
            }

            var gained = new LiveGain(new ConcatenatingSampleProvider(parts), volume ?? (() => 1f));
            using var output = new WaveOut { DeviceNumber = device, BufferMilliseconds = 60, NumberOfBuffers = 3 };
            var finished = new TaskCompletionSource();
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    finished.TrySetException(args.Exception);
                }
                else
                {
                    finished.TrySetResult();
                }
            };

            output.Init(gained);
            using var registration = cancellationToken.Register(() => output.Stop());
            output.Play();
            await finished.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }
    }

    /// <summary>Stereo at <see cref="OutputRate"/>, whatever the clip was recorded as (Piper clips are 22.05 kHz mono).</summary>
    private static ISampleProvider ToOutputFormat(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 1)
        {
            source = new MonoToStereoSampleProvider(source);
        }

        return source.WaveFormat.SampleRate == OutputRate ? source : new WdlResamplingSampleProvider(source, OutputRate);
    }
}

/// <summary>Scales samples by a gain that is re-read on every buffer, so the volume control works mid-message.</summary>
internal sealed class LiveGain(ISampleProvider source, Func<float> gain) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int read = source.Read(buffer);
        float factor = Math.Clamp(gain(), 0f, 1f);
        if (factor < 1f)
        {
            for (int i = 0; i < read; i++)
            {
                buffer[i] *= factor;
            }
        }

        return read;
    }
}
