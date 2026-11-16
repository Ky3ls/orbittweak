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
    public string Recorder { get; set; } = "orbit";   // orbit (built-in) | nvidia | gamebar | amd | obs
    public bool Flash { get; set; } = true;
    public bool Auto { get; set; }                    // auto-clip after a firefight
    public int Sens { get; set; } = 2;                // 1 = only big fights, 2 = normal, 3 = every shot exchange
    public bool Kills { get; set; }                   // kill clipper on/off
    public string Detect { get; set; } = "mark";      // how kills are noticed: mark = at the crosshair (the server's kill message or GTA's red X, no kill feed needed), feed = read the kill feed
    public string Words { get; set; } = KillText.DefaultWords; // kill words the message at the crosshair is searched for
    public bool Net { get; set; } = true;             // safety net: a firefight in which no kill was noticed is clipped anyway
    public bool Ai { get; set; } = true;              // AI check (needs an OpenRouter key): a vision model looks at firefights in which no kill was noticed
    public string AiModels { get; set; } = KillJudge.DefaultModels;
    public bool Banner { get; set; } = true;          // short note with the kill count of the round in the game when a kill was noticed
    public bool Hud { get; set; } = true;             // small counter in the game: recording dot and the kills of the round
    public string? MontageMusic { get; set; }         // music file laid under montages (null = game sound only)
    public string MontageTitle { get; set; } = "";    // big line of the montage intro (empty = in-game name, else "HIGHLIGHTS")
    public bool MontageIntro { get; set; } = true;    // montage: intro over the blurred first clip and "GG" outro
    public bool MontageCounter { get; set; } = true;  // montage: kill counter chip ("KILL 3 / 8") and "DOUBLE KILL" tags
    public bool HudClock { get; set; }                // the counter also shows the time of day
    public bool HudTime { get; set; } = true;         // … and how long the game has been running
    public bool AutoHighlight { get; set; }           // after the game was closed: join the kill clips of that round into a highlight video
    public int HighlightTail { get; set; } = 8;       // highlight video: only the last seconds of each clip (0 = whole clips)
    public int ShareMb { get; set; } = 10;            // "share for Discord": clips larger than this are made smaller first
    public DateTime? ProvenAt { get; set; }           // when a kill was last noticed at the crosshair: while that is recent the safety net stays quiet
    public bool Deaths { get; set; }
    public string Name { get; set; } = "";            // in-game name as shown in the kill feed
    public string Area { get; set; } = "auto";        // kill feed position: auto | tr | tl | r | tc
    // built-in recorder
    public int Before { get; set; } = 20;             // seconds before the kill / key press
    public int After { get; set; } = 5;               // seconds after the kill
    public string Quality { get; set; } = "std";      // low = 720p30, std = 1080p60, high = native 60
    public bool Sound { get; set; } = true;           // record the game sound
}

public enum Anchor { Center, BottomLeft, TopCenter, TopLeft }

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
    /// <summary>The game window seen last (0 once it is closed).</summary>
    public static IntPtr Window
    {
        get
        {
            if (_gameWnd != IntPtr.Zero && !IsWindow(_gameWnd)) _gameWnd = IntPtr.Zero;
            return _gameWnd;
        }
    }
    public static bool Minimized => _gameWnd != IntPtr.Zero && IsIconic(_gameWnd);

#if DEBUG
    // Test aid, debug builds only: a window of the process named in ORBIT_TEST_GAME counts as the game,
    // even when it is not in front (lets the kill clipper be tested without starting FiveM).
    private static readonly string? TestGame = Environment.GetEnvironmentVariable("ORBIT_TEST_GAME");
    private static bool TestIsGame()
    {
        try
        {
            using var p = Process.GetProcessesByName(TestGame!).FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);
            if (p == null) { _game = false; return false; }
            _gameWnd = p.MainWindowHandle; _pid = (uint)p.Id; _game = true;
            return true;
        }
        catch { return false; }
    }
#endif

    /// <summary>True when the focused window belongs to the GTA/FiveM game process.</summary>
    public static bool IsGame()
    {
#if DEBUG
        if (!string.IsNullOrEmpty(TestGame)) return TestIsGame();
#endif
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
    /// <summary>Notes and counters fade and grow in and fade out again instead of just appearing (never the crosshair itself).</summary>
    public bool Animate { get; set; }
    private bool _leaving;
    private readonly ScaleTransform _scale = new(1, 1);

    private void Play(bool show)
    {
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(show ? 220 : 200);
        _img.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        _img.RenderTransform = _scale;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(show ? 0 : _img.Opacity, show ? 1 : 0, dur) { EasingFunction = ease };
        if (!show) fade.Completed += (_, _) => { if (_leaving) { ShowWindow(_h, 0); _shown = false; _leaving = false; } };
        var size = new System.Windows.Media.Animation.DoubleAnimation(show ? 0.86 : 1, show ? 1 : 0.94, dur) { EasingFunction = ease };
        _img.BeginAnimation(UIElement.OpacityProperty, fade);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, size);
    }

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
            if (_shown && Animate && _h != IntPtr.Zero) { if (!_leaving) { _leaving = true; Play(false); } }
            else if (_shown) { ShowWindow(_h, 0); _shown = false; }
            return;
        }
        Ensure();
        if (_h == IntPtr.Zero) return;
        if (Animate && (!_shown || _leaving)) { _leaving = false; Play(true); }
        int x, y;
        if (Anchor == Anchor.Center) { var (cx, cy) = GameFocus.Center(); x = cx - _pw / 2 + _ox; y = cy - _ph / 2 + _oy; }
        else
        {
            var (l, t, r0, b) = GameFocus.Client();
            if (Anchor == Anchor.BottomLeft) { x = l + _ox; y = b - _ph - _oy; }
            else if (Anchor == Anchor.TopLeft) { x = l + _ox; y = t + _oy; }
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
#if DEBUG
        // a test instance (own data folder) never types: the keys would land in whatever the user has in front
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORBIT_TEST_DATA"))) return "Testlauf: es wird nichts getippt";
#endif
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

// SNAP TAP and WASD "Laggen": see Core/SnapTap.cs (keyboard hook) and Core/WasdLogic.cs (what happens to the keys).

// =====================================================================
//  CLIPPER – either the built-in recorder ("orbit", see Recorder.cs) or the
//  save-replay shortcut of the user's own recorder is pressed
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
        ["orbit"] = ("Eingebaut", Array.Empty<ushort>()),
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
        if (r.keys.Length == 0) return; // built-in recorder: nothing to press
#if DEBUG
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORBIT_TEST_DATA"))) return; // a test instance presses no keys
#endif
        await Task.Delay(120); // let the user release the clip key first
        var down = r.keys.Select(k => K(k, false)).ToArray();
        var up = r.keys.Reverse().Select(k => K(k, true)).ToArray();
        SendInput((uint)down.Length, down, Marshal.SizeOf<INPUT>());
        await Task.Delay(60);
        SendInput((uint)up.Length, up, Marshal.SizeOf<INPUT>());
    }

    public static string VideosDir => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    /// <summary>Where the built-in recorder puts its clips.</summary>
    public static string OwnDir => Path.Combine(VideosDir, "OrbitTweak");

    /// <summary>Recording settings of the built-in recorder for a quality level.</summary>
    public static ReplayRecorder.Options Quality(string q, bool sound) => q switch
    {
        "low" => new(30, 720, 6, sound),
        "high" => new(60, 1440, 20, sound),
        _ => new(60, 1080, 12, sound),
    };

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
        return files.OrderByDescending(f => f.LastWriteTime).Take(80).Select(f => (object)new
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

    /// <summary>
    /// Moves clips to the recycle bin, so nothing is gone for good. Only video files directly in the folder of the
    /// built-in recorder (Videos\OrbitTweak) are accepted; anything else is counted as failed.
    /// </summary>
    public static (int gone, int failed) Recycle(IEnumerable<string> rels)
    {
        int gone = 0, failed = 0;
        var own = Path.GetFullPath(OwnDir).TrimEnd('\\') + "\\";
        foreach (var rel in rels)
        {
            try
            {
                var full = Resolve(rel);
                if (full == null || !string.Equals(Path.GetDirectoryName(full) + "\\", own, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) { failed++; continue; }
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                gone++;
            }
            catch { failed++; }
        }
        return (gone, failed);
    }

    public static void EnableGameBarCapture()
    {
        const string k = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR";
        Reg.Set(k, "AppCaptureEnabled", 1);
        Reg.Set(k, "HistoricalCaptureEnabled", 1);
        Reg.Set(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 1);
    }
}


/// <summary>Small text file of what the clipper noticed and saved. Helps when something was not clipped as expected.</summary>
internal static class ClipLog
{
    public static string File => Path.Combine(AppDirs.Local, "clipper.log");
    private static readonly object Gate = new();

    public static void Add(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                var fi = new FileInfo(File);
                if (fi.Exists && fi.Length > 300_000)
                {
                    var all = System.IO.File.ReadAllLines(File);
                    System.IO.File.WriteAllLines(File, all.Skip(all.Length / 2)); // keep the newer half
                }
                System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

/// <summary>Is the player shooting right now? Left mouse button, or the right trigger of an Xbox-style controller.</summary>
internal static class FireInput
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [StructLayout(LayoutKind.Sequential)] private struct XINPUT_STATE { public uint Packet; public ushort Buttons; public byte Lt, Rt; public short Lx, Ly, Rx, Ry; }
    [DllImport("xinput1_4.dll")] private static extern int XInputGetState(int index, out XINPUT_STATE state);
    private static bool _pad; private static long _padCheck;
#if DEBUG
    // Test aid, debug builds only: with ORBIT_TEST_FIRE set, the player counts as shooting exactly while the file named
    // there exists. The real mouse and controller are not looked at then, so a test is not disturbed by (and does not
    // react to) whoever is using the PC.
    private static readonly string? TestFire = Environment.GetEnvironmentVariable("ORBIT_TEST_FIRE");
#endif

    public static bool Held()
    {
#if DEBUG
        if (!string.IsNullOrEmpty(TestFire)) return File.Exists(TestFire);
#endif
        if ((GetAsyncKeyState(0x01) & 0x8000) != 0) return true;
        // a controller that is not connected is only looked for every two seconds (the query is slow then)
        long now = Environment.TickCount64;
        if (!_pad && now - _padCheck < 2000) return false;
        _padCheck = now;
        try { _pad = XInputGetState(0, out var s) == 0; return _pad && s.Rt > 40; }
        catch { _pad = false; _padCheck = now + 60_000; return false; }
    }
}

/// <summary>
/// Auto-clip: watches only the user's own shooting (left mouse button / controller trigger) while FiveM is in front and
/// the cursor is hidden (= actual gameplay, not menus). A burst of shooting followed by a few quiet seconds counts as a
/// firefight. Nothing in the game is read.
/// </summary>
internal sealed class AutoClip
{
    [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public int x, y; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO ci);

    public static volatile bool GameInFront;
    public volatile int Sens = 2;
    /// <summary>shots, seconds of fire, seconds from the first to the last shot</summary>
    public event Action<int, double, double>? Fight;
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

#if DEBUG
    private static readonly bool TestGame = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORBIT_TEST_GAME"));
#endif

    private static bool CursorHidden()
    {
#if DEBUG
        if (TestGame) return true; // the stand-in game window has no hidden cursor
#endif
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
            bool lmb = GameInFront && FireInput.Held() && CursorHidden();
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
                // whether a clip is really saved (not too soon after the last one, not already saved as a kill) is decided by the listener
                if (shots >= needShots || fire >= needFire)
                {
                    _last = now;
                    try { Fight?.Invoke(shots, Math.Round(fire, 1), Math.Max(0, (lastShot - start).TotalSeconds)); } catch { }
                }
                start = DateTime.MinValue; shots = 0; fire = 0;
            }
        }
    }
}
