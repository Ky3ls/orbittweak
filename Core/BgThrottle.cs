using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrbitTweak.Core;

/// <summary>
/// "Hintergrund-Apps ausbremsen": while the game runs, the usual heavy programs next to it (browsers, launchers, cloud
/// apps) get a lower CPU priority and, on Windows 11, the efficiency mode (EcoQoS: efficient clock, E-cores on hybrid
/// CPUs), so the game gets the processor first. Nothing is closed. Voice apps are never touched; a program that plays
/// sound or is the window in use right now is left alone (and gets its speed back at once). When the game ends, every
/// program gets back exactly the priority and efficiency setting it had before.
/// </summary>
internal static class BgThrottle
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    /// <summary>Programs that are slowed down (process names without .exe). Voice, music and mouse/keyboard tools are not in it on purpose.</summary>
    private static readonly HashSet<string> Heavy = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "firefox", "msedge", "opera", "opera_gx", "brave", "vivaldi",
        "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "riotclientux", "riotclientservices",
        "telegram", "whatsapp", "slack", "notion", "code", "cursor", "claude", "onedrive", "dropbox", "googledrivefs",
        "wallpaper32", "wallpaper64", "msedgewebview2",
    };

    /// <summary>Processes that draw the window of another program (WebView2 in the new Teams/Outlook, CEF in the Epic and Steam clients).</summary>
    private static readonly HashSet<string> Helpers = new(StringComparer.OrdinalIgnoreCase) { "msedgewebview2", "epicwebhelper", "steamwebhelper" };

    /// <summary>What a slowed program had before: priority, and whether (and with which masks) its efficiency setting was changed.</summary>
    private readonly record struct Was(string Name, ProcessPriorityClass Prio, bool Eco, uint Ctl, uint St);
    private static readonly Dictionary<int, Was> Saved = new();
    public static bool Active { get { lock (Saved) return Saved.Count > 0; } }

    /// <summary>Slows down what is not slowed down yet (programs started since the last call included). Returns how many were new.</summary>
    public static int Apply()
    {
        int own = Environment.ProcessId, n = 0;
        bool eco = SysInfo.Build >= 22000;   // before Windows 11 the tag only means "low QoS", which acts on battery only
        // WebView2 processes of this app must stay fast: its own page runs in them
        var mine = new HashSet<int>();
        try { foreach (var p in Process.GetProcessesByName("msedgewebview2")) using (p) if (ParentIs(p, own)) mine.Add(p.Id); } catch { }
        // a program that plays sound (YouTube, a stream) keeps full speed, all its processes: the one with the sound
        // session is not always the one that decodes it
        var loud = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pid in AudioSessions.ActivePids()) { try { using var p = Process.GetProcessById(pid); loud.Add(p.ProcessName); } catch { } }
        var (front, frontPid) = Foreground();
        // the window in use can be drawn by helper processes under it: they count as that program
        var frontKids = new HashSet<int>();
        if (frontPid > 0)
            foreach (var hn in Helpers)
                try { foreach (var p in Process.GetProcessesByName(hn)) using (p) if (p.Id != frontPid && ParentIs(p, frontPid)) frontKids.Add(p.Id); } catch { }
        bool Spared(int pid, string name) => loud.Contains(name) || name.Equals(front, StringComparison.OrdinalIgnoreCase) || frontKids.Contains(pid);
        lock (Saved)
        {
            bool changed = false;
            // alt-tabbed to it, or it started playing sound: its speed back at once (it is slowed again once that is over)
            foreach (var pid in Saved.Where(x => Spared(x.Key, x.Value.Name)).Select(x => x.Key).ToList())
            {
                Give(pid, Saved[pid]); Saved.Remove(pid); changed = true;
            }
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    try
                    {
                        if (!Heavy.Contains(p.ProcessName) || Spared(p.Id, p.ProcessName)) continue;
                        if (p.Id == own || mine.Contains(p.Id) || Saved.ContainsKey(p.Id)) continue;
                        var prio = p.PriorityClass;
                        if (prio is ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal) continue;   // already low: leave it alone
                        var before = eco ? Qos.Get(p.Id) : null;
                        p.PriorityClass = ProcessPriorityClass.BelowNormal;
                        bool tagged = eco && Qos.Set(p.Id, Qos.SPEED, Qos.SPEED);
                        Saved[p.Id] = new Was(p.ProcessName, prio, tagged, before?.ctl ?? 0, before?.st ?? 0);
                        n++;
                    }
                    catch { }   // protected or just ended
                }
            if (n > 0 || changed) WriteJournal();
        }
        return n;
    }

    // What was slowed is also written to a small file. If the app ends without tidying up (crash, power cut on the
    // app only), the next start reads it and gives the programs that are still running their speed back.
    private static string Journal => System.IO.Path.Combine(AppDirs.Local, "bgbrake.json");
    private static void WriteJournal()
    {
        try
        {
            if (Saved.Count == 0) { if (System.IO.File.Exists(Journal)) System.IO.File.Delete(Journal); return; }
            System.IO.Directory.CreateDirectory(AppDirs.Local);
            System.IO.File.WriteAllText(Journal, System.Text.Json.JsonSerializer.Serialize(Saved.Select(x => new[]
            {
                x.Key.ToString(), x.Value.Name, ((int)x.Value.Prio).ToString(), x.Value.Eco ? "1" : "0", x.Value.Ctl.ToString(), x.Value.St.ToString()
            }).ToList()));
        }
        catch { }
    }

    /// <summary>Called once when the app starts: undoes what a run that ended badly left behind.</summary>
    public static int Recover()
    {
        int n = 0;
        try
        {
            if (!System.IO.File.Exists(Journal)) return 0;
            var rows = System.Text.Json.JsonSerializer.Deserialize<List<string[]>>(System.IO.File.ReadAllText(Journal)) ?? new();
            foreach (var r in rows)
            {
                try
                {
                    if (r.Length < 4 || !int.TryParse(r[0], out var pid) || !int.TryParse(r[2], out var prio)) continue;
                    using var p = Process.GetProcessById(pid);
                    if (!p.ProcessName.Equals(r[1], StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.PriorityClass == ProcessPriorityClass.BelowNormal) p.PriorityClass = (ProcessPriorityClass)prio;
                    // journals of versions before 2.26 have no masks: 0/0 is "Windows decides"
                    uint ctl = r.Length >= 6 && uint.TryParse(r[4], out var c) ? c : 0, st = r.Length >= 6 && uint.TryParse(r[5], out var s) ? s : 0;
                    if (r[3] == "1") Qos.Set(pid, ctl, st);
                    n++;
                }
                catch { }
            }
            System.IO.File.Delete(Journal);
        }
        catch { }
        return n;
    }

    /// <summary>Gives every slowed program back what it had.</summary>
    public static int Restore()
    {
        int n = 0;
        lock (Saved)
        {
            foreach (var (pid, was) in Saved) if (Give(pid, was)) n++;
            Saved.Clear(); WriteJournal();
        }
        return n;
    }

    private static bool Give(int pid, Was was)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.ProcessName.Equals(was.Name, StringComparison.OrdinalIgnoreCase)) return false;   // the number belongs to another program by now
            p.PriorityClass = was.Prio;
            if (was.Eco) Qos.Set(pid, was.Ctl, was.St);   // exactly the masks it had (usually 0/0: Windows decides), not "full speed"
            return true;
        }
        catch { return false; }
    }

    /// <summary>Name and id of the process that owns the window in use; (null, 0) when there is none.</summary>
    private static (string? name, int pid) Foreground()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return (null, 0);
            GetWindowThreadProcessId(h, out var pid);
            if (pid == 0) return (null, 0);
            using var p = Process.GetProcessById((int)pid);
            return (p.ProcessName, (int)pid);
        }
        catch { return (null, 0); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PBI { public IntPtr Reserved1, Peb, Reserved2a, Reserved2b, UniquePid, ParentPid; }
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr h, int cls, ref PBI info, int size, out int ret);
    private static bool ParentIs(Process p, int parent)
    {
        try
        {
            // a WebView2 helper can be a grandchild: look two steps up
            var pbi = new PBI();
            if (NtQueryInformationProcess(p.Handle, 0, ref pbi, Marshal.SizeOf<PBI>(), out _) != 0) return false;
            int pp = pbi.ParentPid.ToInt32();
            if (pp == parent) return true;
            using var up = Process.GetProcessById(pp);
            var pbi2 = new PBI();
            return NtQueryInformationProcess(up.Handle, 0, ref pbi2, Marshal.SizeOf<PBI>(), out _) == 0 && pbi2.ParentPid.ToInt32() == parent;
        }
        catch { return false; }
    }
}
