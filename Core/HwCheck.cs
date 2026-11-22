using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using static OrbitTweak.Core.HwCheckLogic;
using DEVMODE = OrbitTweak.Core.FpsLimit.DEVMODE;

namespace OrbitTweak.Core;

// =====================================================================
//  Monitor-Check und RAM-Check (2.31), the Windows side; the rules are in HwCheckLogic.cs.
//  Monitor: the active displays (QueryDisplayConfig for the monitor's name and connection, EnumDisplaySettings for the
//  modes). A switch changes only that display's refresh rate (ChangeDisplaySettingsEx, tested first with CDS_TEST),
//  never while FiveM runs, and goes back by itself after 15 seconds unless it is kept, the way Windows' own display
//  settings do it: a monitor that shows nothing at the new rate cannot be clicked. The rate before is kept in
//  hwcheck.json next to the settings, so "Zurückstellen" works after a restart too.
//  RAM: Win32_PhysicalMemory through PowerShell, read once per run (modules do not change while Windows runs).
//  Nothing here touches FiveM or the game files.
// =====================================================================
internal static unsafe class HwCheck
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsExW(string device, int mode, ref DEVMODE dm, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsExW(string device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevicesW(string? device, uint i, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, byte* pathInfo, ref uint modes, byte* modeInfo, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(byte* packet);

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

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint CDS_UPDATEREGISTRY = 1, CDS_TEST = 2, QDC_ONLY_ACTIVE_PATHS = 2;
    private const int PathSize = 72, ModeSize = 64, SourceNameSize = 84, TargetNameSize = 420;

    /// <summary>How long a new rate waits for "Behalten" before it goes back by itself.</summary>
    public const int RevertSeconds = 15;

    public const string FiveMRuns = "FiveM läuft gerade. Ein Wechsel der Bildwiederholrate wirft das Spiel aus dem Vollbild oder lässt "
        + "das Bild kurz schwarz werden: schließ FiveM zuerst.";

    public sealed record Display(string Device, string Friendly, Conn Conn, bool Primary, Mode Cur, int Best);

    // ---------------------------------------------------------------- displays ----------------------------------------------------------------
    private static DEVMODE NewMode() => new() { dmDeviceName = "", dmFormName = "", dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    private static DEVMODE? Current(string device)
    {
        var dm = NewMode();
        return EnumDisplaySettingsExW(device, ENUM_CURRENT_SETTINGS, ref dm, 0) ? dm : null;
    }

    private static Mode ModeOf(DEVMODE dm) => new(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmBitsPerPel, dm.dmDisplayFrequency, (dm.dmDisplayFlags & DM_INTERLACED) != 0);

    /// <summary>The modes Windows offers for the display (only those the monitor reports it can show).</summary>
    private static List<Mode> Modes(string device)
    {
        var list = new List<Mode>();
        for (int i = 0; i < 5000; i++)
        {
            var dm = NewMode();
            if (!EnumDisplaySettingsExW(device, i, ref dm, 0)) break;
            list.Add(ModeOf(dm));
        }
        return list;
    }

    /// <summary>The active displays, the main one first. Empty when Windows answers nothing.</summary>
    public static List<Display> Displays()
    {
        var list = new List<Display>();
        var named = Named();
        if (named.Count == 0) named = Attached();   // DisplayConfig unavailable: GDI names, without the monitors' names
        foreach (var (dev, friendly, tech) in named)
        {
            try
            {
                if (Current(dev) is not { } cur) continue;
                var c = ModeOf(cur);
                list.Add(new(dev, friendly, ConnOf(tech), cur.dmPositionX == 0 && cur.dmPositionY == 0, c, BestHz(c, Modes(dev))));
            }
            catch { }
        }
        return list.OrderByDescending(d => d.Primary).ThenBy(d => d.Device, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>GDI name (\\.\DISPLAY1), the monitor's own name (EDID) and the connection of each active path. A display
    /// that is mirrored to two monitors is one GDI display: it is listed once.</summary>
    private static List<(string dev, string friendly, uint tech)> Named()
    {
        var list = new List<(string, string, uint)>();
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var np, out var nm) != 0 || np == 0) return list;
            var paths = new byte[np * PathSize];
            var modes = new byte[Math.Max(1, nm) * ModeSize];
            fixed (byte* pp = paths, pm = modes)
            {
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, pp, ref nm, pm, IntPtr.Zero) != 0) return list;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < np; i++)
                {
                    byte* p = pp + i * PathSize;
                    var src = new byte[SourceNameSize];
                    string dev;
                    fixed (byte* s = src)
                    {
                        // DISPLAYCONFIG_SOURCE_DEVICE_NAME: header (type 1, size, adapter LUID, source id), WCHAR[32]
                        *(int*)s = 1; *(uint*)(s + 4) = SourceNameSize; *(long*)(s + 8) = *(long*)p; *(uint*)(s + 16) = *(uint*)(p + 8);
                        if (DisplayConfigGetDeviceInfo(s) != 0) continue;
                        dev = Chars(s + 20, 32);
                    }
                    if (dev.Length == 0 || !seen.Add(dev)) continue;
                    string friendly = "";
                    uint tech = *(uint*)(p + 36);   // targetInfo.outputTechnology
                    var tgt = new byte[TargetNameSize];
                    fixed (byte* t = tgt)
                    {
                        // DISPLAYCONFIG_TARGET_DEVICE_NAME: header (type 2, size, adapter LUID, target id), flags,
                        // outputTechnology, EDID ids, connector, WCHAR[64] friendly name, WCHAR[128] device path
                        *(int*)t = 2; *(uint*)(t + 4) = TargetNameSize; *(long*)(t + 8) = *(long*)(p + 20); *(uint*)(t + 16) = *(uint*)(p + 28);
                        if (DisplayConfigGetDeviceInfo(t) == 0) friendly = Chars(t + 36, 64);
                    }
                    list.Add((dev, friendly, tech));
                }
            }
        }
        catch { list.Clear(); }
        return list;
    }

    private static string Chars(byte* at, int max)
    {
        var c = new ReadOnlySpan<char>(at, max);
        int n = c.IndexOf('\0');
        return new string(n < 0 ? c : c[..n]).Trim();
    }

    private static List<(string dev, string friendly, uint tech)> Attached()
    {
        var list = new List<(string, string, uint)>();
        try
        {
            for (uint i = 0; i < 16; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
                // attached to the desktop, not a mirroring driver
                if ((dd.StateFlags & 1) != 0 && (dd.StateFlags & 8) == 0) list.Add((dd.DeviceName, "", 0xFFFFFFFF));
            }
        }
        catch { }
        return list;
    }

    // ---------------------------------------------------------------- the store ----------------------------------------------------------------
    /// <summary>A switch OrbitTweak made: the rate before and after at that resolution.</summary>
    private sealed class Switched
    {
        public int From { get; set; }
        public int To { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        /// <summary>The monitor's own name (EDID), "" when Windows did not know it.</summary>
        public string Name { get; set; } = "";
        public DateTime At { get; set; }
    }

    private static string StoreFile => Path.Combine(AppDirs.Roaming, "hwcheck.json");

    private static Dictionary<string, Switched> Load()
    {
        try
        {
            if (File.Exists(StoreFile))
                return JsonSerializer.Deserialize<Dictionary<string, Switched>>(File.ReadAllText(StoreFile)) is { } d
                    ? new(d, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static void Save(Dictionary<string, Switched> d)
    {
        try
        {
            Directory.CreateDirectory(AppDirs.Roaming);
            if (d.Count == 0) { File.Delete(StoreFile); return; }
            var tmp = StoreFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(d));
            File.Move(tmp, StoreFile, true);
        }
        catch { }
    }

    /// <summary>The switch OrbitTweak made on that display, while it is still the same monitor at that resolution.</summary>
    private static Switched? Known(Dictionary<string, Switched> store, Display d) =>
        store.TryGetValue(d.Device, out var s) && SameSwitch(s.Name, s.W, s.H, d.Friendly, d.Cur.W, d.Cur.H) ? s : null;

    // ---------------------------------------------------------------- Diagnose rows ----------------------------------------------------------------
    public static List<Row> MonitorRows()
    {
        var rows = new List<Row>();
        try
        {
            var ds = Displays();
            var store = Load();
            for (int i = 0; i < ds.Count; i++)
            {
                var d = ds[i];
                var s = Known(store, d);
                if (MonitorRow(MonitorName(i + 1, ds.Count, d.Friendly, d.Conn), d.Device, d.Cur.Hz, d.Best, d.Conn, s?.From, s?.To) is { } r) rows.Add(r);
            }
        }
        catch { }
        return rows;
    }

    /// <summary>What the page shows in the monitor dialog.</summary>
    public static object DisplayInfo()
    {
        var ds = Displays();
        var store = Load();
        string? pendingDev;
        lock (Gate) pendingDev = _pending?.Device;
        return new
        {
            fivem = FiveM.IsRunning,
            laptop = Hw.HasBattery,
            revertIn = RevertSeconds,
            displays = ds.Select((d, i) =>
            {
                var s = Known(store, d);
                return new
                {
                    device = d.Device,
                    name = MonitorName(i + 1, ds.Count, d.Friendly, d.Conn),
                    conn = d.Conn.ToString().ToLowerInvariant(),
                    primary = d.Primary,
                    w = d.Cur.W, h = d.Cur.H,
                    hz = d.Cur.Hz, best = d.Best,
                    switchedFrom = s != null && s.To == d.Cur.Hz ? s.From : (int?)null,
                    pending = string.Equals(pendingDev, d.Device, StringComparison.OrdinalIgnoreCase),
                };
            }).ToList(),
        };
    }

    // ---------------------------------------------------------------- switching ----------------------------------------------------------------
    public sealed record HzResult(bool Ok, string? Error, string Device = "", int From = 0, int To = 0);

    private static readonly object Gate = new();
    private sealed record Pending(string Device, int From, int To, System.Threading.Timer Timer, object Token);
    private static Pending? _pending;
    private static bool _exitHooked;

    /// <summary>
    /// Switches one display to the highest rate Windows offers at its resolution. The switch goes back by itself after
    /// <see cref="RevertSeconds"/> unless <see cref="Keep"/> is called; then onAutoRevert gets the result.
    /// </summary>
    public static HzResult SetHz(string device, Action<HzResult> onAutoRevert)
    {
        lock (Gate)
        {
            if (FiveM.IsRunning) return new(false, FiveMRuns, device);
            // one unconfirmed switch at a time: nobody said "Behalten" to the first one, so it is never kept silently
            if (_pending != null)
                return new(false, "Ein Monitor wartet noch auf „Behalten“. Bestätige ihn zuerst oder warte, bis OrbitTweak ihn zurückgestellt hat.", device);
            var d = Displays().FirstOrDefault(x => string.Equals(x.Device, device, StringComparison.OrdinalIgnoreCase));
            if (d == null) return new(false, "Diesen Monitor findet Windows gerade nicht. Starte die Prüfung neu.", device);
            if (!WorthSwitching(d.Cur.Hz, d.Best)) return new(false, $"Der Monitor läuft schon mit {d.Cur.Hz} Hz, mehr bietet Windows hier nicht an.", device);

            var err = Apply(d.Device, d.Best);
            if (err != null) return new(false, err, device);

            var store = Load();
            // switched again on top of an earlier switch: the rate before OrbitTweak's first one stays the way back
            int from = Known(store, d) is { } prev && prev.To == d.Cur.Hz ? prev.From : d.Cur.Hz;
            store[d.Device] = new Switched { From = from, To = d.Best, W = d.Cur.W, H = d.Cur.H, Name = Clean(d.Friendly, 64), At = DateTime.Now };
            Save(store);

            var dev = d.Device;
            var token = new object();   // this switch's own countdown: a callback already on its way for an older one does nothing
            var timer = new System.Threading.Timer(_ =>
            {
                HzResult r;
                lock (Gate)
                {
                    if (_pending is not { } pp || !ReferenceEquals(pp.Token, token)) return;
                    pp.Timer.Dispose(); _pending = null;
                    r = BackLocked(dev);
                }
                try { onAutoRevert(r); } catch { }
            }, null, Timeout.Infinite, Timeout.Infinite);
            _pending = new(dev, from, d.Best, timer, token);
            timer.Change(RevertSeconds * 1000, Timeout.Infinite);
            if (!_exitHooked)
            {
                _exitHooked = true;
                // OrbitTweak closed during the countdown: nobody said "Behalten", so the rate goes back as well
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try { lock (Gate) if (_pending is { } p) { p.Timer.Dispose(); _pending = null; BackLocked(p.Device); } }
                    catch { }
                };
            }
            return new(true, null, dev, from, d.Best);
        }
    }

    /// <summary>"Behalten": the new rate stays. Too late when the countdown has already put the old one back.</summary>
    public static HzResult Keep(string device)
    {
        lock (Gate)
        {
            if (_pending is not { } p || !string.Equals(p.Device, device, StringComparison.OrdinalIgnoreCase))
                return new(false, "Zu spät: OrbitTweak hat die alte Bildwiederholrate schon wieder eingestellt.", device);
            p.Timer.Dispose(); _pending = null;
            return new(true, null, p.Device, p.From, p.To);
        }
    }

    /// <summary>Back to the rate before OrbitTweak's switch. Refused while FiveM runs, except during the countdown: an
    /// unconfirmed switch goes back in a few seconds anyway, and "Zurück" is the way out of a picture that is not right.</summary>
    public static HzResult Back(string device)
    {
        lock (Gate)
        {
            bool pending = _pending is { } p && string.Equals(p.Device, device, StringComparison.OrdinalIgnoreCase);
            if (!pending && FiveM.IsRunning) return new(false, FiveMRuns, device);
            if (pending) { _pending!.Timer.Dispose(); _pending = null; }
            return BackLocked(device);
        }
    }

    private static HzResult BackLocked(string device)
    {
        var store = Load();
        if (!store.TryGetValue(device, out var s)) return new(false, "OrbitTweak hat an diesem Monitor nichts umgestellt.", device);
        if (Current(device) is not { } cur) return new(false, "Diesen Monitor findet Windows gerade nicht.", device);
        if (cur.dmDisplayFrequency != s.To)
        {
            // changed since by hand (or by a laptop's own power plan): that is the user's choice now
            store.Remove(device); Save(store);
            return new(false, $"Der Monitor läuft nicht mehr mit {s.To} Hz, du hast ihn inzwischen anders eingestellt. OrbitTweak lässt ihn so.", device);
        }
        var err = Apply(device, s.From);
        if (err != null) return new(false, err, device);
        store.Remove(device); Save(store);
        return new(true, null, device, s.To, s.From);
    }

    /// <summary>Only the refresh rate changes: resolution, colour depth, position and rotation stay as they are.
    /// null = done; otherwise the German reason.</summary>
    private static string? Apply(string device, int hz)
    {
        if (Current(device) is not { } cur) return "Windows liefert für diesen Monitor gerade keine Einstellungen.";
        var dm = cur;
        dm.dmDisplayFrequency = hz;
        dm.dmDisplayFlags &= ~DM_INTERLACED;
        dm.dmFields = SwitchFields(cur.dmFields);
        int test = ChangeDisplaySettingsExW(device, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != 0) return ChangeError(test);
        int r = ChangeDisplaySettingsExW(device, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        var (err, restore) = SwitchOutcome(r, hz, cur.dmDisplayFrequency, Current(device)?.dmDisplayFrequency ?? 0);
        if (restore)
        {
            // not done, but the display moved anyway: without a countdown for it, the mode before goes back at once
            var before = cur;
            before.dmFields = SwitchFields(cur.dmFields);
            err += ChangeDisplaySettingsExW(device, ref before, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == 0
                ? $" OrbitTweak hat wieder {cur.dmDisplayFrequency} Hz eingestellt."
                : " Stell die Bildwiederholrate bitte in den Windows-Anzeigeeinstellungen zurück.";
        }
        return err;
    }

    // ---------------------------------------------------------------- RAM ----------------------------------------------------------------
    private static readonly object RamGate = new();
    private static List<Module>? _ram;

    /// <summary>The RAM modules; null when PowerShell or WMI gives no complete answer (then asked again next time).</summary>
    public static List<Module>? Ram()
    {
        lock (RamGate)
        {
            if (_ram != null) return _ram;
            try
            {
                var mods = ParseModules(Proc.Run("powershell", $"-NoProfile -NonInteractive -Command \"{RamQuery}\"", 20_000));
                if (mods != null) _ram = mods;
                return mods;
            }
            catch { return null; }
        }
    }

    public static List<Row> RamRowsNow(List<Module>? mods)
    {
        try { return RamRows(mods, Hw.HasBattery); }
        catch { return new(); }
    }

    /// <summary>What the page shows in the RAM dialog. ok = false when the modules could not be read.</summary>
    public static object RamInfo()
    {
        var mods = Ram();
        if (mods == null) return new { ok = false, error = "Windows hat die RAM-Riegel nicht verraten (WMI antwortet nicht). Starte die Prüfung später noch einmal." };
        var (cs, n) = Channels(mods);
        var (xs, cfg, rated, gen) = Xmp(mods);
        return new
        {
            ok = true,
            laptop = Laptop(mods, Hw.HasBattery),
            channel = cs.ToString().ToLowerInvariant(),
            channels = n,
            xmp = xs.ToString().ToLowerInvariant(),
            configured = cfg, rated, gen,
            modules = mods.Select(m => new
            {
                slot = Clean(m.Locator, 40),
                bank = Clean(m.Bank, 40),
                chan = ChannelKey(m.Locator, m.Bank),
                gb = (int)Math.Round(m.Capacity / 1073741824.0),
                gen = Gen(m.Type),
                speed = m.Configured,
                rated = Rated(m.Part)?.mts,
                part = Clean(m.Part, 40),
                maker = Maker(m.Maker, m.Part),
            }).ToList(),
        };
    }
}
