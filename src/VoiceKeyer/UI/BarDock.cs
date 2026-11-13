using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VoiceKeyer.UI;

public enum DockEdge
{
    None,
    Top,
    Bottom,
}

public enum DockAlign
{
    Free,
    Left,
    Centre,
    Right,
}

/// <summary>
/// Snaps the bar when it is dropped near a screen edge. Dropped near the bottom (just above the taskbar) or the top, it
/// docks there: the strip is registered with Windows as an application desktop toolbar (the same mechanism the taskbar
/// uses, but with no taskbar buttons), so maximized windows stop at the bar instead of going under it. Dropped near the
/// left or right side it only snaps against that side. Dragging the bar away gives the space back.
/// All positions are worked out in physical pixels and applied with SetWindowPos, which keeps scaled displays right.
/// </summary>
public sealed class BarDock : IDisposable
{
    /// <summary>How close (in 96-dpi units) an edge must be for the bar to snap to it.</summary>
    private const double SnapDistance = 40;

    private readonly Window _window;
    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly int _callbackMessage = RegisterWindowMessage("VoiceKeyerAppBar");
    private bool _registered;
    private bool _positioning;

    public BarDock(Window window)
    {
        _window = window;
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle) ?? throw new InvalidOperationException("Window has no handle yet.");
        _source.AddHook(WndProc);
        _window.SizeChanged += (_, _) => Reposition();
    }

    public DockEdge Edge { get; private set; } = DockEdge.None;

    public DockAlign Align { get; private set; } = DockAlign.Free;

    /// <summary>Gives the reserved space back (called before a drag, so the bar can be dropped anywhere).</summary>
    public void Undock()
    {
        Edge = DockEdge.None;
        if (_registered)
        {
            KeepIcons(() =>
            {
                var data = NewData();
                SHAppBarMessage(AbmRemove, ref data);
            });
            _registered = false;
        }
    }

    /// <summary>Finishes once the desktop icons have been put back after the last dock or undock (wait for it before exiting).</summary>
    public Task IconsSettled { get; private set; } = Task.CompletedTask;

    // Where icons were before the strip pushed them aside, so they go back there when it is given back.
    private readonly Dictionary<string, DesktopIcons.Icon> _pushedAside = [];
    // The layout being put back right now (a dock or undock in the meantime starts from it, not from Explorer's reshuffle).
    private DesktopIcons.Layout? _pending;
    private int _generation;

    /// <summary>
    /// Runs a change to the reserved strip without disturbing the desktop icons. When the work area changes, Explorer
    /// re-lays the icons, and when it grows back it restores an old saved layout, so moving icons while Voice Keyer is
    /// docked or closed sends them "all over the place". The layout is read first and put back once Explorer is done; only
    /// icons under the strip move, to the nearest clear spot, and they return to their own spot when the strip goes away.
    /// </summary>
    private void KeepIcons(Action change)
    {
        DesktopIcons.Layout? layout = _pending ?? DesktopIcons.Read();
        bool hadBefore = TryGetMonitor(out _, out Rect32 before);
        change();
        if (layout is null || !hadBefore || !TryGetMonitor(out Rect32 monitor, out Rect32 work) || work.Same(before))
        {
            return;
        }

        _pending = Arrange(layout, monitor, work);
        IconsSettled = PutBackAsync(_pending, ++_generation);
    }

    private DesktopIcons.Layout Arrange(DesktopIcons.Layout layout, Rect32 monitor, Rect32 work)
    {
        var targets = new Dictionary<string, DesktopIcons.Icon>(layout.Icons);
        int w = layout.CellWidth, h = layout.CellHeight;
        int slack = Math.Min(h / 4, 12);
        bool Fits(int x, int y) =>
            x >= work.Left - slack && y >= work.Top - slack && x + w <= work.Right + slack && y + h <= work.Bottom + slack;
        bool OnMonitor(DesktopIcons.Icon icon) =>
            icon.X + w / 2 >= monitor.Left && icon.X + w / 2 < monitor.Right && icon.Y + h / 2 >= monitor.Top && icon.Y + h / 2 < monitor.Bottom;
        bool Free(string key, int x, int y) =>
            targets.All(other => other.Key == key || Math.Abs(other.Value.X - x) >= w || Math.Abs(other.Value.Y - y) >= h);

        // Icons the strip pushed aside go back to their own spot once it is clear again.
        foreach (var (key, original) in _pushedAside.ToList())
        {
            if (!targets.ContainsKey(key))
            {
                _pushedAside.Remove(key);
            }
            else if (Fits(original.X, original.Y) && Free(key, original.X, original.Y))
            {
                targets[key] = original;
                _pushedAside.Remove(key);
            }
        }

        // Icons now under the strip move to the nearest clear spot on the icon grid.
        foreach (var (key, icon) in targets.ToList())
        {
            if (!OnMonitor(icon) || Fits(icon.X, icon.Y))
            {
                continue;
            }

            DesktopIcons.Icon? best = null;
            long bestDistance = long.MaxValue;
            for (int row = -30; row <= 30; row++)
            {
                for (int column = -30; column <= 30; column++)
                {
                    int x = icon.X + column * w, y = icon.Y + row * h;
                    long distance = (long)column * column * w * w + (long)row * row * h * h;
                    if (distance < bestDistance && Fits(x, y) && Free(key, x, y))
                    {
                        (best, bestDistance) = (icon with { X = x, Y = y }, distance);
                    }
                }
            }

            if (best is not null)
            {
                _pushedAside.TryAdd(key, icon);
                targets[key] = best;
            }
        }

        return layout with { Icons = targets };
    }

    /// <summary>Explorer re-lays the icons a moment after the work area changes, so the layout is applied a few times.</summary>
    private async Task PutBackAsync(DesktopIcons.Layout layout, int generation)
    {
        foreach (int delay in (int[])[250, 500, 750, 1500])
        {
            await Task.Delay(delay);
            if (generation != _generation)
            {
                return;
            }

            DesktopIcons.Apply(layout.Icons.Values);
        }

        _pending = null;
    }

    /// <summary>After a drag: docks or snaps to whatever edge the bar was dropped near.</summary>
    public void SnapAfterDrag()
    {
        Undock();
        if (!TryGetMonitor(out Rect32 monitor, out Rect32 work) || !GetWindowRect(_handle, out Rect32 bar))
        {
            return;
        }

        int distance = (int)Math.Round(SnapDistance * Scale());
        int centre = work.Left + (work.Width - bar.Width) / 2;
        Align = Math.Abs(bar.Left - work.Left) < distance ? DockAlign.Left
            : Math.Abs(bar.Right - work.Right) < distance ? DockAlign.Right
            : Math.Abs(bar.Left - centre) < distance * 2 ? DockAlign.Centre
            : DockAlign.Free;

        if (Math.Abs(bar.Bottom - work.Bottom) < distance)
        {
            Dock(DockEdge.Bottom, Align);
        }
        else if (Math.Abs(bar.Top - work.Top) < distance)
        {
            Dock(DockEdge.Top, Align);
        }
        else
        {
            Move(AlignedLeft(work, bar), bar.Top);
        }
    }

    /// <summary>Docks the bar to the top or bottom of the monitor it is on, reserving that strip.</summary>
    public void Dock(DockEdge edge, DockAlign align)
    {
        Align = align;
        Edge = edge;
        if (edge == DockEdge.None)
        {
            Undock();
            return;
        }

        if (!_registered)
        {
            var data = NewData();
            data.CallbackMessage = (uint)_callbackMessage;
            _registered = SHAppBarMessage(AbmNew, ref data) != IntPtr.Zero;
        }

        Reposition();
    }

    /// <summary>Reserves the strip again and moves the bar into it (taskbar moved, screen size or scaling changed).</summary>
    private void Reposition()
    {
        if (Edge == DockEdge.None || !_registered || _positioning)
        {
            return;
        }

        if (!TryGetMonitor(out Rect32 monitor, out _) || !GetWindowRect(_handle, out Rect32 bar))
        {
            return;
        }

        _positioning = true;
        try
        {
            int height = bar.Height;
            var data = NewData();
            data.Edge = Edge == DockEdge.Top ? AbeTop : AbeBottom;
            data.Rect = monitor;
            if (Edge == DockEdge.Top)
            {
                data.Rect.Bottom = data.Rect.Top + height;
            }
            else
            {
                data.Rect.Top = data.Rect.Bottom - height;
            }

            // Windows moves the proposed strip clear of the taskbar and other docked bars; keep our height on that side.
            SHAppBarMessage(AbmQueryPos, ref data);
            if (Edge == DockEdge.Top)
            {
                data.Rect.Bottom = data.Rect.Top + height;
            }
            else
            {
                data.Rect.Top = data.Rect.Bottom - height;
            }

            KeepIcons(() => SHAppBarMessage(AbmSetPos, ref data));
            Move(AlignedLeft(data.Rect, bar), data.Rect.Top);
        }
        finally
        {
            _positioning = false;
        }
    }

    private int AlignedLeft(Rect32 area, Rect32 bar) => Align switch
    {
        DockAlign.Left => area.Left,
        DockAlign.Right => area.Right - bar.Width,
        DockAlign.Centre => area.Left + (area.Width - bar.Width) / 2,
        _ => Math.Clamp(bar.Left, area.Left, Math.Max(area.Left, area.Right - bar.Width)),
    };

    private void Move(int left, int top) =>
        SetWindowPos(_handle, IntPtr.Zero, left, top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);

    private double Scale() =>
        PresentationSource.FromVisual(_window)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _callbackMessage && _registered)
        {
            switch ((int)wParam)
            {
                case AbnPosChanged:
                    _window.Dispatcher.BeginInvoke(Reposition);
                    break;
                case AbnFullScreenApp:
                    // Step back while a full-screen program (video, game) is on this monitor, then come back on top.
                    _window.Topmost = lParam == IntPtr.Zero;
                    break;
            }
        }
        else if (_registered)
        {
            switch (msg)
            {
                case WmActivate:
                    var activate = NewData();
                    SHAppBarMessage(AbmActivate, ref activate);
                    break;
                case WmWindowPosChanged:
                    var changed = NewData();
                    SHAppBarMessage(AbmWindowPosChanged, ref changed);
                    break;
                case WmDisplayChange:
                case WmDpiChanged:
                    _window.Dispatcher.BeginInvoke(Reposition);
                    break;
            }
        }

        return IntPtr.Zero;
    }

    private bool TryGetMonitor(out Rect32 monitor, out Rect32 work)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        IntPtr handle = MonitorFromWindow(_handle, MonitorDefaultToNearest);
        bool ok = handle != IntPtr.Zero && GetMonitorInfo(handle, ref info);
        monitor = info.Monitor;
        work = info.Work;
        return ok;
    }

    private AppBarData NewData() => new() { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = _handle };

    public void Dispose()
    {
        Undock();
        _source.RemoveHook(WndProc);
    }

    private const uint AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3, AbmActivate = 6, AbmWindowPosChanged = 9;
    private const uint AbeTop = 1, AbeBottom = 3;
    private const int AbnPosChanged = 1, AbnFullScreenApp = 2;
    private const int WmActivate = 0x0006, WmWindowPosChanged = 0x0047, WmDisplayChange = 0x007E, WmDpiChanged = 0x02E0;
    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;

        public readonly bool Same(Rect32 other) =>
            Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect32 Rect;
        public IntPtr LParam;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
