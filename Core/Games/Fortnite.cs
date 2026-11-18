using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using static OrbitTweak.Core.FortniteLogic;

namespace OrbitTweak.Core;

// =====================================================================
//  Fortnite (2.30), the Windows part: finds the install and the settings file, reads and writes Fortnite's own
//  GameUserSettings.ini (only while every Fortnite process is closed, since the game saves its whole settings object
//  over the file while it runs and on exit) and the Windows per-app GPU preference. Each key's value before the first
//  change goes to OrbitTweak's backup store ("og.fortnite.<key>", "-" = was not there), Revert puts exactly that back.
//  Easy Anti-Cheat: no game file, no running process is touched; processes are found by name only, no handle is opened.
//  The rules (keys, values, texts, parsing) are in FortniteLogic.cs.
// =====================================================================

/// <summary>Fortnite (2.30): seven settings, six of them in Fortnite's GameUserSettings.ini. Pure rules: Core/Games/FortniteLogic.cs.</summary>
internal sealed class FortniteTweaks : IGameTweaks
{
    public string Id => FortniteLogic.Id;
    public string Name => "Fortnite";

    /// <summary>One change at a time: two quick clicks must not read the file before the other one wrote it.</summary>
    private static readonly object Gate = new();

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>%LocalAppData%\FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini of the signed-in user (Fortnite makes it on its first start).</summary>
    public static string ConfigPath => Path.Combine(Local, "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini");
    private static string LogPath => Path.Combine(Local, "FortniteGame", "Saved", "Logs", "FortniteGame.log");
    private static string BkGpuKey => $@"{Bk.User}\{GpuBackup}";

    /// <summary>Any process of the launch chain, by name only (a snapshot of the process list, no handle on the game).</summary>
    public static bool IsRunning()
    {
        var names = new HashSet<string>(ProcessNames, StringComparer.OrdinalIgnoreCase);
        Process[] all;
        try { all = Process.GetProcesses(); } catch { return false; }
        bool hit = false;
        foreach (var p in all)
        {
            try { if (!hit && names.Contains(p.ProcessName)) hit = true; } catch { }
            p.Dispose();
        }
        return hit;
    }

    // ---------- the install ----------

    /// <summary>
    /// The install folder: Epic Games Launcher manifests, then LauncherInstalled.dat, then the "Base Directory" line of
    /// FortniteGame.log (covers the new launcher, Legendary and Heroic; read only while Fortnite is closed, only up to
    /// that line and only that line is used, since tournament rules ban tools that read live game state), then the
    /// default folder. A folder counts only
    /// with FortniteClient-Win64-Shipping.exe in it.
    /// </summary>
    public static string? Root(bool running)
    {
        foreach (var c in Candidates(running))
            try { if (File.Exists(ExePath(c))) return NormalizeRoot(c); } catch { }
        return null;
    }

    private static IEnumerable<string> Candidates(bool running)
    {
        var dir = Path.Combine(ProgramData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        string[] items = Array.Empty<string>();
        try { if (Directory.Exists(dir)) items = Directory.GetFiles(dir, "*.item"); } catch { }
        foreach (var f in items)
            if (ManifestRoot(ReadSmall(f)) is { } r) yield return r;

        foreach (var r in LauncherInstalledRoots(ReadSmall(Path.Combine(ProgramData, "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat"))))
            yield return r;

        if (!running && FromLog() is { } fromLog) yield return fromLog;

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games", "Fortnite");
    }

    /// <summary>A small JSON record of the launcher (read only, never written); null when it cannot be read.</summary>
    private static string? ReadSmall(string f)
    {
        try { return File.Exists(f) && new FileInfo(f).Length < 4 << 20 ? File.ReadAllText(f) : null; }
        catch { return null; }
    }

    /// <summary>The install folder from the start of the log; read line by line only up to the "Base Directory" line,
    /// which comes after the plugin and pak mount lines (line 100 to 200 in 2018-2019 logs, later in UE5 builds).</summary>
    private static string? FromLog()
    {
        try { return File.Exists(LogPath) ? LogRoot(LogLines(LogPath).Take(LogScanLines)) : null; }
        catch { return null; }
    }

    /// <summary>The lines of the log, read lazily (shared read: the launcher may hold it open).</summary>
    private static IEnumerable<string> LogLines(string f)
    {
        using var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(s, detectEncodingFromByteOrderMarks: true);
        while (r.ReadLine() is { } l) yield return l;
    }

    // ---------- the hardware ----------
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
    private static readonly Lazy<bool> _nvidia = new(ProbeNvidia);
    /// <summary>An NVIDIA graphics card with a driver is in this PC (Reflex needs one).</summary>
    public static bool Nvidia => _nvidia.Value;

    /// <summary>Any hardware adapter DXGI lists with NVIDIA's PCI vendor id (0x10DE).</summary>
    private static unsafe bool ProbeNvidia()
    {
        try
        {
            var iid1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid1, out var f) < 0 || f == IntPtr.Zero) return false;
            try
            {
                var enum1 = (delegate* unmanaged<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)f)[12];   // IDXGIFactory1::EnumAdapters1
                byte* desc = stackalloc byte[320];                                                  // DXGI_ADAPTER_DESC1 (312 bytes)
                for (uint i = 0; i < 16; i++)
                {
                    IntPtr a;
                    if (enum1(f, i, &a) < 0 || a == IntPtr.Zero) break;
                    try
                    {
                        var getDesc1 = (delegate* unmanaged<IntPtr, byte*, int>)(*(IntPtr**)a)[10];  // IDXGIAdapter1::GetDesc1
                        if (getDesc1(a, desc) < 0 || (*(uint*)(desc + 304) & 2) != 0) continue;      // Flags: DXGI_ADAPTER_FLAG_SOFTWARE
                        if (IsNvidia(*(uint*)(desc + 256))) return true;                              // VendorId
                    }
                    finally { Marshal.Release(a); }
                }
                return false;
            }
            finally { Marshal.Release(f); }
        }
        catch { return false; }
    }

    // ---------- the page ----------

    public GameInfo Info()
    {
        bool running = IsRunning();
        var root = Root(running);
        var cfg = ConfigPath;
        bool found = File.Exists(cfg);
        return new GameInfo
        {
            Id = Id, Name = Name, Installed = root != null, Path = root, Running = running,
            ConfigFound = found, ConfigPath = cfg, Note = FortniteLogic.Note(root != null, found, running), AntiCheat = FortniteLogic.AntiCheat,
        };
    }

    public List<GameItem> Items()
    {
        var root = Root(IsRunning());
        var exe = root != null ? ExePath(root) : null;
        var cfg = ConfigPath;
        string? ini = null, why = null;
        if (!File.Exists(cfg)) why = MsgNoConfig;
        else try { ini = Load(cfg).Text; } catch { why = MsgUnreadable; }
        int hz = FpsLimit.RefreshHz();

        var list = new List<GameItem>();
        foreach (var d in Defs)
        {
            object? value;
            string itemWhy;
            bool changed;
            if (d.Id == "gpu")
            {
                value = exe != null && GpuPrefs.Get(exe, GpuField) == "2";
                itemWhy = !Hw.SwitchableGpus ? MsgOneGpu : exe == null ? MsgNoInstall : "";
                changed = GpuPrefs.Backed(GpuBackup).Length > 0;
            }
            else
            {
                value = ini != null ? Read(d.Id, ini) : null;
                itemWhy = why ?? (d.Id == "reflex" && !Nvidia ? MsgNoNvidia
                                : d.Id == "shadows" && !ShadowsAvailable(ini) ? MsgPerfShadows : "");
                changed = KeysOf(d.Id).Any(k => Bk.Has(BackupKey(k.Key)));
            }
            list.Add(new GameItem
            {
                Id = d.Id, Title = d.Title, Sum = d.Sum, Desc = d.Desc, Kind = d.Kind, Value = value,
                Options = Options(d.Id, value, hz)?.Select(o => new GameOption(o.V, o.T)).ToList(),
                Rec = d.Rec, RecValue = d.Rec ? RecValue(d.Id, value) : null, Effect = d.Effect, Source = d.Source,
                NeedsClosed = d.NeedsClosed, Available = itemWhy.Length == 0, Why = itemWhy, Changed = changed,
            });
        }
        return list;
    }

    public object? Set(string itemId, JsonElement value)
    {
        lock (Gate)
        {
            if (Find(itemId) == null) throw new InvalidOperationException(MsgUnknown);
            if (itemId == "gpu") return SetGpu(FortniteLogic.Switch(value));

            Writes(itemId, value);   // a value that is not allowed is refused before anything is read
            if (IsRunning()) throw new InvalidOperationException(MsgRunning);
            var cfg = ConfigPath;
            if (!File.Exists(cfg)) throw new InvalidOperationException(MsgNoConfig);
            var (text, enc) = LoadOrThrow(cfg);
            if (itemId == "reflex" && !Nvidia) throw new InvalidOperationException(MsgNoNvidia);
            if (itemId == "shadows" && !ShadowsAvailable(text)) throw new InvalidOperationException(MsgPerfShadows);
            var writes = Writes(itemId, value, text, Reg.Get(Bk.User, BackupKey("PreferredFeatureLevel")) as string);

            var (next, backups) = PlanSet(text, writes, k => Bk.Has(k));
            foreach (var (k, before) in backups) Reg.SetStr(Bk.User, k, before);   // the backup is there before the file changes
            if (next != text)
            {
                try { Save(cfg, next, enc); }
                catch (Exception ex)
                {
                    foreach (var (k, _) in backups) Bk.Drop(k);   // nothing changed: no backup that says otherwise
                    throw new InvalidOperationException(WriteError(ex));
                }
            }
            foreach (var k in Settled(writes, k => Reg.Get(Bk.User, k) as string)) Bk.Drop(k);
            return Read(itemId, LoadOrThrow(cfg).Text);
        }
    }

    /// <summary>
    /// The Windows per-app GPU preference of the Shipping exe (the one that renders). On: "GpuPreference=2" (Hohe
    /// Leistung). Off: what was there before OrbitTweak (also "Windows entscheidet"); a "Hohe Leistung" the player set in
    /// Windows himself is backed up before it goes. Takes effect at the next start, so the game may run.
    /// </summary>
    private static bool SetGpu(bool on)
    {
        if (!Hw.SwitchableGpus) throw new InvalidOperationException(MsgOneGpu);
        var root = Root(IsRunning()) ?? throw new InvalidOperationException(MsgNoInstall);
        var exe = ExePath(root);
        var cur = GpuPrefs.Get(exe, GpuField);
        var backup = Reg.Get(BkGpuKey, exe) as string;
        if (on)
        {
            if (cur != "2") GpuPrefs.SetField(exe, GpuField, "2", GpuBackup);
            if (Reg.Get(BkGpuKey, exe) as string == "2") Reg.Del(BkGpuKey, exe);   // it was on before OrbitTweak: nothing of ours left
        }
        else if (backup != null && backup != "2") GpuPrefs.RestoreField(exe, GpuField, GpuBackup);
        else if (cur == "2")
        {
            if (backup == null) Reg.SetStr(BkGpuKey, exe, "2");
            var next = TweakLogic.WithField(Reg.Get(GpuPrefs.Key, exe) as string, GpuField, null);
            if (next == null) Reg.Del(GpuPrefs.Key, exe);
            else Reg.SetStr(GpuPrefs.Key, exe, next);
        }
        return GpuPrefs.Get(exe, GpuField) == "2";
    }

    public int Revert()
    {
        lock (Gate)
        {
            var pending = Defs.Where(d => IsIni(d.Id) && KeysOf(d.Id).Any(k => Bk.Has(BackupKey(k.Key)))).ToList();
            // all or nothing: while Fortnite runs it would save its own values over the file again
            if (pending.Count > 0 && IsRunning()) throw new InvalidOperationException(MsgRunning);

            int n = 0;
            var exes = GpuPrefs.Backed(GpuBackup);   // every exe with a backup, also of an install that has moved since
            foreach (var exe in exes) GpuPrefs.RestoreField(exe, GpuField, GpuBackup);
            if (exes.Length > 0) n++;
            if (pending.Count == 0) return n;

            var keys = pending.SelectMany(d => KeysOf(d.Id)).Where(k => Bk.Has(BackupKey(k.Key))).ToList();
            var cfg = ConfigPath;
            if (File.Exists(cfg))
            {
                var (text, enc) = LoadOrThrow(cfg);
                var next = PlanRevert(text, keys.Select(k => (k.Section, k.Key, Reg.Get(Bk.User, BackupKey(k.Key)) as string)));
                if (next != text)
                {
                    try { Save(cfg, next, enc); }
                    catch (Exception ex) { throw new InvalidOperationException(WriteError(ex)); }
                }
                n += pending.Count;
            }
            // without the file (deleted, Fortnite reset) nothing of OrbitTweak's is left to undo
            foreach (var k in keys) Bk.Drop(BackupKey(k.Key));
            return n;
        }
    }

    // ---------- the file ----------

    private static (string Text, TextEnc Enc) Load(string f) => Decode(File.ReadAllBytes(f));

    private static (string Text, TextEnc Enc) LoadOrThrow(string f)
    {
        try { return Load(f); }
        catch (Exception) { throw new InvalidOperationException(MsgUnreadable); }
    }

    /// <summary>Writes the bytes back in the file's own encoding; a file the player made read-only is read-only again afterwards.</summary>
    private static void Save(string f, string text, TextEnc enc)
    {
        var bytes = Encode(text, enc);
        var attr = File.GetAttributes(f);
        bool ro = attr.HasFlag(FileAttributes.ReadOnly);
        if (ro) File.SetAttributes(f, attr & ~FileAttributes.ReadOnly);
        try { File.WriteAllBytes(f, bytes); }
        finally { if (ro) File.SetAttributes(f, attr); }
    }

    private static string WriteError(Exception ex) => ex is UnauthorizedAccessException ? MsgNoAccess : MsgLocked;
}
