using System.IO.Ports;
using VoiceKeyer.Services;

namespace VoiceKeyer.Audio;

/// <summary>Keys and unkeys the transmitter around a clip. Always unkeys on dispose.</summary>
public sealed class Ptt : IDisposable
{
    private readonly AppSettings _settings;
    private SerialPort? _port;

    public Ptt(AppSettings settings) => _settings = settings;

    public void Key()
    {
        if (_settings.Ptt == PttMethod.None)
        {
            return;
        }

        _port = new SerialPort(_settings.PttPort, 38400, Parity.None, 8, StopBits.One)
        {
            // Do not let the driver raise lines when it opens the port: that would key the radio early.
            RtsEnable = false,
            DtrEnable = false,
            Handshake = Handshake.None,
        };
        _port.Open();

        switch (_settings.Ptt)
        {
            case PttMethod.SerialRts:
                _port.RtsEnable = true;
                break;
            case PttMethod.SerialDtr:
                _port.DtrEnable = true;
                break;
            case PttMethod.CatCommand:
                _port.Write(_settings.CatOn);
                break;
        }
    }

    public void Unkey()
    {
        if (_port is null)
        {
            return;
        }

        try
        {
            switch (_settings.Ptt)
            {
                case PttMethod.SerialRts:
                    _port.RtsEnable = false;
                    break;
                case PttMethod.SerialDtr:
                    _port.DtrEnable = false;
                    break;
                case PttMethod.CatCommand:
                    _port.Write(_settings.CatOff);
                    break;
            }
        }
        finally
        {
            _port.Dispose();
            _port = null;
        }
    }

    public void Dispose() => Unkey();
}
