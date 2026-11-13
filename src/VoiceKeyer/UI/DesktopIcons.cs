using System.Runtime.InteropServices;

namespace VoiceKeyer.UI;

/// <summary>
/// Reads and sets the positions of the desktop icons through Explorer's own desktop view (IFolderView), the documented
/// way to move desktop icons from another program. Used around docking: when the reserved strip changes the work area,
/// Explorer re-lays the icons (and on growing back it restores an old saved layout), so the layout is put back afterwards.
/// Every call fails quietly (returns null/false): icons are a nicety and must never stop the bar from docking.
/// </summary>
internal static class DesktopIcons
{
    /// <summary>One icon: its item ID (the key used to find it again) and its top-left position in screen pixels.</summary>
    internal sealed record Icon(byte[] Pidl, int X, int Y);

    /// <summary>The current layout, keyed by item ID, plus the grid cell size (both in screen pixels).</summary>
    internal sealed record Layout(Dictionary<string, Icon> Icons, int CellWidth, int CellHeight);

    public static Layout? Read()
    {
        try
        {
            if (!TryGetView(out IFolderView? view, out Point32 origin))
            {
                return null;
            }

            var icons = new Dictionary<string, Icon>();
            if (view!.ItemCount(SvgioAllView, out int count) != 0)
            {
                return null;
            }

            for (int i = 0; i < count; i++)
            {
                if (view.Item(i, out IntPtr pidl) != 0 || pidl == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    if (view.GetItemPosition(pidl, out Point32 point) == 0)
                    {
                        byte[] bytes = PidlBytes(pidl);
                        icons[Convert.ToBase64String(bytes)] = new Icon(bytes, point.X + origin.X, point.Y + origin.Y);
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }

            int width = 75, height = 75;
            if (view.GetSpacing(out Point32 spacing) == 0 && spacing.X > 0 && spacing.Y > 0)
            {
                (width, height) = (spacing.X, spacing.Y);
            }

            return new Layout(icons, width, height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Moves each icon to the given screen position (icons that no longer exist are skipped).</summary>
    public static void Apply(IReadOnlyCollection<Icon> icons)
    {
        if (icons.Count == 0)
        {
            return;
        }

        var pidls = new List<IntPtr>(icons.Count);
        try
        {
            if (!TryGetView(out IFolderView? view, out Point32 origin))
            {
                return;
            }

            var points = new List<Point32>(icons.Count);
            foreach (Icon icon in icons)
            {
                IntPtr pidl = Marshal.AllocCoTaskMem(icon.Pidl.Length);
                Marshal.Copy(icon.Pidl, 0, pidl, icon.Pidl.Length);
                pidls.Add(pidl);
                points.Add(new Point32 { X = icon.X - origin.X, Y = icon.Y - origin.Y });
            }

            view!.SelectAndPositionItems((uint)pidls.Count, [.. pidls], [.. points], SvsiPositionItem);
        }
        catch (Exception)
        {
            // Explorer restarted or the desktop is not available: leave the icons as they are.
        }
        finally
        {
            pidls.ForEach(Marshal.FreeCoTaskMem);
        }
    }

    /// <summary>Copies a child item ID (a run of SHITEMIDs ending in a zero length) into a byte array.</summary>
    private static byte[] PidlBytes(IntPtr pidl)
    {
        int length = 0;
        while (true)
        {
            ushort size = (ushort)Marshal.ReadInt16(pidl, length);
            if (size == 0)
            {
                break;
            }

            length += size;
        }

        var bytes = new byte[length + 2];
        Marshal.Copy(pidl, bytes, 0, length + 2);
        return bytes;
    }

    /// <summary>Finds the desktop's folder view and the screen position of its top-left corner (icon positions are relative to it).</summary>
    private static bool TryGetView(out IFolderView? view, out Point32 origin)
    {
        view = null;
        origin = default;

        var windows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellWindows, throwOnError: true)!)!;
        object location = CsidlDesktop;
        object root = null!; // VT_EMPTY
        object? dispatch = windows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch);
        if (dispatch is not IServiceProvider provider)
        {
            return false;
        }

        Guid service = SidTopLevelBrowser, browserId = typeof(IShellBrowser).GUID;
        if (provider.QueryService(ref service, ref browserId, out object browserObject) != 0 || browserObject is not IShellBrowser browser)
        {
            return false;
        }

        if (browser.QueryActiveShellView(out object shellView) != 0 || shellView is not IFolderView folderView)
        {
            return false;
        }

        if (shellView is IOleWindow window && window.GetWindow(out IntPtr defView) == 0)
        {
            IntPtr list = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            ClientToScreen(list != IntPtr.Zero ? list : defView, ref origin);
        }

        view = folderView;
        return true;
    }

    private const int CsidlDesktop = 0, SwcDesktop = 8, SwfoNeedDispatch = 1;
    private const uint SvgioAllView = 2, SvsiPositionItem = 0x80;
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X, Y;
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void Count();
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object location, [MarshalAs(UnmanagedType.Struct)] ref object root,
            int windowClass, out int hwnd, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();

        [PreserveSig]
        int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object shellView);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleWindow
    {
        [PreserveSig]
        int GetWindow(out IntPtr hwnd);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder();

        [PreserveSig]
        int Item(int index, out IntPtr pidl);

        [PreserveSig]
        int ItemCount(uint flags, out int count);

        void Items();
        void GetSelectionMarkedItem();
        void GetFocusedItem();

        [PreserveSig]
        int GetItemPosition(IntPtr pidl, out Point32 point);

        [PreserveSig]
        int GetSpacing(out Point32 spacing);

        void GetDefaultSpacing();
        void GetAutoArrange();
        void SelectItem();

        [PreserveSig]
        int SelectAndPositionItems(uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls,
            [MarshalAs(UnmanagedType.LPArray)] Point32[] points, uint flags);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref Point32 point);
}
