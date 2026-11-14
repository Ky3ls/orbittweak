using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace OrbitTweak.Core;

// Live features that work while FiveM is running – nothing is injected into the game.
//  * Crosshair: a transparent, click-through, topmost window centered on the primary screen.
//  * Screen color: Windows gamma ramp (all GPUs) + NVIDIA digital vibrance (NVAPI).

public class CrosshairCfg
{
    public bool On { get; set; }
    public bool OnlyGame { get; set; }   // off = stays visible when tabbing out
    public int Key { get; set; } = 0x79; // F10
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public string? Json { get; set; }
}

public class MinimapCfg
{
    public bool On { get; set; }
    public string? Json { get; set; }
    public int X { get; set; }   // px from the left edge of the game picture
    public int Y { get; set; }   // px from the bottom edge
}

public class ClipCfg
{
    public int Key { get; set; } = 0x78;              // F9
    public string Recorder { get; set; } = "nvidia";  // nvidia | gamebar | amd | obs
    public bool Flash { get; set; } = true;
    public bool Auto { get; set; }                    // auto-clip after a firefight
    public int Sens { get; set; } = 2;                // 1 = only big fights, 2 = normal, 3 = every shot exchange
    public bool Kills { get; set; }                   // auto-clip on kills read from the kill feed (OCR)
    public bool Deaths { get; set; }
    public string Name { get; set; } = "";            // in-game name as shown in the kill feed
    public string Area { get; set; } = "tr";          // kill feed position: tr | tl | r | tc
}

public enum Anchor { Center, BottomLeft, TopCenter }

public class DisplayCfg
{
    public bool On { get; set; }
    public bool OnlyGame { get; set; } = true;
    public int Vibrance { get; set; }     // 0..100  (0 = your normal NVIDIA setting)
    public int Brightness { get; set; }   // -50..50
    public int Contrast { get; set; }     // -50..50
    public int Gamma { get; set; } = 100; // 50..200 (%)
    public int Key { get; set; }          // optional hotkey, 0 = none
    public bool GammaNeutral => Brightness == 0 && Contrast == 0 && Gamma == 100;
}

internal static class GameFocus
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);
    private static uint _pid;
    private static bool _game;
    private static IntPtr _gameWnd;
    public static uint Pid => _game ? _pid : 0;

    /// <summary>True when the focused window belongs to the GTA/FiveM game process.</summary>
    public static bool IsGame()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        GetWindowThreadProcessId(h, out var pid);
        if (pid != _pid)
        {
            _pid = pid;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                var n = p.ProcessName;
                _game = n.Contains("GTAProcess", StringComparison.OrdinalIgnoreCase) || n.Equals("GTA5", StringComparison.OrdinalIgnoreCase);
            }
            catch { _game = false; }
        }
        if (_game) _gameWnd = h;
        return _game;
    }

    /// <summary>
    /// Exact center (physical pixels) of the game picture: the client area of the game window
    /// (works for borderless, windowed and other monitors). Falls back to the primary screen.
    /// </summary>
    /// <summary>Game picture rectangle in screen pixels (or the primary screen).</summary>
    public static (int l, int t, int r, int b) Client()
    {
        var h = _gameWnd;
        if (h != IntPtr.Zero && IsWindow(h) && IsWindowVisible(h) && !IsIconic(h) && GetClientRect(h, out var r) && r.R > 200 && r.B > 200)
        {
            var p = new POINT();
            ClientToScreen(h, ref p);
            return (p.X, p.Y, p.X + r.R, p.Y + r.B);
        }
        return (0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
    }

    public static (int x, int y) Center()
    {
        var h = _gameWnd;
        if (h != IntPtr.Zero && IsWindow(h) && IsWindowVisible(h) && !IsIconic(h) && GetClientRect(h, out var r) && r.R > 200 && r.B > 200)
        {
            var p = new POINT();
            ClientToScreen(h, ref p);
            return (p.X + r.R / 2, p.Y + r.B / 2);
        }
        return (GetSystemMetrics(0) / 2, GetSystemMetrics(1) / 2);
    }
}

// =====================================================================
//  CROSSHAIR OVERLAY
// =====================================================================
internal sealed class CrosshairOverlay
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);

    private Window? _w;
    private IntPtr _h;
    private readonly Image _img = new() { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
    private int _pw, _ph, _ox, _oy;
    private bool _shown;
    public Anchor Anchor { get; set; } = Anchor.Center;

    public bool HasImage => _pw > 0;

    public void SetOffset(int ox, int oy) { _ox = ox; _oy = oy; }

    public void SetImage(byte[] png, int ox, int oy)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = new MemoryStream(png);
        bi.EndInit();
        bi.Freeze();
        _pw = bi.PixelWidth; _ph = bi.PixelHeight; _ox = ox; _oy = oy;
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.NearestNeighbor);
        _img.Source = bi;
        Ensure();
    }

    private void Ensure()
    {
        if (_w != null) return;
        _w = new Window
        {
            Title = "OrbitTweak Crosshair", WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
            ResizeMode = ResizeMode.NoResize, Focusable = false, IsHitTestVisible = false,
            Width = 32, Height = 32, Left = -32000, Top = -32000, Content = _img
        };
        _w.SourceInitialized += (_, _) =>
        {
            _h = new WindowInteropHelper(_w).Handle;
            var ex = GetWindowLongPtr(_h, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(_h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST));
        };
        _w.Show();
        ShowWindow(_h, 0);
    }

    /// <summary>
    /// Called every tick: shows/hides, keeps the window above the game and re-centers it.
    /// The image has an odd size with the crosshair on its middle pixel, so that pixel lands
    /// exactly on the center of the game picture. Position and size are checked every tick,
    /// which also corrects any DPI rescaling Windows/WPF might apply.
    /// </summary>
    public void Update(bool visible)
    {
        if (!HasImage) visible = false;
        if (!visible)
        {
            if (_shown) { ShowWindow(_h, 0); _shown = false; }
            return;
        }
        Ensure();
        if (_h == IntPtr.Zero) return;
        int x, y;
        if (Anchor == Anchor.Center) { var (cx, cy) = GameFocus.Center(); x = cx - _pw / 2 + _ox; y = cy - _ph / 2 + _oy; }
        else
        {
            var (l, t, r0, b) = GameFocus.Client();
            if (Anchor == Anchor.BottomLeft) { x = l + _ox; y = b - _ph - _oy; }
            else { x = (l + r0) / 2 - _pw / 2 + _ox; y = t + _oy; }
        }
        bool moved = !GetWindowRect(_h, out var r) || r.L != x || r.T != y || r.R - r.L != _pw || r.B - r.T != _ph;
        if (!_shown || moved) SetWindowPos(_h, HWND_TOPMOST, x, y, _pw, _ph, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        else SetWindowPos(_h, HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | SWP_NOACTIVATE);
        _shown = true;
    }

    public void Close()
    {
        try { _w?.Close(); } catch { }
        _w = null; _h = IntPtr.Zero; _shown = false;
    }
}

// =====================================================================
//  SCREEN COLOR (gamma ramp)
// =====================================================================
internal static class ScreenColor
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAMP { [MarshalAs(UnmanagedType.ByValArray, SizeConst = 768)] public ushort[] V; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? dev, uint i, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateDC(string driver, string? device, string? output, IntPtr init);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(IntPtr dc, ref RAMP r);
    [DllImport("gdi32.dll")] private static extern bool GetDeviceGammaRamp(IntPtr dc, ref RAMP r);

    private static readonly Dictionary<string, ushort[]> Orig = new();
    private const string IcmKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";

    public static bool RangeUnlocked => Reg.Is(IcmKey, "GdiIcmGammaRange", 256);
    public static void UnlockRange() => Reg.Set(IcmKey, "GdiIcmGammaRange", 256);

    private static IEnumerable<string> Devices()
    {
        for (uint i = 0; i < 16; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) yield break;
            if ((dd.StateFlags & 1) != 0) yield return dd.DeviceName; // attached to desktop
        }
    }

    private static ushort[] Build(DisplayCfg c)
    {
        var v = new ushort[768];
        double g = Math.Clamp(c.Gamma, 30, 300) / 100.0, k = 1 + Math.Clamp(c.Contrast, -80, 80) / 100.0, b = Math.Clamp(c.Brightness, -80, 80) / 200.0;
        for (int i = 0; i < 256; i++)
        {
            double y = Math.Pow(i / 255.0, 1.0 / g);
            y = (y - 0.5) * k + 0.5 + b;
            var u = (ushort)Math.Round(Math.Clamp(y, 0, 1) * 65535);
            v[i] = v[i + 256] = v[i + 512] = u;
        }
        return v;
    }

    /// <summary>Applies the ramp to all screens. Returns false if Windows rejected it (range too large).</summary>
    public static bool Apply(DisplayCfg c)
    {
        var ramp = new RAMP { V = Build(c) };
        bool ok = true, any = false;
        foreach (var d in Devices())
        {
            var dc = CreateDC(d, null, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) continue;
            try
            {
                if (!Orig.ContainsKey(d))
                {
                    var o = new RAMP { V = new ushort[768] };
                    if (GetDeviceGammaRamp(dc, ref o)) Orig[d] = o.V;
                }
                any = true;
                if (!SetDeviceGammaRamp(dc, ref ramp)) ok = false;
            }
            finally { DeleteDC(dc); }
        }
        return any && ok;
    }

    public static void Restore()
    {
        foreach (var (d, v) in Orig)
        {
            var dc = CreateDC(d, null, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) continue;
            try { var r = new RAMP { V = v }; SetDeviceGammaRamp(dc, ref r); }
            finally { DeleteDC(dc); }
        }
    }
}

// =====================================================================
//  NVIDIA DIGITAL VIBRANCE (NVAPI)
// =====================================================================
internal static class NvVibrance
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitD();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumD(int i, out IntPtr h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetD(IntPtr h, uint output, ref DVC info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetD(IntPtr h, uint output, int level);

    [StructLayout(LayoutKind.Sequential)]
    private struct DVC { public uint Version; public int Current, Min, Max; }

    private static bool? _ok;
    private static EnumD? _enum;
    private static GetD? _get;
    private static SetD? _set;
    private static readonly Dictionary<IntPtr, int> Orig = new();

    private static T? Fn<T>(uint id) where T : Delegate
    {
        var p = QueryInterface(id);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    public static bool Available
    {
        get
        {
            if (_ok != null) return _ok.Value;
            try
            {
                var init = Fn<InitD>(0x0150E828);
                _ok = init != null && init() == 0;
                if (_ok == true)
                {
                    _enum = Fn<EnumD>(0x9ABDD40D);
                    _get = Fn<GetD>(0x4085DE45);
                    _set = Fn<SetD>(0x172409B4);
                    _ok = _enum != null && _get != null && _set != null && Displays().Any();
                }
            }
            catch { _ok = false; }
            return _ok.Value;
        }
    }

    private static List<IntPtr> Displays()
    {
        var l = new List<IntPtr>();
        for (int i = 0; i < 8; i++) { if (_enum!(i, out var h) != 0) break; l.Add(h); }
        return l;
    }

    /// <summary>0 = the user's normal level, 100 = maximum vibrance.</summary>
    public static bool Set(int pct)
    {
        if (!Available) return false;
        bool any = false;
        try
        {
            foreach (var h in Displays())
            {
                var info = new DVC { Version = (uint)Marshal.SizeOf<DVC>() | (1u << 16) };
                if (_get!(h, 0, ref info) != 0) continue;
                if (!Orig.ContainsKey(h)) Orig[h] = info.Current;
                var baseLvl = Orig[h];
                var lvl = baseLvl + (int)Math.Round((info.Max - baseLvl) * Math.Clamp(pct, 0, 100) / 100.0);
                if (_set!(h, 0, lvl) == 0) any = true;
            }
        }
        catch { }
        return any;
    }

    public static void Restore()
    {
        if (_set == null) return;
        try { foreach (var (h, lvl) in Orig) _set(h, 0, lvl); } catch { }
    }
}

// =====================================================================
//  LIVE DISPLAY MODE – turns the running game window borderless (like
//  "Borderless Gaming"): only the window style/position is changed.
// =====================================================================
internal static class GameWindow
{
    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const long WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000, WS_SYSMENU = 0x80000, WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000, WS_POPUP = 0x80000000;
    private const long WS_EX_DLGMODALFRAME = 0x1, WS_EX_CLIENTEDGE = 0x200, WS_EX_STATICEDGE = 0x20000, WS_EX_WINDOWEDGE = 0x100;
    private const uint SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);

    private static IntPtr _saved;
    private static long _savedStyle, _savedEx;
    private static RECT _savedRect;
    public static IntPtr LastAuto;

    public static IntPtr Find()
    {
        try
        {
            using var p = FiveM.GameProcess();
            var h = p?.MainWindowHandle ?? IntPtr.Zero;
            return h != IntPtr.Zero && IsWindow(h) ? h : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }

    private static RECT Monitor(IntPtr h)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(h, 2), ref mi);
        return mi.rcMonitor;
    }

    /// <summary>"windowed" (has a title bar), "borderless" (no frame, fills the monitor) or "fullscreen".</summary>
    public static string Mode(IntPtr h)
    {
        var st = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
        if ((st & WS_CAPTION) == WS_CAPTION) return "windowed";
        return "borderless";
    }

    public static object Info()
    {
        var h = Find();
        if (h == IntPtr.Zero) return new { found = false };
        GetClientRect(h, out var c);
        var mode = IsIconic(h) ? "fullscreen" : Mode(h);
        return new { found = true, mode, w = c.R, h = c.B };
    }

    public static string? MakeBorderless(IntPtr h = default)
    {
        if (h == IntPtr.Zero) h = Find();
        if (h == IntPtr.Zero) return "FiveM läuft nicht (Spiel noch nicht gestartet)";
        if (IsIconic(h)) return "Das Spiel ist minimiert oder im exklusiven Vollbild. Drück im Spiel Alt+Enter und versuch es nochmal.";
        var st = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
        var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        if ((st & WS_CAPTION) == WS_CAPTION)
        {
            _saved = h; _savedStyle = st; _savedEx = ex;
            GetWindowRect(h, out _savedRect);
        }
        SetWindowLongPtr(h, GWL_STYLE, new IntPtr((st & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)) | WS_POPUP));
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex & ~(WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_WINDOWEDGE)));
        var m = Monitor(h);
        SetWindowPos(h, IntPtr.Zero, m.L, m.T, m.R - m.L, m.B - m.T, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOZORDER | SWP_NOACTIVATE);
        return null;
    }

    public static string? MakeWindowed()
    {
        var h = Find();
        if (h == IntPtr.Zero) return "FiveM läuft nicht (Spiel noch nicht gestartet)";
        if (h == _saved && _savedStyle != 0)
        {
            SetWindowLongPtr(h, GWL_STYLE, new IntPtr(_savedStyle));
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(_savedEx));
            SetWindowPos(h, IntPtr.Zero, _savedRect.L, _savedRect.T, _savedRect.R - _savedRect.L, _savedRect.B - _savedRect.T, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOZORDER | SWP_NOACTIVATE);
            return null;
        }
        var st = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
        SetWindowLongPtr(h, GWL_STYLE, new IntPtr((st & ~WS_POPUP) | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX));
        var m = Monitor(h);
        int w = (m.R - m.L) * 3 / 4, hh = (m.B - m.T) * 3 / 4;
        SetWindowPos(h, IntPtr.Zero, m.L + (m.R - m.L - w) / 2, m.T + (m.B - m.T - hh) / 2, w, hh, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOZORDER | SWP_NOACTIVATE);
        return null;
    }
}

// =====================================================================
//  F8 CONSOLE SENDER – types official FiveM console commands for the user
//  (same as pressing F8 and typing). Only plain keyboard input, nothing injected.
// =====================================================================
internal static class ConsoleSender
{
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);

    private const uint KEYUP = 0x2, UNICODE = 0x4;
    private const ushort VK_F8 = 0x77, VK_RETURN = 0x0D, VK_MENU = 0x12, VK_SHIFT = 0x10, VK_CONTROL = 0x11;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private static INPUT Vk(ushort vk, bool up) => new() { type = 1, ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = up ? KEYUP : 0 } };
    private static INPUT Ch(char c, bool up) => new() { type = 1, ki = new KEYBDINPUT { wScan = c, dwFlags = UNICODE | (up ? KEYUP : 0) } };
    private static void Send(params INPUT[] i) => SendInput((uint)i.Length, i, Marshal.SizeOf<INPUT>());
    private static void Tap(ushort vk) => Send(Vk(vk, false), Vk(vk, true));

    private static void Focus(IntPtr h)
    {
        // a tap on ALT lifts Windows' foreground lock so focus can be handed over
        Send(Vk(VK_MENU, false), Vk(VK_MENU, true));
        SetForegroundWindow(h);
    }

    /// <summary>Opens the F8 console in FiveM, types the commands, closes it and returns focus.</summary>
    public static async Task<string?> Run(IList<string> cmds, IntPtr returnTo)
    {
        if (cmds.Count == 0) return null;
        foreach (var c in cmds)
            if (!System.Text.RegularExpressions.Regex.IsMatch(c, @"^[A-Za-z0-9_]+ [A-Za-z0-9_\.\-]+$")) return "Ungültiger Befehl";
        var game = GameWindow.Find();
        if (game == IntPtr.Zero) return "FiveM läuft nicht";
        await Lock.WaitAsync();
        try
        {
            bool wasFg = GetForegroundWindow() == game;
            if (!wasFg)
            {
                if (IsIconic(game)) ShowWindow(game, 9);
                Focus(game);
                for (int i = 0; i < 30 && GetForegroundWindow() != game; i++) await Task.Delay(50);
                if (GetForegroundWindow() != game) return "FiveM konnte nicht in den Vordergrund geholt werden";
                await Task.Delay(300);
            }
            // make sure no modifier is stuck, then F8 → type → Enter → F8
            Send(Vk(VK_SHIFT, true), Vk(VK_CONTROL, true), Vk(VK_MENU, true));
            await Task.Delay(120);
            Tap(VK_F8);
            await Task.Delay(280);
            foreach (var cmd in cmds)
            {
                foreach (var ch in cmd) { Send(Ch(ch, false), Ch(ch, true)); await Task.Delay(4); }
                await Task.Delay(40);
                Tap(VK_RETURN);
                await Task.Delay(140);
            }
            Tap(VK_F8);
            await Task.Delay(150);
            if (!wasFg && returnTo != IntPtr.Zero) Focus(returnTo);
            return null;
        }
        finally { Lock.Release(); }
    }
}

// =====================================================================
//  SNAP TAP (SOCD) – when A and D (or W and S) are held together, the last
//  pressed key wins and the other is released, like on "Snap Tap" keyboards.
//  Low-level keyboard hook, only active while the game is in front.
// =====================================================================
internal sealed class SnapTap
{
    private delegate IntPtr HookProc(int code, IntPtr wp, IntPtr lp);
    [StructLayout(LayoutKind.Sequential)] private struct KBD { public uint vk, scan, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint tid, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private static readonly IntPtr Marker = new(0x0B17_5A1A);
    private static readonly Dictionary<uint, uint> Opp = new() { [0x41] = 0x44, [0x44] = 0x41, [0x57] = 0x53, [0x53] = 0x57 };
    private readonly HookProc _proc;
    private IntPtr _hook;
    private Thread? _thread;
    private uint _tid;
    private readonly HashSet<uint> _down = new();      // physically held
    private readonly HashSet<uint> _muted = new();     // held but released by us

    /// <summary>Set by the UI timer – the hook itself never does any slow work.</summary>
    public static volatile bool GameInFront;

    public SnapTap() { _proc = Hook; }

    // The hook runs on its own high-priority thread with its own message loop, so a busy UI
    // can never delay keyboard input (that caused laggy WASD before).
    public void Enable(bool on)
    {
        if (on && _thread == null)
        {
            _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "SnapTap" };
            _thread.Start();
        }
        else if (!on && _thread != null)
        {
            PostThreadMessage(_tid, 0x12, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
            _thread.Join(500);
            _thread = null;
        }
    }

    private void Run()
    {
        _tid = GetCurrentThreadId();
        _hook = SetWindowsHookEx(13, _proc, GetModuleHandle(null), 0);
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero; _down.Clear(); _muted.Clear();
    }

    private static void Key(uint vk, bool up)
    {
        var i = new[] { new INPUT { type = 1, ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = up ? 2u : 0u, dwExtraInfo = Marker } } };
        SendInput(1, i, Marshal.SizeOf<INPUT>());
    }

    private IntPtr Hook(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<KBD>(lp);
            if (k.extra != Marker && (k.flags & 0x10) == 0 && Opp.TryGetValue(k.vk, out var opp))
            {
                int msg = wp.ToInt32();
                bool down = msg is 0x100 or 0x104, up = msg is 0x101 or 0x105;
                if (!GameInFront)
                {
                    if (up) { _down.Remove(k.vk); _muted.Remove(k.vk); } else if (down) _down.Add(k.vk);
                }
                else if (down)
                {
                    if (_muted.Contains(k.vk)) return new IntPtr(1);              // auto-repeat of a muted key
                    if (_down.Add(k.vk) && _down.Contains(opp) && _muted.Add(opp)) Key(opp, true);
                }
                else if (up)
                {
                    _down.Remove(k.vk);
                    if (_muted.Remove(k.vk)) return new IntPtr(1);              // already released by us
                    if (_down.Contains(opp) && _muted.Remove(opp)) Key(opp, false); // give the other direction back
                }
            }
        }
        return CallNextHookEx(_hook, code, wp, lp);
    }
}

// =====================================================================
//  CLIPPER – presses the save-replay shortcut of the user's recorder
//  (NVIDIA Instant Replay, Xbox Game Bar, AMD ReLive, OBS replay buffer)
// =====================================================================
internal static class Clipper
{
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);

    // VK lists; the last key is the "main" key
    public static readonly Dictionary<string, (string label, ushort[] keys)> Recorders = new()
    {
        ["nvidia"] = ("Alt+F10", new ushort[] { 0x12, 0x79 }),
        ["gamebar"] = ("Win+Alt+G", new ushort[] { 0x5B, 0x12, 0x47 }),
        ["amd"] = ("Strg+Shift+S", new ushort[] { 0x11, 0x10, 0x53 }),
        ["obs"] = ("Strg+Alt+F12", new ushort[] { 0x11, 0x12, 0x7B }),
    };

    private static INPUT K(ushort vk, bool up)
    {
        uint flags = up ? 2u : 0u;
        if (vk is 0x5B or 0x2D or 0x24 or 0x21) flags |= 1; // extended key
        return new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = flags } };
    }

    public static async Task Save(string recorder)
    {
        if (!Recorders.TryGetValue(recorder, out var r)) r = Recorders["nvidia"];
        await Task.Delay(120); // let the user release the clip key first
        var down = r.keys.Select(k => K(k, false)).ToArray();
        var up = r.keys.Reverse().Select(k => K(k, true)).ToArray();
        SendInput((uint)down.Length, down, Marshal.SizeOf<INPUT>());
        await Task.Delay(60);
        SendInput((uint)up.Length, up, Marshal.SizeOf<INPUT>());
    }

    public static string VideosDir => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    public static List<object> List()
    {
        var root = VideosDir;
        var files = new List<FileInfo>();
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };
            foreach (var ext in new[] { "*.mp4", "*.mkv", "*.mov" })
                files.AddRange(new DirectoryInfo(root).EnumerateFiles(ext, opts));
        }
        catch { }
        return files.OrderByDescending(f => f.LastWriteTime).Take(40).Select(f => (object)new
        {
            name = Path.GetFileNameWithoutExtension(f.Name),
            rel = Path.GetRelativePath(root, f.FullName).Replace('\\', '/'),
            folder = Path.GetFileName(Path.GetDirectoryName(f.FullName)),
            size = f.Length,
            date = f.LastWriteTime,
            ext = f.Extension.ToLowerInvariant()
        }).ToList();
    }

    public static string? Resolve(string rel)
    {
        var full = Path.GetFullPath(Path.Combine(VideosDir, rel));
        return full.StartsWith(Path.GetFullPath(VideosDir), StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
    }

    public static void EnableGameBarCapture()
    {
        const string k = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR";
        Reg.Set(k, "AppCaptureEnabled", 1);
        Reg.Set(k, "HistoricalCaptureEnabled", 1);
        Reg.Set(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 1);
    }
}


/// <summary>
/// Auto-clip: watches only the user's own mouse (left button) while FiveM is in front and the cursor is hidden
/// (= actual gameplay, not menus). A burst of shooting followed by a few quiet seconds counts as a firefight,
/// then the normal clip hotkey of the recorder is sent. Nothing in the game is read.
/// </summary>
internal sealed class AutoClip
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public int x, y; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO ci);

    public static volatile bool GameInFront;
    public volatile int Sens = 2;
    public event Action<int, double>? Fight; // shots, seconds of fire
    private volatile bool _on;
    private Thread? _t;
    private DateTime _last = DateTime.MinValue;

    public void Enable(bool on)
    {
        _on = on;
        if (on && (_t == null || !_t.IsAlive))
        {
            _t = new Thread(Loop) { IsBackground = true, Name = "OrbitAutoClip", Priority = ThreadPriority.BelowNormal };
            _t.Start();
        }
    }

    private static bool CursorHidden()
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        return GetCursorInfo(ref ci) && (ci.flags & 1) == 0;
    }

    private void Loop()
    {
        DateTime start = DateTime.MinValue, lastShot = DateTime.MinValue;
        int shots = 0; double fire = 0; bool was = false;
        while (_on)
        {
            Thread.Sleep(16);
            var now = DateTime.UtcNow;
            bool lmb = GameInFront && (GetAsyncKeyState(0x01) & 0x8000) != 0 && CursorHidden();
            if (lmb)
            {
                if (!was) { shots++; if (start == DateTime.MinValue) start = now; }
                fire += 0.016; lastShot = now;
            }
            was = lmb;
            if (start == DateTime.MinValue) continue;
            // fight is over after 3 s without shooting, or forced after 45 s so the replay buffer still holds it
            if ((now - lastShot).TotalSeconds >= 3 || (now - start).TotalSeconds >= 45)
            {
                var (needShots, needFire) = Sens switch { 1 => (12, 3.0), 3 => (3, 0.6), _ => (6, 1.4) };
                if ((shots >= needShots || fire >= needFire) && (now - _last).TotalSeconds >= 20)
                {
                    _last = now;
                    try { Fight?.Invoke(shots, Math.Round(fire, 1)); } catch { }
                }
                start = DateTime.MinValue; shots = 0; fire = 0;
            }
        }
    }
}
