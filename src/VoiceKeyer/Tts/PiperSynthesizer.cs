using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VoiceKeyer.Audio;
using VoiceKeyer.Macros;

namespace VoiceKeyer.Tts;

/// <summary>
/// Renders text to a wav file with piper.exe (the engine bundled with Keyer, with a Piper .onnx voice model).
/// Results are cached on disk, so a phrase is only synthesized once per voice and setting.
/// </summary>
public sealed class PiperSynthesizer : IDisposable
{
    private readonly string _cacheFolder;
    private readonly Dictionary<string, Task<string>> _inFlight = new();
    private readonly List<(string Key, PiperWorker Worker)> _workers = [];
    private const int MaxWorkers = 3;

    public PiperSynthesizer(string cacheFolder)
    {
        _cacheFolder = cacheFolder;
        Directory.CreateDirectory(cacheFolder);
    }

    public string PiperExe { get; set; } = string.Empty;

    public string? ModelPath { get; set; }

    /// <summary>Returns the path of the (silence-trimmed) wav for the action, synthesizing it if not cached.</summary>
    public async Task<string> RenderAsync(SpeakAction action, CancellationToken cancellationToken)
    {
        if (!File.Exists(PiperExe))
        {
            throw new FileNotFoundException("piper.exe not found. Set its location in Settings.", PiperExe);
        }

        if (ModelPath is null || !File.Exists(ModelPath))
        {
            throw new FileNotFoundException("Voice model not found. Pick a voice in Settings.", ModelPath);
        }

        string path = Path.Combine(_cacheFolder, CacheKey(action) + ".wav");
        if (File.Exists(path))
        {
            return path;
        }

        // One render per phrase: a key pressed while the same phrase is still being prepared in the background
        // waits for that render instead of starting another. Cancelling only stops the wait; the render finishes
        // and is cached for next time.
        Task<string> render;
        lock (_inFlight)
        {
            if (!_inFlight.TryGetValue(path, out render!))
            {
                render = Task.Run(() => RenderToCacheAsync(action, path));
                _inFlight[path] = render;
            }
        }

        return await render.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RenderToCacheAsync(SpeakAction action, string path)
    {
        try
        {
            string temp = path + ".tmp";
            try
            {
                await GetWorker(action).RenderAsync(action.Text, temp, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
            {
                // The resident piper failed; drop it and render this phrase the slow way, which reports a real error.
                DropWorker(action);
                await RunPiperAsync(action, temp, CancellationToken.None).ConfigureAwait(false);
            }

            WavTrimmer.TrimSilence(temp, path);
            File.Delete(temp);
            return path;
        }
        finally
        {
            lock (_inFlight)
            {
                _inFlight.Remove(path);
            }
        }
    }

    private string WorkerKey(SpeakAction action)
    {
        var info = new FileInfo(ModelPath!);
        return string.Create(CultureInfo.InvariantCulture,
            $"{ModelPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{action.LengthScale}|{action.NoiseScale}|{action.NoiseW}");
    }

    /// <summary>The running piper for this voice and speech settings, started (and kept) on first use.</summary>
    private PiperWorker GetWorker(SpeakAction action)
    {
        string key = WorkerKey(action);
        lock (_workers)
        {
            int index = _workers.FindIndex(w => w.Key == key);
            if (index >= 0 && _workers[index].Worker.IsAlive)
            {
                return _workers[index].Worker;
            }

            if (index >= 0)
            {
                _workers[index].Worker.Dispose();
                _workers.RemoveAt(index);
            }

            while (_workers.Count >= MaxWorkers)
            {
                _workers[0].Worker.Dispose();
                _workers.RemoveAt(0);
            }

            var worker = PiperWorker.Start(PiperExe, ModelPath!, action.LengthScale, action.NoiseScale, action.NoiseW);
            _workers.Add((key, worker));
            return worker;
        }
    }

    private void DropWorker(SpeakAction action)
    {
        string key = WorkerKey(action);
        lock (_workers)
        {
            int index = _workers.FindIndex(w => w.Key == key);
            if (index >= 0)
            {
                _workers[index].Worker.Dispose();
                _workers.RemoveAt(index);
            }
        }
    }

    /// <summary>Stops the resident piper.exe processes.</summary>
    public void Dispose()
    {
        lock (_workers)
        {
            foreach (var (_, worker) in _workers)
            {
                worker.Dispose();
            }

            _workers.Clear();
        }
    }

    private string CacheKey(SpeakAction action)
    {
        var info = new FileInfo(ModelPath!);
        string key = string.Create(CultureInfo.InvariantCulture,
            $"{ModelPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{action.LengthScale}|{action.NoiseScale}|{action.NoiseW}|{action.Text}");
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private async Task RunPiperAsync(SpeakAction action, string outputFile, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(PiperExe)
        {
            WorkingDirectory = Path.GetDirectoryName(PiperExe)!, // piper finds espeak-ng-data next to itself
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        start.ArgumentList.Add("--model");
        start.ArgumentList.Add(ModelPath!);
        start.ArgumentList.Add("--output_file");
        start.ArgumentList.Add(outputFile);
        start.ArgumentList.Add("--length_scale");
        start.ArgumentList.Add(action.LengthScale.ToString("0.###", CultureInfo.InvariantCulture));
        if (action.NoiseScale is double noise)
        {
            start.ArgumentList.Add("--noise_scale");
            start.ArgumentList.Add(noise.ToString("0.###", CultureInfo.InvariantCulture));
        }

        if (action.NoiseW is double noiseW)
        {
            start.ArgumentList.Add("--noise_w");
            start.ArgumentList.Add(noiseW.ToString("0.###", CultureInfo.InvariantCulture));
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start piper.exe.");
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteLineAsync(action.Text.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0 || !File.Exists(outputFile))
        {
            string error = await stderr.ConfigureAwait(false);
            throw new InvalidOperationException($"piper.exe failed (exit {process.ExitCode}): {error.Trim()}");
        }
    }
}
