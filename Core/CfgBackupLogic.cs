using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>One settings file the backup knows. <see cref="Root"/> is a placeholder (<see cref="CfgBackupLogic.AppData"/>,
/// <see cref="CfgBackupLogic.LocalAppData"/> or <see cref="CfgBackupLogic.FiveMApp"/>), <see cref="Rel"/> the path below it
/// with backslashes, <see cref="Name"/> the file name the page shows, <see cref="What"/> what is in it.</summary>
public sealed record CfgFile(string Key, string Root, string Rel, string Name, string What);

/// <summary>Where the placeholders point on this PC. A null root is unknown (FiveM not installed, say).</summary>
public sealed record CfgRoots(string? AppData, string? LocalAppData, string? FiveMApp);

/// <summary>manifest.json of a backup zip: what is in it and where each file came from (as a placeholder path, so no
/// user name is in it and a backup still fits after Windows or FiveM moved).</summary>
public sealed class CfgManifest
{
    public int Format { get; set; }
    public string Kind { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public string App { get; set; } = "";
    public List<CfgEntry> Files { get; set; } = new();
}

public sealed class CfgEntry
{
    public string Key { get; set; } = "";
    /// <summary>The original place with a placeholder, e.g. %APPDATA%\CitizenFX\fivem.cfg.</summary>
    public string Path { get; set; } = "";
    /// <summary>The file's name inside the zip.</summary>
    public string Entry { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTimeOffset? Modified { get; set; }
}

/// <summary>A restore step: write <see cref="Entry"/> to <see cref="Target"/>, or leave it with the reason <see cref="Skip"/>.</summary>
public sealed record CfgPlanItem(CfgEntry Entry, CfgFile File, string? Target, string? Skip);

/// <summary>A backup on disk as the clean-up sees it. Kind null: its manifest could not be read (kept, never pruned).</summary>
public sealed record CfgStored(string Name, string? Kind, DateTimeOffset Created);

/// <summary>
/// The pure part of the settings backup (2.31): which files hold FiveM's user settings, placeholders and real paths,
/// the manifest, names, the clean-up of old automatic backups, and the checks a backup has to pass before anything is
/// written back. No Windows calls here, so tests/CfgBackupLogic.Tests checks it anywhere.
///
/// Which files (citizenfx/fivem): fxd:/ is %AppData%\CitizenFX\ (rage-device-five/src/CitizenMount.Shared.cpp).
/// fivem.cfg is FiveM's archived console state, written by glue/src/ConsoleBinding.cpp: every key binding ("bind",
/// and "rbind" for the keys of server scripts, gta-core-five/src/GameInput.cpp), the pause-menu settings as profile_*
/// convars (gta-core-five/src/GameProfileSettings.cpp) and FiveM's own options. gta5_settings.xml is the graphics file
/// of installs since June 2020, %LocalAppData%\Rockstar Games\GTA V\settings.xml the one of older installs (see
/// GfxFile.cs). FiveM.app\CitizenFX.ini keeps the GTA V folder and the update channel.
/// Left out on purpose: caches, crash dumps and logs (big, no settings), the server-script storage (kvs) and NUI
/// storage (cookies and data of servers), and the Rockstar sign-in (ros_*), which is private.
/// </summary>
public static class CfgBackupLogic
{
    public const string AppData = "%APPDATA%", LocalAppData = "%LOCALAPPDATA%", FiveMApp = "<FiveM.app>";
    public const string Manual = "manual", Auto = "auto", Safety = "safety";
    public const int Format = 1;
    /// <summary>Automatic backups kept; manual ones are never removed by OrbitTweak.</summary>
    public const int KeepAuto = 10;
    /// <summary>The snapshots taken before a restore that are kept.</summary>
    public const int KeepSafety = 5;
    /// <summary>A settings file is a few KB (fivem.cfg with hundreds of binds stays under 100 KB). Anything bigger is
    /// not a settings file any more and is left out.</summary>
    public const long MaxFile = 4L * 1024 * 1024;
    /// <summary>A backup zip bigger than this is not one of ours and is not opened.</summary>
    public const long MaxZip = 24L * 1024 * 1024;
    public const long MaxManifest = 256 * 1024;
    public const string ManifestName = "manifest.json";
    public const string NamePrefix = "fivem-";

    public static readonly CfgFile[] Files =
    {
        new("fivemcfg", AppData, @"CitizenFX\fivem.cfg", "fivem.cfg", "Tastenbelegung und Spiel-Einstellungen"),
        new("gfx", AppData, @"CitizenFX\gta5_settings.xml", "gta5_settings.xml", "Grafik"),
        new("ini", FiveMApp, "CitizenFX.ini", "CitizenFX.ini", "GTA-V-Ordner und Update-Kanal"),
        new("gtaxml", LocalAppData, @"Rockstar Games\GTA V\settings.xml", "settings.xml", "Grafik älterer FiveM-Installationen"),
    };

    // ---------------------------------------------------------------- placeholders and paths
    public static string Placeholder(CfgFile f) => f.Root + "\\" + f.Rel;

    private static string Norm(string p) => p.Replace('/', '\\').Trim();

    public static CfgFile? ByPath(string? placeholder)
    {
        if (string.IsNullOrWhiteSpace(placeholder)) return null;
        var p = Norm(placeholder);
        return Files.FirstOrDefault(f => string.Equals(Placeholder(f), p, StringComparison.OrdinalIgnoreCase));
    }

    public static CfgFile? ByKey(string? key) => key == null ? null : Files.FirstOrDefault(f => f.Key == key);

    public static string? RootOf(CfgRoots r, string root) => root switch
    {
        AppData => r.AppData,
        LocalAppData => r.LocalAppData,
        FiveMApp => r.FiveMApp,
        _ => null,
    };

    private static readonly Regex Drive = new(@"^[A-Za-z]:\\", RegexOptions.CultureInvariant);
    private static readonly Regex Unc = new(@"^\\\\[^\\/:*?""<>|]+\\[^\\/:*?""<>|]+", RegexOptions.CultureInvariant);

    /// <summary>An absolute Windows folder (drive or network share) without "." or ".." parts and without characters
    /// Windows does not allow in names.</summary>
    public static bool SafeRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || root.Length > 240) return false;
        var r = root.Replace('/', '\\');
        bool unc = r.StartsWith(@"\\", StringComparison.Ordinal);
        if (!(unc ? Unc.IsMatch(r) : Drive.IsMatch(r))) return false;
        if (r.StartsWith(@"\\?\", StringComparison.Ordinal) || r.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
        var rest = unc ? r[2..] : r[3..];
        foreach (var c in rest) if (c < 32 || "<>\"|?*:".Contains(c)) return false;
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            if (part.Trim() is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')) return false;
        return true;
    }

    /// <summary>The real path of a known placeholder path, or null for a path the backup does not know or a root that
    /// is unknown or not a plain folder. Only the paths in <see cref="Files"/> ever resolve.</summary>
    public static string? Resolve(string? placeholder, CfgRoots roots)
    {
        var f = ByPath(placeholder);
        if (f == null) return null;
        var root = RootOf(roots, f.Root);
        if (!SafeRoot(root)) return null;
        return root!.Replace('/', '\\').TrimEnd('\\') + "\\" + f.Rel;
    }

    /// <summary>A real path shown with its placeholder instead of the user's folder ("%APPDATA%\OrbitTweak\backups"), or
    /// null when it is under none of the roots. The longest root wins: FiveM.app usually is under %LOCALAPPDATA%.</summary>
    public static string? ToPlaceholder(string? full, CfgRoots roots)
    {
        if (string.IsNullOrWhiteSpace(full)) return null;
        var p = Norm(full);
        if (p.Split('\\').Any(s => s is "." or "..")) return null;
        var list = new[] { (FiveMApp, roots.FiveMApp), (LocalAppData, roots.LocalAppData), (AppData, roots.AppData) }
            .Where(x => SafeRoot(x.Item2))
            .Select(x => (name: x.Item1, root: x.Item2!.Replace('/', '\\').TrimEnd('\\')))
            .OrderByDescending(x => x.root.Length);
        foreach (var (name, root) in list)
        {
            if (p.Equals(root, StringComparison.OrdinalIgnoreCase)) return name;
            if (p.Length > root.Length && p[root.Length] == '\\' && p.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return name + p[root.Length..];
        }
        return null;
    }

    // ---------------------------------------------------------------- names
    private static readonly Regex NameRx = new(@"^fivem-(\d{4})-(\d{2})-(\d{2})_(\d{2})(\d{2})(\d{2})(?:-(\d{1,3}))?\.zip$", RegexOptions.CultureInvariant);

    /// <summary>fivem-2026-11-22_140205.zip; a second one in the same second gets -2, -3 …</summary>
    public static string NameFor(DateTime local, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var stem = NamePrefix + local.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        if (!taken.Contains(stem + ".zip")) return stem + ".zip";
        for (int i = 2; i < 1000; i++) if (!taken.Contains($"{stem}-{i}.zip")) return $"{stem}-{i}.zip";
        throw new InvalidOperationException("Zu viele Sicherungen in derselben Sekunde.");
    }

    /// <summary>Only names made by <see cref="NameFor"/>: no folders, no other files.</summary>
    public static bool ValidName(string? name) => name != null && TimeOf(name) != null;

    public static DateTime? TimeOf(string? name)
    {
        if (name == null) return null;
        var m = NameRx.Match(name);
        if (!m.Success) return null;
        int G(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        try { return new DateTime(G(1), G(2), G(3), G(4), G(5), G(6), DateTimeKind.Local); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>The order of backups made in the same second: 1 for the first, 2 for "-2" …</summary>
    public static int SeqOf(string? name)
    {
        var m = name == null ? Match.Empty : NameRx.Match(name);
        return m.Success && m.Groups[7].Success ? int.Parse(m.Groups[7].Value, CultureInfo.InvariantCulture) : 1;
    }

    // ---------------------------------------------------------------- zip entries and hashes
    public static string EntryName(CfgFile f) => "files/" + f.Key + "/" + f.Name;

    private static readonly Regex EntryRx = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*(?:/[A-Za-z0-9][A-Za-z0-9._-]*)*$", RegexOptions.CultureInvariant);

    /// <summary>A zip entry name that cannot leave a folder: relative, forward slashes, no "..", plain characters.</summary>
    public static bool SafeEntryName(string? e) =>
        e != null && e.Length <= 200 && EntryRx.IsMatch(e) && !e.Split('/').Any(s => s is "." or ".." || s.EndsWith('.'));

    public static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static readonly Regex ShaRx = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    public static bool ValidSha(string? s) => s != null && ShaRx.IsMatch(s);

    // ---------------------------------------------------------------- manifest
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // the manifest is read by people too (it never goes into a web page): "<FiveM.app>" stays as it is
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CfgManifest NewManifest(string kind, DateTimeOffset created, string app, IEnumerable<(CfgFile file, long size, string sha, DateTimeOffset? modified)> files) => new()
    {
        Format = Format, Kind = kind, Created = created, App = app,
        Files = files.Select(x => new CfgEntry
        {
            Key = x.file.Key, Path = Placeholder(x.file), Entry = EntryName(x.file), Size = x.size, Sha256 = x.sha, Modified = x.modified,
        }).ToList(),
    };

    public static string Serialize(CfgManifest m) => JsonSerializer.Serialize(m, Json);

    /// <summary>Reads and checks a manifest; null with a German reason when it is not one this version can restore.</summary>
    public static CfgManifest? Parse(string? json, out string? error)
    {
        CfgManifest? m = null;
        try { if (!string.IsNullOrWhiteSpace(json)) m = JsonSerializer.Deserialize<CfgManifest>(json, Json); }
        catch (JsonException) { }
        catch (NotSupportedException) { }
        if (m == null) { error = "Die Inhaltsliste der Sicherung ist unlesbar."; return null; }
        error = Validate(m);
        return error == null ? m : null;
    }

    /// <summary>Null when every entry is one of the known files at its known place with a plausible size and hash.</summary>
    public static string? Validate(CfgManifest m)
    {
        if (m.Format > Format) return "Diese Sicherung stammt aus einer neueren OrbitTweak-Version.";
        if (m.Format != Format) return "Diese Sicherung hat ein unbekanntes Format.";
        if (m.Kind is not (Manual or Auto or Safety)) return "Diese Sicherung hat eine unbekannte Art.";
        if (m.Files == null || m.Files.Count == 0) return "In dieser Sicherung ist keine Datei.";
        if (m.Files.Count > Files.Length) return "In dieser Sicherung sind mehr Dateien, als OrbitTweak sichert.";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in m.Files)
        {
            if (e == null) return "Die Inhaltsliste der Sicherung ist unvollständig.";
            var f = ByPath(e.Path);
            if (f == null || f.Key != e.Key) return "Die Sicherung nennt einen Ort, den OrbitTweak nicht beschreibt.";
            if (!seen.Add(f.Key)) return $"{f.Name} steht zweimal in der Sicherung.";
            if (e.Entry != EntryName(f) || !SafeEntryName(e.Entry)) return $"{f.Name}: ungültiger Name in der Sicherung.";
            if (e.Size < 0 || e.Size > MaxFile) return $"{f.Name}: ungültige Größe in der Sicherung.";
            if (!ValidSha(e.Sha256)) return $"{f.Name}: ungültige Prüfsumme in der Sicherung.";
        }
        return null;
    }

    /// <summary>The same files with the same content as the newest backup: an automatic one would add nothing.</summary>
    public static bool SameContent(CfgManifest? last, IEnumerable<(string key, string sha)> now)
    {
        if (last?.Files == null) return false;
        var a = last.Files.Where(e => e != null).Select(e => (e.Key, e.Sha256)).OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
        var b = now.OrderBy(x => x.key, StringComparer.Ordinal).ToList();
        return a.Count == b.Count && a.Zip(b).All(p => p.First.Key == p.Second.key && p.First.Sha256 == p.Second.sha);
    }

    // ---------------------------------------------------------------- clean-up and schedule
    /// <summary>What to delete: automatic backups beyond the newest <see cref="KeepAuto"/>, snapshots before a restore
    /// beyond the newest <see cref="KeepSafety"/>. Manual ones and ones that cannot be read are never deleted.</summary>
    public static List<string> Prune(IEnumerable<CfgStored> all)
    {
        var del = new List<string>();
        foreach (var (kind, keep) in new[] { (Auto, KeepAuto), (Safety, KeepSafety) })
            del.AddRange(all.Where(s => s.Kind == kind && ValidName(s.Name))
                .OrderByDescending(s => s.Created).ThenByDescending(s => SeqOf(s.Name))
                .Skip(keep).Select(s => s.Name));
        return del;
    }

    /// <summary>At most one automatic backup per calendar day (in the local time of <paramref name="now"/>). A clock
    /// that was set back makes it due again instead of blocking it until that day comes.</summary>
    public static bool AutoDue(DateTimeOffset? last, DateTimeOffset now) =>
        last is not { } l || l.ToOffset(now.Offset).Date != now.Date;

    /// <summary>FiveM was just started: seen closed on the look before and open now. A first look that already finds it
    /// running (OrbitTweak opened during a session) is no start: its files may be in the middle of being written.</summary>
    public static bool Started(bool? before, bool now) => before == false && now;

    // ---------------------------------------------------------------- restore
    /// <summary>The steps of a restore: the chosen files (all when <paramref name="keys"/> is empty) at their known
    /// places on this PC. A key that is not in the backup is an error; a place that cannot be found is skipped.</summary>
    public static List<CfgPlanItem> Plan(CfgManifest m, IReadOnlyCollection<string>? keys, CfgRoots roots, out string? error)
    {
        error = Validate(m);
        var plan = new List<CfgPlanItem>();
        if (error != null) return plan;
        var want = keys is { Count: > 0 } ? new HashSet<string>(keys, StringComparer.Ordinal) : null;
        if (want != null && want.Any(k => !m.Files.Any(e => e.Key == k)))
        {
            error = "Diese Datei ist nicht in der Sicherung.";
            return plan;
        }
        foreach (var e in m.Files)
        {
            if (want != null && !want.Contains(e.Key)) continue;
            var f = ByPath(e.Path)!;
            var target = Resolve(e.Path, roots);
            plan.Add(new CfgPlanItem(e, f, target, target != null ? null
                : f.Root == FiveMApp ? "FiveM-Ordner nicht gefunden" : "Ordner nicht gefunden"));
        }
        return plan;
    }

    /// <summary>The files of a restore that must stay as they are: there now, but not readable for the snapshot taken
    /// first (locked, too big). Writing them would lose their current state for good. In the order of <paramref name="write"/>.</summary>
    public static List<string> Uncovered(IEnumerable<string> write, IEnumerable<string> unread)
    {
        var u = new HashSet<string>(unread, StringComparer.Ordinal);
        return write.Where(u.Contains).Distinct().ToList();
    }

    /// <summary>What "Rückgängig" writes back: only the files this restore wrote (or tried to) whose earlier state the
    /// snapshot holds. Files it did not touch stay as they are, even if they changed since; a file that was not there
    /// before is not in the snapshot and stays. In the order of <see cref="Files"/>.</summary>
    public static List<string> UndoKeys(IEnumerable<string> touched, IEnumerable<string> saved)
    {
        var t = new HashSet<string>(touched, StringComparer.Ordinal);
        var s = new HashSet<string>(saved, StringComparer.Ordinal);
        return Files.Select(f => f.Key).Where(k => t.Contains(k) && s.Contains(k)).ToList();
    }

    public static string KindLabel(string? kind) => kind switch
    {
        Manual => "Von dir",
        Auto => "Automatisch",
        Safety => "Vor dem Wiederherstellen",
        _ => "Unbekannt",
    };
}
