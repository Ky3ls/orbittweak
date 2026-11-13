using System.IO;
using System.IO.Compression;

namespace OrbitTweak.Core;

/// <summary>
/// Manages client mod packs (.rpf) in FiveM's own "mods" folder – only on the user's request.
/// Disabled mods are moved to "mods_orbit_off" so they can be switched back on.
/// </summary>
internal static class Mods
{
    public static string? Dir => FiveM.AppDir is { } a ? Path.Combine(a, "mods") : null;
    private static string? OffDir => FiveM.AppDir is { } a ? Path.Combine(a, "mods_orbit_off") : null;

    private static bool IsRpf(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var b = new byte[4];
            return f.Read(b, 0, 4) == 4 && (System.Text.Encoding.ASCII.GetString(b) is "RPF7" or "7FPR");
        }
        catch { return false; }
    }

    public static object List(Dictionary<string, string> cats)
    {
        if (Dir is not { } on || OffDir is not { } off) return new { found = false, items = Array.Empty<object>() };
        var items = new List<object>();
        foreach (var (dir, enabled) in new[] { (on, true), (off, false) })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.rpf"))
                items.Add(new { name = f.Name, on = enabled, size = f.Length, date = f.LastWriteTime, cat = cats.TryGetValue(f.Name, out var c) ? c : "other" });
        }
        return new { found = true, dir = on, items };
    }

    public static (int count, string? error) Install(IEnumerable<string> files, string cat, Dictionary<string, string> cats)
    {
        if (Dir is not { } dir) return (0, "FiveM nicht gefunden");
        if (FiveM.IsRunning) return (0, "Bitte FiveM zuerst schließen");
        Directory.CreateDirectory(dir);
        int n = 0;
        foreach (var file in files)
        {
            if (file.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsRpf(file)) continue;
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(dir, name), true);
                cats[name] = cat; n++;
            }
            else if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var z = ZipFile.OpenRead(file);
                foreach (var e in z.Entries.Where(e => e.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
                {
                    var target = Path.Combine(dir, Path.GetFileName(e.Name));
                    e.ExtractToFile(target, true);
                    if (!IsRpf(target)) { File.Delete(target); continue; }
                    cats[Path.GetFileName(e.Name)] = cat; n++;
                }
            }
        }
        return n == 0 ? (0, "Keine gültige .rpf-Mod gefunden") : (n, null);
    }

    public static string? Toggle(string name, bool on)
    {
        if (Dir is not { } dir || OffDir is not { } off) return "FiveM nicht gefunden";
        if (FiveM.IsRunning) return "Bitte FiveM zuerst schließen";
        name = Path.GetFileName(name);
        var from = Path.Combine(on ? off : dir, name);
        var to = Path.Combine(on ? dir : off, name);
        if (!File.Exists(from)) return "Mod nicht gefunden";
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to, true);
        return null;
    }

    public static string? Delete(string name)
    {
        if (Dir is not { } dir || OffDir is not { } off) return "FiveM nicht gefunden";
        if (FiveM.IsRunning) return "Bitte FiveM zuerst schließen";
        name = Path.GetFileName(name);
        foreach (var d in new[] { dir, off }) { var p = Path.Combine(d, name); if (File.Exists(p)) File.Delete(p); }
        return null;
    }
}
