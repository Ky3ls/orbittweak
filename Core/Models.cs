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

public class Settings
{
    public string Accent { get; set; } = "ember";
    public string Mode { get; set; } = "dark";
    public bool AutoUpdate { get; set; } = true;
    public bool AutoInstall { get; set; }
    public bool AutoPriority { get; set; } = true;
    public bool AutoRamClean { get; set; }
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
    public MinimapCfg Minimap { get; set; } = new();
    public ClipCfg Clip { get; set; } = new();
    public Dictionary<string, string> ModCats { get; set; } = new();
    public bool ModsAck { get; set; }
    public long BgVersion { get; set; }
    public PadCfg Pad { get; set; } = new();
    public string? BgPreset { get; set; }
    public int XhairRev { get; set; }
    public DisplayCfg Display { get; set; } = new();

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrbitTweak");
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
                return s;
            }
        }
        catch { }
        return new Settings();
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
