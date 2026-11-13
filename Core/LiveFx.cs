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
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    private static uint _pid;
    private static bool _game;

    /// <summary>True when the focused window belongs to the GTA/FiveM game process.</summary>
    public static bool IsGame()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        GetWindowThreadProcessId(h, out var pid);
        if (pid == _pid) return _game;
        _pid = pid;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            var n = p.ProcessName;
            _game = n.Contains("GTAProcess", StringComparison.OrdinalIgnoreCase) || n.Equals("GTA5", StringComparison.OrdinalIgnoreCase);
        }
        catch { _game = false; }
        return _game;
    }
}

// =====================================================================
//  CROSSHAIR OVERLAY
// =====================================================================
internal sealed class CrosshairOverlay
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);

    private Window? _w;
    private IntPtr _h;
    private readonly Image _img = new() { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
    private int _pw, _ph, _ox, _oy;
    private bool _shown;

    public bool HasImage => _pw > 0;

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
        if (_shown) Place(true);
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

    private void Place(bool show)
    {
        if (_h == IntPtr.Zero) return;
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        int x = sw / 2 - _pw / 2 + _ox, y = sh / 2 - _ph / 2 + _oy;
        SetWindowPos(_h, HWND_TOPMOST, x, y, _pw, _ph, SWP_NOACTIVATE | (show ? SWP_SHOWWINDOW : 0));
    }

    /// <summary>Called every tick: shows/hides and keeps the window above the game.</summary>
    public void Update(bool visible)
    {
        if (!HasImage) visible = false;
        if (visible)
        {
            Ensure();
            if (!_shown) { Place(true); _shown = true; }
            else SetWindowPos(_h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else if (_shown)
        {
            ShowWindow(_h, 0);
            _shown = false;
        }
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
