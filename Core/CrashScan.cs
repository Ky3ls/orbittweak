using System.Diagnostics;
using System.IO;
using System.Text;

namespace OrbitTweak.Core;

/// <summary>
/// Absturz-Analyse (2.31): reads what FiveM leaves behind after a crash – the dumps in FiveM.app\crashes (only their names
/// and times), the session logs in FiveM.app\logs and data\cache\crashometry – and hands it to <see cref="CrashScanLogic"/>.
/// Files are only read, shared with FiveM (it may still be writing its log); the game process is never touched. The
/// formats are described at the top of Core/CrashScanLogic.cs.
/// </summary>
internal static class CrashScan
{
    /// <summary>Newest session logs looked at (FiveM keeps every session's log until the Cleaner removes them).</summary>
    private const int MaxLogs = 40;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);
    /// <summary>The BEGIN line and the first build tags are at the start of a log …</summary>
    private const int HeadBytes = 64 * 1024;
    /// <summary>… and a crash ends its session, so its dialog is near the end: a huge log is read at both ends only.</summary>
    private const int TailBytes = 16 * 1024 * 1024;
    /// <summary>Reading stops after this, with what was found until then.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    private static readonly object Gate = new();
    /// <summary>Logs already read, by path, as long as length and time stay the same (old sessions never change).</summary>
    private static readonly Dictionary<string, (long len, DateTime mtime, CrashLog log)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public sealed record Result(bool Found, List<CrashReport> Crashes, int Logs, int Dumps);

    public static string? CrashDir => FiveM.AppDir is { } a ? Path.Combine(a, "crashes") : null;
    public static string? LogDir => FiveM.AppDir is { } a ? Path.Combine(a, "logs") : null;
    public static string? PluginDir => FiveM.AppDir is { } a ? Path.Combine(a, "plugins") : null;

    public static Result Read()
    {
        if (FiveM.AppDir is not { } app || !Directory.Exists(app)) return new(false, new(), 0, 0);

        var dumps = new List<(string name, DateTime utc)>();
        if (CrashDir is { } cd && Directory.Exists(cd))
            foreach (var f in new DirectoryInfo(cd).EnumerateFiles("*.dmp"))
                if (!f.Name.EndsWith("-full.dmp", StringComparison.OrdinalIgnoreCase)) dumps.Add((f.Name, f.LastWriteTimeUtc));
        dumps = dumps.OrderByDescending(d => d.utc).Take(30).ToList();
        // logs older than the oldest of the newest ten dumps cannot belong to one of them
        DateTime? oldestDump = dumps.Count > 0 ? dumps.Take(CrashScanLogic.MaxCrashes).Min(d => d.utc) : null;

        var logs = new List<FileInfo>();
        if (LogDir is { } ld && Directory.Exists(ld))
            logs = new DirectoryInfo(ld).EnumerateFiles("CitizenFX_log_*.log")
                .Where(f => DateTime.UtcNow - f.LastWriteTimeUtc < MaxAge)
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxLogs).ToList();

        var reports = new List<CrashReport>();
        var sw = Stopwatch.StartNew();
        int read = 0;
        foreach (var f in logs)
        {
            if (sw.Elapsed > Budget) break;
            if (reports.Count >= CrashScanLogic.MaxCrashes && (oldestDump is not { } od || f.LastWriteTimeUtc < od)) break;
            CrashLog log;
            try { log = Load(f); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            read++;
            foreach (var b in log.Blocks) reports.Add(CrashScanLogic.FromBlock(b, log, f.Name));
        }

        // crashometry belongs to the newest session only (the launcher deletes it at every start)
        if (logs.FirstOrDefault() is { } newest && Server(app, newest) is { } server)
            foreach (var r in reports.Where(r => r.Log == newest.Name)) r.Server = server;

        return new(true, CrashScanLogic.Merge(reports, dumps), read, dumps.Count);
    }

    private static string? Server(string app, FileInfo newestLog)
    {
        try
        {
            var f = new FileInfo(Path.Combine(app, "data", "cache", "crashometry"));
            if (!f.Exists || f.Length > 1024 * 1024) return null;
            var start = CrashScanLogic.StartFromName(newestLog.Name) ?? newestLog.CreationTimeUtc;
            if (f.LastWriteTimeUtc < start.AddMinutes(-1)) return null;   // left from a session whose log is gone
            return CrashScanLogic.ServerOf(CrashScanLogic.ParseCrashometry(ReadShared(f.FullName)));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static CrashLog Load(FileInfo f)
    {
        lock (Gate)
            if (Cache.TryGetValue(f.FullName, out var c) && c.len == f.Length && c.mtime == f.LastWriteTimeUtc) return c.log;
        var log = CrashScanLogic.ScanLog(Lines(f.FullName), CrashScanLogic.StartFromName(f.Name));
        lock (Gate)
        {
            if (Cache.Count > 4 * MaxLogs) Cache.Clear();
            Cache[f.FullName] = (f.Length, f.LastWriteTimeUtc, log);
        }
        return log;
    }

    private static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[fs.Length];
        int n = 0;
        while (n < buf.Length && fs.Read(buf, n, buf.Length - n) is var k and > 0) n += k;
        return n == buf.Length ? buf : buf[..n];
    }

    /// <summary>The log's lines; of a huge log only the first 64 KB and the last 16 MB (whole lines only).</summary>
    private static List<string> Lines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var lines = new List<string>();
        if (fs.Length <= HeadBytes + TailBytes)
        {
            using var sr = new StreamReader(fs, Encoding.UTF8, true);
            while (sr.ReadLine() is { } l) lines.Add(l);
            return lines;
        }
        lines.AddRange(Chunk(fs, 0, HeadBytes, dropFirst: false));
        lines.AddRange(Chunk(fs, fs.Length - TailBytes, TailBytes, dropFirst: true));
        return lines;
    }

    private static IEnumerable<string> Chunk(FileStream fs, long at, int count, bool dropFirst)
    {
        var buf = new byte[count];
        fs.Seek(at, SeekOrigin.Begin);
        int n = 0;
        while (n < count && fs.Read(buf, n, count - n) is var k and > 0) n += k;
        var parts = Encoding.UTF8.GetString(buf, 0, n).Split('\n');
        // a cut line at either end is no line
        int from = dropFirst ? 1 : 0, to = parts.Length - 1;
        for (int i = from; i < to; i++) yield return parts[i].TrimEnd('\r');
    }
}
