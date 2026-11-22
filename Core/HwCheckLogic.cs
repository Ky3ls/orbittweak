using System.Globalization;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>
/// Monitor-Check und RAM-Check (2.31): the plain rules behind two rows of the Diagnose list, without Windows calls, so
/// they can be checked anywhere (tests/HwCheckLogic.Tests). Core/HwCheck.cs reads the displays and the RAM modules.
///   Monitor: the refresh rate a display runs at against the highest one Windows offers at the same resolution and
///            colour depth. Windows only lists what the monitor reports over the cable it is connected with, so a
///            144 Hz monitor on an HDMI 1.4 cable shows up as 60 Hz at most: OrbitTweak cannot see more than that.
///   RAM:     single channel (one module, or all modules in the same channel by their slot labels) and XMP/EXPO that
///            is probably off (the modules run at a JEDEC default while their part number names a higher speed).
///            Unknown labels, soldered memory and unknown part numbers are no claim.
/// </summary>
internal static class HwCheckLogic
{
    /// <summary>A row of the Diagnose list. Act = the button's own label; Guide = the button explains, it fixes nothing.</summary>
    public sealed record Row(string Name, string Value, string St, string? Fix = null, string? Act = null, bool Guide = false);

    // =====================================================================
    //  MONITOR
    // =====================================================================
    /// <summary>A display mode as EnumDisplaySettings lists it.</summary>
    public readonly record struct Mode(int W, int H, int Bpp, int Hz, bool Interlaced = false);

    /// <summary>How the display is connected, as far as Windows tells (DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY).</summary>
    public enum Conn { Unknown, Hdmi, DisplayPort, Dvi, Vga, Internal, Wireless }

    public static Conn ConnOf(uint tech) => tech switch
    {
        0 => Conn.Vga,                                   // HD15
        4 => Conn.Dvi,
        5 => Conn.Hdmi,
        10 => Conn.DisplayPort,                          // external
        6 or 11 or 13 or 0x80000000 => Conn.Internal,    // LVDS, embedded DisplayPort, embedded UDI, internal
        15 => Conn.Wireless,                             // Miracast
        _ => Conn.Unknown,
    };

    /// <summary>
    /// The highest refresh rate Windows offers at the resolution and colour depth the display runs at, interlaced modes
    /// left out; 0 when there is none. A display turned by 90° lists its modes either way round, so both count.
    /// </summary>
    public static int BestHz(Mode cur, IEnumerable<Mode> modes)
    {
        int best = 0;
        foreach (var m in modes)
        {
            if (m.Interlaced || m.Hz <= 1 || m.Bpp != cur.Bpp) continue;
            bool same = (m.W == cur.W && m.H == cur.H) || (m.W == cur.H && m.H == cur.W);
            if (same && m.Hz > best) best = m.Hz;
        }
        return best;
    }

    /// <summary>
    /// Worth a switch: at least 10 Hz and 8 % more. 59 against 60 Hz (the same NTSC rate written two ways) or 360
    /// against 380 Hz is no point to fix; 60 → 75, 144 → 165 and 240 → 260 are.
    /// </summary>
    public static bool WorthSwitching(int cur, int best) => cur > 1 && best - cur >= 10 && best >= cur * 1.08;

    /// <summary>
    /// The row name: "Monitor" when there is one, otherwise numbered; with the monitor's own name when Windows knows it
    /// (a laptop panel has none: "Laptop-Bildschirm").
    /// </summary>
    public static string MonitorName(int number, int count, string? friendly, Conn conn)
    {
        var f = Clean(friendly, 32);
        var head = conn == Conn.Internal && f.Length == 0 ? "Laptop-Bildschirm" : "Monitor";
        if (count > 1) head += " " + number;
        return f.Length > 0 ? $"{head} · {f}" : head;
    }

    /// <summary>
    /// The row of one display. switchedTo is the rate OrbitTweak switched it to (from the store), only counted while the
    /// display still runs at it. null = nothing known to say (no rate read).
    /// </summary>
    public static Row? MonitorRow(string name, string device, int curHz, int bestHz, Conn conn, int? switchedFrom, int? switchedTo)
    {
        if (curHz <= 1 || bestHz <= 1) return null;
        if (switchedFrom is int from && switchedTo == curHz && from != curHz)
            return new(name, $"{curHz} Hz, von OrbitTweak umgestellt", "ok", "hw:hzback:" + device, $"Auf {from} Hz zurück");
        if (WorthSwitching(curHz, bestHz))
            return new(name, $"Läuft mit {curHz} Hz, kann aber {bestHz} Hz", "warn", "hw:hz:" + device);
        // at the top already. A low top on an external monitor is worth one honest sentence: a 144 Hz monitor on the wrong
        // cable or port offers only 60 Hz, and then Windows (and OrbitTweak) does not see more
        int top = Math.Max(curHz, bestHz);
        if (top <= 75 && conn is not Conn.Internal and not Conn.Wireless)
            return new(name, $"{curHz} Hz, mehr bietet Windows hier nicht an. Kann dein Monitor laut Datenblatt mehr, bremst meist "
                + (conn == Conn.Hdmi ? "das HDMI-Kabel oder der Anschluss (DisplayPort probieren)" : "Kabel oder Anschluss"), "info");
        return new(name, top <= 75 ? $"{curHz} Hz, mehr bietet Windows hier nicht an" : $"{curHz} Hz, das Höchste, was Windows anbietet", "ok");
    }

    /// <summary>German text of a ChangeDisplaySettingsEx result that is not DISP_CHANGE_SUCCESSFUL.</summary>
    public static string ChangeError(int code) => code switch
    {
        1 => "Windows will dafür neu starten. OrbitTweak hat nichts umgestellt.",
        -1 => "Der Grafiktreiber hat den Wechsel abgelehnt.",
        -2 => "Diesen Modus lehnt Windows für den Monitor ab.",
        -3 => "Windows konnte die Einstellung nicht speichern.",
        _ => $"Windows hat den Wechsel abgelehnt (Code {code}).",
    };

    // DEVMODE dmFields (wingdi.h)
    public const int DM_POSITION = 0x20, DM_DISPLAYORIENTATION = 0x80, DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000,
        DM_DISPLAYFLAGS = 0x200000, DM_DISPLAYFREQUENCY = 0x400000, DM_INTERLACED = 2;

    /// <summary>
    /// The dmFields of a switch that changes only the refresh rate: size, colour depth, rate and the display flags
    /// (progressive), plus the position and the rotation the driver reported for the mode it runs now, so a second or a
    /// turned monitor stays where and how it is. The printer half of DEVMODE is never passed on.
    /// </summary>
    public static int SwitchFields(int current) =>
        (current & (DM_POSITION | DM_DISPLAYORIENTATION)) | DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFLAGS | DM_DISPLAYFREQUENCY;

    /// <summary>
    /// What a switch came to, from ChangeDisplaySettingsEx's result and the rate the display runs at afterwards (0 =
    /// not readable). error null = done. restore = the display runs at another rate than before although the switch is
    /// not done (refused half-way, or a third rate): the mode before has to be put back, there is no countdown for it.
    /// </summary>
    public static (string? error, bool restore) SwitchOutcome(int result, int wantHz, int beforeHz, int nowHz)
    {
        if (result == 0 && nowHz == wantHz) return (null, false);
        bool moved = nowHz > 1 && nowHz != beforeHz;
        if (result != 0) return (ChangeError(result), moved);
        return (nowHz > 1 && !moved ? $"Windows hat den Wechsel angenommen, der Monitor läuft aber weiter mit {nowHz} Hz."
            : nowHz > 1 ? $"Windows hat den Wechsel angenommen, der Monitor lief danach aber mit {nowHz} statt {wantHz} Hz."
            : "Windows hat den Wechsel angenommen, verrät aber nicht, wie der Monitor jetzt läuft.", moved);
    }

    /// <summary>
    /// A switch in the store belongs to the display it was found under: the same resolution (either way round), and the
    /// same monitor when both names are known (after a driver update Windows can hand \\.\DISPLAY1 to another monitor).
    /// </summary>
    public static bool SameSwitch(string? storedName, int storedW, int storedH, string? friendly, int w, int h)
    {
        bool size = (storedW == w && storedH == h) || (storedW == h && storedH == w);
        var a = Clean(storedName, 64);
        var b = Clean(friendly, 64);
        return size && (a.Length == 0 || b.Length == 0 || string.Equals(a, b, StringComparison.OrdinalIgnoreCase));
    }

    // =====================================================================
    //  RAM
    // =====================================================================
    /// <summary>One RAM module as Win32_PhysicalMemory reports it. Speeds in MT/s (what Windows calls MHz there).</summary>
    public sealed record Module(long Capacity, int Speed, int Configured, int Type, int FormFactor, string Locator, string Bank, string Part, string Maker);

    /// <summary>
    /// PowerShell that prints one "mod=" line per module (fields tab-separated, in <see cref="Module"/>'s order) and a last
    /// "count=" line, so a cut-off answer is seen as such. No double quotes: it goes inside -Command "…".
    /// </summary>
    public const string RamQuery =
        "$m = @(Get-CimInstance -ClassName Win32_PhysicalMemory -ErrorAction Stop); "
        + "foreach ($x in $m) { 'mod=' + (@($x.Capacity, $x.Speed, $x.ConfiguredClockSpeed, $x.SMBIOSMemoryType, $x.FormFactor, "
        + "$x.DeviceLocator, $x.BankLabel, $x.PartNumber, $x.Manufacturer) -join [char]9) }; 'count=' + $m.Count";

    /// <summary>The modules from <see cref="RamQuery"/>'s output; null when it is not that output or not complete.</summary>
    public static List<Module>? ParseModules(string? output)
    {
        var list = new List<Module>();
        int? count = null;
        foreach (var raw in (output ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("count=", StringComparison.Ordinal))
            {
                if (!int.TryParse(line[6..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var c)) return null;
                count = c;
                continue;
            }
            if (!line.StartsWith("mod=", StringComparison.Ordinal)) continue;
            var f = line[4..].Split('\t');
            if (f.Length != 9) return null;   // a field with a tab in it: better no answer than a module too few
            long.TryParse(f[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var cap);
            list.Add(new(cap, Int(f[1]), Int(f[2]), Int(f[3]), Int(f[4]), f[5].Trim(), f[6].Trim(), f[7].Trim(), f[8].Trim()));
        }
        if (count != list.Count) return null;
        list.RemoveAll(m => m.Capacity <= 0);
        return list.Count > 0 ? list : null;
    }

    private static int Int(string s) => int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>SMBIOS memory type → DDR generation for the checks: 3, 4, 5; 0 for anything else (LPDDR is soldered,
    /// "unknown" or plain "RAM" is what virtual machines report).</summary>
    public static int Gen(int smbiosType) => smbiosType switch { 24 => 3, 26 => 4, 34 => 5, _ => 0 };

    // ---------- channels ----------
    // "ChannelA-DIMM0", "Controller1-ChannelA-DIMM0" (Intel; two memory controllers on DDR5 boards), "P0 CHANNEL A" (AMD
    // bank label), "ChB_Dimm1", "P0_Node0_Channel1_Dimm0" (Supermicro)
    private static readonly Regex ChanRx = new(@"(?:Controller\s*(\d+)[\s_-]*)?(?<![A-Z])Ch(?:annel)?\s*[_-]?\s*([A-H]|\d)(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // "DIMM_A1", "DIMMA1", "DIMM A2", "P1-DIMMB1" (ASUS, MSI, Supermicro)
    private static readonly Regex DimmRx = new(@"(?:^|[^A-Z])DIMM[\s_-]?([A-H])\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // "DDR4_A1", "DDR5_B2" (Gigabyte)
    private static readonly Regex DdrRx = new(@"^DDR\d?[\s_-]?([A-H])\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // "A1_DIMM0", "B1_DIMM1"
    private static readonly Regex SlotRx = new(@"^([A-H])\d[\s_-]?DIMM\s*\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The channel a module sits in, from its slot label (DeviceLocator) or else its bank label; null when neither says it.
    /// Labels like "DIMM1", "DIMM 0" with "BANK 2", "XMM1" or "Bottom-Slot 1(left)" name the slot, not the channel: there
    /// the channel is not guessed.
    /// </summary>
    public static string? ChannelKey(string? locator, string? bank) => KeyOf(locator) ?? KeyOf(bank);

    private static string? KeyOf(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return null;
        var m = ChanRx.Match(s);
        if (m.Success) return (m.Groups[1].Success ? "C" + m.Groups[1].Value + "-" : "") + m.Groups[2].Value.ToUpperInvariant();
        foreach (var rx in new[] { DdrRx, SlotRx, DimmRx })
        {
            m = rx.Match(s);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        }
        return null;
    }

    // "P1-DIMMA1", "CPU1_DIMM_A1" (slot labels of two-socket boards), "P0 CHANNEL A", "P0_Node0_Channel1_Dimm0" (bank labels)
    private static readonly Regex SocketRx = new(@"^(?:P|CPU)\s*(\d)(?=[\s_-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The CPU socket a module belongs to by its labels (slot label first); null when they do not say it. Two
    /// sockets each have their own channel A (and their own channel count): there no claim is made.</summary>
    public static string? SocketOf(string? locator, string? bank)
    {
        foreach (var s in new[] { locator, bank })
            if (SocketRx.Match((s ?? "").Trim()) is { Success: true } m) return m.Groups[1].Value;
        return null;
    }

    public enum ChanState { Unknown, Single, Multi }

    /// <summary>
    /// Single: one plugged module (DIMM or SO-DIMM), or several that all sit in one channel. Multi: modules in at least
    /// two channels (count = how many). Unknown: soldered or unknown memory types, a slot label that does not name
    /// its channel, or modules on two CPU sockets.
    /// </summary>
    public static (ChanState state, int channels) Channels(IReadOnlyList<Module> mods)
    {
        if (mods.Count == 0 || mods.Any(m => Gen(m.Type) == 0)) return (ChanState.Unknown, 0);
        if (mods.Count == 1)
            return mods[0].FormFactor is 8 or 12 && !Soldered(mods[0]) ? (ChanState.Single, 1) : (ChanState.Unknown, 0);
        if (mods.Select(m => SocketOf(m.Locator, m.Bank)).Distinct().Count() > 1) return (ChanState.Unknown, 0);
        var keys = mods.Select(m => ChannelKey(m.Locator, m.Bank)).ToList();
        if (keys.Any(k => k == null)) return (ChanState.Unknown, 0);
        int n = keys.Distinct().Count();
        return n == 1 ? (ChanState.Single, 1) : (ChanState.Multi, n);
    }

    private static bool Soldered(Module m) =>
        Regex.IsMatch(m.Locator + " " + m.Bank, @"on\s*-?\s*board|soldered|\bMB\b|system\s*board", RegexOptions.IgnoreCase);

    /// <summary>"2 × 16 GB", "16 GB + 8 GB".</summary>
    public static string Sizes(IReadOnlyList<Module> mods)
    {
        var gb = mods.Select(m => (int)Math.Round(m.Capacity / 1073741824.0)).ToList();
        if (gb.Distinct().Count() == 1) return gb.Count == 1 ? $"{gb[0]} GB" : $"{gb.Count} × {gb[0]} GB";
        return string.Join(" + ", gb.OrderByDescending(x => x).Select(x => $"{x} GB"));
    }

    // ---------- rated speed from the part number ----------
    private static readonly RegexOptions RxO = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    // Corsair: CMK32GX4M2B3200C16, CMW16GX4M2C3200C16W, CMH32GX5M2B6000C30, CMK32GX5M2B6000Z30 (EXPO), CMT32GX5M2X6200C36
    private static readonly Regex Corsair = new(@"^CM[A-Z]{1,4}\d{1,3}GX([345])M\d{1,2}[A-Z](\d{4})[CZ]\d{1,2}", RxO);
    // G.Skill: F4-3600C16D-32GTZNC, F4-3200C16-8GVKB (module), F5-6000J3038F16GX2-TZ5RK, F5-5600U3636C16GX2-RS5K
    private static readonly Regex GSkill = new(@"^F([345])-(\d{4})[A-Z]\d{1,2}", RxO);
    // Kingston module numbers: KHX3200C16D4/8GX, KF3600C17D4/8GX, KF3200C20S4/16G, KF5600C40-16
    private static readonly Regex KingstonModule = new(@"^(?:KHX|KF|HX)(\d{4})[CS]\d{2}", RxO);
    // Kingston kit numbers: KF436C17BBK2/16, KF560C36BBEK2-32, HX432C16FB3K2/16, KF432S20IBK2/32
    private static readonly Regex KingstonKit = new(@"^(?:KF|HX)([345])(\d{2})[CS]\d{2}", RxO);
    // Crucial Ballistix: BL2K8G32C16U4B, BL8G32C16U4B.M8FE1, BLM16G44C19U4B (MAX), BL16G36C16U4BL (RGB)
    private static readonly Regex Ballistix = new(@"^BL[MT]?(?:\d+K)?\d+G(\d{2})C\d{2}[US]4", RxO);
    // Crucial Ballistix Sport/Tactical/Elite: BLS8G4D240FSB, BLS16G4D30AESB.M16FE, BLT8G4D30AETA, BLE8G4D36BEEAK
    private static readonly Regex BallistixD = new(@"^BL[SETM](?:\d+K)?\d+G4D(\d{2,3})[A-Z]", RxO);
    // Crucial Pro DDR5 overclocking modules: CP16G60C36U5B, CP2K16G60C36U5W (the 5600 CL46 ones are plain JEDEC modules)
    private static readonly Regex CrucialPro = new(@"^CP(?:\d+K)?\d+G(\d{2})C\d{2}U5", RxO);
    // TeamGroup, as the modules report it: TEAMGROUP-UD4-3600, TEAMGROUP-UD5-6000
    private static readonly Regex TeamModule = new(@"^TEAMGROUP-[US]D([45])-(\d{4})", RxO);
    // TeamGroup T-Force and T-Create kits: TF3D416G3200HC16FDC01, TLZGD416G3200HC16CDC01, FF3D532G6000HC38ADC01, CTCED532G6000HC30DC01
    private static readonly Regex TeamKit = new(@"^(?:T[FLDX]|F[FLP]|CTC)[A-Z0-9]*?D([45])\d{1,3}G(\d{4})HC\d{2}", RxO);
    // Patriot Viper: PVS416G320C6K, PVS416G360C8, PVB416G300C6K, PVE2416G320C8K, PVV532G600C30K, PVX532G800C38K
    private static readonly Regex Patriot = new(@"^PV[A-Z0-9]*?([45])\d{1,3}G(\d{3})C\d", RxO);
    // ADATA XPG: AX4U320016G16A-DT50, AX4U41338G19J-DGM50X, AX5U6000C3016G-DCLARBK
    private static readonly Regex Xpg = new(@"^AX([45])[US](\d{4})", RxO);

    /// <summary>
    /// The DDR generation and speed (MT/s) a module is sold with, read from its part number; null for anything else
    /// (OEM modules from Samsung, SK hynix, Micron, ValueRAM and the like are plain JEDEC modules without XMP or EXPO,
    /// and a part number OrbitTweak does not know is no claim either).
    /// </summary>
    public static (int gen, int mts)? Rated(string? part)
    {
        var p = (part ?? "").Trim();
        if (p.Length < 6) return null;
        Match m;
        if ((m = Corsair.Match(p)).Success) return Ok(Num(m, 1), Num(m, 2));
        if ((m = GSkill.Match(p)).Success) return Ok(Num(m, 1), Num(m, 2));
        if ((m = KingstonModule.Match(p)).Success) return Ok(0, Num(m, 1));
        if ((m = KingstonKit.Match(p)).Success) return Ok(Num(m, 1), Two(Num(m, 1), Num(m, 2)));
        if ((m = Ballistix.Match(p)).Success) return Ok(4, Two(4, Num(m, 1)));
        if ((m = BallistixD.Match(p)).Success) return Ok(4, m.Groups[1].Value.Length == 3 ? Three(Num(m, 1)) : Two(4, Num(m, 1)));
        if ((m = CrucialPro.Match(p)).Success) { var s = Two(5, Num(m, 1)); return s >= 6000 ? Ok(5, s) : null; }
        if ((m = TeamModule.Match(p)).Success) return Ok(Num(m, 1), Num(m, 2));
        if ((m = TeamKit.Match(p)).Success) return Ok(Num(m, 1), Num(m, 2));
        if ((m = Patriot.Match(p)).Success) return Ok(Num(m, 1), Three(Num(m, 2)));
        if ((m = Xpg.Match(p)).Success) return Ok(Num(m, 1), Num(m, 2));
        return null;
    }

    private static int Num(Match m, int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);

    /// <summary>gen 0 = not in the part number: told by the speed. Speeds outside DDR4 (1600–5333) and DDR5 (4000–9600)
    /// are no answer; where both fit (4000–5333) a number without its generation stays open.</summary>
    private static (int gen, int mts)? Ok(int gen, int mts)
    {
        bool d4 = mts is >= 1600 and <= 5333, d5 = mts is >= 4000 and <= 9600;
        if (gen == 4) return d4 ? (4, mts) : null;
        if (gen == 5) return d5 ? (5, mts) : null;
        if (gen == 3) return mts is >= 800 and <= 3200 ? (3, mts) : null;
        if (gen == 0 && d4 && !d5) return (4, mts);
        if (gen == 0 && d5 && !d4) return (5, mts);
        if (gen == 0 && d4 && d5) return (0, mts);
        return null;
    }

    /// <summary>Two digits as Kingston and Crucial write them: 26 = 2666, 29 = 2933, 37 = 3733; otherwise × 100.</summary>
    private static int Two(int gen, int v) => gen == 4 || gen == 3
        ? v switch { 21 => 2133, 26 => 2666, 29 => 2933, 33 => 3333, 34 => 3466, 37 => 3733, 41 => 4133, 42 => 4266, 46 => 4600, 53 => 5333, 13 => 1333, 16 => 1600, 18 => 1866, _ => v * 100 }
        : v * 100;

    /// <summary>Three digits as Patriot and Ballistix Sport write them: 266 = 2666, 293 = 2933; otherwise × 10.</summary>
    private static int Three(int v) => v switch { 213 => 2133, 266 => 2666, 293 => 2933, 333 => 3333, 346 => 3466, 373 => 3733, 413 => 4133, 426 => 4266, 453 => 4533, 466 => 4666, 533 => 5333, _ => v * 10 };

    /// <summary>The speeds boards fall back to without XMP/EXPO (the modules' JEDEC profile), give or take 2 MT/s
    /// (Windows reports 2667 for 2666).</summary>
    public static bool IsJedecDefault(int gen, int mts)
    {
        int[] bins = gen == 4 ? new[] { 2133, 2400, 2666, 2933, 3200 } : gen == 5 ? new[] { 4800, 5200, 5600 } : Array.Empty<int>();
        return bins.Any(b => Math.Abs(b - mts) <= 2);
    }

    public enum XmpState { None, Unknown, On, Off, Lower }

    /// <summary>
    /// Off: the modules run at a JEDEC default and their part number names a higher speed, so XMP/EXPO is probably off.
    /// Lower: below the named speed but not at a JEDEC default (set by hand, or the board holds back with four modules).
    /// On: at least the named speed. Unknown: a part number OrbitTweak cannot read. None: no row (not DDR4/DDR5, mixed,
    /// no speed reported, or a speed that looks halved).
    /// </summary>
    public static (XmpState state, int configured, int rated, int gen) Xmp(IReadOnlyList<Module> mods)
    {
        if (mods.Count == 0) return (XmpState.None, 0, 0, 0);
        int gen = Gen(mods[0].Type);
        if (gen is not (4 or 5) || mods.Any(m => Gen(m.Type) != gen)) return (XmpState.None, 0, 0, gen);
        int cfg = mods.Min(m => m.Configured);
        // a speed outside what that generation runs at: an old BIOS that reports the clock (half the data rate)
        if (gen == 4 ? cfg is < 1866 or > 5600 : cfg is < 3200 or > 9600) return (XmpState.None, 0, 0, gen);
        int rated = int.MaxValue;
        foreach (var m in mods)
        {
            var r = Rated(m.Part);
            if (r is not { } rr || (rr.gen != 0 && rr.gen != gen)) return (XmpState.Unknown, cfg, 0, gen);
            rated = Math.Min(rated, rr.mts);   // mixed kits: the slowest one decides
        }
        if (rated <= cfg + 50) return (XmpState.On, cfg, rated, gen);
        return (IsJedecDefault(gen, cfg) ? XmpState.Off : XmpState.Lower, cfg, rated, gen);
    }

    /// <summary>A laptop: it runs on a battery, or a module is an SO-DIMM. Laptop BIOSes rarely have XMP at all.</summary>
    public static bool Laptop(IReadOnlyList<Module> mods, bool battery) => battery || mods.Any(m => m.FormFactor == 12);

    /// <summary>The two RAM rows ("RAM-Kanäle", "RAM-Takt"); none for what is not known.</summary>
    public static List<Row> RamRows(IReadOnlyList<Module>? mods, bool battery)
    {
        var rows = new List<Row>();
        if (mods is not { Count: > 0 }) return rows;
        bool laptop = Laptop(mods, battery);

        var (cs, n) = Channels(mods);
        if (cs == ChanState.Single)
            rows.Add(new("RAM-Kanäle", mods.Count == 1 ? $"Single Channel: nur ein Riegel ({Sizes(mods)})" : $"Single Channel: {(mods.Count == 2 ? "beide" : "alle")} Riegel im selben Kanal",
                "warn", "hw:ram:chan", "Anleitung", true));
        else if (cs == ChanState.Multi)
            rows.Add(new("RAM-Kanäle", $"{(n == 2 ? "Dual" : n == 3 ? "Triple" : n == 4 ? "Quad" : n + "-fach")} Channel · {Sizes(mods)}", "ok"));

        var (xs, cfg, rated, gen) = Xmp(mods);
        var ddr = "DDR" + gen;
        switch (xs)
        {
            case XmpState.On:
                rows.Add(new("RAM-Takt", $"{cfg} MT/s {ddr}, wie auf den Riegeln angegeben", "ok"));
                break;
            case XmpState.Off when !laptop:
                // DDR4 kits only have XMP; DDR5 kits come with XMP, EXPO (AMD) or both
                rows.Add(new("RAM-Takt", $"{(gen == 5 ? "XMP/EXPO" : "XMP")} wahrscheinlich aus: {cfg} statt {rated} MT/s", "warn", "hw:ram:xmp", "Anleitung", true));
                break;
            case XmpState.Off:
                rows.Add(new("RAM-Takt", $"{cfg} MT/s {ddr}, die Riegel können {rated}. Laptops bieten XMP im BIOS aber meist nicht an", "info", "hw:ram:xmp"));
                break;
            case XmpState.Lower:
                rows.Add(new("RAM-Takt", $"{cfg} MT/s {ddr}, angegeben sind {rated}: von Hand gesenkt oder vom Mainboard gebremst", "info", "hw:ram:xmp"));
                break;
            case XmpState.Unknown:
                rows.Add(new("RAM-Takt", $"{cfg} MT/s {ddr}. Ob deine Riegel mehr können, verrät ihre Teilenummer nicht", "info", "hw:ram:info"));
                break;
        }
        return rows;
    }

    /// <summary>
    /// The maker for the module list: from the part number's own scheme first (many modules report the maker only as a
    /// JEDEC code such as "029E" or "04CD"), then the reported name when it is a name; "" when neither says it.
    /// </summary>
    public static string Maker(string? maker, string? part)
    {
        var p = (part ?? "").Trim().ToUpperInvariant();
        string? byPart =
            p.StartsWith("CM") ? "Corsair"
            : Regex.IsMatch(p, @"^F[345]-") ? "G.Skill"
            : p.StartsWith("KF") || p.StartsWith("KHX") || p.StartsWith("HX") || p.StartsWith("KVR") ? "Kingston"
            : p.StartsWith("BL") || p.StartsWith("CT") || p.StartsWith("CP") ? "Crucial"
            : p.StartsWith("TEAMGROUP") || TeamKit.IsMatch(p) ? "TeamGroup"
            : p.StartsWith("PV") || p.StartsWith("PSD") ? "Patriot"
            : p.StartsWith("AX") || p.StartsWith("AD4") || p.StartsWith("AD5") ? "ADATA"
            : Regex.IsMatch(p, @"^M[34]\d{2}[A-Z]") ? "Samsung"
            : p.StartsWith("HMA") || p.StartsWith("HMC") || p.StartsWith("HMT") ? "SK hynix"
            : p.StartsWith("MTA") || p.StartsWith("MTC") ? "Micron"
            : null;
        if (byPart != null) return byPart;
        var m = Clean(maker, 24);
        // a JEDEC id ("029E", "80CE0000…"), or a placeholder
        if (Regex.IsMatch(m, @"^[0-9A-F]{4,16}$", RegexOptions.IgnoreCase) || Regex.IsMatch(m, @"^(unknown|undefined|not specified|manufacturer\d*|-)$", RegexOptions.IgnoreCase)) return "";
        return m;
    }

    /// <summary>Printable text from SMBIOS or EDID: control characters out, trimmed, cut to max.</summary>
    public static string Clean(string? s, int max)
    {
        var t = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return t.Length > max ? t[..max].TrimEnd() : t;
    }
}
