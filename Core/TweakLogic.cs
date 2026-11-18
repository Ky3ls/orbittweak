namespace OrbitTweak.Core;

/// <summary>
/// The parts of the tweaks that are plain logic, without Windows calls, so they can be checked anywhere
/// (tests/ClipLogic.Tests).
/// </summary>
internal static class TweakLogic
{
    // ---------- per-app graphics string (HKCU\Software\Microsoft\DirectX\UserGpuPreferences) ----------
    // Windows keeps several fields in one value, e.g. "GpuPreference=2;SwapEffectUpgradeEnable=1;AutoHDREnable=1;".
    // A tweak only ever changes its own field and keeps the others as they are.

    /// <summary>Splits "k=v;k2=v2;" into its fields, in order. Pieces without "=" are kept as they are (value null).</summary>
    public static List<(string key, string? val)> ParseFields(string? s)
    {
        var list = new List<(string, string?)>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var raw in s.Split(';'))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            int i = p.IndexOf('=');
            list.Add(i < 0 ? (p, null) : (p[..i], p[(i + 1)..]));
        }
        return list;
    }

    public static string BuildFields(IEnumerable<(string key, string? val)> fields)
        => string.Concat(fields.Select(f => f.val == null ? f.key + ";" : $"{f.key}={f.val};"));

    /// <summary>Value of one field, null when it is not there.</summary>
    public static string? Field(string? s, string key)
    {
        foreach (var (k, v) in ParseFields(s)) if (k.Equals(key, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }

    /// <summary>The string with one field set (val) or removed (val null); every other field stays. Empty result = null.</summary>
    public static string? WithField(string? s, string key, string? val)
    {
        var list = ParseFields(s);
        int at = list.FindIndex(f => f.key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (val == null) { if (at >= 0) list.RemoveAt(at); }
        else if (at >= 0) list[at] = (list[at].key, val);
        else list.Add((key, val));
        return list.Count == 0 ? null : BuildFields(list);
    }

    // ---------- accessibility shortcuts (STICKYKEYS / TOGGLEKEYS / FILTERKEYS / MOUSEKEYS) ----------
    public const uint HOTKEYACTIVE = 0x4, CONFIRMHOTKEY = 0x8;

    /// <summary>Flags with only the shortcut switched off; every other bit (the feature itself included) stays.</summary>
    public static uint WithoutHotkey(uint flags) => flags & ~HOTKEYACTIVE;

    /// <summary>Gives back the shortcut bits of the saved flags and keeps what the user changed in the feature since.</summary>
    public static uint RestoreHotkey(uint current, uint saved)
        => (current & ~(HOTKEYACTIVE | CONFIRMHOTKEY)) | (saved & (HOTKEYACTIVE | CONFIRMHOTKEY));

    // ---------- page file (HKLM\...\Memory Management, PagingFiles) ----------
    /// <summary>
    /// True when Windows manages the page file (the default "?:\pagefile.sys", or a drive with system-managed size
    /// "C:\pagefile.sys 0 0"), or when a custom setup gives a commit limit (RAM + page files) of at least 32 GB.
    /// False when the page file is switched off or small. ramMb is the installed RAM (<see cref="InstalledRamMb"/>).
    /// </summary>
    public static bool PagefileOk(string[]? pagingFiles, double ramMb)
    {
        if (pagingFiles == null) return false;
        long sumMax = 0;
        foreach (var l in pagingFiles)
        {
            var p = l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            if (p.Length == 1) return true;                                   // "?:\pagefile.sys": automatic for all drives
            if (p.Length >= 3 && p[1] == "0" && p[2] == "0") return true;     // system-managed size on that drive
            if (p.Length >= 3 && long.TryParse(p[2], out var max)) sumMax += max;
        }
        return sumMax > 0 && ramMb + sumMax >= 32768;
    }

    /// <summary>
    /// The installed RAM in MB: what the firmware reports (installedKb), else the usable RAM rounded to whole GB.
    /// Usable RAM is a little less (a 16 GB PC shows about 16 250 MB), and a page file the size of the RAM must still count as enough.
    /// </summary>
    public static double InstalledRamMb(ulong installedKb, double usableMb)
        => installedKb > 0 ? installedKb / 1024.0 : Math.Round(usableMb / 1024) * 1024;

    // ---------- backups ----------
    /// <summary>
    /// Whether a backup of the value as it is now should be written before a tweak sets "target": only when the value
    /// is not already the target (then it is the real "before" value, also over an old backup). When it already is the
    /// target, an existing backup is the real one and stays.
    /// </summary>
    public static bool ShouldBackup(string? live, string target) => (live ?? "-") != target;

    /// <summary>"a,b,c" → three ints, null when it is not exactly that.</summary>
    public static int[]? Ints3(string? s)
    {
        var p = s?.Split(',');
        if (p is not { Length: 3 }) return null;
        var r = new int[3];
        for (int i = 0; i < 3; i++) if (!int.TryParse(p[i], out r[i])) return null;
        return r;
    }

    /// <summary>Dual-CCD Ryzen X3D (7900X3D, 7950X3D, 9900X3D, 9950X3D, 7945HX3D …): AMD's driver steers games onto the cache chiplet.</summary>
    public static bool IsDualCcdX3D(string cpuName) => System.Text.RegularExpressions.Regex.IsMatch(cpuName, @"Ryzen 9 \d{4}[A-Z]*X3D", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
