using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace OrbitTweak.Core;

/// <summary>
/// The montage with slow motion: the newest kill clips cut together by ffmpeg. Every clip runs normally up to the
/// kill, the kill itself in slow motion with a slight zoom, then normally again; with a white flash at every cut,
/// the "KILL n / N" note, a cinema frame, stronger colours, title and end card and the user's own song.
/// ffmpeg is used where it is found (the app's own copy, or one that is installed); otherwise it is loaded once.
/// </summary>
internal static class FfMontage
{
    private static string OwnDir => Path.Combine(AppDirs.Local, "ffmpeg");
    private const string Url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    /// <summary>ffmpeg.exe with ffprobe.exe next to it, or null.</summary>
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
            try { if (File.Exists(c) && File.Exists(Path.Combine(Path.GetDirectoryName(c)!, "ffprobe.exe"))) return c; } catch { }
        return null;
    }

    /// <summary>Loads ffmpeg once into the app's own folder (about 90 MB). Returns null, or what went wrong.</summary>
    public static async Task<string?> Download(Action<int, string> progress)
    {
        var zip = Path.Combine(Path.GetTempPath(), "OrbitTweak-ffmpeg.zip");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            using var res = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);
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
    private static string N(double v) => v.ToString("0.###", Inv);

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

    /// <summary>Length of a clip in seconds and whether it has sound.</summary>
    private static async Task<(double dur, bool audio)> Probe(string probe, string file)
    {
        var (_, o) = await Run(probe, $"-v error -show_entries format=duration:stream=codec_type -of default=nw=1 \"{file}\"");
        double dur = 0; bool audio = false;
        foreach (var x in o.Split('\n'))
        {
            var t = x.Trim();
            if (t.StartsWith("duration=")) double.TryParse(t[9..], NumberStyles.Float, Inv, out dur);
            if (t == "codec_type=audio") audio = true;
        }
        return (dur, audio);
    }

    /// <summary>
    /// Cuts the montage. "after" = the seconds the clipper keeps after a kill (the kill sits that far before the end
    /// of a clip). Has to be called on the app's main thread (the cards are drawn there).
    /// </summary>
    public static async Task<(string? file, string? error, int clips)> Make(string ffmpeg, List<FileInfo> clips, double after, string? music, string? accentHex, Action<int, string> progress)
    {
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        var tmp = Path.Combine(Path.GetTempPath(), "OrbitTweak-montage-" + Guid.NewGuid().ToString("N")[..8]);
        var target = Path.Combine(Clipper.OwnDir, $"Montage {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4");
        try
        {
            Directory.CreateDirectory(tmp);
            progress(1, "Clips werden gelesen …");
            var use = new List<(string file, double dur, bool audio)>();
            foreach (var c in clips) { var (d, a) = await Probe(probe, c.FullName); if (d > 3) use.Add((c.FullName, d, a)); }
            if (use.Count < 2) return (null, "Zu wenige lesbare Kill-Clips", use.Count);
            int n = use.Count; const int W = 1920, H = 1080; const double slow = 2.5, introLen = 1.8, outroLen = 2.2;

            string intro = Path.Combine(tmp, "intro.png"), outro = Path.Combine(tmp, "outro.png"), frame = Path.Combine(tmp, "frame.png");
            var first = clips[0].LastWriteTime; var last = clips[^1].LastWriteTime;
            MontageArt.Card(intro, "HIGHLIGHTS", $"{n} Kills · {(first.Date == last.Date ? last.ToString("dd.MM.yyyy") : first.ToString("dd.MM.") + " bis " + last.ToString("dd.MM.yyyy"))}", accentHex);
            MontageArt.Card(outro, "GG", "geschnitten mit OrbitTweak", accentHex);
            MontageArt.Vignette(frame);
            var notes = new List<string>();
            for (int i = 0; i < n; i++) { var f = Path.Combine(tmp, $"note{i}.png"); MontageArt.Note(f, i + 1, n, accentHex); notes.Add(f); }

            // inputs: clips, notes, frame, intro, outro, music
            var args = new StringBuilder("-hide_banner -nostdin -y ");
            foreach (var u in use) args.Append($"-i \"{u.file}\" ");
            foreach (var f in notes) args.Append($"-i \"{f}\" ");
            args.Append($"-i \"{frame}\" -loop 1 -framerate 60 -t {N(introLen)} -i \"{intro}\" -loop 1 -framerate 60 -t {N(outroLen)} -i \"{outro}\" ");
            int iFrame = 2 * n, iIntro = 2 * n + 1, iOutro = 2 * n + 2, iMusic = 2 * n + 3;
            bool hasMusic = !string.IsNullOrEmpty(music) && File.Exists(music);
            if (hasMusic) args.Append($"-stream_loop -1 -i \"{music}\" ");

            var g = new StringBuilder(); double total = introLen + outroLen;
            g.Append($"[{iFrame}:v]scale={W}:{H},split={n}{string.Concat(Enumerable.Range(0, n).Select(i => $"[fr{i}]"))};\n");
            const string sil = "aevalsrc=0:channel_layout=stereo:sample_rate=48000";
            g.Append($"[{iIntro}:v]scale={W}:{H},setsar=1,fps=60,format=yuv420p,fade=t=out:st={N(introLen - 0.3)}:d=0.3[vin];{sil}:d={N(introLen)}[ain];\n");
            for (int i = 0; i < n; i++)
            {
                var (_, dur, audio) = use[i];
                // the part that is shown, and inside it the moment of the kill in slow motion
                // where the kill is: from the mark written when the clip was saved (the last kill, if there were several);
                // older clips have none, there it follows from the seconds the clipper keeps after a kill
                var marks = ClipMarks.Get(use[i].file);
                double kill = marks.Length > 0 ? dur - marks.Min() : dur - Math.Max(2, after) - 0.7;
                kill = Math.Clamp(kill, 1.0, dur - 0.6);
                double a = Math.Max(0, kill - 4.0), d = Math.Min(dur, kill + 2.4);
                double b = Math.Clamp(kill - 0.7, a + 0.3, d - 0.9), c = Math.Clamp(kill + 0.6, b + 0.4, d - 0.2);
                double len = (b - a) + (c - b) * slow + (d - c); total += len;
                g.Append($"[{i}:v]fps=60,scale={W}:{H}:force_original_aspect_ratio=decrease,pad={W}:{H}:(ow-iw)/2:(oh-ih)/2,setsar=1,split=3[x{i}a][x{i}b][x{i}c];\n");
                g.Append($"[x{i}a]trim={N(a)}:{N(b)},setpts=PTS-STARTPTS[v{i}a];\n");
                g.Append($"[x{i}b]trim={N(b)}:{N(c)},setpts=(PTS-STARTPTS)*{N(slow)},scale={(int)(W * 1.1)}:{(int)(H * 1.1)},crop={W}:{H}[v{i}b];\n");
                g.Append($"[x{i}c]trim={N(c)}:{N(d)},setpts=PTS-STARTPTS[v{i}c];\n");
                g.Append($"[v{i}a][v{i}b][v{i}c]concat=n=3:v=1:a=0,eq=contrast=1.06:saturation=1.2[v{i}j];\n");
                g.Append($"[v{i}j][fr{i}]overlay=0:0[v{i}k];[{n + i}:v]scale={(int)(MontageArt.NoteW * 0.62)}:-1[nt{i}];\n");
                g.Append($"[v{i}k][nt{i}]overlay=56:90:enable='lt(t,2.6)',fade=t=in:st=0:d=0.22:color=white,format=yuv420p[v{i}];\n");
                if (audio)
                {
                    g.Append($"[{i}:a]aresample=48000,aformat=channel_layouts=stereo,asplit=3[y{i}a][y{i}b][y{i}c];\n");
                    g.Append($"[y{i}a]atrim={N(a)}:{N(b)},asetpts=PTS-STARTPTS[a{i}a];[y{i}b]atrim={N(b)}:{N(c)},asetpts=PTS-STARTPTS,atempo=0.5,atempo=0.8,volume=0.7[a{i}b];[y{i}c]atrim={N(c)}:{N(d)},asetpts=PTS-STARTPTS[a{i}c];\n");
                    g.Append($"[a{i}a][a{i}b][a{i}c]concat=n=3:v=0:a=1[a{i}];\n");
                }
                else g.Append($"{sil}:d={N(len)}[a{i}];\n");
            }
            g.Append($"[{iOutro}:v]scale={W}:{H},setsar=1,fps=60,format=yuv420p,fade=t=in:st=0:d=0.3[vout];{sil}:d={N(outroLen)}[aout];\n");
            g.Append("[vin][ain]" + string.Concat(Enumerable.Range(0, n).Select(i => $"[v{i}][a{i}]")) + $"[vout][aout]concat=n={n + 2}:v=1:a=1[v][ga];\n");
            if (hasMusic)
                g.Append($"[{iMusic}:a]aresample=48000,aformat=channel_layouts=stereo,atrim=0:{N(total)},afade=t=in:st=0:d=0.6,afade=t=out:st={N(total - 2)}:d=2,volume=0.9[mu];[ga]volume=0.4[gl];[gl][mu]amix=inputs=2:duration=first:normalize=0[a]");
            else g.Append("[ga]anull[a]");
            var script = Path.Combine(tmp, "graph.txt");
            File.WriteAllText(script, g.ToString(), new UTF8Encoding(false));
            args.Append($"-filter_complex_script \"{script}\" -map \"[v]\" -map \"[a]\" -c:v libx264 -preset veryfast -crf 19 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart -t {N(total)} -progress pipe:1 -nostats \"{target}\"");

            var (code, err) = await Run(ffmpeg, args.ToString(), l =>
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