using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace VoiceKeyer.UI;

/// <summary>
/// System-wide hotkeys (Win32 RegisterHotKey), so a key works whichever program has the focus.
/// A registered key is taken from every other program until it is unregistered.
/// </summary>
public sealed class GlobalHotKeys : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();

    public GlobalHotKeys(IntPtr windowHandle)
    {
        _handle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle) ?? throw new InvalidOperationException("Window has no handle yet.");
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a key with no modifiers. Returns false if another program already owns it.</summary>
    public bool Register(int id, int virtualKey, Action action)
    {
        if (_actions.ContainsKey(id))
        {
            return true;
        }

        if (!RegisterHotKey(_handle, id, ModNoRepeat, (uint)virtualKey))
        {
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void Unregister(int id)
    {
        if (_actions.Remove(id))
        {
            UnregisterHotKey(_handle, id);
        }
    }

    public void UnregisterAll()
    {
        foreach (int id in _actions.Keys.ToList())
        {
            Unregister(id);
        }
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
