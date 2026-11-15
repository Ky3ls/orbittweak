using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrbitTweak.Core;

/// <summary>
/// "Background on the brakes": while the game runs, the usual heavy programs next to it (browsers, chat and music apps,
/// launchers) get a lower CPU priority and Windows' efficiency mode, so the game gets the processor first. Nothing is
/// closed. When the game ends, every program gets back exactly what it had. Voice programs keep full speed for their
/// sound: they are only put behind the game, not into efficiency mode.
/// </summary>
internal static class BgThrottle
{
    [StructLayout(LayoutKind.Sequential)] private struct POWER_THROTTLING { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessInformation(IntPtr h, int cls, ref POWER_THROTTLING info, int size);
    private const int ProcessPowerThrottling = 4;
    private const uint SPEED = 0x1, PROCESS_SET_INFORMATION = 0x0200;

    /// <summary>Programs that are slowed down (process names without .exe, lower case).</summary>
    private static readonly HashSet<string> Heavy = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "firefox", "msedge", "opera", "opera_gx", "brave", "vivaldi",
        "spotify", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "riotclientux", "riotclientservices",
        "telegram", "whatsapp", "slack", "notion", "code", "cursor", "claude", "onedrive", "dropbox", "googledrivefs",
        "wallpaper32", "wallpaper64", "icue", "razer synapse", "lghub", "lghub_agent", "msedgewebview2",
    };
    /// <summary>Voice programs: behind the game, but never in efficiency mode (the sound would stutter).</summary>
    private static readonly HashSet<string> Voice = new(StringComparer.OrdinalIgnoreCase) { "discord", "discordcanary", "discordptb", "teamspeak", "ts3client_win64", "ts6client" };

    private static readonly Dictionary<int, (string name, ProcessPriorityClass prio, bool eco)> Saved = new();
    public static bool Active => Saved.Count > 0;

    /// <summary>Slows down what is not slowed down yet (programs started since the last call included). Returns how many were new.</summary>
    public static int Apply()
    {
        int own = Environment.ProcessId, n = 0;
        // WebView2 processes of this app must stay fast: its own page runs in them
        var mine = new HashSet<int>();
        try { foreach (var p in Process.GetProcessesByName("msedgewebview2")) using (p) if (ParentIs(p, own)) mine.Add(p.Id); } catch { }
        lock (Saved)
        {
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    try
                    {
                        bool voice = Voice.Contains(p.ProcessName);
                        if (!voice && !Heavy.Contains(p.ProcessName)) continue;
                        if (p.Id == own || mine.Contains(p.Id) || Saved.ContainsKey(p.Id)) continue;
                        var prio = p.PriorityClass;
                        if (prio is ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal) continue;   // already low: leave it alone
                        if (voice && prio != ProcessPriorityClass.Normal) continue;                           // the part of a voice program that carries the sound is raised by the program itself: hands off
                        p.PriorityClass = ProcessPriorityClass.BelowNormal;
                        bool eco = !voice && Eco(p.Id, true);
                        Saved[p.Id] = (p.ProcessName, prio, eco); n++;
                    }
                    catch { }   // protected or just ended
                }
            if (n > 0) WriteJournal();
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
            System.IO.File.WriteAllText(Journal, System.Text.Json.JsonSerializer.Serialize(Saved.Select(x => new[] { x.Key.ToString(), x.Value.name, ((int)x.Value.prio).ToString(), x.Value.eco ? "1" : "0" }).ToList()));
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
                    if (r[3] == "1") Eco(pid, false);
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
            foreach (var (pid, was) in Saved)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (!p.ProcessName.Equals(was.name, StringComparison.OrdinalIgnoreCase)) continue;   // the number belongs to another program by now
                    p.PriorityClass = was.prio;
                    if (was.eco) Eco(pid, false);
                    n++;
                }
                catch { }
            }
            Saved.Clear(); WriteJournal();
        }
        return n;
    }

    /// <summary>The game itself must never be held back by Windows' efficiency mode.</summary>
    public static void FullSpeed(int pid) => Eco(pid, false);

    private static bool Eco(int pid, bool on)
    {
        var h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            var st = new POWER_THROTTLING { Version = 1, ControlMask = SPEED, StateMask = on ? SPEED : 0 };
            return SetProcessInformation(h, ProcessPowerThrottling, ref st, Marshal.SizeOf<POWER_THROTTLING>());
        }
        finally { CloseHandle(h); }
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
