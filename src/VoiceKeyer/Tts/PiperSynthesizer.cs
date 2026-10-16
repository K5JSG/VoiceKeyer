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
public sealed class PiperSynthesizer
{
    private readonly string _cacheFolder;

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

        string temp = path + ".tmp";
        await RunPiperAsync(action, temp, cancellationToken).ConfigureAwait(false);
        WavTrimmer.TrimSilence(temp, path);
        File.Delete(temp);
        return path;
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
