using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32.SafeHandles;

namespace OrbitTweak.Core;

// =====================================================================
//  CONTROLLER DISPLAY – shows the controller and pressed buttons live
//  (on-screen overlay for streams + an OBS browser source).
//  Reads Xbox pads via XInput and PlayStation pads (DS4/DualSense) via HID.
// =====================================================================

public class PadCfg
{
    public bool On { get; set; }
    public string Mode { get; set; } = "auto";      // auto (when a controller is used) | on | off
    public string Layout { get; set; } = "auto";   // auto | xbox | ps
    public string Body { get; set; } = "black";    // black | white
    public string Color { get; set; } = "orig";    // orig | theme | #RRGGBB
    public string Corner { get; set; } = "br";     // tl | tr | bl | br
    public int Size { get; set; } = 75;            // %
    public int Opacity { get; set; } = 90;         // %
    public int Key { get; set; }
    public string Accent { get; set; } = "#FF7A3D";
}

public class PadState
{
    public bool C { get; set; }
    public string T { get; set; } = "xbox";
    public string Name { get; set; } = "";
    public Dictionary<string, bool> B { get; set; } = new();
    public double Lx { get; set; }
    public double Ly { get; set; }
    public double Rx { get; set; }
    public double Ry { get; set; }
    public double Lt { get; set; }
    public double Rt { get; set; }
}

internal static class PadInput
{
    [StructLayout(LayoutKind.Sequential)] private struct XGAMEPAD { public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
    [StructLayout(LayoutKind.Sequential)] private struct XSTATE { public uint Packet; public XGAMEPAD Pad; }
    [DllImport("xinput1_4.dll")] private static extern int XInputGetState(int idx, out XSTATE s);

    [StructLayout(LayoutKind.Sequential)] private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, Version; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort a, b, c, d, e, f, g, h, i, j;
    }
    [StructLayout(LayoutKind.Sequential)] private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid Class; public int Flags; public IntPtr Reserved; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tpl);

    public static readonly PadState State = new();
    public static DateTime LastUse = DateTime.MinValue;
    /// <summary>Controller connected and touched within the last 45 seconds.</summary>
    public static bool Active => State.C && DateTime.Now - LastUse < TimeSpan.FromSeconds(45);
    public static event Action? Changed;
    private static Thread? _thread;
    private static volatile bool _run;
    private static volatile int _users;
    private static DateTime _sonySeen = DateTime.MinValue;
    private static readonly object Lock = new();

    public static void Use(bool on)
    {
        _users = Math.Max(0, _users + (on ? 1 : -1));
        if (_users > 0 && _thread == null)
        {
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "PadInput", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
        else if (_users == 0 && _thread != null) { _run = false; _thread = null; }
    }

    private static readonly string[] Keys = { "a", "b", "x", "y", "lb", "rb", "back", "start", "ls", "rs", "up", "down", "left", "right", "guide", "pad" };
    private static string _last = "";

    private static void Publish(PadState s)
    {
        var sig = JsonSerializer.Serialize(s);
        if (sig == _last) return;
        _last = sig;
        if (s.C && (s.B.Values.Any(v => v) || Math.Abs(s.Lx) > .3 || Math.Abs(s.Ly) > .3 || Math.Abs(s.Rx) > .3 || Math.Abs(s.Ry) > .3 || s.Lt > .15 || s.Rt > .15))
            LastUse = DateTime.Now;
        lock (Lock)
        {
            State.C = s.C; State.T = s.T; State.Name = s.Name; State.B = s.B;
            State.Lx = s.Lx; State.Ly = s.Ly; State.Rx = s.Rx; State.Ry = s.Ry; State.Lt = s.Lt; State.Rt = s.Rt;
        }
        Changed?.Invoke();
    }

    public static string Json() { lock (Lock) return JsonSerializer.Serialize(State, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }); }

    private static void Loop()
    {
        var scan = DateTime.MinValue;
        Task? sony = null;
        while (_run)
        {
            if (DateTime.Now - scan > TimeSpan.FromSeconds(3))
            {
                scan = DateTime.Now;
                if (sony == null || sony.IsCompleted)
                {
                    var dev = FindSony();
                    if (dev != null) sony = Task.Run(() => ReadSony(dev.Value.path, dev.Value.len, dev.Value.pid));
                }
            }
            // XInput is used whenever no PlayStation pad sent data in the last second
            if (DateTime.Now - _sonySeen > TimeSpan.FromSeconds(1)) PollXInput();
            Thread.Sleep(6);
        }
    }

    private static bool _xNone;
    private static void PollXInput()
    {
        try
        {
            for (int i = 0; i < 4; i++)
            {
                if (XInputGetState(i, out var st) != 0) continue;
                var p = st.Pad; ushort b = p.Buttons;
                double ax(short v) => Math.Clamp(v / 32767.0, -1, 1);
                Publish(new PadState
                {
                    C = true, T = "xbox", Name = "Xbox Controller",
                    B = new()
                    {
                        ["up"] = (b & 0x1) != 0, ["down"] = (b & 0x2) != 0, ["left"] = (b & 0x4) != 0, ["right"] = (b & 0x8) != 0,
                        ["start"] = (b & 0x10) != 0, ["back"] = (b & 0x20) != 0, ["ls"] = (b & 0x40) != 0, ["rs"] = (b & 0x80) != 0,
                        ["lb"] = (b & 0x100) != 0, ["rb"] = (b & 0x200) != 0, ["a"] = (b & 0x1000) != 0, ["b"] = (b & 0x2000) != 0,
                        ["x"] = (b & 0x4000) != 0, ["y"] = (b & 0x8000) != 0,
                    },
                    Lx = ax(p.LX), Ly = -ax(p.LY), Rx = ax(p.RX), Ry = -ax(p.RY), Lt = p.LT / 255.0, Rt = p.RT / 255.0
                });
                _xNone = false;
                return;
            }
        }
        catch (DllNotFoundException) { }
        if (!_xNone) { _xNone = true; Publish(new PadState { C = false }); }
    }

    private static readonly HashSet<ushort> SonyPids = new() { 0x05C4, 0x09CC, 0x0BA0, 0x0CE6, 0x0DF2 };

    private static (string path, int len, ushort pid)? FindSony()
    {
        HidD_GetHidGuid(out var g);
        var set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        if (set == new IntPtr(-1)) return null;
        try
        {
            for (uint i = 0; ; i++)
            {
                var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out var req, IntPtr.Zero);
                var buf = Marshal.AllocHGlobal(req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(buf + 4);
                    if (path == null || !path.Contains("vid_054c", StringComparison.OrdinalIgnoreCase)) continue;
                    using var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (h.IsInvalid) continue;
                    var at = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                    if (!HidD_GetAttributes(h, ref at) || at.VendorID != 0x054C || !SonyPids.Contains(at.ProductID)) continue;
                    if (!HidD_GetPreparsedData(h, out var pp)) continue;
                    try { HidP_GetCaps(pp, out var caps); if (caps.UsagePage == 1 && caps.InputReportByteLength > 0) return (path, caps.InputReportByteLength, at.ProductID); }
                    finally { HidD_FreePreparsedData(pp); }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    private static async Task ReadSony(string path, int len, ushort pid)
    {
        bool ds5 = pid is 0x0CE6 or 0x0DF2;
        try
        {
            using var h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero); // GENERIC_READ, OVERLAPPED
            if (h.IsInvalid) return;
            using var fs = new FileStream(h, FileAccess.Read, 0, true);
            var b = new byte[len];
            while (_run)
            {
                int n = await fs.ReadAsync(b.AsMemory(0, len));
                if (n < 10) continue;
                var s = ParseSony(b, n, ds5);
                if (s == null) continue;
                _sonySeen = DateTime.Now;
                Publish(s);
            }
        }
        catch { }
    }

    private static PadState? ParseSony(byte[] r, int n, bool ds5)
    {
        int o; bool full;
        if (ds5) { if (r[0] == 0x01 && n >= 64) { o = 1; full = true; } else if (r[0] == 0x31 && n >= 12) { o = 2; full = true; } else if (r[0] == 0x01) { o = 1; full = false; } else return null; }
        else { if (r[0] == 0x01) { o = 1; full = false; } else if (r[0] == 0x11 && n >= 12) { o = 3; full = false; } else return null; }
        double ax(byte v) => Math.Clamp((v - 128) / 127.0, -1, 1);
        byte b1, b2, b3, l2, r2;
        if (full) { l2 = r[o + 4]; r2 = r[o + 5]; b1 = r[o + 7]; b2 = r[o + 8]; b3 = r[o + 9]; }   // DualSense full report
        else { b1 = r[o + 4]; b2 = r[o + 5]; b3 = r[o + 6]; l2 = r[o + 7]; r2 = r[o + 8]; }         // DS4 / simple report
        int hat = b1 & 0x0F;
        bool up = hat is 7 or 0 or 1, right = hat is 1 or 2 or 3, down = hat is 3 or 4 or 5, left = hat is 5 or 6 or 7;
        return new PadState
        {
            C = true, T = "ps", Name = ds5 ? "PlayStation 5 DualSense" : "PlayStation 4 DualShock",
            B = new()
            {
                ["x"] = (b1 & 0x10) != 0, ["a"] = (b1 & 0x20) != 0, ["b"] = (b1 & 0x40) != 0, ["y"] = (b1 & 0x80) != 0,
                ["lb"] = (b2 & 0x01) != 0, ["rb"] = (b2 & 0x02) != 0, ["back"] = (b2 & 0x10) != 0, ["start"] = (b2 & 0x20) != 0,
                ["ls"] = (b2 & 0x40) != 0, ["rs"] = (b2 & 0x80) != 0, ["guide"] = (b3 & 0x01) != 0, ["pad"] = (b3 & 0x02) != 0,
                ["up"] = up, ["down"] = down, ["left"] = left, ["right"] = right,
            },
            Lx = ax(r[o]), Ly = ax(r[o + 1]), Rx = ax(r[o + 2]), Ry = ax(r[o + 3]), Lt = l2 / 255.0, Rt = r2 / 255.0
        };
    }
}

public class PadPart
{
    public string Id { get; set; } = "";
    public string K { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double R { get; set; }
    public double Mv { get; set; }
    public string? Col { get; set; }
    public string? Png { get; set; }
    public string? Png2 { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public System.Windows.Media.Imaging.BitmapSource? Img { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public System.Windows.Media.Imaging.BitmapSource? Img2 { get; set; }
}

public class PadPack
{
    public double W { get; set; }
    public double H { get; set; }
    public string? Art { get; set; }
    public List<PadPart> Parts { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public System.Windows.Media.Imaging.BitmapSource? ArtImg { get; set; }

    private static System.Windows.Media.Imaging.BitmapSource? Decode(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        var bi = new System.Windows.Media.Imaging.BitmapImage();
        bi.BeginInit(); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(Convert.FromBase64String(b64)); bi.EndInit(); bi.Freeze();
        return bi;
    }

    public void Prepare()
    {
        ArtImg = Decode(Art); Art = null;
        foreach (var p in Parts) { p.Img = Decode(p.Png); p.Img2 = Decode(p.Png2); p.Png = p.Png2 = null; }
    }
}

/// <summary>Draws the controller picture plus the pressed / moving parts on top.</summary>
internal sealed class PadView : FrameworkElement
{
    public string Layout = "xbox";
    public Dictionary<string, PadPack> Packs = new();
    public PadPack? Pack => Packs.TryGetValue(Layout, out var p) ? p : null;

    private static SolidColorBrush Br(string? hex, double a = 1)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex ?? "#FF7A3D");
        var b = new SolidColorBrush(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B)); b.Freeze(); return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var pk = Pack; if (pk == null) return;
        PadState s = PadInput.State;
        var b = s.B;
        bool On(string id) => b.TryGetValue(id, out var v) && v;
        if (pk.ArtImg != null) dc.DrawImage(pk.ArtImg, new Rect(0, 0, pk.W, pk.H));
        foreach (var p in pk.Parts)
        {
            var rect = new Rect(p.X, p.Y, p.W, p.H);
            switch (p.K)
            {
                case "trig":
                {
                    double v = Math.Clamp(p.Id == "lt" ? s.Lt : s.Rt, 0, 1);
                    if (v > 0.02 && p.Img != null) { dc.PushOpacity(v); dc.DrawImage(p.Img, rect); dc.Pop(); }
                    break;
                }
                case "img": if (On(p.Id) && p.Img != null) dc.DrawImage(p.Img, rect); break;
                case "dot": if (On(p.Id)) dc.DrawEllipse(Br(p.Col, .75), null, new Point(p.X + p.W / 2, p.Y + p.H / 2), p.W / 2, p.H / 2); break;
                case "rect": if (On(p.Id)) dc.DrawRoundedRectangle(Br(p.Col, .5), null, rect, p.R, p.R); break;
                case "stick":
                {
                    double ax = p.Id == "ls" ? s.Lx : s.Rx, ay = p.Id == "ls" ? s.Ly : s.Ry;
                    var img = On(p.Id) ? p.Img2 : p.Img;
                    if (img != null) dc.DrawImage(img, new Rect(p.X + ax * p.Mv, p.Y + ay * p.Mv, p.W, p.H));
                    break;
                }
            }
        }
    }
}

internal sealed class PadOverlay
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);

    private Window? _w;
    private IntPtr _h;
    private bool _shown, _subscribed;
    private readonly PadView _view = new() { Width = 750, Height = 630 };
    private PadCfg _cfg = new();
    private DateTime _lastDraw;

    public PadView View => _view;

    public void Configure(PadCfg cfg, string layout)
    {
        _cfg = cfg; _view.Layout = layout;
        if (_view.Pack is { } pk) { _view.Width = pk.W; _view.Height = pk.H; }
        if (_w != null) _w.Opacity = Math.Clamp(cfg.Opacity, 20, 100) / 100.0;
        _view.InvalidateVisual();
    }

    private void Ensure()
    {
        if (_w != null) return;
        _w = new Window
        {
            Title = "OrbitTweak Controller", WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
            ResizeMode = ResizeMode.NoResize, Focusable = false, IsHitTestVisible = false,
            Width = 32, Height = 32, Left = -32000, Top = -32000, Opacity = _cfg.Opacity / 100.0,
            Content = new Viewbox { Stretch = Stretch.Uniform, Child = _view }
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

    private void OnInput()
    {
        if (_cfg.Layout == "auto")
        {
            var l = PadInput.State.T == "ps" ? "ps" : "xbox";
            if (l != _view.Layout) _w?.Dispatcher.BeginInvoke(() => { _view.Layout = l; if (_view.Pack is { } pk) { _view.Width = pk.W; _view.Height = pk.H; } });
        }
        // redraw at most ~60 fps, on the UI thread
        if (DateTime.Now - _lastDraw < TimeSpan.FromMilliseconds(15)) return;
        _lastDraw = DateTime.Now;
        _w?.Dispatcher.BeginInvoke(() => _view.InvalidateVisual());
    }

    /// <summary>Called by the UI timer. In "auto" mode the input reader keeps running to notice a controller.</summary>
    public void Tick()
    {
        bool listen = _cfg.Mode != "off";
        if (listen && !_subscribed) { PadInput.Use(true); PadInput.Changed += OnInput; _subscribed = true; }
        if (!listen && _subscribed) { PadInput.Changed -= OnInput; PadInput.Use(false); _subscribed = false; }
        Update(_cfg.Mode == "on" || (_cfg.Mode == "auto" && PadInput.Active));
    }

    public bool Visible => _shown;

    public void Update(bool visible)
    {
        if (!visible) { if (_shown) { ShowWindow(_h, 0); _shown = false; } return; }
        Ensure();
        if (_h == IntPtr.Zero) return;
        var (l, t, r, b) = GameFocus.Client();
        var pk = _view.Pack; double bw = pk?.W ?? 750, bh = pk?.H ?? 630;
        int w = (int)(bw * 0.42 * _cfg.Size / 100.0), h = (int)(bh * 0.42 * _cfg.Size / 100.0), m = 24;
        int x = _cfg.Corner.EndsWith("l") ? l + m : r - w - m;
        int y = _cfg.Corner.StartsWith("t") ? t + m : b - h - m;
        bool moved = !GetWindowRect(_h, out var rc) || rc.L != x || rc.T != y || rc.R - rc.L != w || rc.B - rc.T != h;
        if (!_shown || moved) SetWindowPos(_h, HWND_TOPMOST, x, y, w, h, 0x10 | 0x40);
        else SetWindowPos(_h, HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        _shown = true;
    }

    public void Close()
    {
        if (_subscribed) { PadInput.Changed -= OnInput; PadInput.Use(false); _subscribed = false; }
        Update(false);
        try { _w?.Close(); } catch { }
        _w = null; _h = IntPtr.Zero; _shown = false;
    }
}
