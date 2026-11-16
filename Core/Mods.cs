using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>One file a mod put somewhere.</summary>
public sealed class ModFile
{
    /// <summary>mods = FiveM.app\mods, citizen = FiveM.app\citizen, gta = the GTA V folder.</summary>
    public string Root { get; set; } = "mods";
    /// <summary>Path below the root, with '/'.</summary>
    public string Rel { get; set; } = "";
    /// <summary>Size of our file: a different size at the target means someone else (Steam/Rockstar file check) replaced it.</summary>
    public long Size { get; set; }
}

/// <summary>One installed mod, with every file it placed. Kept in mods.v1.json next to the settings.</summary>
public sealed class InstalledMod
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Cat { get; set; } = "other";
    /// <summary>The file it was installed from ("GunSounds.zip").</summary>
    public string Source { get; set; } = "";
    public string? CatalogId { get; set; }
    public bool On { get; set; } = true;
    public DateTime Date { get; set; }
    public List<ModFile> Files { get; set; } = new();
}

internal sealed class ModManifest
{
    public List<InstalledMod> Mods { get; set; } = new();
    /// <summary>Category and library id by file name, from the settings of versions before 2.24; used once when the mods folder is read.</summary>
    public Dictionary<string, string> LegacyCats { get; set; } = new();
    public Dictionary<string, string> LegacyCatalog { get; set; } = new();
}

internal sealed class InstallOpts
{
    /// <summary>The category the user picked; only used when the content does not tell.</summary>
    public string Cat = "";
    public string? Title, CatalogId;
    /// <summary>The version folder the user chose when an archive holds several versions.</summary>
    public string? Variant;
    /// <summary>No one to ask (mod library): the FiveM version first, otherwise the first one.</summary>
    public bool AutoVariant;
    /// <summary>Sound archives go into the GTA V folder (works on pure-mode 1 servers too) instead of FiveM's citizen folder.</summary>
    public bool SoundToGta;
}

internal sealed class InstallResult
{
    public int Count;
    public string? Error;
    public List<string> Notes = new();
    /// <summary>Several versions in the archive: the version folders to choose from ("" = top folder).</summary>
    public List<string>? Choose;
    public List<InstalledMod> Mods = new();
    /// <summary>Mods switched off because the new one replaces the same file.</summary>
    public List<string> TurnedOff = new();

    public object Ui(IEnumerable<string>? files = null) => new
    {
        count = Count, error = Error, notes = Notes.Distinct().ToList(), choose = Choose, off = TurnedOff,
        files = Choose != null ? files : null,
        mods = Mods.Select(m => new { id = m.Id, name = m.Title, cat = m.Cat, places = m.Files.Select(f => f.Root).Distinct().ToList() }),
    };
}

/// <summary>
/// Installs client mods where FiveM really loads them, and keeps track of every file so a mod can be switched off,
/// on and deleted again – only on the user's request:
/// - mod packages (assembly.xml inside) → FiveM.app\mods\ot-&lt;name&gt;-&lt;id&gt;.rpf
/// - raw archives / loose game files → wrapped into such a package first
/// - sound archives (WEAPONS_PLAYER.rpf …) → FiveM.app\citizen\platform\audio\sfx, or the GTA V folder on request;
///   a file that was there before is kept in %LocalAppData%\OrbitTweak\mod-backup and put back when the mod is switched off.
/// Switched-off files wait in FiveM.app\mods_orbit_off\&lt;id&gt;\&lt;root&gt;\…
/// </summary>
internal static class Mods
{
    public static string? Dir => FiveM.AppDir is { } a ? Path.Combine(a, "mods") : null;
    private static string? OffDir => FiveM.AppDir is { } a ? Path.Combine(a, "mods_orbit_off") : null;
    private static string ManifestPath => Path.Combine(AppDirs.Roaming, "mods.v1.json");
    private static string BackupDir => Path.Combine(AppDirs.Local, "mod-backup");

    /// <summary>The GTA V folder FiveM uses (CitizenFX.ini IVPath), when it has the sound archives.</summary>
    public static string? GtaDir
    {
        get
        {
            try { var iv = CitizenIni.Read("IVPath"); return iv != null && Directory.Exists(Path.Combine(iv, "x64", "audio", "sfx")) ? iv : null; }
            catch { return null; }
        }
    }

    private static readonly object Gate = new();
    private static ModManifest? _m;
    private static ModManifest M
    {
        get
        {
            if (_m != null) return _m;
            try { if (File.Exists(ManifestPath)) _m = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(ManifestPath)); } catch { }
            return _m ??= new ModManifest();
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDirs.Roaming);
            var tmp = ManifestPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(M, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, ManifestPath, true);
        }
        catch { }
    }

    /// <summary>
    /// The name a file had before a browser or Discord added " (3)" or " - Kopie" to it. FiveM's mods folder does not care
    /// about file names (it only needs ".rpf" in small letters); the name only matters where the game itself opens the archive:
    /// sound archives such as WEAPONS_PLAYER.rpf, whose NG encryption key is made from the name.
    /// </summary>
    public static string CleanName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        string before;
        do
        {
            before = name;
            name = Regex.Replace(name, @"(\s*\(\d+\)|\s*-\s*(Kopie|Copy)(\s*\(\d+\))?|\s+copy)$", "", RegexOptions.IgnoreCase).TrimEnd();
        } while (name != before && name.Length > 0);
        return (name.Length > 0 ? name : Path.GetFileNameWithoutExtension(file)) + ".rpf";
    }

    // ---------------------------------------------------------------- paths

    private static string? RootDir(string root) => root switch
    {
        "mods" => Dir,
        "citizen" => FiveM.AppDir is { } a ? Path.Combine(a, "citizen") : null,
        "gta" => GtaDir,
        _ => null,
    };
    private static string Sys(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);
    private static string? Target(ModFile f) => RootDir(f.Root) is { } r ? Path.Combine(r, Sys(f.Rel)) : null;
    private static string OffPath(InstalledMod m, ModFile f) => Path.Combine(OffDir!, m.Id, f.Root, Sys(f.Rel));
    private static string BackupPath(ModFile f) => Path.Combine(BackupDir, f.Root, Sys(f.Rel));
    private static string Shown(ModFile f) => (f.Root switch { "mods" => @"FiveM.app\mods\", "citizen" => @"FiveM.app\citizen\", _ => @"GTA V\" }) + f.Rel.Replace('/', '\\');
    private static bool Same(ModFile a, ModFile b) => a.Root == b.Root && a.Rel.Equals(b.Rel, StringComparison.OrdinalIgnoreCase);
    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    // ---------------------------------------------------------------- legacy data and files placed by others

    /// <summary>Takes over categories and library ids from the settings of older versions. True when the settings changed.</summary>
    public static bool ImportLegacy(Dictionary<string, string> cats, Dictionary<string, List<string>> catalog)
    {
        if (cats.Count == 0 && catalog.Count == 0) return false;
        lock (Gate)
        {
            foreach (var kv in cats) M.LegacyCats[kv.Key] = kv.Value;
            foreach (var kv in catalog) foreach (var n in kv.Value) M.LegacyCatalog[n] = kv.Key;
            Save();
        }
        cats.Clear(); catalog.Clear();
        return true;
    }

    private static string? Lookup(Dictionary<string, string> d, string key) => d.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private static InstalledMod Adopted(FileInfo f, bool on)
    {
        string cat = Lookup(M.LegacyCats, f.Name) ?? "";
        if (cat.Length == 0)
            try { cat = ModClassifier.Classify(f.FullName, f.Name).Cat ?? ""; } catch { }
        bool mm = f.Name.Equals(MinimapBuilder.FileName, StringComparison.OrdinalIgnoreCase);
        return new InstalledMod
        {
            Id = NewId(), Title = mm ? "Eigene Minimap" : Path.GetFileNameWithoutExtension(f.Name), Source = f.Name, On = on, Date = f.LastWriteTime,
            Cat = mm ? "minimap" : cat.Length > 0 ? cat : ModClassifier.GuessByName(f.Name), CatalogId = Lookup(M.LegacyCatalog, f.Name),
            Files = new() { new ModFile { Root = "mods", Rel = f.Name, Size = f.Length } },
        };
    }

    /// <summary>Brings the list in line with the disk: packs put into the mods folder by hand (or by versions before 2.24) are added, deleted ones dropped.</summary>
    private static void Sync()
    {
        if (Dir is not { } dir || OffDir is not { } off) return;
        bool changed = false;
        var known = new HashSet<string>(M.Mods.SelectMany(m => m.Files.Where(f => f.Root == "mods").Select(f => f.Rel)), StringComparer.OrdinalIgnoreCase);
        static bool IsRpf(FileInfo f) => f.Extension.Equals(".rpf", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(dir))
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles().Where(IsRpf))
                    if (known.Add(f.Name)) { M.Mods.Add(Adopted(f, true)); changed = true; }
            // older versions switched packs off by moving them straight into mods_orbit_off
            if (Directory.Exists(off))
                foreach (var f in new DirectoryInfo(off).EnumerateFiles().Where(IsRpf))
                {
                    var m = Adopted(f, false);
                    var to = OffPath(m, m.Files[0]);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Move(f.FullName, to);
                    M.Mods.Add(m); changed = true;
                }
        }
        catch { }
        // deleted by hand: forget it (a file whose folder is unknown right now, e.g. no GTA path, is kept)
        changed |= M.Mods.RemoveAll(m => m.Files.Count == 0 || m.Files.All(f =>
        {
            var p = m.On ? Target(f) : OffPath(m, f);
            return p != null && !File.Exists(p);
        })) > 0;
        if (M.LegacyCats.Count + M.LegacyCatalog.Count > 0) { M.LegacyCats.Clear(); M.LegacyCatalog.Clear(); changed = true; }
        if (changed) Save();
    }

    private static InstalledMod? Find(string key) =>
        M.Mods.FirstOrDefault(m => m.Id == key)
        ?? M.Mods.FirstOrDefault(m => m.Files.Any(f => f.Root == "mods" && f.Rel.Equals(key, StringComparison.OrdinalIgnoreCase)))
        ?? M.Mods.FirstOrDefault(m => m.Source.Equals(key, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- list

    private static readonly Dictionary<string, (long len, DateTime t, string? why, bool fix)> CheckCache = new();

    /// <summary>Why FiveM would ignore a file in its mods folder, and whether "Reparieren" can fix it.</summary>
    private static (string? why, bool fix) Check(string path)
    {
        var fi = new FileInfo(path);
        if (CheckCache.TryGetValue(path, out var c) && c.len == fi.Length && c.t == fi.LastWriteTimeUtc) return (c.why, c.fix);
        (string? why, bool fix) r;
        try
        {
            var info = ModClassifier.Classify(path, fi.Name);
            r = info.Kind switch
            {
                RpfKind.Package when info.Problem != null => ("Wirkt nicht: " + info.Problem, false),
                RpfKind.Package when !fi.Name.EndsWith(".rpf", StringComparison.Ordinal) => ("Wirkt nicht: FiveM erkennt nur die Endung „.rpf“ in kleinen Buchstaben.", true),
                RpfKind.Package => (null, false),
                RpfKind.AudioArchive when info.Encrypted => (ModText.Broken + " Sound-Packs gehören in den citizen-Ordner.", true),
                RpfKind.AudioArchive => (ModText.Broken + " Unverschlüsseltes Sound-Archiv, nicht FiveM-fertig.", false),
                RpfKind.StreamArchive or RpfKind.DlcPack => (ModText.Broken + " Rohes Archiv ohne assembly.xml.", true),
                _ => (ModText.Broken, false),
            };
        }
        catch { r = (ModText.Broken + " Kein gültiges RPF-Archiv.", false); }
        CheckCache[path] = (fi.Length, fi.LastWriteTimeUtc, r.why, r.fix);
        return r;
    }

    private static object Item(InstalledMod m)
    {
        string? broken = null; bool fix = false; long size = 0;
        foreach (var f in m.Files)
        {
            var path = m.On ? Target(f) : OffPath(m, f);
            if (path == null) { broken ??= "GTA-V-Ordner nicht gefunden (Pfad in CitizenFX.ini fehlt)"; size += f.Size; continue; }
            if (!File.Exists(path)) { broken ??= "Datei fehlt: " + Path.GetFileName(f.Rel); continue; }
            var len = new FileInfo(path).Length; size += len;
            if (broken != null) continue;
            if (m.On && f.Root != "mods" && len != f.Size) broken = "Datei wurde ersetzt, z. B. durch „Dateien überprüfen“ von Steam/Rockstar. Neu installieren.";
            else if (f.Root == "mods") (broken, fix) = Check(path);
        }
        var roots = m.Files.Select(f => f.Root).Distinct().ToList();
        return new
        {
            id = m.Id, name = m.Title, file = m.Source, on = m.On, size, date = m.Date, cat = m.Cat, catalog = m.CatalogId,
            places = roots, paths = m.Files.Select(Shown).ToList(),
            // highest pure level the mod still works on: FiveM's own folder only without pure mode, sounds in the GTA folder also on level 1
            pure = roots.Count > 0 && roots.All(r => r == "gta") ? 1 : 0,
            broken, fix,
        };
    }

    public static object List()
    {
        lock (Gate)
        {
            if (Dir is not { } dir) return new { found = false, items = Array.Empty<object>() };
            Sync();
            return new { found = true, dir, gta = GtaDir != null, items = M.Mods.OrderByDescending(m => m.Date).Select(Item).ToList() };
        }
    }

    public static List<string> CatalogIds()
    {
        lock (Gate) { Sync(); return M.Mods.Where(m => m.CatalogId != null).Select(m => m.CatalogId!).Distinct().ToList(); }
    }

    /// <summary>For the minimap page: is the pack in the mods folder, and switched on?</summary>
    public static (bool installed, bool on, long size, DateTime? date) State(string modsFile)
    {
        lock (Gate)
        {
            Sync();
            var m = M.Mods.FirstOrDefault(x => x.Files.Any(f => f.Root == "mods" && f.Rel.Equals(modsFile, StringComparison.OrdinalIgnoreCase)));
            if (m == null) return (false, false, 0, null);
            var p = m.On ? Target(m.Files[0]) : OffPath(m, m.Files[0]);
            var fi = p != null && File.Exists(p) ? new FileInfo(p) : null;
            return (fi != null, m.On, fi?.Length ?? 0, fi?.LastWriteTime);
        }
    }

    /// <summary>Registers a pack OrbitTweak just wrote into the mods folder itself (own minimap). An older switched-off copy is dropped.</summary>
    public static void Adopt(string modsFile, string title, string cat)
    {
        lock (Gate)
        {
            foreach (var old in M.Mods.Where(m => m.Files.Any(f => f.Root == "mods" && f.Rel.Equals(modsFile, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                if (!old.On) TryDeleteDir(Path.Combine(OffDir!, old.Id));
                M.Mods.Remove(old);
            }
            var fi = new FileInfo(Path.Combine(Dir!, modsFile));
            M.Mods.Add(new InstalledMod { Id = NewId(), Title = title, Cat = cat, Source = modsFile, Date = DateTime.Now, Files = new() { new ModFile { Root = "mods", Rel = modsFile, Size = fi.Exists ? fi.Length : 0 } } });
            Save();
        }
    }

    // ---------------------------------------------------------------- on / off / delete

    private static string? Busy() => Dir == null ? "FiveM nicht gefunden" : FiveM.IsRunning ? "Bitte FiveM zuerst schließen" : null;

    private static string Friendly(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Kein Zugriff auf die Datei. Läuft GTA V oder der Rockstar Launcher noch?",
        IOException io when io.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) => "Datei wird gerade benutzt. GTA V und den Rockstar Launcher schließen.",
        _ => ex.Message,
    };

    /// <summary>A file that was at the target before: moved to the backup folder once, never overwritten.</summary>
    private static void SaveOriginal(ModFile f, string target)
    {
        var b = BackupPath(f);
        if (File.Exists(b)) { File.Delete(target); return; }   // the backup is the older original
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.Move(target, b);
    }

    private static void RestoreOriginal(ModFile f, string target)
    {
        var b = BackupPath(f);
        if (!File.Exists(b)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(b, target, true);
    }

    private static void TurnOff(InstalledMod m)
    {
        foreach (var f in m.Files)
        {
            if (Target(f) is not { } t) continue;
            if (File.Exists(t))
            {
                if (f.Root != "mods" && new FileInfo(t).Length != f.Size)
                {
                    // the game's file check put a fresh original there: leave it, the old backup is outdated
                    try { File.Delete(BackupPath(f)); } catch { }
                    continue;
                }
                var o = OffPath(m, f);
                Directory.CreateDirectory(Path.GetDirectoryName(o)!);
                File.Move(t, o, true);
            }
            RestoreOriginal(f, t);
        }
        m.On = false;
    }

    /// <summary>Switches a mod on. Another mod that replaces the same file is switched off first (its title lands in <paramref name="off"/>).</summary>
    private static void TurnOn(InstalledMod m, List<string> off)
    {
        foreach (var f in m.Files)
        {
            if (Target(f) == null) throw new IOException(f.Root == "gta" ? "GTA-V-Ordner nicht gefunden (Pfad in CitizenFX.ini fehlt)" : "FiveM nicht gefunden");
            if (!File.Exists(OffPath(m, f))) throw new IOException("Datei fehlt: " + Path.GetFileName(f.Rel));
        }
        var done = new List<ModFile>();
        try
        {
            foreach (var f in m.Files)
            {
                foreach (var other in M.Mods.Where(x => x != m && x.On && x.Files.Any(g => Same(g, f))).ToList()) { TurnOff(other); off.Add(other.Title); }
                var t = Target(f)!;
                Directory.CreateDirectory(Path.GetDirectoryName(t)!);
                if (File.Exists(t)) SaveOriginal(f, t);
                File.Move(OffPath(m, f), t);
                done.Add(f);
            }
        }
        catch
        {
            // put back what was moved already, the mod stays off
            foreach (var f in done)
                try { var t = Target(f)!; var o = OffPath(m, f); File.Move(t, o, true); RestoreOriginal(f, t); } catch { }
            throw;
        }
        m.On = true;
    }

    public static (string? error, List<string> off) Toggle(string key, bool on)
    {
        var off = new List<string>();
        lock (Gate)
        {
            if (Busy() is { } busy) return (busy, off);
            Sync();
            if (Find(key) is not { } m) return ("Mod nicht gefunden", off);
            if (m.On == on) return (null, off);
            try { if (on) TurnOn(m, off); else TurnOff(m); return (null, off); }
            catch (Exception ex) { return (Friendly(ex), off); }
            finally { Save(); }
        }
    }

    private static void TryDeleteDir(string d) { try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }

    /// <summary>Removes a mod's files; a file that was there before it comes back.</summary>
    private static void Remove(InstalledMod m)
    {
        if (m.On) TurnOff(m);
        TryDeleteDir(Path.Combine(OffDir!, m.Id));
        M.Mods.Remove(m);
    }

    /// <summary>Deletes a mod by its id (or, for older callers, its file name in the mods folder).</summary>
    public static string? Delete(string key)
    {
        lock (Gate)
        {
            if (Busy() is { } busy) return busy;
            Sync();
            if (Find(key) is not { } m) return null;
            try { Remove(m); return null; }
            catch (Exception ex) { return Friendly(ex); }
            finally { Save(); }
        }
    }

    /// <summary>Deletes everything installed from the mod library entry <paramref name="catalogId"/>.</summary>
    public static string? RemoveCatalog(string catalogId)
    {
        lock (Gate)
        {
            if (Busy() is { } busy) return busy;
            Sync();
            var list = M.Mods.Where(m => m.CatalogId == catalogId).ToList();
            if (list.Count == 0) return "Nicht installiert";
            try { foreach (var m in list) Remove(m); return null; }
            catch (Exception ex) { return Friendly(ex); }
            finally { Save(); }
        }
    }

    /// <summary>
    /// Fixes a file FiveM ignores in its mods folder: an NG sound archive moves to the citizen (or GTA) folder,
    /// a raw archive is wrapped into a package, ".RPF" gets a small ".rpf".
    /// </summary>
    public static InstallResult Repair(string key, bool soundToGta)
    {
        var r = new InstallResult();
        var temps = new List<string>();
        try
        {
            lock (Gate)
            {
                if (Busy() is { } busy) { r.Error = busy; return r; }
                Sync();
                if (Find(key) is not { } m || m.Files.FirstOrDefault(f => f.Root == "mods") is not { } f) { r.Error = "Mod nicht gefunden"; return r; }
                var path = m.On ? Target(f)! : OffPath(m, f);
                if (!File.Exists(path)) { r.Error = "Datei fehlt"; return r; }
                var info = ModClassifier.Classify(path, f.Rel);
                if (info.Kind == RpfKind.Package && info.Problem == null)
                {
                    var rel = Path.GetFileNameWithoutExtension(f.Rel) + ".rpf";
                    var to = Path.Combine(Path.GetDirectoryName(path)!, rel);
                    File.Move(path, to + ".tmp"); File.Move(to + ".tmp", to);   // two steps: only the case of the name changes
                    f.Rel = rel; Save(); r.Mods.Add(m); r.Count = 1;
                    return r;
                }
                var plan = PlanFile(path, new InstallOpts { AutoVariant = true }, temps);
                plan.Source = m.Source;
                if (plan.Error != null) { r.Error = plan.Error; return r; }
                var o = new InstallOpts { Cat = m.Cat, Title = m.Title, CatalogId = m.CatalogId, SoundToGta = soundToGta };
                bool wasOn = m.On;
                var fixedMod = Apply(plan, o, r);      // also drops the old entry (same source file)
                if (M.Mods.Contains(m)) Remove(m);
                if (!wasOn) TurnOff(fixedMod);
                r.Mods.Add(fixedMod); r.Count = fixedMod.Files.Count; r.Notes.AddRange(plan.Notes);
                Save();
                return r;
            }
        }
        catch (Exception ex) { r.Error = Friendly(ex); return r; }
        finally { foreach (var t in temps) TryDeleteDir(t); }
    }

    // ---------------------------------------------------------------- install

    private enum CK { Package, Oiv, Sound, Citizen, StreamRpf, Dlc, Loose, Data }

    /// <summary>One installable thing found in what the user picked.</summary>
    private sealed class Cand
    {
        public CK Kind; public string Path = "", Rel = "", Key = "", Name = "";
        public string? Title, Cat, Note, Reject;
    }

    private sealed class Plan
    {
        public string Source = "";
        public string? Error, Cat;
        public List<string> Notes = new(), Variants = new(), AllPaths = new();
        public List<Cand> Items = new();
    }

    /// <summary>Installs .rpf / .zip / .rar / .7z / .oiv files. Every file becomes one entry in "Meine Mods".</summary>
    public static InstallResult Install(IEnumerable<string> files, InstallOpts o)
    {
        var r = new InstallResult();
        if (Busy() is { } busy) { r.Error = busy; return r; }
        var temps = new List<string>();
        try
        {
            // reading and unpacking can take a while: outside the lock
            var plans = files.Select(f => PlanFile(f, o, temps)).ToList();
            var choose = plans.SelectMany(p => p.Variants).Distinct().ToList();
            if (choose.Count > 0) { r.Choose = choose; return r; }
            var errors = new List<string>();
            lock (Gate)
            {
                Sync();
                foreach (var p in plans)
                {
                    if (p.Error != null) { errors.Add(plans.Count > 1 ? $"{p.Source}: {p.Error}" : p.Error); continue; }
                    try
                    {
                        var m = Apply(p, o, r);
                        r.Mods.Add(m); r.Count += m.Files.Count; r.Notes.AddRange(p.Notes);
                    }
                    catch (Exception ex) { errors.Add((plans.Count > 1 ? p.Source + ": " : "") + Friendly(ex)); }
                }
                Save();
            }
            if (r.Mods.Count == 0) r.Error = errors.FirstOrDefault() ?? "Keine installierbare Mod-Datei gefunden";
            else r.Notes.AddRange(errors);
        }
        catch (Exception ex) { r.Error = Friendly(ex); }
        finally { foreach (var t in temps) TryDeleteDir(t); }
        return r;
    }

    private static Plan PlanFile(string file, InstallOpts o, List<string> temps)
    {
        var name = Path.GetFileName(file);
        var p = new Plan { Source = name };
        if (!File.Exists(file)) { p.Error = "Datei nicht gefunden"; return p; }
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var cands = new List<Cand>();
        int sounds = 0;
        if (ext == ".rpf") { cands.Add(FromRpf(file, name, name)); p.AllPaths.Add(name); }
        else if (ext is ".zip" or ".oiv" or ".rar" or ".7z")
        {
            var temp = Path.Combine(Path.GetTempPath(), "OrbitTweak-mod-" + NewId());
            temps.Add(temp);
            Directory.CreateDirectory(temp);
            if (Extract(file, ext, temp) is { } err) { p.Error = err; return p; }
            sounds = Walk(temp, cands, p);
        }
        else { p.Error = "Dateityp wird nicht unterstützt (.rpf, .zip, .rar, .7z oder .oiv)"; return p; }

        // one candidate per target: the FiveM version wins over SP versions, rejected copies only count when nothing else is there
        var keep = new List<Cand>(); var rejects = new List<Cand>(); var ambiguous = new List<(List<Cand> g, List<string> labels)>();
        foreach (var g in cands.GroupBy(c => c.Key))
        {
            var ok = g.Where(c => c.Reject == null).OrderBy(c => c.Rel, StringComparer.OrdinalIgnoreCase).ToList();
            if (ok.Count == 0) { rejects.Add(g.First()); continue; }
            if (ok.Count == 1) { keep.Add(ok[0]); continue; }
            var fivem = ok.Where(c => c.Rel.Contains("fivem", StringComparison.OrdinalIgnoreCase)).ToList();
            if (fivem.Count == 1) { keep.Add(fivem[0]); continue; }
            var list = fivem.Count > 1 ? fivem : ok;
            ambiguous.Add((list, Labels(list)));
        }
        if (ambiguous.Count > 0)
        {
            var all = ambiguous.SelectMany(a => a.labels).Distinct().ToList();
            if (o.Variant == null && !o.AutoVariant) { p.Variants = all; return p; }
            foreach (var (g, labels) in ambiguous)
            {
                int i = o.Variant == null ? 0 : labels.IndexOf(o.Variant);
                if (i < 0) i = g.FindIndex(c => c.Rel.StartsWith(o.Variant + "/", StringComparison.OrdinalIgnoreCase));
                keep.Add(g[Math.Max(0, i)]);
            }
            // files that only exist in the other versions' folders stay out as well
            if (o.Variant != null)
                keep.RemoveAll(c => all.Any(l => l != o.Variant && l.Length > 0 && c.Rel.StartsWith(l + "/", StringComparison.OrdinalIgnoreCase)));
        }
        if (keep.Count == 0)
        {
            p.Error = rejects.FirstOrDefault()?.Reject ?? (sounds > 0 ? ModText.LooseSound : "Keine installierbare Mod-Datei gefunden");
            return p;
        }
        foreach (var rj in rejects) p.Notes.Add($"{Path.GetFileName(rj.Rel)} übersprungen: {rj.Reject}");
        if (sounds > 0 && !keep.Any(c => c.Kind == CK.Sound)) p.Notes.Add(ModText.LooseSound);
        p.Items = keep;
        p.Cat = keep.Any(c => c.Kind == CK.Sound) ? "sound"
            : keep.Select(c => c.Cat).FirstOrDefault(c => c != null) ?? ModClassifier.GuessByTargets(Array.Empty<string>(), p.AllPaths);
        return p;
    }

    /// <summary>For each candidate: its folder up to the first folder that differs between the versions ("FiveM", "Singleplayer").</summary>
    private static List<string> Labels(List<Cand> g)
    {
        var dirs = g.Select(c => c.Rel.Split('/')[..^1]).ToList();
        int i = 0;
        while (dirs.All(d => d.Length > i) && dirs.All(d => d[i].Equals(dirs[0][i], StringComparison.OrdinalIgnoreCase))) i++;
        var labels = dirs.Select(d => string.Join('/', d.Take(i + 1))).ToList();
        return labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() == labels.Count ? labels : g.Select(c => c.Rel).ToList();
    }

    private static Cand FromRpf(string path, string name, string rel)
    {
        RpfInfo info;
        try { info = ModClassifier.Classify(path, name); }
        catch (Exception ex) { return new Cand { Kind = CK.Package, Path = path, Rel = rel, Key = "bad:" + rel, Reject = "Kein gültiges RPF-Archiv (" + ex.Message + ")" }; }
        var c = new Cand { Path = path, Rel = rel, Name = info.Canon, Title = info.Title, Cat = info.Cat, Note = info.Note };
        var canon = info.Canon.ToLowerInvariant();
        switch (info.Kind)
        {
            case RpfKind.Package: c.Kind = CK.Package; c.Key = "pkg:" + canon; c.Reject = info.Problem is { } pr ? "Wirkt nicht: " + pr : null; break;
            case RpfKind.DlcPack: c.Kind = CK.Dlc; c.Key = "dlc:" + canon; break;
            case RpfKind.AudioArchive: c.Kind = CK.Sound; c.Key = "sound:" + canon; c.Reject = info.Encrypted ? null : ModText.OpenSound; break;
            case RpfKind.StreamArchive: c.Kind = CK.StreamRpf; c.Key = "stream:" + canon; break;
            default: c.Kind = CK.Package; c.Key = "bad:" + canon; c.Reject = info.Problem ?? ModText.Unknown; break;
        }
        return c;
    }

    /// <summary>Unpacks an archive. Nested archives (a zip with "FiveM.zip" and "SP.zip" inside) are unpacked into a folder of their name.</summary>
    private static string? Extract(string file, string ext, string dir, int depth = 0)
    {
        try
        {
            if (ext is ".zip" or ".oiv") ZipFile.ExtractToDirectory(file, dir, true);
            else
            {
                var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
                if (!File.Exists(tar)) return "Windows kann .rar/.7z hier nicht entpacken. Bitte das Archiv als .zip speichern.";
                Proc.Run(tar, $"-xf \"{file}\" -C \"{dir}\"", 300_000);
                if (!Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any())
                    return "Archiv konnte nicht entpackt werden. Unter älterem Windows bitte als .zip speichern.";
            }
        }
        catch (Exception ex) { return "Archiv konnte nicht entpackt werden: " + ex.Message; }
        if (depth < 2)
            foreach (var inner in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList())
            {
                var ie = Path.GetExtension(inner).ToLowerInvariant();
                if (ie is not (".zip" or ".rar" or ".7z" or ".oiv")) continue;
                var to = Path.Combine(Path.GetDirectoryName(inner)!, Path.GetFileNameWithoutExtension(inner));
                while (Directory.Exists(to) || File.Exists(to)) to += "_";
                Directory.CreateDirectory(to);
                if (Extract(inner, ie, to, depth + 1) == null) try { File.Delete(inner); } catch { }
            }
        return null;
    }

    /// <summary>Looks at every unpacked file. Returns how many loose sound files (.awc) were seen.</summary>
    private static int Walk(string root, List<Cand> cands, Plan p)
    {
        int sounds = 0;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (path: f, rel: Path.GetRelativePath(root, f).Replace('\\', '/'))).ToList();
        p.AllPaths.AddRange(files.Select(f => f.rel));
        // OpenIV packages: a folder with assembly.xml and content/
        var oivs = files.Where(f => Path.GetFileName(f.rel).Equals("assembly.xml", StringComparison.OrdinalIgnoreCase)
                                    && Directory.Exists(Path.Combine(Path.GetDirectoryName(f.path)!, "content")))
                        .Select(f => Path.GetDirectoryName(f.path)!).ToList();
        foreach (var d in oivs)
            cands.Add(new Cand { Kind = CK.Oiv, Path = d, Rel = Path.GetRelativePath(root, d).Replace('\\', '/') + "/assembly.xml", Key = "oiv", Name = Path.GetFileName(d) });
        foreach (var (path, rel) in files)
        {
            if (oivs.Any(d => path.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            var lower = rel.ToLowerInvariant();
            var segs = rel.Split('/');
            var file = segs[^1];
            var ext = Path.GetExtension(file).ToLowerInvariant();
            // a "citizen" folder keeps its layout (FiveM.app\citizen\…)
            int ci = Array.FindIndex(segs, s => s.Equals("citizen", StringComparison.OrdinalIgnoreCase));
            if (ci >= 0 && ci < segs.Length - 1)
            {
                var sub = string.Join('/', segs[(ci + 1)..]);
                var c = new Cand { Kind = CK.Citizen, Path = path, Rel = rel, Key = "citizen:" + sub.ToLowerInvariant(), Name = sub };
                if (ext == ".rpf")
                {
                    uint enc = 0;
                    try { using var s = File.OpenRead(path); enc = RpfToc.ReadEncryption(s, 0); } catch { }
                    if (enc is not (RpfToc.NG or RpfToc.AES))
                        c.Reject = ModClassifier.SoundPack(Path.GetFileNameWithoutExtension(file)) != null ? ModText.OpenSound
                            : "Unverschlüsseltes Archiv im citizen-Ordner: FiveM würde mit „Detected modified game files“ abbrechen.";
                }
                cands.Add(c);
                continue;
            }
            if (ext == ".rpf") { cands.Add(FromRpf(path, file, rel)); continue; }
            int di = lower.IndexOf("common/data/", StringComparison.Ordinal);
            if (di < 0) di = lower.IndexOf("x64/data/", StringComparison.Ordinal);
            if (di >= 0 && (di == 0 || lower[di - 1] == '/'))
            {
                var gp = lower[di..];
                // FiveM never loads these three from a package
                if (gp is "common/data/gameconfig.xml" or "common/data/ai/scenarios.meta" or "common/data/ai/conditionalanims.meta") continue;
                cands.Add(new Cand { Kind = CK.Data, Path = path, Rel = rel, Key = "data:" + gp, Name = gp, Cat = ModClassifier.GuessByTargets(new[] { gp }, Array.Empty<string>()) });
                continue;
            }
            if (ModClassifier.StreamExt.Contains(ext) && (ext == ".gfx" || StreamRpfWriter.IsRsc7(path)))
            {
                var n = file.ToLowerInvariant();
                cands.Add(new Cand { Kind = CK.Loose, Path = path, Rel = rel, Key = "loose:" + n, Name = n });
                continue;
            }
            if (ext is ".awc" or ".oac" or ".rel") sounds++;
        }
        // loose game files get a category from all of them together
        var looseCat = ModClassifier.GuessByTargets(Array.Empty<string>(), cands.Where(c => c.Kind is CK.Loose).Select(c => c.Name));
        foreach (var c in cands.Where(c => c.Kind is CK.Loose)) c.Cat = looseCat;
        return sounds;
    }

    private static string Slug(string s, int max = 32)
    {
        var d = s.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d)
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (ch < 128 && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        var r = sb.ToString().Trim('-');
        if (r.Length > max) r = r[..max].Trim('-');
        return r.Length > 0 ? r : "mod";
    }

    /// <summary>File name for a package in the mods folder: ASCII and a small ".rpf", FiveM's check is case-sensitive.</summary>
    private static string PkgName(string title) => $"ot-{Slug(title)}-{NewId()[..4]}.rpf";

    private static void CopyInto(string src, string target)
    {
        var tmp = target + ".tmp";
        try { File.Copy(src, tmp, true); File.Move(tmp, target, true); }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }

    /// <summary>Puts a file into the citizen or GTA folder: an enabled mod with the same file is switched off, a file of the game is backed up.</summary>
    private static void PlaceShared(InstalledMod mod, ModFile f, string src, InstallResult r)
    {
        if (mod.Files.Any(g => Same(g, f))) return;
        var t = Target(f) ?? throw new IOException(f.Root == "gta" ? "GTA-V-Ordner nicht gefunden" : "FiveM nicht gefunden");
        foreach (var other in M.Mods.Where(x => x.On && x.Files.Any(g => Same(g, f))).ToList()) { TurnOff(other); r.TurnedOff.Add(other.Title); }
        Directory.CreateDirectory(Path.GetDirectoryName(t)!);
        if (File.Exists(t)) SaveOriginal(f, t);
        mod.Files.Add(f);       // first, so a failed copy is cleaned up with the rest
        CopyInto(src, t);
        f.Size = new FileInfo(t).Length;
    }

    private static string Pretty(string source)
    {
        var s = Path.GetFileNameWithoutExtension(CleanName(source));
        if (s.Any(char.IsLower)) s = Regex.Replace(s, "_+", " ").Trim();    // "Gun_Sounds_v2" → "Gun Sounds v2", game names like WEAPONS_PLAYER stay
        return s.Length > 0 ? s : source;
    }

    private static InstalledMod Apply(Plan p, InstallOpts o, InstallResult r)
    {
        var dir = Dir!;
        Directory.CreateDirectory(dir);
        var title = o.Title is { Length: > 0 } t ? t : p.Items.Count == 1 && p.Items[0].Title is { Length: > 0 } pt ? pt : Pretty(p.Source);
        var cat = p.Cat ?? (o.Cat is { Length: > 0 } and not "other" ? o.Cat : ModClassifier.GuessByName(p.Source));
        var mod = new InstalledMod { Id = NewId(), Title = title, Cat = cat, Source = p.Source, CatalogId = o.CatalogId, Date = DateTime.Now };
        var gta = o.SoundToGta && GtaDir != null;
        var wrap = new PackageBuilder.Items();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Unique(string n) { var b = n; for (int i = 2; !names.Add(n); i++) n = Path.GetFileNameWithoutExtension(b) + "-" + i + Path.GetExtension(b); return n; }
        var sounds = new List<string>();
        try
        {
            foreach (var c in p.Items)
            {
                switch (c.Kind)
                {
                    case CK.Package:
                    {
                        var rel = PkgName(c.Title ?? Path.GetFileNameWithoutExtension(c.Name));
                        mod.Files.Add(new ModFile { Root = "mods", Rel = rel });
                        CopyInto(c.Path, Path.Combine(dir, rel));
                        mod.Files[^1].Size = new FileInfo(Path.Combine(dir, rel)).Length;
                        if (c.Note != null) p.Notes.Add(c.Note);
                        break;
                    }
                    case CK.Oiv:
                    {
                        var rel = PkgName(title);
                        var target = Path.Combine(dir, rel);
                        mod.Files.Add(new ModFile { Root = "mods", Rel = rel });
                        PackageBuilder.RepackOiv(c.Path, target);
                        mod.Files[^1].Size = new FileInfo(target).Length;
                        var info = ModClassifier.Classify(target, rel);
                        if (info.Problem != null) throw new InvalidDataException("OpenIV-Paket: " + info.Problem);
                        if (info.Note != null) p.Notes.Add(info.Note);
                        mod.Cat = p.Cat ?? info.Cat ?? mod.Cat;
                        break;
                    }
                    case CK.Sound:
                    {
                        var f = new ModFile { Root = gta ? "gta" : "citizen", Rel = (gta ? "x64/audio/sfx/" : "platform/audio/sfx/") + c.Name };
                        PlaceShared(mod, f, c.Path, r);
                        sounds.Add(Path.GetFileNameWithoutExtension(c.Name));
                        break;
                    }
                    case CK.Citizen: PlaceShared(mod, new ModFile { Root = "citizen", Rel = c.Name }, c.Path, r); break;
                    case CK.StreamRpf: wrap.StreamArchives.Add((c.Path, Unique(Slug(Path.GetFileNameWithoutExtension(c.Name), 40) + ".rpf"))); break;
                    case CK.Dlc: wrap.Dlcs.Add((c.Path, Unique("dlc_" + Slug(Path.GetFileNameWithoutExtension(c.Name), 24).Replace('-', '_')))); break;
                    case CK.Loose: wrap.Loose.Add((c.Path, c.Name)); break;
                    case CK.Data: wrap.Data.Add((c.Path, c.Name)); break;
                }
            }
            if (wrap.Any)
            {
                var rel = PkgName(title);
                mod.Files.Add(new ModFile { Root = "mods", Rel = rel });
                PackageBuilder.Build(wrap, Path.Combine(dir, rel), title, Guid.NewGuid());
                mod.Files[^1].Size = new FileInfo(Path.Combine(dir, rel)).Length;
                p.Notes.Add(ModText.Wrapped);
            }
        }
        catch
        {
            // nothing half-installed stays behind: files that did not arrive are dropped (a backed-up original goes back), the rest is removed
            foreach (var f in mod.Files.ToList())
            {
                var tp = Target(f);
                if (tp != null && File.Exists(tp)) continue;
                try { if (tp != null) RestoreOriginal(f, tp); } catch { }
                mod.Files.Remove(f);
            }
            try { TurnOff(mod); } catch { }
            TryDeleteDir(Path.Combine(OffDir!, mod.Id));
            throw;
        }
        if (sounds.Count > 0) p.Notes.Insert(0, ModText.Sound(sounds, gta));
        if (o.SoundToGta && !gta && sounds.Count > 0) p.Notes.Add("GTA-V-Ordner nicht gefunden (Pfad in CitizenFX.ini), die Sounds liegen deshalb im citizen-Ordner.");
        M.Mods.Add(mod);
        // the same pack installed again, an update from the library, or the raw copy an older version put into the mods folder: the old entry goes
        long size = mod.Files.Sum(f => f.Size);
        foreach (var old in M.Mods.Where(m => m != mod && ((o.CatalogId != null && m.CatalogId == o.CatalogId)
                     || (m.Source.Equals(mod.Source, StringComparison.OrdinalIgnoreCase)
                         && (m.Files.Sum(f => f.Size) == size || (m.Files.Count == 1 && m.Files[0].Root == "mods" && m.Files[0].Rel.Equals(m.Source, StringComparison.OrdinalIgnoreCase)))))).ToList())
        {
            try { Remove(old); } catch { }
            r.TurnedOff.Remove(old.Title);
        }
        return mod;
    }
}
