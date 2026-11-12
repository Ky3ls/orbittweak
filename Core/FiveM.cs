using System.IO;
using System.Diagnostics;

namespace OrbitTweak.Core;

internal static class FiveM
{
    public static Settings Settings { get; set; } = new();

    /// <summary>Folder containing FiveM.exe (usually %LocalAppData%\FiveM).</summary>
    public static string? Root
    {
        get
        {
            if (!string.IsNullOrEmpty(Settings.FiveMPath) && File.Exists(Path.Combine(Settings.FiveMPath, "FiveM.exe")))
                return Settings.FiveMPath;
            var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM");
            return File.Exists(Path.Combine(def, "FiveM.exe")) ? def : null;
        }
    }

    public static string? Exe => Root is { } r ? Path.Combine(r, "FiveM.exe") : null;
    public static string? AppDir => Root is { } r ? Path.Combine(r, "FiveM.app") : null;
    public static string? DataDir => AppDir is { } a ? Path.Combine(a, "data") : null;
    public static string? CitizenIni => AppDir is { } a ? Path.Combine(a, "CitizenFX.ini") : null;

    public static IEnumerable<string> CacheDirs()
    {
        if (DataDir is not { } d) yield break;
        yield return Path.Combine(d, "cache");
        yield return Path.Combine(d, "server-cache");
        yield return Path.Combine(d, "server-cache-priv");
    }

    public static IEnumerable<string> LogDirs()
    {
        if (AppDir is not { } a) yield break;
        yield return Path.Combine(a, "logs");
        yield return Path.Combine(a, "crashes");
    }

    /// <summary>All FiveM executables we want to apply per-app tweaks to.</summary>
    public static List<string> GameExecutables()
    {
        var list = new List<string>();
        if (Exe is { } e) list.Add(e);
        if (DataDir is { } d)
        {
            var sub = Path.Combine(d, "cache", "subprocess");
            if (Directory.Exists(sub))
                list.AddRange(Directory.GetFiles(sub, "*GTAProcess.exe"));
        }
        return list;
    }

    public static Process[] Processes() =>
        Process.GetProcesses().Where(p =>
        {
            try { return p.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToArray();

    public static Process? GameProcess() =>
        Processes().FirstOrDefault(p => p.ProcessName.Contains("GTAProcess", StringComparison.OrdinalIgnoreCase));

    public static bool IsRunning => Processes().Length > 0;

    /// <summary>Returns e.g. "b3095" from a running game process.</summary>
    public static string? RunningBuild()
    {
        var g = GameProcess();
        if (g == null) return null;
        var n = g.ProcessName; // FiveM_b3095_GTAProcess
        var parts = n.Split('_');
        return parts.FirstOrDefault(x => x.Length > 1 && x[0] == 'b' && char.IsDigit(x[1]));
    }

    public static readonly string[] BackgroundApps =
    {
        "OneDrive", "Teams", "ms-teams", "Skype", "PhoneExperienceHost", "YourPhone",
        "EpicGamesLauncher", "EADesktop", "Origin", "UbisoftConnect", "upc", "Battle.net",
        "GalaxyClient", "CCleaner64", "AdobeARM", "Widgets", "WidgetService"
    };

    public static int KillBackgroundApps()
    {
        int n = 0;
        foreach (var name in BackgroundApps)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(true); n++; } catch { }
                finally { p.Dispose(); }
            }
        return n;
    }
}
