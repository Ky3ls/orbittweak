using System.IO;
using System.Text.Json;

namespace OrbitTweak.Core;

public class Tweak
{
    public string Id { get; init; } = "";
    public string Cat { get; init; } = "system";
    public string Name { get; init; } = "";
    public string Desc { get; init; } = "";
    public string Icon { get; init; } = "zap";
    public string? Badge { get; init; }
    public bool Recommended { get; init; } = true;
    public bool NeedsRestart => Badge == "NEUSTART";
    public Func<bool> Check { get; init; } = () => false;
    public Action Apply { get; init; } = () => { };
    public Action Revert { get; init; } = () => { };

    public bool SafeCheck() { try { return Check(); } catch { return false; } }
}

public class CleanTarget
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Desc { get; init; } = "";
    public string Icon { get; init; } = "trash";
    public bool Selected { get; init; } = true;
    public Func<IEnumerable<string>> Dirs { get; init; } = () => Array.Empty<string>();
    public Func<long>? CustomSize { get; init; }
    public Action? CustomClean { get; init; }
    public string[] KeepFolders { get; init; } = Array.Empty<string>();
}

public class LaunchCfg
{
    public string Build { get; set; } = "Auto";
    public int Pure { get; set; }
    public Dictionary<string, bool> Pre { get; set; } = new()
    {
        ["ram"] = true, ["dns"] = true, ["kill"] = false, ["cache"] = false, ["prio"] = true
    };
}

/// <summary>Where the app keeps its files.</summary>
internal static class AppDirs
{
#if DEBUG
    // Test aid, debug builds only: ORBIT_TEST_DATA points a test run at its own folder, so it never touches the real settings.
    private static readonly string? Test = Environment.GetEnvironmentVariable("ORBIT_TEST_DATA") is { Length: > 0 } t ? t : null;
#else
    private const string? Test = null;
#endif
    /// <summary>Settings.</summary>
    public static string Roaming => Test ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrbitTweak");
    /// <summary>Working files: web pages, browser profile, clipper log.</summary>
    public static string Local => Test ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitTweak");
}

public class Settings
{
    public string Accent { get; set; } = "ember";
    public string Mode { get; set; } = "dark";
    public bool AutoUpdate { get; set; } = true;
    public bool AutoInstall { get; set; }
    public bool AutoPriority { get; set; } = true;
    public bool AutoRamClean { get; set; }
    /// <summary>While the game runs, heavy programs next to it are put behind it (lower priority, efficiency mode).</summary>
    public bool BgThrottle { get; set; }
    public bool RestoreBeforeBoost { get; set; } = true;
    public bool PurgeStandby { get; set; } = true;
    public bool TimerRes { get; set; }
    public bool FirstRun { get; set; } = true;
    public string? FiveMPath { get; set; }
    public DateTime? LastBoost { get; set; }
    public LaunchCfg Launch { get; set; } = new();
    public List<string> Favorites { get; set; } = new();
    public CrosshairCfg Xhair { get; set; } = new();
    public bool XhairAck { get; set; }
    public bool AutoBorderless { get; set; }
    public Dictionary<string, string> F8 { get; set; } = new();
    public bool F8Auto { get; set; } = true;
    public bool XhairShrink { get; set; } = true;
    public bool SnapTap { get; set; }
    /// <summary>When the WASD tweak (keyboard buffer size) was last changed here: it only takes effect after a restart of Windows.</summary>
    public DateTime? WasdSetAt { get; set; }
    /// <summary>WASD "Laggen": held movement keys are let go for Gap ms every Every ms while the game is in front.</summary>
    public WasdLagCfg Lag { get; set; } = new();
    public MinimapCfg Minimap { get; set; } = new();
    public ClipCfg Clip { get; set; } = new();
    public Dictionary<string, string> ModCats { get; set; } = new();
    public bool ModsAck { get; set; }
    public long BgVersion { get; set; }
    public PadCfg Pad { get; set; } = new();
    public string? BgPreset { get; set; }
    public Dictionary<string, List<string>> CatalogMods { get; set; } = new();
    public string? AuthToken { get; set; }
    public string? AuthRole { get; set; }
    public DateTime? AuthOkUntil { get; set; }
    public int XhairRev { get; set; }
    public DisplayCfg Display { get; set; } = new();
    public MinimapModCfg MinimapMod { get; set; } = new();
    public int ClipRev { get; set; }
    public int LagRev { get; set; }

    private static string Dir => AppDirs.Roaming;
    private static string FilePath => Path.Combine(Dir, "settings.v2.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                // 2.5.1: crosshair should stay visible when tabbing out
                if (s.XhairRev < 1) { s.Xhair.OnlyGame = false; s.XhairRev = 1; }
                // 2.21: the built-in recorder and the automatic kill-feed position become the default once
                if (s.ClipRev < 1) { s.Clip.Recorder = "orbit"; s.Clip.Area = "auto"; s.ClipRev = 1; }
                // the old default of "Laggen" (30 ms every 90 ms) was hardly visible in the game: now "Stark"
                // "Laggen" now makes the figure twitch instead of stop: the old stop-and-go values would be far too coarse
                if (s.LagRev < 2) { s.Lag.Gap = 35; s.Lag.Every = 80; s.LagRev = 2; }
                return s;
            }
        }
        catch { }
        return new Settings { XhairRev = 1, ClipRev = 1, LagRev = 2 }; // fresh install: nothing to migrate
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
