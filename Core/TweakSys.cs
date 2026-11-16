using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>How a tweak is being switched right now, for the thread that runs Apply or Revert.</summary>
internal static class TweakRun
{
    /// <summary>The user flipped this one switch (not Boost, not "Alle zurücksetzen").</summary>
    [ThreadStatic] public static bool Explicit;
    /// <summary>"Alle zurücksetzen": only what OrbitTweak changed is undone, nothing falls back to a Windows default.</summary>
    [ThreadStatic] public static bool All;
}

/// <summary>
/// Values as they were before a tweak changed them, so switching it off gives back exactly what was there.
/// Per-user settings keep theirs in HKCU, machine settings in HKLM. A value that did not exist is stored as "-".
/// </summary>
internal static class Bk
{
    public const string User = @"HKCU\Software\OrbitTweak\Backup";
    public const string Machine = @"HKLM\SOFTWARE\OrbitTweak\Backup";

    /// <summary>Saves the value as it is now, unless it already is what the tweak sets (then an existing backup is the real one).</summary>
    public static void Keep(string key, string? live, string target, string root = User)
    {
        if (TweakLogic.ShouldBackup(live, target)) Reg.SetStr(root, key, live ?? "-");
    }
    /// <summary>The saved value; null when there is no backup or the value did not exist before.</summary>
    public static string? Take(string key, string root = User) => Reg.Get(root, key) is string v && v != "-" ? v : null;
    public static bool Has(string key, string root = User) => Reg.Get(root, key) != null;
    public static bool HasPrefix(string prefix, string root = User) => Reg.ValueNames(root).Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    public static void Drop(string key, string root = User) => Reg.Del(root, key);
}

/// <summary>
/// Which tweaks OrbitTweak switched on. "Alle zurücksetzen" uses it for tweaks whose "on" state can also be Windows'
/// own default or the user's choice (game mode, HAGS, power plan …): those are only undone when OrbitTweak set them.
/// </summary>
internal static class Marks
{
    private const string K = @"HKLM\SOFTWARE\OrbitTweak\Applied";
    public static bool Has(string id) => Reg.Is(K, id, 1);
    public static void Set(string id) { try { Reg.Set(K, id, 1); } catch { } }
    public static void Clear(string id) => Reg.Del(K, id);
}

/// <summary>SystemParametersInfo: changes the live setting at once and saves it for the next sign-in.</summary>
internal static class Spi
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint a, uint u, ref uint pv, uint f);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint a, uint u, IntPtr pv, uint f);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint a, uint u, [In, Out] int[] pv, uint f);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint a, uint u, [In, Out] uint[] pv, uint f);

    /// <summary>SPIF_UPDATEINIFILE | SPIF_SENDCHANGE.</summary>
    private const uint SAVE = 0x01 | 0x02;
    private static void Ok(bool ok) { if (!ok) throw new Win32Exception(); }

    /// <summary>A setting read through a pointer to a DWORD or BOOL (keyboard delay, menu animation …). uint.MaxValue = failed.</summary>
    public static uint Get(uint action) { uint v = 0; return SystemParametersInfo(action, 0, ref v, 0) ? v : uint.MaxValue; }
    /// <summary>A setting passed in uiParam (keyboard delay and speed).</summary>
    public static void SetParam(uint action, uint value) => Ok(SystemParametersInfo(action, value, IntPtr.Zero, SAVE));
    /// <summary>A BOOL passed by value in pvParam (menu, combo box, tooltip animation …).</summary>
    public static void SetBool(uint action, bool on) => Ok(SystemParametersInfo(action, 0, (IntPtr)(on ? 1 : 0), SAVE));

    public static int[]? GetMouse() { var m = new int[3]; return SystemParametersInfo(0x0003, 0, m, 0) ? m : null; }
    public static void SetMouse(int[] m) => Ok(SystemParametersInfo(0x0004, 0, m, SAVE));

    /// <summary>STICKYKEYS/TOGGLEKEYS/FILTERKEYS/MOUSEKEYS: every field is a DWORD, the first one is cbSize.</summary>
    public static uint[]? GetStruct(uint get, int words)
    {
        var s = new uint[words]; s[0] = (uint)(words * 4);
        return SystemParametersInfo(get, (uint)(words * 4), s, 0) ? s : null;
    }
    public static void SetStruct(uint set, uint[] s) => Ok(SystemParametersInfo(set, (uint)(s.Length * 4), s, SAVE));

    /// <summary>ANIMATIONINFO.iMinAnimate (window minimize/maximize animation), -1 = failed.</summary>
    public static int GetMinAnimate() { var a = new[] { 8, 0 }; return SystemParametersInfo(0x0048, 8, a, 0) ? a[1] : -1; }
    public static void SetMinAnimate(int v) => Ok(SystemParametersInfo(0x0049, 8, new[] { 8, v }, SAVE));

    /// <summary>SPI_SETLANGTOGGLE: Windows reads HKCU\Keyboard Layout\Toggle again.</summary>
    public static void LangToggle() => SystemParametersInfo(0x005B, 0, IntPtr.Zero, 0x02);
}

/// <summary>
/// The per-app graphics settings of Windows (Settings › System › Display › Graphics), one string per exe:
/// "GpuPreference=2;SwapEffectUpgradeEnable=1;…". A tweak only changes its own field and keeps a backup of it.
/// </summary>
internal static class GpuPrefs
{
    public const string Key = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    public const string Global = "DirectXUserGlobalSettings";
    private static string BkKey(string backup) => $@"{Bk.User}\{backup}";

    public static string? Get(string exe, string field) => TweakLogic.Field(Reg.Get(Key, exe) as string, field);
    public static string? GetGlobal(string field) => TweakLogic.Field(Reg.Get(Key, Global) as string, field);
    public static string[] Backed(string backup) => Reg.ValueNames(BkKey(backup));

    public static void SetField(string exe, string field, string val, string backup)
    {
        var cur = Reg.Get(Key, exe) as string;
        if (Reg.Get(BkKey(backup), exe) == null) Reg.SetStr(BkKey(backup), exe, TweakLogic.Field(cur, field) ?? "-");
        var next = TweakLogic.WithField(cur, field, val);
        if (next != null && next != cur) Reg.SetStr(Key, exe, next);
    }

    /// <summary>Puts the saved field back (or removes it when there was none); the value goes only when no field is left.</summary>
    public static void RestoreField(string exe, string field, string backup)
    {
        var b = Reg.Get(BkKey(backup), exe) as string;
        var cur = Reg.Get(Key, exe) as string;
        var next = TweakLogic.WithField(cur, field, b is null or "-" ? null : b);
        if (next == null) Reg.Del(Key, exe);
        else if (next != cur) Reg.SetStr(Key, exe, next);
        Reg.Del(BkKey(backup), exe);
    }

    /// <summary>
    /// Undoes a field everywhere it was set: every exe with a backup (also of FiveM builds that are gone by now).
    /// legacy: the value versions before 2.26 wrote without a backup; such entries under the FiveM folder lose the field.
    /// </summary>
    public static void RestoreAll(string field, string backup, string? legacy)
    {
        var backed = new HashSet<string>(Backed(backup), StringComparer.OrdinalIgnoreCase);
        foreach (var exe in backed) RestoreField(exe, field, backup);
        if (legacy == null || FiveM.Root is not { } root) return;
        foreach (var exe in Reg.ValueNames(Key))
            if (!backed.Contains(exe) && exe.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Get(exe, field) == legacy) RestoreField(exe, field, backup);
    }

    /// <summary>FiveM.exe and the subprocesses that create Direct3D devices: the game and the browser of the game UI (NUI).</summary>
    public static List<string> Targets()
    {
        var list = new List<string>();
        if (FiveM.Exe is { } e) list.Add(e);
        if (FiveM.DataDir is { } d)
        {
            var sub = Path.Combine(d, "cache", "subprocess");
            try
            {
                if (Directory.Exists(sub))
                {
                    list.AddRange(Directory.GetFiles(sub, "*GTAProcess.exe"));
                    list.AddRange(Directory.GetFiles(sub, "*ChromeBrowser.exe"));
                }
            }
            catch { }
        }
        return list;
    }

    /// <summary>The game exe of the newest build (FiveM.exe when no build was downloaded yet).</summary>
    public static string? Main()
    {
        try
        {
            if (FiveM.DataDir is { } d && Directory.Exists(Path.Combine(d, "cache", "subprocess")))
            {
                var g = new DirectoryInfo(Path.Combine(d, "cache", "subprocess")).GetFiles("*GTAProcess.exe").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (g != null) return g.FullName;
            }
        }
        catch { }
        return FiveM.Exe;
    }

    /// <summary>While a field is switched on, an exe of a new FiveM build gets it too. Returns how many were added.</summary>
    public static int Upkeep(string field, string val, string backup)
    {
        if (Backed(backup).Length == 0) return 0;
        int n = 0;
        foreach (var exe in Targets())
            if (Get(exe, field) != val && Reg.Get(BkKey(backup), exe) == null) { SetField(exe, field, val, backup); n++; }
        return n;
    }
}

/// <summary>What the hardware of this PC is (asked once, it does not change while the app runs).</summary>
internal static class Hw
{
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
    [StructLayout(LayoutKind.Sequential)] private struct SYSTEM_POWER_STATUS { public byte AC, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
    [DllImport("powrprof.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool GetPwrCapabilities(IntPtr caps);

    private static readonly Lazy<bool> _switchable = new(ProbeSwitchable);
    private static readonly Lazy<bool> _battery = new(() => GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryFlag != 255);
    private static readonly Lazy<bool> _standby = new(ProbeModernStandby);

    /// <summary>
    /// Two GPUs Windows can choose between (Optimus laptops, desktops with the iGPU on): the adapter for "high performance"
    /// is another one than for "power saving". The same test FiveM uses itself; it only sees adapters that are present.
    /// </summary>
    public static bool SwitchableGpus => _switchable.Value;
    public static bool HasBattery => _battery.Value;
    /// <summary>Modern Standby (S0 low power idle): Windows only allows the balanced plan and the power slider.</summary>
    public static bool ModernStandby => _standby.Value;
    public static bool DualCcdX3D => TweakLogic.IsDualCcdX3D(SysInfo.CpuName);

    private static unsafe bool ProbeSwitchable()
    {
        try
        {
            var iid1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid1, out var f1) < 0 || f1 == IntPtr.Zero) return false;
            try
            {
                var iid6 = new Guid("c1b6694f-ff09-44a9-b03c-77900a0a1d17");   // IDXGIFactory6 (DXGI 1.6, Windows 10 1803+)
                if (Marshal.QueryInterface(f1, ref iid6, out var f6) < 0 || f6 == IntPtr.Zero) return false;
                try
                {
                    var hi = FirstHardware(f6, 2);   // DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE
                    var lo = FirstHardware(f6, 1);   // DXGI_GPU_PREFERENCE_MINIMUM_POWER
                    return hi != null && lo != null && hi != lo;
                }
                finally { Marshal.Release(f6); }
            }
            finally { Marshal.Release(f1); }
        }
        catch { return false; }
    }

    /// <summary>LUID of the first adapter that is not a software one, in the order Windows gives for that preference.</summary>
    private static unsafe long? FirstHardware(IntPtr f6, int pref)
    {
        var iidA = new Guid("29038f61-3839-4626-91fd-086879011a05");   // IDXGIAdapter1
        var enumByPref = (delegate* unmanaged<IntPtr, uint, int, Guid*, IntPtr*, int>)(*(IntPtr**)f6)[29];   // IDXGIFactory6::EnumAdapterByGpuPreference
        byte* desc = stackalloc byte[320];                                                                    // DXGI_ADAPTER_DESC1
        for (uint i = 0; i < 16; i++)
        {
            IntPtr a;
            if (enumByPref(f6, i, pref, &iidA, &a) < 0 || a == IntPtr.Zero) return null;
            try
            {
                var getDesc1 = (delegate* unmanaged<IntPtr, byte*, int>)(*(IntPtr**)a)[10];                 // IDXGIAdapter1::GetDesc1
                if (getDesc1(a, desc) < 0) continue;
                if ((*(uint*)(desc + 304) & 2) != 0) continue;                                                  // DXGI_ADAPTER_FLAG_SOFTWARE
                return *(long*)(desc + 296);                                                                   // AdapterLuid
            }
            finally { Marshal.Release(a); }
        }
        return null;
    }

    private static bool ProbeModernStandby()
    {
        var p = Marshal.AllocHGlobal(256);
        try
        {
            for (int i = 0; i < 256; i++) Marshal.WriteByte(p, i, 0);
            return GetPwrCapabilities(p) && Marshal.ReadByte(p, 20) != 0;   // SYSTEM_POWER_CAPABILITIES.AoAc
        }
        catch { return false; }
        finally { Marshal.FreeHGlobal(p); }
    }

    // ---------- hardware-accelerated GPU scheduling (HAGS) ----------
    [StructLayout(LayoutKind.Sequential)] private struct ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct ADAPTERINFO { public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint NumOfSources; public int bPrecise; }
    [StructLayout(LayoutKind.Sequential)] private struct QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pData; public uint Size; }
    [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref ENUMADAPTERS2 e);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QUERYADAPTERINFO q);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref uint h);
    private const int KMTQAITYPE_WDDM_2_7_CAPS = 70;

    /// <summary>
    /// What the graphics driver says about HAGS (D3DKMT_WDDM_2_7_CAPS of the first adapter that supports it).
    /// known = false when the driver could not be asked (old Windows or driver): then nothing is assumed.
    /// </summary>
    public static (bool known, bool supported, bool enabled, bool byDefault) Hags()
    {
        try
        {
            var e = new ENUMADAPTERS2();
            if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0) return (false, false, false, false);
            int sz = Marshal.SizeOf<ADAPTERINFO>();
            var buf = Marshal.AllocHGlobal(sz * (int)e.NumAdapters);
            var caps = Marshal.AllocHGlobal(4);
            try
            {
                e.pAdapters = buf;
                if (D3DKMTEnumAdapters2(ref e) != 0) return (false, false, false, false);
                bool known = false; (bool, bool, bool)? hit = null;
                for (int i = 0; i < e.NumAdapters; i++)
                {
                    var info = Marshal.PtrToStructure<ADAPTERINFO>(buf + i * sz);
                    Marshal.WriteInt32(caps, 0);
                    var q = new QUERYADAPTERINFO { hAdapter = info.hAdapter, Type = KMTQAITYPE_WDDM_2_7_CAPS, pData = caps, Size = 4 };
                    if (D3DKMTQueryAdapterInfo(ref q) == 0)
                    {
                        uint v = (uint)Marshal.ReadInt32(caps);
                        if ((v & ~0xFu) == 0)   // the 28 reserved bits are 0: an answer to this very question
                        {
                            known = true;
                            if (hit == null && (v & 1) != 0) hit = (true, (v & 2) != 0, (v & 4) != 0);
                        }
                    }
                    var h = info.hAdapter; D3DKMTCloseAdapter(ref h);
                }
                if (hit is { } x) return (true, x.Item1, x.Item2, x.Item3);
                return (known, false, false, false);
            }
            finally { Marshal.FreeHGlobal(buf); Marshal.FreeHGlobal(caps); }
        }
        catch { return (false, false, false, false); }
    }
}

/// <summary>Power plans: read with powrprof (works for hidden settings, no localized text), written with powercfg.</summary>
internal static class Power
{
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid set, out uint idx);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid set, out uint idx);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);

    public const string BALANCED = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HIGHPERF = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string ULTIMATE = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public static readonly Regex Guid_ = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    public static string? Active()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var p) == 0 && p != IntPtr.Zero)
                try { return Marshal.PtrToStructure<Guid>(p).ToString(); } finally { LocalFree(p); }
        }
        catch { }
        var m = Guid_.Match(Proc.Run("powercfg", "/getactivescheme"));
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    /// <summary>Lines of "powercfg /list" (GUID and name of every plan).</summary>
    public static string[] ListLines() => Proc.Run("powercfg", "/list").Split('\n');
    public static List<string> Schemes() => ListLines().Select(l => Guid_.Match(l)).Where(m => m.Success).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
    public static bool Exists(string g) => Schemes().Contains(g.ToLowerInvariant());

    /// <summary>The value index of a setting in a plan, -1 when the plan or the setting does not exist.</summary>
    public static int Read(string scheme, string sub, string set, bool ac)
    {
        try
        {
            Guid s = new(scheme), a = new(sub), b = new(set);
            uint r = ac ? PowerReadACValueIndex(IntPtr.Zero, ref s, ref a, ref b, out var v) : PowerReadDCValueIndex(IntPtr.Zero, ref s, ref a, ref b, out v);
            return r == 0 ? (int)v : -1;
        }
        catch { return -1; }
    }

    public static void Write(string scheme, string sub, string set, bool ac, int v) => Proc.Run("powercfg", $"/set{(ac ? "ac" : "dc")}valueindex {scheme} {sub} {set} {v}");
    /// <summary>Makes Windows take changed values of the active plan.</summary>
    public static void Refresh() => Proc.Run("powercfg", "/setactive SCHEME_CURRENT");

    /// <summary>The value Windows (or the PC maker) ships for this plan, null when there is none (for example a copied plan).</summary>
    public static int? Default(string scheme, string sub, string set, bool ac)
    {
        var k = $@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\{sub}\{set}\DefaultPowerSchemeValues\{scheme}";
        return (Reg.Get(k, ac ? "ProvACSettingIndex" : "ProvDCSettingIndex") ?? Reg.Get(k, ac ? "ACSettingIndex" : "DCSettingIndex")) as int?;
    }

    /// <summary>
    /// Every plan whose value is exactly "bad" while its Windows default is something else gets the default back
    /// (or fallback(plan) when there is no default). Plans whose default is "bad" stay as they are. Returns whether anything changed.
    /// </summary>
    public static bool RestoreDefaultWhere(string sub, string set, int bad, bool ac, Func<string, int?> fallback)
    {
        bool changed = false;
        foreach (var s in Schemes())
        {
            if (Read(s, sub, set, ac) != bad) continue;
            var d = Default(s, sub, set, ac) ?? fallback(s);
            if (d is int v && v != bad) { Write(s, sub, set, ac, v); changed = true; }
        }
        if (changed) Refresh();
        return changed;
    }
}

/// <summary>Windows' quality-of-service tag of a process (SetProcessInformation, ProcessPowerThrottling).</summary>
internal static class Qos
{
    [StructLayout(LayoutKind.Sequential)] private struct PT { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessInformation(IntPtr h, int cls, ref PT info, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessInformation(IntPtr h, int cls, ref PT info, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    private const int ProcessPowerThrottling = 4;
    private const uint PROCESS_SET_INFORMATION = 0x0200, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    /// <summary>PROCESS_POWER_THROTTLING_EXECUTION_SPEED: EcoQoS when set in the state, HighQoS when only in the control mask.</summary>
    public const uint SPEED = 0x1;
    /// <summary>PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION: in the control mask with state 0 = always honor timer requests.</summary>
    public const uint TIMER = 0x4;

    private static bool On(IntPtr h, uint control, uint state)
    {
        var st = new PT { Version = 1, ControlMask = control, StateMask = state };
        return SetProcessInformation(h, ProcessPowerThrottling, ref st, Marshal.SizeOf<PT>());
    }

    public static bool Set(int pid, uint control, uint state)
    {
        var h = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return On(h, control, state); } finally { CloseHandle(h); }
    }

    /// <summary>The masks a process has now; null when they cannot be read.</summary>
    public static (uint ctl, uint st)? Get(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var st = new PT { Version = 1 };
            return GetProcessInformation(h, ProcessPowerThrottling, ref st, Marshal.SizeOf<PT>()) ? (st.ControlMask, st.StateMask) : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Full speed and kept timer accuracy for the game, also minimized or behind other windows (Windows 10 may refuse the timer part).</summary>
    public static bool High(int pid) => Set(pid, SPEED | TIMER, 0) || Set(pid, SPEED, 0);
    /// <summary>Back to "Windows decides".</summary>
    public static void Reset(int pid) => Set(pid, 0, 0);
    public static bool SetOwn(uint control, uint state) { try { return On(GetCurrentProcess(), control, state); } catch { return false; } }
}

/// <summary>The 0.5 ms timer request of the "timer" tweak: held only while the game runs.</summary>
internal static class GameTimer
{
    [DllImport("ntdll.dll")] private static extern int NtSetTimerResolution(uint desired, [MarshalAs(UnmanagedType.U1)] bool set, out uint current);
    private static readonly object Gate = new();
    private static bool _held;
    public static bool Held { get { lock (Gate) return _held; } }

    /// <summary>Asks for 0.5 ms. Returns the resolution Windows runs at now (ms), null when it was held already.</summary>
    public static double? Hold()
    {
        lock (Gate)
        {
            if (_held) return null;
            // Windows 11 may ignore the timer request of a program whose window is minimized and silent (OrbitTweak in the tray)
            Qos.SetOwn(Qos.TIMER, 0);
            uint cur = 0;
            try { NtSetTimerResolution(5000, true, out cur); } catch { }
            _held = true;
            return cur / 10000.0;
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            if (!_held) return;
            try { NtSetTimerResolution(5000, false, out _); } catch { }
            Qos.SetOwn(0, 0);
            _held = false;
        }
    }
}

/// <summary>
/// Wi-Fi while the game runs: no background scans for other networks, media streaming mode on. Both are held per
/// client handle of the WLAN service, so they end with the handle (also when the app ends without tidying up).
/// </summary>
internal static class WlanGame
{
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint ver, IntPtr res, out uint negotiated, out IntPtr h);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr h, IntPtr res);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr h, IntPtr res, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanSetInterface(IntPtr h, ref Guid iface, int opcode, uint size, IntPtr data, IntPtr res);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr p);
    private const int BG_SCAN = 2, STREAMING = 3;   // wlan_intf_opcode_background_scan_enabled, wlan_intf_opcode_media_streaming_mode
    private const int ENTRY = 532;                  // WLAN_INTERFACE_INFO: GUID 16 + WCHAR[256] 512 + state 4
    private static readonly object Gate = new();
    private static IntPtr _h;
    private static readonly HashSet<Guid> Ifs = new();

    /// <summary>A handle is open (the settings may be held): Restore gives everything back.</summary>
    public static bool Active { get { lock (Gate) return _h != IntPtr.Zero; } }

    /// <summary>Sets both on every connected Wi-Fi adapter (again after a reconnect, which resets them). True the first time it took hold.</summary>
    public static bool Apply()
    {
        lock (Gate)
        {
            try
            {
                if (_h == IntPtr.Zero && WlanOpenHandle(2, IntPtr.Zero, out _, out _h) != 0) { _h = IntPtr.Zero; return false; }
                bool first = Ifs.Count == 0;
                if (WlanEnumInterfaces(_h, IntPtr.Zero, out var list) != 0) return false;
                try
                {
                    int n = Marshal.ReadInt32(list);
                    for (int i = 0; i < n; i++)
                    {
                        var e = list + 8 + i * ENTRY;
                        var g = Marshal.PtrToStructure<Guid>(e);
                        if (Marshal.ReadInt32(e + 16 + 512) != 1) continue;   // wlan_interface_state_connected
                        bool a = Set(g, BG_SCAN, 0), b = Set(g, STREAMING, 1);
                        if (a || b) Ifs.Add(g);
                    }
                }
                finally { WlanFreeMemory(list); }
                return first && Ifs.Count > 0;
            }
            catch { return false; }
        }
    }

    /// <summary>Gives every adapter its scans back and closes the handle. True when an adapter had been changed.</summary>
    public static bool Restore()
    {
        lock (Gate)
        {
            if (_h == IntPtr.Zero) return false;
            bool any = Ifs.Count > 0;
            foreach (var g in Ifs) { try { Set(g, BG_SCAN, 1); Set(g, STREAMING, 0); } catch { } }
            Ifs.Clear();
            try { WlanCloseHandle(_h, IntPtr.Zero); } catch { }
            _h = IntPtr.Zero;
            return any;
        }
    }

    private static bool Set(Guid g, int op, int val)
    {
        var p = Marshal.AllocHGlobal(4);
        try { Marshal.WriteInt32(p, val); return WlanSetInterface(_h, ref g, op, 4, p, IntPtr.Zero) == 0; }
        finally { Marshal.FreeHGlobal(p); }
    }
}

/// <summary>The network connection the PC uses to reach the internet.</summary>
internal static class NetLink
{
    public static NetworkInterface? DefaultRoute()
    {
        try
        {
            var up = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any))).ToList();
            return up.FirstOrDefault(n => n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet)
                   ?? up.FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>null = no connection, true = Wi-Fi, false = cable (or something else).</summary>
    public static bool? DefaultIsWifi() => DefaultRoute() is { } n ? n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 : null;

    public static bool HasWifi()
    {
        try { return NetworkInterface.GetAllNetworkInterfaces().Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211); }
        catch { return false; }
    }
}

/// <summary>Which processes play sound right now (active audio sessions on every output device).</summary>
internal static class AudioSessions
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator { [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection devices); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint n); [PreserveSig] int Item(uint i, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface); }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator { [PreserveSig] int GetCount(out int n); [PreserveSig] int GetSession(int i, [MarshalAs(UnmanagedType.IUnknown)] out object session); }
    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr ctx);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr ctx);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(IntPtr g, IntPtr ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    public static HashSet<int> ActivePids()
    {
        var set = new HashSet<int>();
        var com = new List<object>();
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator(); com.Add(en);
            if (en.EnumAudioEndpoints(0, 1, out var col) != 0 || col == null) return set;   // eRender, DEVICE_STATE_ACTIVE
            com.Add(col);
            col.GetCount(out var n);
            for (uint i = 0; i < n; i++)
            {
                if (col.Item(i, out var dev) != 0 || dev == null) continue;
                com.Add(dev);
                var iid = typeof(IAudioSessionManager2).GUID;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out var o) != 0 || o is not IAudioSessionManager2 mgr) continue;
                com.Add(o);
                if (mgr.GetSessionEnumerator(out var se) != 0 || se == null) continue;
                com.Add(se);
                se.GetCount(out var c);
                for (int k = 0; k < c; k++)
                {
                    if (se.GetSession(k, out var so) != 0 || so == null) continue;
                    com.Add(so);
                    if (so is IAudioSessionControl2 s2 && s2.GetState(out var st) == 0 && st == 1 && s2.GetProcessId(out var pid) == 0 && pid != 0)
                        set.Add((int)pid);
                }
            }
        }
        catch { }
        finally { foreach (var o in com) try { Marshal.ReleaseComObject(o); } catch { } }
        return set;
    }
}
