using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace OrbitTweak.Core;

/// <summary>
/// The montage with slow motion: the newest kill clips cut together by ffmpeg (what it looks like is planned in
/// <see cref="MontageCut"/>, the pictures are drawn by <see cref="MontageArt"/>). ffmpeg is used where it is found (the
/// app's own copy, or one that is installed); otherwise it is loaded once.
/// </summary>
internal static class FfMontage
{
    private static string OwnDir => Path.Combine(AppDirs.Local, "ffmpeg");
    // a fixed version (the one the montage was tried with), not "latest": a new ffmpeg may drop options. If that
    // file is ever gone, the current release is taken (the montage also works with it, see MontageCut.Build).
    private const string Url = "https://github.com/GyanD/codexffmpeg/releases/download/7.1.1/ffmpeg-7.1.1-essentials_build.zip";
    private const string Latest = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    /// <summary>ffmpeg.exe with ffprobe.exe next to it (version 5 or newer, older ones lack filters the montage needs), or null.</summary>
    public static string? Find()
    {
        var cands = new List<string> { Path.Combine(OwnDir, "ffmpeg.exe") };
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            try { cands.Add(Path.Combine(d.Trim(), "ffmpeg.exe")); } catch { }
        try
        {
            var pk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(pk))
                foreach (var d in Directory.GetDirectories(pk, "Gyan.FFmpeg*"))
                    cands.AddRange(Directory.GetFiles(d, "ffmpeg.exe", SearchOption.AllDirectories));
        }
        catch { }
        foreach (var c in cands)
            try { if (File.Exists(c) && File.Exists(Path.Combine(Path.GetDirectoryName(c)!, "ffprobe.exe")) && Major(c) >= 5) return c; } catch { }
        return null;
    }

    private static readonly Dictionary<string, int> Majors = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Main version of an ffmpeg ("ffmpeg version 7.1-essentials…" = 7); builds named by date count as new (99).</summary>
    private static int Major(string exe)
    {
        var key = exe + "|" + File.GetLastWriteTimeUtc(exe).Ticks;
        lock (Majors) if (Majors.TryGetValue(key, out var known)) return known;
        int v = 99;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, "-hide_banner -version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            var first = p.StandardOutput.ReadLine() ?? "";
            p.WaitForExit(3000);
            var m = System.Text.RegularExpressions.Regex.Match(first, @"version\s+n?(\d+)\.");
            if (m.Success) v = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        catch { }
        lock (Majors) Majors[key] = v;
        return v;
    }

    /// <summary>Loads ffmpeg once into the app's own folder (about 90 MB). Returns null, or what went wrong.</summary>
    public static async Task<string?> Download(Action<int, string> progress)
    {
        var zip = Path.Combine(Path.GetTempPath(), "OrbitTweak-ffmpeg.zip");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            var res = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) { res.Dispose(); res = await http.GetAsync(Latest, HttpCompletionOption.ResponseHeadersRead); }
            using var owned = res;
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? 0, got = 0;
            await using (var src = await res.Content.ReadAsStreamAsync())
            await using (var dst = File.Create(zip))
            {
                var buf = new byte[1 << 16]; int n, last = -1;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n)); got += n;
                    int pct = total > 0 ? (int)(got * 95 / total) : 0;
                    if (pct != last) { last = pct; progress(Math.Max(1, pct), $"Lade Schnittprogramm … {got / 1048576} MB"); }
                }
            }
            progress(96, "Entpacke …");
            Directory.CreateDirectory(OwnDir);
            using (var z = ZipFile.OpenRead(zip))
                foreach (var e in z.Entries)
                    if (e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                        e.ExtractToFile(Path.Combine(OwnDir, e.Name), true);
            return Find() != null ? null : "Im Archiv war kein ffmpeg";
        }
        catch (Exception ex) { return "Schnittprogramm konnte nicht geladen werden: " + ex.Message; }
        finally { try { File.Delete(zip); } catch { } }
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static async Task<(int code, string output)> Run(string exe, string args, Action<string>? line = null)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg startet nicht");
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        var err = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) { if (err.Length < 6000) err.AppendLine(e.Data); } };
        p.BeginErrorReadLine();
        var sb = new StringBuilder(); string? l;
        while ((l = await p.StandardOutput.ReadLineAsync()) != null) { if (line != null) line(l); else sb.AppendLine(l); }
        await p.WaitForExitAsync();
        return (p.ExitCode, line != null ? err.ToString() : sb.ToString());
    }

    /// <summary>Length of a clip in seconds, whether it has sound, and its picture size.</summary>
    private static async Task<(double dur, bool audio, int w, int h)> Probe(string probe, string file)
    {
        var (_, o) = await Run(probe, $"-v error -show_entries format=duration:stream=codec_type,width,height -of default=nw=1 \"{file}\"");
        double dur = 0; bool audio = false; int w = 0, h = 0;
        foreach (var x in o.Split('\n'))
        {
            var t = x.Trim();
            if (t.StartsWith("duration=")) double.TryParse(t[9..], NumberStyles.Float, Inv, out dur);
            else if (t == "codec_type=audio") audio = true;
            else if (t.StartsWith("width=") && w == 0) int.TryParse(t[6..], out w);
            else if (t.StartsWith("height=") && h == 0) int.TryParse(t[7..], out h);
        }
        return (dur, audio, w, h);
    }

    /// <summary>How many kills a clip has by its name: "Kill …" = 1, "3 Kills …" = 3.</summary>
    private static int KillsInName(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(\d+) Kills ");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? Math.Clamp(n, 1, 20) : 1;
    }

    /// <summary>
    /// Cuts the montage (see <see cref="MontageCut"/> for what it does). "after" = the seconds the clipper keeps after a
    /// kill (where clips have no kill marks, the kill is guessed from it). title = the big line of the intro (empty =
    /// "HIGHLIGHTS"); counter = kill chips and streak tags; slow = slow motion on the kills.
    /// Has to be called on the app's main thread (the pictures are drawn there).
    /// </summary>
    public static async Task<(string? file, string? error, int clips)> Make(string ffmpeg, List<FileInfo> clips, double after, string? music, string? accentHex, Action<int, string> progress,
        string? title = null, bool intro = true, bool counter = true, bool slow = true)
    {
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        var tmp = Path.Combine(Path.GetTempPath(), "OrbitTweak-montage-" + Guid.NewGuid().ToString("N")[..8]);
        var target = Path.Combine(Clipper.OwnDir, $"Montage {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4");
        try
        {
            Directory.CreateDirectory(tmp);
            progress(1, "Clips werden gelesen …");
            var src = new List<MontageCut.Source>(); var used = new List<FileInfo>();
            foreach (var c in clips)
            {
                var (d, a, w, h) = await Probe(probe, c.FullName);
                if (d <= 3) continue;
                // where the kills are: the marks written when the clip was saved (seconds before its end)
                var kills = ClipMarks.Get(c.FullName).Select(m => d - m).Where(k => k > 0).OrderBy(k => k).ToArray();
                src.Add(new MontageCut.Source(c.FullName, d, a, w, h, kills, Math.Max(KillsInName(c.Name), kills.Length)));
                used.Add(c);
            }
            if (src.Count < 2) return (null, "Zu wenige lesbare Kill-Clips", src.Count);
            int n = src.Count;
            var plan = MontageCut.Make(src, new MontageCut.Options(after, intro, slow ? 2 : 1));

            // the pictures, at the size of the video
            string? introPng = null, outroPng = null, shade = Path.Combine(tmp, "shade.png");
            MontageArt.Shade(shade, plan.W, plan.H);
            if (intro)
            {
                DateTime first = used[0].LastWriteTime, last = used[^1].LastWriteTime;
                var when = first.Date == last.Date ? last.ToString("dd.MM.yyyy") : first.ToString("dd.MM.") + " – " + last.ToString("dd.MM.yyyy");
                var big = string.IsNullOrWhiteSpace(title) ? "HIGHLIGHTS" : title.Trim();
                introPng = Path.Combine(tmp, "intro.png"); outroPng = Path.Combine(tmp, "outro.png");
                MontageArt.Title(introPng, plan.W, plan.H, when, big, plan.Kills == 1 ? "1 Kill" : $"{plan.Kills} Kills", accentHex);
                MontageArt.Title(outroPng, plan.W, plan.H, plan.Kills == 1 ? "1 KILL" : $"{plan.Kills} KILLS", "GG", "geschnitten mit OrbitTweak", accentHex);
            }
            var chips = new Dictionary<int, string>(); var tags = new Dictionary<int, string>();
            foreach (var k in plan.Parts.SelectMany(x => x.Kills))
            {
                if (counter && !chips.ContainsKey(k.Number))
                {
                    var f = Path.Combine(tmp, $"chip{k.Number}.png"); MontageArt.Chip(f, plan.H, k.Number, plan.Kills, accentHex); chips[k.Number] = f;
                }
                if (counter && MontageCut.StreakName(k.Streak) is { } name && !tags.ContainsKey(k.Number))
                {
                    var f = Path.Combine(tmp, $"tag{k.Number}.png"); MontageArt.Tag(f, plan.H, name, accentHex); tags[k.Number] = f;
                }
            }

            var script = Path.Combine(tmp, "graph.txt");
            var (args, graph) = MontageCut.Build(plan, new MontageCut.Art(introPng, outroPng, chips, tags, shade), music is { Length: > 0 } && File.Exists(music) ? music : null,
                script, target, Major(ffmpeg) >= 7);
            File.WriteAllText(script, graph, new UTF8Encoding(false));
            double total = plan.Total;
            var (code, err) = await Run(ffmpeg, args, l =>
            {
                if (l.StartsWith("out_time_us=") && long.TryParse(l[12..], out var us) && us > 0)
                    progress((int)Math.Clamp(us / 1e6 / total * 100, 2, 99), $"Montage aus {n} Clips wird geschnitten …");
            });
            if (code != 0 || !File.Exists(target) || new FileInfo(target).Length < 100_000)
            {
                try { if (File.Exists(target)) File.Delete(target); } catch { }
                var lastLine = err.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(x => x.Trim().Length > 0)?.Trim() ?? "unbekannter Fehler";
                return (null, "Schnitt fehlgeschlagen: " + (lastLine.Length > 160 ? lastLine[..160] : lastLine), n);
            }
            return (target, null, n);
        }
        catch (Exception ex) { return (null, ex.Message, clips.Count); }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}

/// <summary>
/// Where the kills sit in a clip: written when a kill clip is saved (seconds before the end of the clip), read by the
/// montage so the slow motion lands on the kill itself. One small file, the newest 400 clips.
/// </summary>
internal static class ClipMarks
{
    private static string FilePath => Path.Combine(AppDirs.Local, "clipmarks.json");
    private static readonly object Gate = new();

    private static Dictionary<string, double[]> Load()
    {
        try { if (File.Exists(FilePath)) return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(FilePath)) ?? new(); } catch { }
        return new();
    }

    public static void Add(string clipFile, IEnumerable<double> secondsBeforeEnd)
    {
        var marks = secondsBeforeEnd.Where(x => x is >= 0 and < 600).Select(x => Math.Round(x, 2)).ToArray();
        if (marks.Length == 0) return;
        lock (Gate)
        {
            try
            {
                var all = Load();
                all[Path.GetFileName(clipFile)] = marks;
                if (all.Count > 400) foreach (var k in all.Keys.Take(all.Count - 400).ToList()) all.Remove(k);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(all));
            }
            catch { }
        }
    }

    /// <summary>Seconds before the end of the clip at which its kills happened; empty if the clip has no marks.</summary>
    public static double[] Get(string clipFile)
    {
        lock (Gate) return Load().TryGetValue(Path.GetFileName(clipFile), out var m) ? m : Array.Empty<double>();
    }
}