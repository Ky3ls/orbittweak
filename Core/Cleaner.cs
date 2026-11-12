using System.IO;
namespace OrbitTweak.Core;

internal static class Cleaner
{
    private static readonly EnumerationOptions Opts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static List<CleanTarget> Targets() => new()
    {
        new CleanTarget
        {
            Id = "fm", Name = "FiveM Cache", Icon = "gamepad",
            Desc = "cache, server-cache & server-cache-priv – behebt Ladeprobleme und Texture-Loss.",
            Dirs = FiveM.CacheDirs, KeepFolders = new[] { "game", "game-storage" }
        },
        new CleanTarget
        {
            Id = "logs", Name = "FiveM Logs & Crashdumps", Icon = "file",
            Desc = "Alte Logdateien und Absturzberichte.",
            Dirs = FiveM.LogDirs
        },
        new CleanTarget
        {
            Id = "temp", Name = "Temporäre Dateien", Icon = "folder",
            Desc = "Benutzer- und System-Temp-Ordner.",
            Dirs = () => new[] { Path.GetTempPath(), Path.Combine(Win, "Temp") },
            KeepFolders = new[] { ".net" } // our own single-file runtime lives here
        },
        new CleanTarget
        {
            Id = "shader", Name = "Shader-Cache (DirectX / GPU)", Icon = "monitor",
            Desc = "D3DSCache, NVIDIA & AMD Shader-Cache – wird beim nächsten Start sauber neu gebaut.",
            Dirs = () => new[]
            {
                Path.Combine(Local, "D3DSCache"),
                Path.Combine(Local, "NVIDIA", "DXCache"),
                Path.Combine(Local, "NVIDIA", "GLCache"),
                Path.Combine(Local, "AMD", "DxCache"),
                Path.Combine(Local, "AMD", "DxcCache"),
                Path.Combine(Local, "AMD", "VkCache"),
            }
        },
        new CleanTarget
        {
            Id = "wu", Name = "Windows-Update Downloads", Icon = "download",
            Desc = "Bereits installierte Update-Pakete.",
            Dirs = () => new[] { Path.Combine(Win, "SoftwareDistribution", "Download") }
        },
        new CleanTarget
        {
            Id = "dumps", Name = "Absturzberichte & Dumps", Icon = "alert",
            Desc = "Windows-Fehlerberichte und Speicherabbilder.",
            Dirs = () => new[]
            {
                Path.Combine(Local, "CrashDumps"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportQueue"),
            }
        },
        new CleanTarget
        {
            Id = "bin", Name = "Papierkorb", Icon = "trash", Selected = false,
            Desc = "Leert den Papierkorb endgültig.",
            CustomSize = Native.RecycleBinSize,
            CustomClean = Native.EmptyRecycleBin
        },
    };

    private static bool Kept(CleanTarget t, string root, string path)
    {
        if (t.KeepFolders.Length == 0) return false;
        var rel = Path.GetRelativePath(root, path);
        var first = rel.Split(Path.DirectorySeparatorChar)[0];
        return t.KeepFolders.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    public static long Measure(CleanTarget t)
    {
        if (t.CustomSize != null) return t.CustomSize();
        long sum = 0;
        foreach (var d in t.Dirs())
        {
            if (!Directory.Exists(d)) continue;
            try
            {
                foreach (var f in new DirectoryInfo(d).EnumerateFiles("*", Opts))
                {
                    if (Kept(t, d, f.FullName)) continue;
                    try { sum += f.Length; } catch { }
                }
            }
            catch { }
        }
        return sum;
    }

    /// <summary>Deletes contents of the target's folders (never the folders themselves). Returns freed bytes.</summary>
    public static long Clean(CleanTarget t)
    {
        if (t.CustomClean != null)
        {
            var s = t.CustomSize?.Invoke() ?? 0;
            t.CustomClean();
            return s;
        }
        long freed = 0;
        foreach (var d in t.Dirs())
        {
            if (!Directory.Exists(d)) continue;
            var di = new DirectoryInfo(d);
            foreach (var f in di.EnumerateFiles())
            {
                try { var l = f.Length; f.Attributes = FileAttributes.Normal; f.Delete(); freed += l; } catch { }
            }
            foreach (var sub in di.EnumerateDirectories())
            {
                if (Kept(t, d, sub.FullName)) continue;
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) { try { sub.Delete(); } catch { } continue; }
                long size = 0;
                try { size = sub.EnumerateFiles("*", Opts).Sum(x => { try { return x.Length; } catch { return 0L; } }); } catch { }
                try { sub.Delete(true); freed += size; }
                catch
                {
                    // partially locked – delete what we can
                    foreach (var f in sub.EnumerateFiles("*", Opts))
                        try { var l = f.Length; f.Delete(); freed += l; } catch { }
                }
            }
        }
        return freed;
    }
}
