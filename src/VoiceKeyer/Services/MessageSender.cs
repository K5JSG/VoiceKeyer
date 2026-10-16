using VoiceKeyer.Audio;
using VoiceKeyer.Macros;
using VoiceKeyer.Tts;

namespace VoiceKeyer.Services;

/// <summary>Runs a function-key message end to end: expand macros, synthesize speech, key the radio, play, unkey.</summary>
public sealed class MessageSender : IMacroContext
{
    private readonly AppSettings _settings;
    private readonly Func<string> _readCallsign;
    private readonly Action _clearCallsign;
    private readonly PiperSynthesizer _synthesizer;
    private CancellationTokenSource? _current;
    private string _entryCall = string.Empty;

    /// <param name="readCallsign">Reads the callsign box on the bar (called on the UI thread).</param>
    /// <param name="clearCallsign">Clears the callsign box (the {WIPE} macro; called on the UI thread).</param>
    public MessageSender(AppSettings settings, PiperSynthesizer synthesizer, Func<string> readCallsign, Action clearCallsign)
    {
        _settings = settings;
        _synthesizer = synthesizer;
        _readCallsign = readCallsign;
        _clearCallsign = clearCallsign;
    }

    public bool IsSending { get; private set; }

    public string MyCall => _settings.MyCall;

    public string Operator => _settings.Operator;

    public string EntryCall => _entryCall;

    public string RoverQth => _settings.RoverQth;

    public string WavFolder => _settings.WavFolder;

    public double DefaultLengthScale => _settings.DefaultLengthScale;

    /// <summary>Raised with a short status line ("Speaking...", errors, warnings).</summary>
    public event Action<string>? Status;

    /// <summary>Raised on the calling (UI) thread when a message starts (true) or finishes (false).</summary>
    public event Action<bool>? SendingChanged;

    public void Stop() => _current?.Cancel();

    /// <summary>Sends a message. A message already playing is stopped first.</summary>
    public async Task SendAsync(string message, bool key = true)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _current = cts;
        IsSending = true;
        SendingChanged?.Invoke(true);
        try
        {
            _entryCall = _readCallsign().Trim().ToUpperInvariant();
            var actions = MacroEngine.Expand(message, this);

            Status?.Invoke("Rendering voice...");
            var files = new List<string>();
            foreach (var action in actions)
            {
                switch (action)
                {
                    case SpeakAction speak:
                        files.Add(await _synthesizer.RenderAsync(speak, cts.Token).ConfigureAwait(true));
                        break;
                    case WavAction wav when File.Exists(wav.Path):
                        files.Add(wav.Path);
                        break;
                    case WavAction wav:
                        Status?.Invoke($"Missing wav: {wav.Path}");
                        break;
                    case CommandAction { Name: "WIPE" }:
                        _clearCallsign();
                        break;
                    case WarningAction warning:
                        Status?.Invoke(warning.Message);
                        break;
                }
            }

            if (files.Count == 0)
            {
                if (!actions.OfType<WarningAction>().Any())
                {
                    Status?.Invoke("Nothing to send: the message has no {TTS ...} or .wav");
                }

                return;
            }

            Status?.Invoke("Sending...");
            await PlayKeyedAsync(files, key, cts.Token).ConfigureAwait(true);
            Status?.Invoke("Ready");
        }
        catch (OperationCanceledException)
        {
            Status?.Invoke("Stopped");
        }
        catch (Exception ex)
        {
            Status?.Invoke(ex.Message);
        }
        finally
        {
            // A newer send may already have replaced this one; only the latest owns the "sending" state.
            if (ReferenceEquals(_current, cts))
            {
                _current = null;
                IsSending = false;
                SendingChanged?.Invoke(false);
            }

            cts.Dispose();
        }
    }

    /// <summary>Renders every speakable phrase in the macro file ahead of time so the first press is instant.</summary>
    public async Task PrerenderAsync(IEnumerable<string> messages, CancellationToken cancellationToken)
    {
        try
        {
            foreach (string message in messages)
            {
                foreach (var speak in MacroEngine.Expand(message, this).OfType<SpeakAction>())
                {
                    await _synthesizer.RenderAsync(speak, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            // Pre-rendering is only an optimisation; the real send reports any problem.
        }
    }

    private async Task PlayKeyedAsync(List<string> files, bool key, CancellationToken cancellationToken)
    {
        using var ptt = new Ptt(_settings);
        try
        {
            if (key)
            {
                ptt.Key();
                await Task.Delay(_settings.PttLeadMs, cancellationToken).ConfigureAwait(false);
            }

            await ClipPlayer.PlayAsync(files, _settings.OutputDevice, cancellationToken, () => _settings.VolumeGain).ConfigureAwait(false);
        }
        finally
        {
            if (key)
            {
                await Task.Delay(_settings.PttTailMs).ConfigureAwait(false);
            }

            ptt.Unkey();
        }
    }
}
