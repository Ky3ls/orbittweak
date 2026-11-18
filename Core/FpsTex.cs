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
    /// <summary>One value per profile of someone else whose limit OrbitTweak set: the name of the profile, then its value
    /// before ("-" = none of its own) and the value OrbitTweak wrote there last, "&lt;backup&gt;|&lt;written&gt;" (FpsLimitLogic.Entry).</summary>
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
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, uint*, byte*, int> _enumApplications;
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
            // only for the card's note on what else is in a profile: without it numOfApps stands in
            _enumApplications = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, uint*, byte*, int>)qi(IdEnumApplications);
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
    /// <summary>
    /// The card's state. For the newest game exe (the build FiveM started last): the profile the driver applies to it,
    /// also before OrbitTweak wrote anything, with the other applications in it (they get the limit too); limit = what
    /// that profile has now, or the kept limit while it only comes on the next start of FiveM (pending); written = what
    /// OrbitTweak wrote to that profile (0 = nothing yet), so the card can tell a value changed outside OrbitTweak.
    /// With a kept limit and the newest exe in no profile, or none cached (FiveM's cache cleaned), that is the own profile
    /// while it exists: the next start puts the exe there and it takes the value the profile has (Upkeep does not write
    /// it again). foreign: every profile of someone else a limit set on the card (or the probe) writes, the newest build's
    /// first: those of all cached builds and those a new limit follows to, also with no exe cached, each with its state
    /// and other applications, so the card can name every one that caps other programs too.
    /// </summary>
    public static object Get()
    {
        int hz = RefreshHz(), suggestion = Suggestion(hz);
        string? reason = !Available ? "nonvidia" : FiveM.Root == null ? "nofivem" : null;
        if (reason != null) return new { available = false, reason, limit = 0, kept = 0, written = 0, hz, suggestion, profile = (string?)null, ours = false, pending = false, others = (int?)0, otherApps = Array.Empty<string>(), foreign = Array.Empty<object>(), exes = 0 };
        // the same exes SetLimit writes for
        var paths = ExePaths(RunningGame());
        // with no profile for the game yet (FiveM never started, or its exe in no profile and no own profile made), a kept
        // limit comes with the next start of FiveM (Upkeep makes the own profile) and is shown until then
        int kept = Stored, limit = kept, written = 0; bool pending = kept > 0; int? others = 0;
        string? profile = null; var otherApps = new List<string>(); var foreign = new List<object>();
        try
        {
            lock (Gate)
            {
                // also with no exe: the own profile and the profiles a new limit follows to are there all the same
                var newest = paths.OrderByDescending(p => { try { return File.GetLastWriteTimeUtc(p); } catch { return DateTime.MinValue; } }).FirstOrDefault();
                var x = Session(false, s => Look(s, paths, newest == null ? "" : Path.GetFileName(newest), kept));
                if (x.newest is { } n)
                {
                    profile = n.Name; written = n.Written; pending = n.Pending;
                    if (!pending) limit = n.Live;
                    otherApps = n.Apps ?? otherApps; others = n.Others;
                }
                foreach (var f in x.foreign)
                    foreign.Add(new { name = f.Name, limit = f.Pending ? kept : f.Live, written = f.Written, pending = f.Pending, others = f.Others,
                        otherApps = (f.Apps ?? new()).Take(3).ToArray(), builds = f.Exes.Select(FriendlyName).Distinct().ToArray() });
            }
        }
        catch { }
        // ours: OrbitTweak keeps a limit; else a limit shown comes from a profile the user or another tool set
        return new { available = true, reason, limit, kept, written, hz, suggestion, profile, ours = kept > 0, pending, others, otherApps = otherApps.Take(3).ToArray(), foreign, exes = paths.Count };
    }

    /// <summary>A driver profile as the card shows it: its limit now, what OrbitTweak wrote there and whether the kept
    /// limit still has to come (FpsLimitLogic.CardState), the other applications in it (null = the driver does not list
    /// them) and how many (null = unknown), and FiveM's game exes in it.</summary>
    private sealed record Seen(string Name, int Live, int Written, bool Pending, List<string>? Apps, int? Others, List<string> Exes);

    /// <summary>The profile of the newest exe (null = none; <paramref name="newest"/> "" = no exe cached) and every profile
    /// of someone else a limit set now writes, as Apply decides them: of each cached exe (FpsLimitLogic.ForeignOf), then
    /// the ones that follow. With a kept limit, a newest exe in no profile or none at all has the own profile while it
    /// exists: the next start of FiveM puts the exe there and it takes the value that profile has now.</summary>
    private static (Seen? newest, List<Seen> foreign) Look(IntPtr s, List<string> paths, string newest, int kept)
    {
        var found = paths.Select(p => (exe: Path.GetFileName(p), prof: ProfileOf(s, p))).ToList();
        var list = new List<Seen>();
        var of = ForeignOf(found.Select(f => (f.exe, f.prof?.name)), newest);
        foreach (var (name, exes) in of)
            list.Add(See(s, found.First(f => f.prof is { } p && p.name.Equals(name, StringComparison.OrdinalIgnoreCase)).prof!.Value.h, name, kept, exes));
        foreach (var (name, h, _) in Following(s, of.Select(f => f.profile).ToList(), kept)) list.Add(See(s, h, name, kept, new()));
        if (found.FirstOrDefault(f => f.exe.Equals(newest, StringComparison.OrdinalIgnoreCase)).prof is not { } np)
        {
            // without a kept limit the probe adds nothing, so the own profile would not be the newest build's
            var own = kept > 0 ? FindProfile(s, OwnProfile) : IntPtr.Zero;
            return (own != IntPtr.Zero ? See(s, own, OwnProfile, kept, new(), own: true) : null, list);
        }
        return (np.name.Equals(OwnProfile, StringComparison.OrdinalIgnoreCase) ? See(s, np.h, np.name, kept, new(), own: true)
            : list.First(x => x.Name.Equals(np.name, StringComparison.OrdinalIgnoreCase)), list);
    }

    private static Seen See(IntPtr s, IntPtr h, string name, int kept, List<string> exes, bool own = false)
    {
        var g = GetFrl(s, h);
        // the other applications by name; without that list only numOfApps (null = unknown)
        var apps = AppsOf(s, h) is { } all ? OtherApps(all) : null;
        int? others = apps?.Count ?? (AppCount(s, h) is { } c ? OthersFromCount(c) : null);
        // the own profile has what OrbitTweak keeps; someone else's by its entry (a pending one counts as not written yet)
        var (written, pending) = own ? (kept, false) : CardState(Reg.Get(BK, name) as string, kept);
        return new Seen(name, ProfileLimit(g.found, g.location, g.value), written, pending, apps, others, exes);
    }

    /// <summary>
    /// Sets the limit (0 = off: every profile OrbitTweak changed gets its value back, its own profile goes). stayed: the
    /// profiles of someone else "Aus" left as they are because their limit was changed outside OrbitTweak, with that limit.
    /// </summary>
    public static (bool ok, string? error, List<(string profile, int limit)> stayed) SetLimit(int limit)
    {
        var stayed = new List<(string, int)>();
        if (!Valid(limit)) return (false, $"Erlaubt sind Aus oder {Min} bis {Max} FPS.", stayed);
        if (!Available) return (false, "Kein NVIDIA-Treiber gefunden. Das FPS-Limit gibt es nur mit einer NVIDIA-Grafikkarte.", stayed);
        try
        {
            lock (Gate)
            {
                int before = Stored;
                var paths = ExePaths(RunningGame());
                // entries of profiles of someone else that change (null = goes): written only once the driver has saved;
                // the pending ones this write made (BackupOf) go again when it did not
                var entries = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                var created = new List<string>();
                try { Session(true, s => limit == 0 ? Off(s, before, entries, stayed) : Apply(s, limit, before, paths, entries, created)); }
                catch { Store(AfterWrite(false, entries, created)); throw; }
                Store(AfterWrite(true, entries, created));
                if (limit == 0) Reg.Del(OWN, "FpsLimit"); else Reg.Set(OWN, "FpsLimit", limit);
                _known = limit == 0 ? null : paths.Select(p => Path.GetFileName(p)).ToList();
            }
            return (true, null, stayed);
        }
        catch (Exception ex) { return (false, ex.Message, stayed); }
    }

    /// <summary>
    /// From the FiveM probe while FiveM runs: once per start of FiveM (Rearm) and for each new game build, whose exe gets
    /// the limit too. Only adds what is new (FpsLimitLogic.UpkeepPlan): a profile OrbitTweak already set is not written
    /// again, so a change made in the NVIDIA Control Panel stays (the card shows it); one it has not written yet (the
    /// driver refused last time, a build in another profile) gets the limit. Cheap until the list of exes changes or
    /// FiveM starts again. Returns how many exes were added to the own profile and the profiles of someone else it set.
    /// </summary>
    public static (int added, List<string> profiles) Upkeep(string? runningProcess)
    {
        int limit = Stored;
        if (limit == 0) return (0, new());
        List<string> paths;
        try { paths = ExePaths(runningProcess); } catch { return (0, new()); }   // only the driver's errors reach the log, once per start
        var names = paths.Select(p => Path.GetFileName(p)).ToList();
        if (!NeedsSync(names, _known) || !Available) return (0, new());
        lock (Gate)
        {
            // entries of the profiles of someone else it sets: written only once the driver has saved; the pending ones
            // this write made (BackupOf) go again when it did not, so the retry takes a fresh backup
            var entries = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var created = new List<string>();
            try
            {
                int added;
                try { added = Session(s => Keep(s, limit, paths, entries, created)); }
                catch { Store(AfterWrite(false, entries, created)); throw; }
                Store(AfterWrite(true, entries, created));
                return (added, entries.Keys.ToList());
            }
            finally { _known = names; }   // a driver that refuses is asked again on the next start of FiveM, not every few seconds
        }
    }

    /// <summary>Writes what FpsLimitLogic.AfterWrite decided into the backup store (null = the entry goes).</summary>
    private static void Store(List<(string name, string? entry)> changes)
    {
        foreach (var (name, e) in changes) if (e == null) Reg.Del(BK, name); else Reg.SetStr(BK, name, e);
    }

    /// <summary>FiveM is closed (the probe sees none of its processes): its next start looks at the profiles once more, so
    /// a profile the card shows as coming with the next start gets the limit then, also after the driver refused.</summary>
    public static void Rearm() => _known = null;

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
    /// <summary>The limit set on the card: into the own profile (exes in no profile go in), every profile of someone else
    /// an exe belongs to, and the profiles OrbitTweak set before that still have its value (FpsLimitLogic.Follows).</summary>
    private static int Apply(IntPtr s, int limit, int before, List<string> paths, Dictionary<string, string?> entries, List<string> created)
    {
        var found = paths.Select(p => (exe: Path.GetFileName(p), prof: ProfileOf(s, p))).ToList();
        var plan = Decide(found.Select(f => (f.exe, f.prof?.name)));
        var own = FindProfile(s, OwnProfile);
        if (plan.AddToOwn.Count > 0 && own == IntPtr.Zero) own = CreateOwnProfile(s);
        int added = AddToOwn(s, own, plan.AddToOwn);
        if (own != IntPtr.Zero) SetFrl(s, own, (uint)limit);
        foreach (var name in plan.Foreign)
        {
            var h = found.First(f => f.prof is { } p && p.name.Equals(name, StringComparison.OrdinalIgnoreCase)).prof!.Value.h;
            // the value before OrbitTweak's first change stays the backup, also when the limit changes again
            var backup = BackupOf(s, h, name, before, created);
            SetFrl(s, h, (uint)limit);
            entries[name] = Entry(backup, limit);
        }
        foreach (var (name, h, backup) in Following(s, plan.Foreign, before))
        {
            SetFrl(s, h, (uint)limit);
            entries[name] = Entry(backup, limit);
        }
        return added;
    }

    /// <summary>The profiles of someone else OrbitTweak set before that no game exe belongs to now (FiveM's cache cleaned,
    /// another build) but that still have its value (FpsLimitLogic.Follows), with their backup: a new limit goes there too.
    /// A pending entry follows nothing (written 0).</summary>
    private static List<(string name, IntPtr h, string backup)> Following(IntPtr s, List<string> foreign, int stored)
    {
        var r = new List<(string, IntPtr, string)>();
        foreach (var name in Reg.ValueNames(BK))
        {
            if (foreign.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            var h = FindProfile(s, name);
            if (h == IntPtr.Zero) continue;
            var (backup, written) = ParseEntry(Reg.Get(BK, name) as string, stored);
            var g = GetFrl(s, h);
            if (Follows(ProfileLimit(g.found, g.location, g.value), written)) r.Add((name, h, backup));
        }
        return r;
    }

    /// <summary>Upkeep's part (FpsLimitLogic.UpkeepPlan); saves only when it changed something. The entries of the
    /// profiles of someone else it sets go into <paramref name="entries"/>, for the caller to write once the driver saved,
    /// the pending ones it made into <paramref name="created"/>.</summary>
    private static (int, bool) Keep(IntPtr s, int limit, List<string> paths, Dictionary<string, string?> entries, List<string> created)
    {
        var found = paths.Select(p => (exe: Path.GetFileName(p), prof: ProfileOf(s, p))).ToList();
        var own = FindProfile(s, OwnProfile);
        var plan = UpkeepPlan(found.Select(f => (f.exe, f.prof?.name)), Reg.ValueNames(BK).Select(n => (n, Reg.Get(BK, n) as string)), limit, own != IntPtr.Zero);
        if (plan.AddToOwn.Count > 0 && own == IntPtr.Zero) own = CreateOwnProfile(s);
        int added = AddToOwn(s, own, plan.AddToOwn);
        if (plan.Own) SetFrl(s, own, (uint)limit);
        foreach (var name in plan.Foreign)
        {
            var h = found.First(f => f.prof is { } p && p.name.Equals(name, StringComparison.OrdinalIgnoreCase)).prof!.Value.h;
            var backup = BackupOf(s, h, name, limit, created);
            SetFrl(s, h, (uint)limit);
            entries[name] = Entry(backup, limit);
        }
        return (added, plan.AddToOwn.Count > 0 || plan.Own || plan.Foreign.Count > 0);
    }

    private static int AddToOwn(IntPtr s, IntPtr own, List<string> exes)
    {
        int added = 0;
        foreach (var exe in exes)
        {
            int st = AddApp(s, own, exe);
            if (st == OK) added++;
            else if (st != ExecutableAlreadyInUse) Check(st, "CreateApplication");   // in a profile under its full path: that one is found next time
        }
        return added;
    }

    /// <summary>The backup for a write to a profile of someone else: its entry's (the value before OrbitTweak's first
    /// change, also from a pending entry), else its value now. A new one goes into the backup store as a pending entry
    /// before anything is written (FpsLimitLogic.PendingEntry), so OrbitTweak ending right after the driver saved does
    /// not lose it, and its name into <paramref name="created"/>: a driver that refuses to save leaves the profile as it
    /// was, and the caller takes exactly those entries out again (FpsLimitLogic.AfterWrite). The written value follows
    /// through the caller's entries once the driver has saved.</summary>
    private static string BackupOf(IntPtr s, IntPtr h, string name, int stored, List<string> created)
    {
        if (Reg.Get(BK, name) is string old) return ParseEntry(old, stored).backup;
        var g = GetFrl(s, h);
        var backup = Backup(g.found, g.location, g.predefined, g.value);
        Reg.SetStr(BK, name, PendingEntry(backup));
        created.Add(name);
        return backup;
    }

    /// <summary>Every profile of someone else gets its value from before back while it still has what OrbitTweak wrote
    /// there, also when its exe is no longer in FiveM's cache; one whose limit was changed since stays as it is
    /// (<paramref name="stayed"/>). Every entry goes (FpsLimitLogic.OffPlan): the profiles are the user's again, and a
    /// limit set later takes a fresh backup. The own profile is deleted.</summary>
    private static int Off(IntPtr s, int before, Dictionary<string, string?> entries, List<(string, int)> stayed)
    {
        var seen = Reg.ValueNames(BK).Select(name =>
        {
            var h = FindProfile(s, name);
            int live = 0;
            if (h != IntPtr.Zero) { var g = GetFrl(s, h); live = ProfileLimit(g.found, g.location, g.value); }
            return (name, h, live, entry: Reg.Get(BK, name) as string);
        }).ToList();
        var plan = OffPlan(seen.Select(x => (x.name, x.h != IntPtr.Zero, x.live, x.entry)), before);
        foreach (var (name, step) in plan.steps)
        {
            if (step != OffStep.Restore) continue;
            var x = seen.First(e => e.name == name);
            if (RestoreValue(ParseEntry(x.entry, before).backup) is { } v) SetFrl(s, x.h, v);
            else UnsetFrl(s, x.h);
        }
        stayed.AddRange(plan.stayed);
        foreach (var name in plan.drop) entries[name] = null;
        var own = FindProfile(s, OwnProfile);
        if (own != IntPtr.Zero) Check(_deleteProfile(s, own), "DeleteProfile");
        return 0;
    }

    // ---------- NVAPI DRS ----------
    private static T Session<T>(bool save, Func<IntPtr, T> body) => Session(s => (body(s), save));

    /// <summary>A DRS session; the body says whether the driver should save what it changed.</summary>
    private static T Session<T>(Func<IntPtr, (T result, bool save)> body)
    {
        IntPtr s;
        Check(_createSession(&s), "CreateSession");
        try
        {
            Check(_loadSettings(s), "LoadSettings");
            var (r, save) = body(s);
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

    /// <summary>NVDRS_PROFILE's numOfApps: how many applications the profile has (null when the driver does not say).</summary>
    private static uint? AppCount(IntPtr s, IntPtr h)
    {
        var p = new byte[ProfileSize]; U32(p, 0, ProfileVer);
        int st;
        fixed (byte* b = p) st = _getProfileInfo(s, h, b);
        return st == OK ? U32(p, ProfNumOfApps) : null;
    }

    /// <summary>The appName of every application in the profile (NvAPI_DRS_EnumApplications, a few at a time); null
    /// when the driver does not give them.</summary>
    private static List<string>? AppsOf(IntPtr s, IntPtr h)
    {
        if (_enumApplications == null) return null;
        const int batch = 8;
        var names = new List<string>();
        var buf = new byte[AppSize * batch];
        for (uint start = 0; start < 4096;)
        {
            Array.Clear(buf);
            for (int i = 0; i < batch; i++) U32(buf, i * AppSize, AppVer);
            uint n = batch; int st;
            fixed (byte* b = buf) st = _enumApplications(s, h, start, &n, b);
            if (st == EndEnumeration) break;
            if (st != OK) return null;
            for (int i = 0; i < Math.Min(n, batch); i++) names.Add(GetStr(buf, i * AppSize + AppName));
            if (n < batch) break;
            start += n;
        }
        return names;
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
