using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using L = OrbitTweak.Core.CfgBackupLogic;

namespace OrbitTweak.Core;

// =====================================================================
//  Einstellungs-Backup (2.31): FiveM's user settings (key bindings, graphics, CitizenFX.ini) as a zip under
//  %AppData%\OrbitTweak\backups, written back on request. Only the files in CfgBackupLogic.Files are ever read or
//  written; FiveM itself, its caches and the game files are never touched. The rules live in CfgBackupLogic.cs.
// =====================================================================
internal static class CfgBackup
{
    /// <summary>One at a time: a backup, a restore, a delete and the automatic backup never run side by side.</summary>
    private static readonly object Gate = new();

    public static string Dir => Path.Combine(AppDirs.Roaming, "backups");
    private static string StateFile => Path.Combine(AppDirs.Roaming, "cfgbackup.json");
    private static string AppName => "OrbitTweak " + Updater.Current.ToString(3);

    /// <summary>Where the placeholders point on this PC. FiveM.app only when it is there.</summary>
    public static CfgRoots Roots()
    {
        static string? Folder(Environment.SpecialFolder f)
        {
            try { var p = Environment.GetFolderPath(f); return p.Length > 0 ? p : null; } catch { return null; }
        }
        string? app = null;
        try { if (FiveM.AppDir is { } a && Directory.Exists(a)) app = a; } catch { }
        return new(Folder(Environment.SpecialFolder.ApplicationData), Folder(Environment.SpecialFolder.LocalApplicationData), app);
    }

    // ---------------------------------------------------------------- "Automatisch sichern"
    internal sealed class AutoState
    {
        public bool Auto { get; set; } = true;
        public DateTimeOffset? LastAuto { get; set; }
    }

    public static AutoState LoadState()
    {
        try { if (File.Exists(StateFile)) return JsonSerializer.Deserialize<AutoState>(File.ReadAllText(StateFile)) ?? new(); }
        catch { }
        return new();
    }

    public static void SaveState(AutoState s)
    {
        Directory.CreateDirectory(AppDirs.Roaming);
        var tmp = StateFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s));
        File.Move(tmp, StateFile, true);
    }

    public static bool SetAuto(bool on)
    {
        lock (Gate)
        {
            var st = LoadState();
            st.Auto = on;
            SaveState(st);
            return st.Auto;
        }
    }

    // ---------------------------------------------------------------- what the page shows
    public sealed record SourceInfo(string Key, string Name, string What, string Path, bool Exists, long Size, DateTimeOffset? Modified, string? Note);
    public sealed record ItemFile(string Key, string Name, string What, long Size);
    public sealed record Item(string Name, DateTimeOffset Created, string? Kind, string KindLabel, long Size, List<ItemFile> Files, bool Ok, string? Error);
    public sealed record Skipped(string Name, string Why);
    /// <summary>Safety: the backup that holds the state from before the restore (the way back); SafetyNew: it was made
    /// just now ("Vor dem Wiederherstellen"), otherwise it is the newest backup, which already held that state.
    /// Undo: the keys "Rückgängig" writes back from it (only files this restore touched). Created: files that were not
    /// there before (the snapshot has no earlier state of them, so "Rückgängig" leaves them).</summary>
    public sealed record RestoreResult(List<string> Restored, List<string> Unchanged, List<Skipped> Skipped, List<Skipped> Failed, string? Safety, bool SafetyNew, List<string> Undo, List<string> Created);

    /// <summary>The settings files as they are now (placeholder paths only: the page never sees the user name).</summary>
    public static List<SourceInfo> Sources(CfgRoots roots)
    {
        var list = new List<SourceInfo>();
        foreach (var f in L.Files)
        {
            var ph = L.Placeholder(f);
            var full = L.Resolve(ph, roots);
            if (full == null) { list.Add(new(f.Key, f.Name, f.What, ph, false, 0, null, f.Root == L.FiveMApp ? "FiveM-Ordner nicht gefunden" : "Ordner nicht gefunden")); continue; }
            try
            {
                var fi = new FileInfo(full);
                if (!fi.Exists) { list.Add(new(f.Key, f.Name, f.What, ph, false, 0, null, null)); continue; }
                list.Add(new(f.Key, f.Name, f.What, ph, true, fi.Length, new DateTimeOffset(fi.LastWriteTime),
                    fi.Length > L.MaxFile ? "Zu groß für eine Einstellungsdatei, wird ausgelassen" : null));
            }
            catch { list.Add(new(f.Key, f.Name, f.What, ph, false, 0, null, "Nicht lesbar")); }
        }
        return list;
    }

    // ---------------------------------------------------------------- reading the current files
    private sealed record Current(CfgFile File, byte[] Data, DateTimeOffset Modified);

    /// <summary>Reads a file FiveM may have open (shared, like Notepad would), never more than <paramref name="max"/> bytes.</summary>
    internal static byte[] ReadShared(string path, long max)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > max) throw new InvalidOperationException("Zu groß für eine Einstellungsdatei");
        using var ms = new MemoryStream((int)Math.Max(0, fs.Length));
        var buf = new byte[81920]; int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) throw new InvalidOperationException("Zu groß für eine Einstellungsdatei");
        }
        return ms.ToArray();
    }

    /// <summary>The settings files that are there now. Unread: files that are there but could not be read (with why).</summary>
    private static (List<Current> cur, List<(CfgFile File, string Why)> unread) ReadCurrent(CfgRoots roots)
    {
        var cur = new List<Current>(); var unread = new List<(CfgFile, string)>();
        foreach (var f in L.Files)
        {
            var full = L.Resolve(L.Placeholder(f), roots);
            if (full == null) continue;
            try
            {
                var fi = new FileInfo(full);
                if (!fi.Exists) continue;
                if (fi.Length > L.MaxFile) { unread.Add((f, "Zu groß für eine Einstellungsdatei")); continue; }
                cur.Add(new(f, ReadShared(full, L.MaxFile), new DateTimeOffset(fi.LastWriteTime)));
            }
            catch (Exception ex) { unread.Add((f, Reason(ex))); }
        }
        return (cur, unread);
    }

    // ---------------------------------------------------------------- the zip
    internal static void WriteZip(string path, CfgManifest m, IEnumerable<(CfgFile file, byte[] data)> files)
    {
        var tmp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var me = z.CreateEntry(L.ManifestName, CompressionLevel.Optimal);
                using (var s = me.Open()) s.Write(Encoding.UTF8.GetBytes(L.Serialize(m)));
                foreach (var (f, data) in files)
                {
                    var e = z.CreateEntry(L.EntryName(f), CompressionLevel.Optimal);
                    using var s = e.Open();
                    s.Write(data);
                }
            }
            File.Move(tmp, path, false);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    internal static ZipArchive OpenZip(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new InvalidOperationException("Diese Sicherung gibt es nicht mehr.");
        if (fi.Length > L.MaxZip) throw new InvalidOperationException("Diese Datei ist zu groß für eine Sicherung von OrbitTweak.");
        try { return ZipFile.OpenRead(path); }
        catch (InvalidDataException) { throw new InvalidOperationException("Die Sicherung ist beschädigt (kein gültiges ZIP)."); }
    }

    private static byte[] ReadEntry(ZipArchiveEntry e, long max)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        var buf = new byte[81920]; int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) break;   // more than it claims: a broken or made-up entry, never unpacked further
        }
        return ms.ToArray();
    }

    /// <summary>The manifest, or null with a German reason.</summary>
    internal static CfgManifest? ReadManifest(ZipArchive z, out string? error)
    {
        var hits = z.Entries.Where(x => x.FullName == L.ManifestName).ToList();
        if (hits.Count != 1) { error = hits.Count == 0 ? "In der Sicherung fehlt die Inhaltsliste." : "Die Inhaltsliste steht zweimal in der Sicherung."; return null; }
        byte[] data;
        try { data = ReadEntry(hits[0], L.MaxManifest); }
        catch (InvalidDataException) { error = "Die Inhaltsliste der Sicherung ist beschädigt."; return null; }
        if (data.Length > L.MaxManifest) { error = "Die Inhaltsliste der Sicherung ist zu groß."; return null; }
        return L.Parse(Encoding.UTF8.GetString(data), out error);
    }

    /// <summary>A file of the backup, only when its size and SHA-256 are the ones in the manifest. Entries are looked up by
    /// the exact name the manifest gives (which <see cref="L.Validate"/> fixed to files/&lt;key&gt;/&lt;name&gt;); a name
    /// in the zip never becomes a path on disk.</summary>
    internal static byte[] ReadVerified(ZipArchive z, CfgEntry entry, string label)
    {
        var hits = z.Entries.Where(x => x.FullName == entry.Entry).ToList();
        if (hits.Count != 1) throw new InvalidOperationException($"Die Sicherung ist beschädigt: {label} {(hits.Count == 0 ? "fehlt" : "steht zweimal darin")}. Es wurde nichts geändert.");
        byte[] data;
        try { data = ReadEntry(hits[0], entry.Size); }
        catch (InvalidDataException) { throw new InvalidOperationException($"Die Sicherung ist beschädigt: {label} lässt sich nicht entpacken. Es wurde nichts geändert."); }
        if (data.Length != entry.Size || L.Sha256(data) != entry.Sha256)
            throw new InvalidOperationException($"Die Sicherung ist beschädigt: {label} passt nicht zur Prüfsumme. Es wurde nichts geändert.");
        return data;
    }

    // ---------------------------------------------------------------- list, create, delete
    public static List<Item> List()
    {
        lock (Gate) return ListLocked(Dir).Select(x => x.item).ToList();
    }

    private static List<(Item item, CfgManifest? m)> ListLocked(string dir)
    {
        var list = new List<(Item, CfgManifest?)>();
        if (!Directory.Exists(dir)) return list;
        foreach (var file in Directory.EnumerateFiles(dir, L.NamePrefix + "*.zip"))
        {
            var name = Path.GetFileName(file);
            if (!L.ValidName(name)) continue;
            long size = 0; CfgManifest? m = null; string? err = null;
            try
            {
                size = new FileInfo(file).Length;
                using var z = OpenZip(file);
                m = ReadManifest(z, out err);
            }
            catch (InvalidOperationException ex) { err = ex.Message; }
            catch (Exception ex) { err = Reason(ex); }
            list.Add((ToItem(name, size, m, err), m));
        }
        return list.OrderByDescending(x => x.Item1.Created).ThenByDescending(x => L.SeqOf(x.Item1.Name)).ToList();
    }

    private static Item ToItem(string name, long size, CfgManifest? m, string? err)
    {
        DateTimeOffset created = DateTimeOffset.MinValue;
        if (m != null) created = m.Created;
        else if (L.TimeOf(name) is DateTime t) try { created = new DateTimeOffset(t); } catch { }
        var files = m?.Files.Select(e => { var f = L.ByPath(e.Path)!; return new ItemFile(f.Key, f.Name, f.What, e.Size); }).ToList() ?? new();
        return new(name, created, m?.Kind, L.KindLabel(m?.Kind), size, files, m != null, m == null ? err ?? "Die Sicherung ist beschädigt." : null);
    }

    /// <summary>A backup of the settings files as they are now. Files that are there but cannot be read are named in
    /// the answer; none at all is an error.</summary>
    public static (Item item, List<Skipped> skipped) Create(string kind)
    {
        lock (Gate) return CreateLocked(kind, Roots(), Dir, null, null);
    }

    private static (Item item, List<Skipped> skipped) CreateLocked(string kind, CfgRoots roots, string dir, string? protect, List<Current>? read)
    {
        List<Current> cur; List<Skipped> skipped;
        if (read != null) { cur = read; skipped = new(); }
        else { var (c, unread) = ReadCurrent(roots); cur = c; skipped = unread.Select(u => new Skipped(u.File.Name, u.Why)).ToList(); }
        if (cur.Count == 0)
            throw new InvalidOperationException(skipped.Count > 0 ? $"{skipped[0].Name}: {skipped[0].Why}"
                : "Keine FiveM-Einstellungen gefunden. Starte FiveM einmal, dann legt FiveM sie an.");
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.Now;
        var name = L.NameFor(now.LocalDateTime, Directory.EnumerateFiles(dir, L.NamePrefix + "*").Select(Path.GetFileName).OfType<string>());
        var m = L.NewManifest(kind, now, AppName, cur.Select(c => (c.File, (long)c.Data.Length, L.Sha256(c.Data), (DateTimeOffset?)c.Modified)));
        var path = Path.Combine(dir, name);
        WriteZip(path, m, cur.Select(c => (c.File, c.Data)));
        Prune(dir, protect);
        return (ToItem(name, new FileInfo(path).Length, m, null), skipped);
    }

    /// <summary>Old automatic backups and snapshots go (CfgBackupLogic.Prune); manual ones always stay.</summary>
    private static void Prune(string dir, string? protect)
    {
        try
        {
            var stored = ListLocked(dir).Select(x => new CfgStored(x.item.Name, x.m?.Kind, x.item.Created));
            foreach (var n in L.Prune(stored))
                if (n != protect) try { File.Delete(Path.Combine(dir, n)); } catch { }
        }
        catch { }
    }

    public static void Delete(string name)
    {
        if (!L.ValidName(name)) throw new InvalidOperationException("Diese Sicherung gibt es nicht.");
        lock (Gate)
        {
            var path = Path.Combine(Dir, name);
            if (!File.Exists(path)) return;   // already gone: the list is read anew anyway
            var at = File.GetAttributes(path);
            if (at.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, at & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
    }

    /// <summary>FiveM was just started: a backup when one is due today and something changed since the newest backup.</summary>
    public static Item? AutoRun()
    {
        lock (Gate)
        {
            var st = LoadState();
            var now = DateTimeOffset.Now;
            if (!st.Auto || !L.AutoDue(st.LastAuto, now)) return null;
            var roots = Roots();
            var (cur, _) = ReadCurrent(roots);
            if (cur.Count == 0) return null;   // nothing there yet: the next start tries again
            var newest = ListLocked(Dir).Select(x => x.m).FirstOrDefault(m => m != null);
            st.LastAuto = now;
            Item? made = null;
            if (!L.SameContent(newest, cur.Select(c => (c.File.Key, L.Sha256(c.Data)))))
                made = CreateLocked(L.Auto, roots, Dir, null, cur).item;
            SaveState(st);
            return made;
        }
    }

    // ---------------------------------------------------------------- restore
    /// <summary>
    /// Writes the chosen files of a backup back to their places. Everything is read and checked first (manifest, known
    /// places, size and hash of each file); then the current state is saved as a snapshot; only then is anything
    /// written. A file that is already like this stays untouched. Read-only files stay read-only. A folder on the way
    /// that is a link (junction, symlink) is not written through.
    /// </summary>
    public static RestoreResult Restore(string name, IReadOnlyCollection<string>? keys)
    {
        if (!L.ValidName(name)) throw new InvalidOperationException("Diese Sicherung gibt es nicht.");
        lock (Gate)
        {
            if (FiveM.IsRunning) throw new InvalidOperationException("Bitte FiveM zuerst schließen. Sonst schreibt FiveM beim Beenden seine Einstellungen über die wiederhergestellten.");
            var roots = Roots();
            var skipped = new List<Skipped>(); var unchanged = new List<string>();
            var ready = new List<(CfgPlanItem p, byte[] data)>();
            using (var z = OpenZip(Path.Combine(Dir, name)))
            {
                var m = ReadManifest(z, out var err) ?? throw new InvalidOperationException(err + " Es wurde nichts geändert.");
                var plan = L.Plan(m, keys, roots, out err);
                if (err != null) throw new InvalidOperationException(err + " Es wurde nichts geändert.");
                foreach (var p in plan)
                {
                    if (p.Target == null) { skipped.Add(new(p.File.Name, p.Skip ?? "Ordner nicht gefunden")); continue; }
                    ready.Add((p, ReadVerified(z, p.Entry, p.File.Name)));
                }
            }

            var write = new List<(CfgPlanItem p, byte[] data)>();
            foreach (var (p, data) in ready)
            {
                var target = p.Target!;
                try
                {
                    if (Directory.Exists(target)) { skipped.Add(new(p.File.Name, "An der Stelle liegt ein Ordner")); continue; }
                    if (Linked(L.RootOf(roots, p.File.Root)!, target)) { skipped.Add(new(p.File.Name, "Die Datei oder ihr Ordner ist eine Verknüpfung, OrbitTweak schreibt nicht hindurch")); continue; }
                    if (File.Exists(target) && new FileInfo(target).Length == p.Entry.Size && L.Sha256(ReadShared(target, L.MaxFile)) == p.Entry.Sha256)
                    { unchanged.Add(p.File.Name); continue; }
                }
                catch (Exception ex) when (ex is not InvalidOperationException) { skipped.Add(new(p.File.Name, Reason(ex))); continue; }
                catch (InvalidOperationException) { /* too big to compare: it is written */ }
                write.Add((p, data));
            }
            if (write.Count == 0) return new(new(), unchanged, skipped, new(), null, false, new(), new());

            // the state as it is now, so this restore can be undone the same way. A file that is there but cannot be read
            // for it (locked, too big) stays as it is: its current state would be lost for good
            var (cur, unread) = ReadCurrent(roots);
            var keep = L.Uncovered(write.Select(w => w.p.File.Key), unread.Select(u => u.File.Key));
            foreach (var (f, why) in unread.Where(u => keep.Contains(u.File.Key)))
                skipped.Add(new(f.Name, "Der jetzige Stand ließ sich nicht vorher sichern: " + why));
            write.RemoveAll(w => keep.Contains(w.p.File.Key));
            if (write.Count == 0) return new(new(), unchanged, skipped, new(), null, false, new(), new());

            // when the newest backup already holds exactly that state, it is the way back (no second copy of the same files)
            string? safety = null; bool safetyNew = false;
            try
            {
                if (cur.Count > 0)
                {
                    var newest = ListLocked(Dir).FirstOrDefault(x => x.m != null);
                    if (newest.m != null && L.SameContent(newest.m, cur.Select(c => (c.File.Key, L.Sha256(c.Data))))) safety = newest.item.Name;
                    else { safety = CreateLocked(L.Safety, roots, Dir, name, cur).item.Name; safetyNew = true; }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Dein jetziger Stand ließ sich nicht sichern, deshalb wurde nichts wiederhergestellt. "
                    + (ex is InvalidOperationException ? ex.Message : Reason(ex)));
            }

            var restored = new List<string>(); var failed = new List<Skipped>(); var created = new List<string>();
            var saved = cur.Select(c => c.File.Key).ToList();
            foreach (var (p, data) in write)
            {
                try
                {
                    WriteFile(p.Target!, data, p.Entry.Sha256);
                    restored.Add(p.File.Name);
                    if (!saved.Contains(p.File.Key)) created.Add(p.File.Name);
                }
                catch (Exception ex) { failed.Add(new(p.File.Name, ex is InvalidOperationException ? ex.Message : Reason(ex))); }
            }
            var undo = safety == null ? new List<string>() : L.UndoKeys(write.Select(w => w.p.File.Key), saved);
            return new(restored, unchanged, skipped, failed, safety, safetyNew, undo, created);
        }
    }

    /// <summary>Is anything between the root and the file (the file included) a junction or symbolic link?</summary>
    private static bool Linked(string root, string target)
    {
        var r = root.TrimEnd('\\', '/');
        for (var p = target; p.Length > r.Length && p.StartsWith(r, StringComparison.OrdinalIgnoreCase); p = Path.GetDirectoryName(p) ?? "")
        {
            try
            {
                if ((File.Exists(p) || Directory.Exists(p)) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)) return true;
            }
            catch { return true; }
        }
        return false;
    }

    /// <summary>Writes next to the file and moves it into place (no half-written file if the PC goes off), keeps a
    /// read-only flag, and reads the result back.</summary>
    internal static void WriteFile(string target, byte[] data, string sha)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".orbit-tmp";
        if (File.Exists(tmp)) { File.SetAttributes(tmp, FileAttributes.Normal); File.Delete(tmp); }
        File.WriteAllBytes(tmp, data);
        bool ro = false;
        if (File.Exists(target))
        {
            var at = File.GetAttributes(target);
            if (at.HasFlag(FileAttributes.ReadOnly)) { ro = true; File.SetAttributes(target, at & ~FileAttributes.ReadOnly); }
        }
        try { File.Move(tmp, target, true); }
        catch
        {
            try { File.Delete(tmp); } catch { }
            if (ro) try { File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly); } catch { }
            throw;
        }
        if (ro) File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
        if (L.Sha256(ReadShared(target, L.MaxFile)) != sha) throw new InvalidOperationException("Die Datei ist nach dem Schreiben anders als in der Sicherung.");
    }

    /// <summary>Why a file could not be read or written, in German and without the user's paths.</summary>
    public static string Reason(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Kein Zugriff, die Datei ist geschützt oder gesperrt",
        DirectoryNotFoundException => "Ordner nicht gefunden",
        FileNotFoundException => "Datei nicht gefunden",
        PathTooLongException => "Der Pfad ist zu lang",
        IOException io when (io.HResult & 0xFFFF) is 32 or 33 => "Die Datei ist gerade von einem anderen Programm geöffnet",
        IOException io when (io.HResult & 0xFFFF) is 112 or 39 => "Auf dem Laufwerk ist kein Platz mehr",
        IOException => "Lese- oder Schreibfehler",
        InvalidOperationException => ex.Message,
        _ => "Unbekannter Fehler",
    };
}
