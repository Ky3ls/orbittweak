using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace OrbitTweak.Core;

/// <summary>
/// Putting clips together after the fact:
///  - Highlight video: the kill clips of the built-in recorder joined into one mp4, oldest first.
///  - Share copy: one clip made small enough for an upload limit (Discord).
/// Both run in a helper process (the same exe started with "--join jobfile"), like the recording, so the Windows
/// media machinery cannot take the app down, and with low priority, so they are not in the way of a running game.
/// </summary>
internal static class Highlight
{
    public class Job
    {
        public List<string> Files { get; set; } = new();
        public double Tail { get; set; }          // 0 = whole clips, otherwise only the last seconds of each clip
        // per clip (same order as Files): the part kept, as seconds before its end [from, to], placed around its kills;
        // missing or empty = the last "Tail" seconds
        public List<double[]> Cuts { get; set; } = new();
        public long MaxBytes { get; set; }        // 0 = as recorded, otherwise the result has to stay below this size
        public string Out { get; set; } = "";
        public double From { get; set; }          // cut: seconds from the start of the (single) clip …
        public double To { get; set; }            // … to here; 0 = no cut
        // montage: the clips cut together with a title card, a note on every clip, a flash at every cut and music
        public bool Montage { get; set; }
        public string? Intro { get; set; }
        public string? Outro { get; set; }
        public string? Vignette { get; set; }
        public List<string> Notes { get; set; } = new();
        public string? Music { get; set; }
    }

    /// <summary>The kill clips in the clips folder, oldest first: "Kill …" and "2 Kills …", not "Gefecht …" and not highlight videos.</summary>
    public static List<FileInfo> KillClips(bool todayOnly)
    {
        var dir = new DirectoryInfo(Clipper.OwnDir);
        if (!dir.Exists) return new();
        var all = dir.EnumerateFiles("*.mp4").ToList();
        var names = new HashSet<string>(all.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        return all
            .Where(f => Regex.IsMatch(f.Name, @"^(Kill|\d+ Kills) \d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2}( \(\d+\))?\.mp4$", RegexOptions.IgnoreCase))
            .Where(f => f.Length > 200_000)                                   // an empty file is a clip whose saving was cut off
            // "name (2).mp4" next to "name.mp4" is the same kill saved twice
            .Where(f => !(Regex.Match(f.Name, @"^(.*) \(\d+\)\.mp4$", RegexOptions.IgnoreCase) is { Success: true } m && names.Contains(m.Groups[1].Value + ".mp4")))
            .Where(f => !todayOnly || f.LastWriteTime.Date == DateTime.Today)
            .OrderBy(f => f.LastWriteTime).ToList();
    }

    // ---------- helper process ----------

    public static int RunChild(string[] args)
    {
        // WinRT media objects must not live on a single-threaded apartment that never pumps messages
        int code = 1;
        var t = new Thread(() => code = Run(args)) { Name = "OrbitJoin" };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start(); t.Join();
        return code;
    }

    private static int Run(string[] args)
    {
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        try
        {
            var job = JsonSerializer.Deserialize<Job>(File.ReadAllText(args[1])) ?? throw new InvalidOperationException("Auftrag fehlt");
            void Progress(double pct) { try { output.WriteLine("p " + pct.ToString("0", CultureInfo.InvariantCulture)); } catch { } }
            // a size limit is aimed at with the data rate; if the result is still too big, once or twice more with less
            double factor = 1;
            for (int attempt = 0; ; attempt++)
            {
                (job.Montage ? MontageJoin(job, Progress) : Join(job, factor, Progress)).GetAwaiter().GetResult();
                long size = new FileInfo(job.Out).Length;
                if (job.MaxBytes <= 0 || size <= job.MaxBytes) break;
                if (attempt == 2) { try { File.Delete(job.Out); } catch { } throw new InvalidOperationException("Der Clip ließ sich nicht klein genug machen"); }
                factor *= (double)job.MaxBytes / size * 0.93;
            }
            output.WriteLine("ok");
            return 0;
        }
        catch (Exception ex) { try { output.WriteLine("error " + ex.Message.Replace('\r', ' ').Replace('\n', ' ')); } catch { } return 1; }
    }

    private static uint Even(double v) => (uint)Math.Max(2, Math.Round(v / 2) * 2);

    /// <summary>Keeps the part of clip number i that the job asks for: its window around the kills, or the last "Tail" seconds.</summary>
    private static void Trim(MediaClip clip, Job job, int i)
    {
        double dur = clip.OriginalDuration.TotalSeconds;
        if (i < job.Cuts.Count && job.Cuts[i] is { Length: 2 } w && w[0] > w[1] && dur > 1.5)
        {
            double from = Math.Clamp(dur - w[0], 0, dur - 1), to = Math.Clamp(dur - w[1], from + 1, dur);
            clip.TrimTimeFromStart = TimeSpan.FromSeconds(from);
            clip.TrimTimeFromEnd = TimeSpan.FromSeconds(dur - to);
        }
        else if (job.Tail > 0 && dur > job.Tail + 0.5) clip.TrimTimeFromStart = clip.OriginalDuration - TimeSpan.FromSeconds(job.Tail);
    }

    /// <summary>Where every clip is cut: around its kills (see MontageCut.Window).</summary>
    private static List<double[]> KillCuts(IEnumerable<string> files, double after, double lead, double post, double maxLen)
        => files.Select(f => { var (a, b) = MontageCut.Window(ClipMarks.Get(f), after, lead, post, maxLen); return new[] { a, b }; }).ToList();

    private static async Task Join(Job job, double factor, Action<double> progress)
    {
        var comp = new MediaComposition();
        uint w = 0, h = 0, bitrate = 0; double fps = 60;
        for (int fi = 0; fi < job.Files.Count; fi++)
        {
            var path = job.Files[fi];
            MediaClip clip;
            try { clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path)); }
            catch { continue; } // a clip that cannot be read (still being written, damaged) is left out
            Trim(clip, job, fi);
            if (job.To > job.From)
            {
                double dur = clip.OriginalDuration.TotalSeconds, from = Math.Clamp(job.From, 0, dur), to = Math.Clamp(job.To, from, dur);
                if (to - from < 0.5) throw new InvalidOperationException("Der Ausschnitt ist zu kurz");
                clip.TrimTimeFromStart = TimeSpan.FromSeconds(from);
                clip.TrimTimeFromEnd = TimeSpan.FromSeconds(dur - to);
            }
            if (w == 0)
            {
                var v = clip.GetVideoEncodingProperties();
                w = v.Width; h = v.Height; bitrate = v.Bitrate;
                if (v.FrameRate.Denominator > 0 && v.FrameRate.Numerator > 0) fps = (double)v.FrameRate.Numerator / v.FrameRate.Denominator;
            }
            comp.Clips.Add(clip);
        }
        if (comp.Clips.Count == 0 || w == 0) throw new InvalidOperationException("Keiner der Clips konnte gelesen werden");
        // same picture size, frame rate and data rate as the first clip …
        var p = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        uint tw = w, th = h; double tf = Math.Clamp(Math.Round(fps), 15, 120); double video = bitrate;
        if (job.MaxBytes > 0)
        {
            // … unless the result has to fit a size: then the data rate follows from size and length, and with little
            // data the picture gets smaller and the frame rate lower rather than blocky
            double secs = Math.Max(1, comp.Duration.TotalSeconds);
            const double audio = 128_000;
            video = (job.MaxBytes * 8 * 0.93 / secs - audio) * factor;
            if (bitrate > 0) video = Math.Min(video, bitrate);
            video = Math.Max(video, 250_000);
            if (video < 3_500_000 && th > 720) { tw = Even(w * 720.0 / h); th = 720; }
            if (video < 1_400_000 && th > 540) { tw = Even(w * 540.0 / h); th = 540; }
            if (video < 2_200_000 && tf > 30) tf = 30;
            if (p.Audio != null) p.Audio.Bitrate = (uint)audio;
        }
        p.Video.Width = tw; p.Video.Height = th;
        if (video > 0) p.Video.Bitrate = (uint)video;
        p.Video.FrameRate.Numerator = (uint)tf; p.Video.FrameRate.Denominator = 1;
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(job.Out)!);
        var outFile = await folder.CreateFileAsync(Path.GetFileName(job.Out), CreationCollisionOption.ReplaceExisting);
        var op = comp.RenderToFileAsync(outFile, MediaTrimmingPreference.Precise, p);
        op.Progress = (_, pct) => progress(pct);
        var res = await op;
        if (res != TranscodeFailureReason.None)
        {
            try { File.Delete(job.Out); } catch { }
            throw new InvalidOperationException("Video konnte nicht geschrieben werden (" + res + ")");
        }
    }

    /// <summary>
    /// The montage: title card, then the last seconds of every clip (the kill sits there), each with its "KILL n / N"
    /// note and a short white flash at the cut, a dark cinema frame over everything, an end card, and music if wanted.
    /// </summary>
    private static async Task MontageJoin(Job job, Action<double> progress)
    {
        var comp = new MediaComposition();
        uint w = 0, h = 0, bitrate = 0; double fps = 60;
        async Task<StorageFile> F(string path) => await StorageFile.GetFileFromPathAsync(path);
        var starts = new List<TimeSpan>(); var at = TimeSpan.Zero;
        bool music = !string.IsNullOrEmpty(job.Music) && File.Exists(job.Music);
        if (job.Intro != null) { var c = await MediaClip.CreateFromImageFileAsync(await F(job.Intro), TimeSpan.FromSeconds(1.8)); comp.Clips.Add(c); at += c.TrimmedDuration; }
        for (int fi = 0; fi < job.Files.Count; fi++)
        {
            var path = job.Files[fi];
            MediaClip clip;
            try { clip = await MediaClip.CreateFromFileAsync(await F(path)); }
            catch { continue; }
            Trim(clip, job, fi);
            if (w == 0)
            {
                var v = clip.GetVideoEncodingProperties();
                w = v.Width; h = v.Height; bitrate = v.Bitrate;
                if (v.FrameRate.Denominator > 0 && v.FrameRate.Numerator > 0) fps = (double)v.FrameRate.Numerator / v.FrameRate.Denominator;
            }
            if (music) clip.Volume = 0.45;   // the game stays audible under the music
            starts.Add(at); comp.Clips.Add(clip); at += clip.TrimmedDuration;
        }
        if (starts.Count == 0 || w == 0) throw new InvalidOperationException("Keiner der Clips konnte gelesen werden");
        var clipsEnd = at;
        if (job.Outro != null) { var c = await MediaClip.CreateFromImageFileAsync(await F(job.Outro), TimeSpan.FromSeconds(2.2)); comp.Clips.Add(c); at += c.TrimmedDuration; }

        var full = new Windows.Foundation.Rect(0, 0, w, h);
        double sc = h / 1080.0;
        // the dark frame over the clips (not over the cards)
        if (job.Vignette != null && starts.Count > 0)
        {
            var layer = new MediaOverlayLayer();
            var vc = await MediaClip.CreateFromImageFileAsync(await F(job.Vignette), clipsEnd - starts[0]);
            layer.Overlays.Add(new MediaOverlay(vc, full, 1) { Delay = starts[0] });
            comp.OverlayLayers.Add(layer);
        }
        // the note of every clip, top left, for its first seconds
        if (job.Notes.Count > 0)
        {
            var layer = new MediaOverlayLayer();
            for (int i = 0; i < starts.Count && i < job.Notes.Count; i++)
            {
                var nc = await MediaClip.CreateFromImageFileAsync(await F(job.Notes[i]), TimeSpan.FromSeconds(2.6));
                layer.Overlays.Add(new MediaOverlay(nc, new Windows.Foundation.Rect(56 * sc, 90 * sc, MontageArt.NoteW * 0.62 * sc, MontageArt.NoteH * 0.62 * sc), 1) { Delay = starts[i] + TimeSpan.FromSeconds(0.25) });
            }
            comp.OverlayLayers.Add(layer);
        }
        // a white flash at every cut: bright for a moment, then half as bright
        {
            var layer = new MediaOverlayLayer();
            foreach (var st in starts)
            {
                layer.Overlays.Add(new MediaOverlay(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 255, 255, 255), TimeSpan.FromSeconds(0.07)), full, 0.6) { Delay = st });
                layer.Overlays.Add(new MediaOverlay(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 255, 255, 255), TimeSpan.FromSeconds(0.09)), full, 0.25) { Delay = st + TimeSpan.FromSeconds(0.07) });
            }
            comp.OverlayLayers.Add(layer);
        }
        if (music)
        {
            var track = await BackgroundAudioTrack.CreateFromFileAsync(await F(job.Music!));
            if (track.OriginalDuration > at) track.TrimTimeFromEnd = track.OriginalDuration - at;
            track.Volume = 0.85;
            comp.BackgroundAudioTracks.Add(track);
        }

        var p = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        p.Video.Width = w; p.Video.Height = h;
        if (bitrate > 0) p.Video.Bitrate = bitrate;
        p.Video.FrameRate.Numerator = (uint)Math.Clamp(Math.Round(fps), 15, 120); p.Video.FrameRate.Denominator = 1;
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(job.Out)!);
        var outFile = await folder.CreateFileAsync(Path.GetFileName(job.Out), CreationCollisionOption.ReplaceExisting);
        var op = comp.RenderToFileAsync(outFile, MediaTrimmingPreference.Precise, p);
        op.Progress = (_, pct) => progress(pct);
        var res = await op;
        if (res != TranscodeFailureReason.None)
        {
            try { File.Delete(job.Out); } catch { }
            throw new InvalidOperationException("Video konnte nicht geschrieben werden (" + res + ")");
        }
    }

    /// <summary>
    /// Makes a montage of the newest kill clips ("Montage date time.mp4" in the clips folder). Has to be called on
    /// the app's main thread (the cards are drawn there). Returns the file or an error text, and how many clips went in.
    /// </summary>
    public static async Task<(string? file, string? error, int clips)> MakeMontage(int count, string? music, string? accentHex, Action<int, string> progress, double after = 5)
    {
        var all = KillClips(false);
        if (all.Count < 2) return (null, "Dafür braucht es mindestens zwei Kill-Clips", all.Count);
        var clips = all.Skip(Math.Max(0, all.Count - Math.Clamp(count, 2, 30))).ToList();
        var tmp = Path.Combine(Path.GetTempPath(), "OrbitTweak-montage-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tmp);
            var files = clips.Select(c => c.FullName).ToList();
            // every clip from 3 s before its first kill to 2 s after its last (not simply its last seconds: with the
            // usual 5 s kept after a kill that left hardly anything before it)
            var job = new Job { Files = files, Tail = 6.5, Cuts = KillCuts(files, after, 3.0, 2.0, 9), Montage = true, Music = music, Out = Clipper.NewFile("Montage") };
            job.Intro = Path.Combine(tmp, "intro.png"); job.Outro = Path.Combine(tmp, "outro.png"); job.Vignette = Path.Combine(tmp, "frame.png");
            var first = clips[0].LastWriteTime; var last = clips[^1].LastWriteTime;
            MontageArt.Card(job.Intro, "HIGHLIGHTS", $"{clips.Count} Kills · {(first.Date == last.Date ? last.ToString("dd.MM.yyyy") : first.ToString("dd.MM.") + " bis " + last.ToString("dd.MM.yyyy"))}", accentHex);
            MontageArt.Card(job.Outro, "GG", "geschnitten mit OrbitTweak", accentHex);
            MontageArt.Vignette(job.Vignette);
            for (int i = 0; i < clips.Count; i++) { var n = Path.Combine(tmp, $"note{i}.png"); MontageArt.Note(n, i + 1, clips.Count, accentHex); job.Notes.Add(n); }
            var err = await RunJob(job, pct => progress(pct, $"Montage aus {clips.Count} Clips wird geschnitten…"));
            return err == null ? (job.Out, null, clips.Count) : (null, err, clips.Count);
        }
        catch (Exception ex) { return (null, ex.Message, clips.Count); }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
    // ---------- app ----------

    /// <summary>Runs one job in the helper process. Returns null, or what went wrong.</summary>
    private static async Task<string?> RunJob(Job job, Action<int> progress)
    {
        var jobFile = Path.Combine(Path.GetTempPath(), "OrbitTweak-join-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            File.WriteAllText(jobFile, JsonSerializer.Serialize(job));
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad unbekannt");
            var psi = new ProcessStartInfo(exe, $"--join \"{jobFile}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Hilfsprozess startet nicht");
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            progress(1);
            string? err = null, line; bool ok = false;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                if (line.StartsWith("p ") && double.TryParse(line[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) progress((int)Math.Clamp(pct, 1, 99));
                else if (line == "ok") ok = true;
                else if (line.StartsWith("error ")) err = line[6..];
            }
            await p.WaitForExitAsync();
            if (ok && File.Exists(job.Out)) return null;
            try { if (File.Exists(job.Out)) File.Delete(job.Out); } catch { }
            return err ?? "Der Hilfsprozess wurde unerwartet beendet";
        }
        catch (Exception ex) { return ex.Message; }
        finally { try { File.Delete(jobFile); } catch { } }
    }

    /// <summary>Joins the kill clips (of today or all) into "Highlights date time.mp4" in the clips folder. Returns the file or an error text.</summary>
    public static Task<(string? file, string? error, int clips)> Make(bool todayOnly, double tail, Action<int, string> progress, double after = 5)
    {
        var clips = KillClips(todayOnly);
        if (clips.Count < 2) return Task.FromResult<(string?, string?, int)>((null, todayOnly ? "Dafür braucht es mindestens zwei Kill-Clips von heute" : "Dafür braucht es mindestens zwei Kill-Clips", clips.Count));
        return Make(clips, tail, progress, after);
    }

    /// <summary>
    /// Joins the given clips, in this order. tail &gt; 0: only about that many seconds of each, placed around its kills
    /// (from tail − 2.5 s before the first kill to 2.5 s after the last); "after" = the seconds the clipper keeps after a
    /// kill, for clips without kill marks.
    /// </summary>
    public static async Task<(string? file, string? error, int clips)> Make(List<FileInfo> clips, double tail, Action<int, string> progress, double after = 5)
    {
        if (clips.Count > 40) clips = clips.Skip(clips.Count - 40).ToList(); // the newest 40
        var target = Clipper.NewFile("Highlights");
        var files = clips.Select(c => c.FullName).ToList();
        var job = new Job { Files = files, Tail = tail, Out = target };
        if (tail > 0) job.Cuts = KillCuts(files, after, Math.Max(1, tail - 2.5), Math.Min(2.5, tail / 2), tail + 4);
        var err = await RunJob(job, pct => progress(pct, $"{clips.Count} Clips werden zusammengefügt…"));
        return err == null ? (target, null, clips.Count) : (null, err, clips.Count);
    }

    /// <summary>
    /// Cuts a part out of a clip. Without a size limit the cut is saved next to the clip ("name (Schnitt).mp4"),
    /// with one it becomes a small copy for sharing. Returns the new file or an error text.
    /// </summary>
    public static async Task<(string? file, string? error)> Cut(string source, double from, double to, long maxBytes, Action<int, string> progress)
    {
        string target;
        try
        {
            if (maxBytes > 0) { Directory.CreateDirectory(ShareDir); target = Path.Combine(ShareDir, Path.GetFileNameWithoutExtension(source) + $" (Schnitt, {maxBytes / 1048576} MB).mp4"); }
            else
            {
                var dir = Path.GetDirectoryName(source)!; var name = Path.GetFileNameWithoutExtension(source);
                target = Path.Combine(dir, name + " (Schnitt).mp4");
                for (int i = 2; File.Exists(target) && i < 100; i++) target = Path.Combine(dir, $"{name} (Schnitt {i}).mp4");
            }
        }
        catch (Exception ex) { return (null, ex.Message); }
        var err = await RunJob(new Job { Files = { source }, From = from, To = to, MaxBytes = maxBytes, Out = target }, pct => progress(pct, "Clip wird geschnitten…"));
        return err == null ? (target, null) : (null, err);
    }
    /// <summary>Where the smaller copies for sharing are kept (they are cleared away after two days).</summary>
    public static string ShareDir => Path.Combine(AppDirs.Local, "share");

    /// <summary>Makes a copy of a clip that stays below maxBytes. Returns the copy or an error text.</summary>
    public static async Task<(string? file, string? error)> Shrink(string source, long maxBytes, Action<int, string> progress)
    {
        try
        {
            Directory.CreateDirectory(ShareDir);
            foreach (var old in Directory.EnumerateFiles(ShareDir, "*.mp4"))
                try { if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-2)) File.Delete(old); } catch { }
        }
        catch (Exception ex) { return (null, ex.Message); }
        var target = Path.Combine(ShareDir, Path.GetFileNameWithoutExtension(source) + $" ({maxBytes / 1048576} MB).mp4");
        var err = await RunJob(new Job { Files = { source }, MaxBytes = maxBytes, Out = target }, pct => progress(pct, "Clip wird verkleinert…"));
        return err == null ? (target, null) : (null, err);
    }
}
