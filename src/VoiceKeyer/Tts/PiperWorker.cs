using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VoiceKeyer.Tts;

/// <summary>
/// A piper.exe kept running with its voice already loaded. Each phrase is sent as one JSON line and comes back
/// in about a tenth of a second, instead of the half second or more it takes to start piper.exe and load the voice.
/// One worker serves one voice and one set of speech settings; phrases are rendered one at a time.
/// </summary>
internal sealed class PiperWorker : IDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<string> _recentErrors = new();

    private PiperWorker(Process process)
    {
        _process = process;
    }

    public bool IsAlive => !_process.HasExited;

    public static PiperWorker Start(string piperExe, string model, double lengthScale, double? noiseScale, double? noiseW)
    {
        var start = new ProcessStartInfo(piperExe)
        {
            WorkingDirectory = Path.GetDirectoryName(piperExe)!, // piper finds espeak-ng-data next to itself
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        start.ArgumentList.Add("--model");
        start.ArgumentList.Add(model);
        start.ArgumentList.Add("--json-input");
        start.ArgumentList.Add("--length_scale");
        start.ArgumentList.Add(lengthScale.ToString("0.###", CultureInfo.InvariantCulture));
        if (noiseScale is double noise)
        {
            start.ArgumentList.Add("--noise_scale");
            start.ArgumentList.Add(noise.ToString("0.###", CultureInfo.InvariantCulture));
        }

        if (noiseW is double width)
        {
            start.ArgumentList.Add("--noise_w");
            start.ArgumentList.Add(width.ToString("0.###", CultureInfo.InvariantCulture));
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start piper.exe.");
        var worker = new PiperWorker(process);
        process.ErrorDataReceived += (_, args) => worker.RememberError(args.Data);
        process.BeginErrorReadLine();
        return worker;
    }

    /// <summary>Renders <paramref name="text"/> into <paramref name="outputFile"/>. Throws if piper stops or does not answer.</summary>
    public async Task RenderAsync(string text, string outputFile, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException("piper.exe stopped: " + RecentErrors());
            }

            File.Delete(outputFile);
            string request = JsonSerializer.Serialize(new { text, output_file = outputFile });
            await _process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Piper prints the finished file's path as soon as it is written.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            string? reply;
            try
            {
                reply = await _process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("piper.exe did not answer in 30 seconds.");
            }

            if (reply is null || !File.Exists(outputFile))
            {
                throw new InvalidOperationException("piper.exe did not produce the voice: " + RecentErrors());
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
    }

    private void RememberError(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_recentErrors)
        {
            _recentErrors.Enqueue(line);
            while (_recentErrors.Count > 6)
            {
                _recentErrors.Dequeue();
            }
        }
    }

    private string RecentErrors()
    {
        lock (_recentErrors)
        {
            return string.Join(" ", _recentErrors);
        }
    }
}
