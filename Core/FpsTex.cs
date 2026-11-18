using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static OrbitTweak.Core.FpsLimitLogic;

namespace OrbitTweak.Core;

// =====================================================================
//  FPS-Limit für FiveM: NVIDIA's "Max Frame Rate" (FRL_FPS) in the driver profile of FiveM's game processes, through
//  NVAPI's driver settings (DRS), the way NVIDIA Profile Inspector and the Control Panel write it. The ids, struct
//  layouts and decisions are in FpsTexLogic.cs. Without nvapi64.dll (no NVIDIA driver) nothing is loaded or touched.
//  DRS does not need the game closed: the driver reads the profile when a game process starts, so a change counts from
//  the next start of FiveM.
// =====================================================================
internal static unsafe class FpsLimit
{
    /// <summary>"FpsLimit": the limit OrbitTweak keeps for FiveM (absent = off). Machine-wide like the driver profiles.</summary>
    private const string OWN = @"HKLM\SOFTWARE\OrbitTweak";
    /// <summary>One value per profile of someone else whose limit OrbitTweak set: the name of the profile, its value before ("-" = none of its own).</summary>
    private const string BK = Bk.Machine + @"\FpsLimit";

    private static readonly object Gate = new();
    private static bool? _ok;
    private static delegate* unmanaged[Cdecl]<IntPtr*, int> _createSession;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _destroySession, _loadSettings, _saveSettings;
    private static delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr*, int> _createProfile;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int> _deleteProfile;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int> _getProfileInfo, _createApplication, _setSetting;
    private static delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr*, int> _findProfileByName;
    private static delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr*, byte*, int> _findApplicationByName;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, byte*, int> _getSetting;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int> _deleteProfileSetting;
    /// <summary>The exe names the profiles were last brought in line for (null = not since the app started).</summary>
    private static List<string>? _known;

    /// <summary>NVIDIA's driver with the DRS functions is there (asked once).</summary>
    public static bool Available { get { lock (Gate) return _ok ??= Load(); } }

    private static bool Load()
    {
        try
        {
            // no NVIDIA driver, no nvapi64.dll: then nothing is loaded and nothing touched
            if (!NativeLibrary.TryLoad("nvapi64.dll", out var lib) || !NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out var q)) return false;
            var qi = (delegate* unmanaged[Cdecl]<uint, IntPtr>)q;
            var init = (delegate* unmanaged[Cdecl]<int>)qi(IdInitialize);
            if (init == null || init() != OK) return false;   // e.g. the driver is installed but no NVIDIA card is in use
            _createSession = (delegate* unmanaged[Cdecl]<IntPtr*, int>)qi(IdCreateSession);
            _destroySession = (delegate* unmanaged[Cdecl]<IntPtr, int>)qi(IdDestroySession);
            _loadSettings = (delegate* unmanaged[Cdecl]<IntPtr, int>)qi(IdLoadSettings);
            _saveSettings = (delegate* unmanaged[Cdecl]<IntPtr, int>)qi(IdSaveSettings);
            _createProfile = (delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr*, int>)qi(IdCreateProfile);
            _deleteProfile = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)qi(IdDeleteProfile);
            _getProfileInfo = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int>)qi(IdGetProfileInfo);
            _findProfileByName = (delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr*, int>)qi(IdFindProfileByName);
            _createApplication = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int>)qi(IdCreateApplication);
            _findApplicationByName = (delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr*, byte*, int>)qi(IdFindApplicationByName);
            _setSetting = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int>)qi(IdSetSetting);
            _getSetting = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, byte*, int>)qi(IdGetSetting);
            _deleteProfileSetting = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int>)qi(IdDeleteProfileSetting);
            return _createSession != null && _destroySession != null && _loadSettings != null && _saveSettings != null
                && _createProfile != null && _deleteProfile != null && _getProfileInfo != null && _findProfileByName != null
                && _createApplication != null && _findApplicationByName != null && _setSetting != null && _getSetting != null
                && _deleteProfileSetting != null;
        }
        catch { return false; }
    }

    /// <summary>The limit OrbitTweak keeps (0 = off).</summary>
    public static int Stored => Reg.Get(OWN, "FpsLimit") is int v ? v : 0;

    // ---------- for the page ----------
    public static object Get()
    {
        int hz = RefreshHz(), suggestion = Suggestion(hz);
        string? reason = !Available ? "nonvidia" : FiveM.Root == null ? "nofivem" : null;
        if (reason != null) return new { available = false, reason, limit = 0, hz, suggestion, profile = (string?)null, ours = false, exes = 0 };
        var paths = ExePaths(null);
        // a profile is only named once the driver has one for the game: with no exe yet (FiveM never started) or a new
        // build not put in yet, there is none, even while OrbitTweak keeps a limit
        int kept = Stored, limit = kept; string? profile = null;
        try
        {
            lock (Gate)
            {
                // what the driver applies to the newest build (the one FiveM started last)
                var newest = paths.OrderByDescending(p => { try { return File.GetLastWriteTimeUtc(p); } catch { return DateTime.MinValue; } }).FirstOrDefault();
                var seen = newest == null ? null : Session(false, s =>
                {
                    if (ProfileOf(s, newest) is not { } p) return ((int, string)?)null;
                    var g = GetFrl(s, p.h);
                    return (ProfileLimit(g.found, g.location, g.value), p.name);
                });
                // an exe in no profile yet is put in on the next start of FiveM (Upkeep): until then the kept limit is shown
                if (seen is { } x) (limit, profile) = (x.Item1, x.Item1 > 0 || kept > 0 ? x.Item2 : null);
            }
        }
        catch { }
        // ours: OrbitTweak keeps a limit; else a limit shown comes from a profile the user or another tool set
        return new { available = true, reason, limit, hz, suggestion, profile, ours = kept > 0, exes = paths.Count };
    }

    /// <summary>Sets the limit (0 = off: every profile OrbitTweak changed gets its value back, its own profile goes).</summary>
    public static (bool ok, string? error) SetLimit(int limit)
    {
        if (!Valid(limit)) return (false, $"Erlaubt sind Aus oder {Min} bis {Max} FPS.");
        if (!Available) return (false, "Kein NVIDIA-Treiber gefunden. Das FPS-Limit gibt es nur mit einer NVIDIA-Grafikkarte.");
        try
        {
            lock (Gate)
            {
                int before = Stored;
                var paths = ExePaths(RunningGame());
                var done = new List<string>();
                Session(true, s => limit == 0 ? Off(s, before, done) : Apply(s, limit, paths));
                foreach (var name in done) Reg.Del(BK, name);   // backups go only once the driver has saved
                if (limit == 0) Reg.Del(OWN, "FpsLimit"); else Reg.Set(OWN, "FpsLimit", limit);
                _known = limit == 0 ? null : paths.Select(p => Path.GetFileName(p)).ToList();
            }
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>
    /// From the FiveM probe while FiveM runs: a new game build brings a new exe, which gets the limit too (into the own
    /// profile, or its own profile's limit with a backup). Cheap until the list of exes changes. Returns how many exes
    /// were added to the own profile.
    /// </summary>
    public static int Upkeep(string? runningProcess)
    {
        int limit = Stored;
        if (limit == 0) return 0;
        var paths = ExePaths(runningProcess);
        var names = paths.Select(p => Path.GetFileName(p)).ToList();
        if (!NeedsSync(names, _known) || !Available) return 0;
        lock (Gate)
        {
            try { return Session(true, s => Apply(s, limit, paths)); }
            finally { _known = names; }   // a driver that refuses is not asked again every few seconds
        }
    }

    /// <summary>Full paths of the game exes: FiveM's subprocess folder holds one per build, the running one is among them.</summary>
    private static List<string> ExePaths(string? runningProcess)
    {
        if (FiveM.DataDir is not { } d) return new();
        var sub = Path.Combine(d, "cache", "subprocess");
        return GameExes(FiveM.GameExecutables(), runningProcess).Select(n => Path.Combine(sub, n)).ToList();
    }

    private static string? RunningGame()
    {
        try { using var g = FiveM.GameProcess(); return g?.ProcessName; } catch { return null; }
    }

    // ---------- writing ----------
    private static int Apply(IntPtr s, int limit, List<string> paths)
    {
        var found = paths.Select(p => (exe: Path.GetFileName(p), prof: ProfileOf(s, p))).ToList();
        var plan = Decide(found.Select(f => (f.exe, f.prof?.name)));
        var own = FindProfile(s, OwnProfile);
        if (plan.AddToOwn.Count > 0 && own == IntPtr.Zero) own = CreateOwnProfile(s);
        int added = 0;
        foreach (var exe in plan.AddToOwn)
        {
            int st = AddApp(s, own, exe);
            if (st == OK) added++;
            else if (st != ExecutableAlreadyInUse) Check(st, "CreateApplication");   // in a profile under its full path: that one is found next time
        }
        if (own != IntPtr.Zero) SetFrl(s, own, (uint)limit);
        foreach (var name in plan.Foreign)
        {
            var h = found.First(f => f.prof is { } p && p.name.Equals(name, StringComparison.OrdinalIgnoreCase)).prof!.Value.h;
            // the value before OrbitTweak's first change stays the backup, also when the limit changes again
            if (!Bk.Has(name, BK)) { var g = GetFrl(s, h); Reg.SetStr(BK, name, Backup(g.found, g.location, g.predefined, g.value)); }
            SetFrl(s, h, (uint)limit);
        }
        return added;
    }

    /// <summary>Every profile of someone else gets its value from before back (a profile that is gone, or whose limit the
    /// user changed since, is left as it is), the own profile is deleted. <paramref name="done"/>: backups that are used up.</summary>
    private static int Off(IntPtr s, int before, List<string> done)
    {
        foreach (var name in Reg.ValueNames(BK))
        {
            var h = FindProfile(s, name);
            if (h != IntPtr.Zero)
            {
                var g = GetFrl(s, h);
                if (ShouldRestore(ProfileLimit(g.found, g.location, g.value), before))
                {
                    if (RestoreValue(Reg.Get(BK, name) as string) is { } v) SetFrl(s, h, v);
                    else UnsetFrl(s, h);
                }
            }
            done.Add(name);
        }
        var own = FindProfile(s, OwnProfile);
        if (own != IntPtr.Zero) Check(_deleteProfile(s, own), "DeleteProfile");
        return 0;
    }

    // ---------- NVAPI DRS ----------
    private static T Session<T>(bool save, Func<IntPtr, T> body)
    {
        IntPtr s;
        Check(_createSession(&s), "CreateSession");
        try
        {
            Check(_loadSettings(s), "LoadSettings");
            var r = body(s);
            if (save) Check(_saveSettings(s), "SaveSettings");
            return r;
        }
        finally { _destroySession(s); }
    }

    private static void Check(int st, string what)
    {
        if (st == OK) return;
        throw new InvalidOperationException(st == InvalidUserPrivilege
            ? "Der NVIDIA-Treiber verlangt Adminrechte für sein Profil."
            : $"Der NVIDIA-Treiber hat abgelehnt ({what}, Fehler {st}).");
    }

    /// <summary>The profile the driver applies to the exe: asked with the full path (then the driver answers exactly that),
    /// else by its name. null = no profile of its own.</summary>
    private static (IntPtr h, string name)? ProfileOf(IntPtr s, string path)
    {
        foreach (var n in new[] { path, Path.GetFileName(path) })
        {
            var app = new byte[AppSize]; U32(app, 0, AppVer);
            var str = Str(n);
            IntPtr h; int st;
            fixed (byte* a = app, w = str) st = _findApplicationByName(s, w, &h, a);
            if (st == OK) return (h, ProfileName(s, h));
            if (st != ExecutableNotFound && st != ProfileNotFound && st != PathAmbiguous) Check(st, "FindApplicationByName");
        }
        return null;
    }

    private static string ProfileName(IntPtr s, IntPtr h)
    {
        var p = new byte[ProfileSize]; U32(p, 0, ProfileVer);
        fixed (byte* b = p) Check(_getProfileInfo(s, h, b), "GetProfileInfo");
        return GetStr(p, ProfName);
    }

    private static IntPtr FindProfile(IntPtr s, string name)
    {
        var str = Str(name); IntPtr h; int st;
        fixed (byte* w = str) st = _findProfileByName(s, w, &h);
        if (st == ProfileNotFound) return IntPtr.Zero;
        Check(st, "FindProfileByName");
        return h;
    }

    private static IntPtr CreateOwnProfile(IntPtr s)
    {
        var p = new byte[ProfileSize]; U32(p, 0, ProfileVer); PutStr(p, ProfName, OwnProfile);
        IntPtr h;
        fixed (byte* b = p) Check(_createProfile(s, b, &h), "CreateProfile");
        return h;
    }

    private static int AddApp(IntPtr s, IntPtr profile, string exe)
    {
        var app = new byte[AppSize]; U32(app, 0, AppVer);
        PutStr(app, AppName, exe); PutStr(app, AppFriendly, FriendlyName(exe));
        fixed (byte* a = app) return _createApplication(s, profile, a);
    }

    private static (bool found, uint location, bool predefined, uint value) GetFrl(IntPtr s, IntPtr h)
    {
        var b = new byte[SettingSize]; U32(b, 0, SettingVer);
        int st;
        fixed (byte* p = b) st = _getSetting(s, h, FrlFpsId, p);
        if (st == SettingNotFound) return (false, 0, false, 0);
        Check(st, "GetSetting");
        return (true, U32(b, SetLocation), U32(b, SetIsCurPredefined) != 0, U32(b, SetCurrent));
    }

    private static void SetFrl(IntPtr s, IntPtr h, uint value)
    {
        var b = new byte[SettingSize];
        U32(b, 0, SettingVer); U32(b, SetId, FrlFpsId); U32(b, SetType, 0 /* NVDRS_DWORD_TYPE */); U32(b, SetCurrent, value);
        fixed (byte* p = b) Check(_setSetting(s, h, p), "SetSetting");
    }

    private static void UnsetFrl(IntPtr s, IntPtr h)
    {
        int st = _deleteProfileSetting(s, h, FrlFpsId);
        if (st != SettingNotFound) Check(st, "DeleteProfileSetting");
    }

    private static void U32(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);
    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    /// <summary>An NvAPI_UnicodeString: UTF-16, zero-filled to its full 2048 characters.</summary>
    private static byte[] Str(string s) { var b = new byte[StrBytes]; PutStr(b, 0, s); return b; }
    private static void PutStr(byte[] b, int at, string s) => Encoding.Unicode.GetBytes(s.AsSpan(0, Math.Min(s.Length, StrBytes / 2 - 1)), b.AsSpan(at, StrBytes));
    private static string GetStr(byte[] b, int at)
    {
        var c = MemoryMarshal.Cast<byte, char>(b.AsSpan(at, StrBytes));
        int n = c.IndexOf('\0');
        return new string(n < 0 ? c : c[..n]);
    }

    // ---------- the monitor ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string? device, int mode, ref DEVMODE dm);

    /// <summary>Refresh rate of the main screen right now (ENUM_CURRENT_SETTINGS), 0 when unknown (1 = "hardware default").</summary>
    public static int RefreshHz()
    {
        try
        {
            var dm = new DEVMODE { dmDeviceName = "", dmFormName = "", dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            return EnumDisplaySettingsW(null, -1, ref dm) && dm.dmDisplayFrequency > 1 ? dm.dmDisplayFrequency : 0;
        }
        catch { return 0; }
    }
}

// =====================================================================
//  Texturbudget: vid_budgetScale in FiveM's fivem.cfg (see TexBudgetLogic). Written only while FiveM is closed (FiveM
//  writes the whole file itself while it runs), with the value before kept in the backup store; every other line stays
//  byte for byte (the file is read and written as Latin-1, which keeps any byte as it is).
// =====================================================================
internal static unsafe class TexBudget
{
    /// <summary>In Bk.User: the value word before the first change, "-" when the file had no such line.</summary>
    private const string BK = "tex.budget";

    /// <summary>fxd:/fivem.cfg; fxd:/ is %AppData%\CitizenFX\ (like Gfx.FiveMPath).</summary>
    public static string CfgPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX", "fivem.cfg");

    public static object Get()
    {
        var f = CfgPath;
        bool found = File.Exists(f);
        int value = 0;
        if (found) try { value = TexBudgetLogic.Read(Load(f).text).value; } catch { found = false; }
        long vram = VramMb;
        int? before = Bk.Has(BK) ? TexBudgetLogic.Parse(Bk.Take(BK)) : null;   // "-" (no line) is FiveM's default 0
        return new { found, value, max = TexBudgetLogic.Max, suggestion = TexBudgetLogic.Suggestion(vram), vramMb = vram, running = FiveM.IsRunning, before, path = f };
    }

    /// <summary>Writes the value (restore: the value before the first change, also "no line" when there was none).</summary>
    public static (int value, string? error) Set(int value, bool restore)
    {
        var f = CfgPath;
        if (!restore && !TexBudgetLogic.Valid(value)) return (0, $"Erlaubt ist 0 bis {TexBudgetLogic.Max}.");
        if (FiveM.IsRunning) return (0, "Bitte FiveM zuerst schließen, FiveM schreibt die Datei beim Spielen selbst neu.");
        if (!File.Exists(f)) return (0, "fivem.cfg nicht gefunden. Starte FiveM einmal, dann legt FiveM die Datei an.");
        if (restore && !Bk.Has(BK)) return (0, "Es gibt noch kein Backup.");
        try
        {
            var (text, bom) = Load(f);
            var (present, raw, _) = TexBudgetLogic.Read(text);
            string? next;
            if (restore) next = Reg.Get(Bk.User, BK) is string b && b != "-" ? b : null;
            else
            {
                if (!Bk.Has(BK)) Reg.SetStr(Bk.User, BK, present ? raw ?? "" : "-");
                next = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            var updated = TexBudgetLogic.Write(text, next);
            if (updated != text) Save(f, bom + updated);
            if (restore) Bk.Drop(BK);
            return (TexBudgetLogic.Read(updated).value, null);
        }
        catch (Exception ex) { return (0, ex.Message); }
    }

    /// <summary>The file as Latin-1 text (each byte one character), a UTF-8 BOM kept apart.</summary>
    private static (string text, string bom) Load(string f)
    {
        var s = Encoding.Latin1.GetString(File.ReadAllBytes(f));
        return s.StartsWith("\u00EF\u00BB\u00BF", StringComparison.Ordinal) ? (s[3..], s[..3]) : (s, "");
    }

    /// <summary>Writes the bytes back; a file the player made read-only is read-only again afterwards.</summary>
    private static void Save(string f, string text)
    {
        var attr = File.GetAttributes(f);
        bool ro = attr.HasFlag(FileAttributes.ReadOnly);
        if (ro) File.SetAttributes(f, attr & ~FileAttributes.ReadOnly);
        try { File.WriteAllBytes(f, Encoding.Latin1.GetBytes(text)); }
        finally { if (ro) File.SetAttributes(f, attr); }
    }

    // ---------- the graphics card's own memory (DXGI) ----------
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
    private static readonly Lazy<long> _vram = new(ProbeVram);
    /// <summary>Dedicated VRAM in MB of the card the game runs on, 0 when unknown.</summary>
    public static long VramMb => _vram.Value;

    /// <summary>
    /// DedicatedVideoMemory of the first hardware adapter Windows gives for "high performance": on a desktop the graphics
    /// card, on a laptop with two GPUs the strong one FiveM runs on. Without DXGI 1.6 the first hardware adapter
    /// (the one of the main screen).
    /// </summary>
    private static long ProbeVram()
    {
        try
        {
            var iid1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid1, out var f1) < 0 || f1 == IntPtr.Zero) return 0;
            try
            {
                var iidA = new Guid("29038f61-3839-4626-91fd-086879011a05");   // IDXGIAdapter1
                var iid6 = new Guid("c1b6694f-ff09-44a9-b03c-77900a0a1d17");   // IDXGIFactory6 (Windows 10 1803+)
                if (Marshal.QueryInterface(f1, ref iid6, out var f6) >= 0 && f6 != IntPtr.Zero)
                {
                    try
                    {
                        var byPref = (delegate* unmanaged<IntPtr, uint, int, Guid*, IntPtr*, int>)(*(IntPtr**)f6)[29];   // EnumAdapterByGpuPreference
                        for (uint i = 0; i < 16; i++)
                        {
                            IntPtr a;
                            if (byPref(f6, i, 2 /* DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE */, &iidA, &a) < 0 || a == IntPtr.Zero) break;
                            if (Dedicated(a) is { } mb) return mb;
                        }
                    }
                    finally { Marshal.Release(f6); }
                }
                var enum1 = (delegate* unmanaged<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)f1)[12];   // IDXGIFactory1::EnumAdapters1
                for (uint i = 0; i < 16; i++)
                {
                    IntPtr a;
                    if (enum1(f1, i, &a) < 0 || a == IntPtr.Zero) break;
                    if (Dedicated(a) is { } mb) return mb;
                }
                return 0;
            }
            finally { Marshal.Release(f1); }
        }
        catch { return 0; }
    }

    /// <summary>DXGI_ADAPTER_DESC1.DedicatedVideoMemory in MB; null for a software adapter. Releases the adapter.</summary>
    private static long? Dedicated(IntPtr a)
    {
        try
        {
            byte* desc = stackalloc byte[320];                                                     // DXGI_ADAPTER_DESC1 (312 bytes)
            var getDesc1 = (delegate* unmanaged<IntPtr, byte*, int>)(*(IntPtr**)a)[10];            // IDXGIAdapter1::GetDesc1
            if (getDesc1(a, desc) < 0 || (*(uint*)(desc + 304) & 2) != 0) return null;             // Flags: DXGI_ADAPTER_FLAG_SOFTWARE
            return (long)(*(ulong*)(desc + 272) / (1024 * 1024));                                  // DedicatedVideoMemory (SIZE_T)
        }
        finally { Marshal.Release(a); }
    }
}
