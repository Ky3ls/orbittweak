using System.Globalization;
using System.Text;

namespace OrbitTweak.Core;

/// <summary>
/// The plan and the ffmpeg filter graph of a montage. Nothing of Windows in here, so the same code can be tried on
/// made-up clips anywhere (tests/ClipLogic.Tests renders one). Per clip:
///  - the part around its kills (a few seconds of build-up, then the kills, then a short tail),
///  - a speed ramp into 2x slow motion on the last kill and out of it again, the in-between pictures blended, 60 fps CFR,
///  - a quick punch-in zoom at every kill and a slow push-in during the slow motion, cinema bars sliding in with it,
///  - a short brightness hit at every kill, a light colour grade in YUV (eq only, no RGB filters) and dark corners,
///  - the kill counter as a killfeed chip that slides in at the kill ("KILL 3 / 8"), "DOUBLE KILL" etc. for streaks,
///  - the game sound slowed (and lowered) with the picture.
/// Around it: an intro over the blurred first clip with title and date, an outro, smooth cross transitions between
/// the clips, and the music under everything, pushed down a little whenever the game is loud. With the beat of the
/// music known (<see cref="MontageBeat"/>), the music starts on its first strong hit and every clip gets just so much
/// lead-in that its (last) kill lands on a beat.
/// </summary>
internal static class MontageCut
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string N(double v) => v.ToString("0.####", Inv);

    /// <summary>One kill clip: length, sound, picture size, and the kill times inside it (seconds from its start) if known.</summary>
    public sealed record Source(string File, double Dur, bool Audio, int W, int H, double[] Kills, int Count = 1);

    /// <summary>After: seconds the clipper keeps after a kill; Intro: intro and outro; Slow: slow motion factor on the last kill of a clip (1 = none).</summary>
    public sealed record Options(double After = 5, bool Intro = true, double Slow = 2);

    /// <summary>A kill moment: seconds into its part of the montage, the running kill number, the streak inside the clip.</summary>
    public sealed record Kill(double T, int Number, int Streak);

    public const int Fps = 60;
    // how much of a clip is shown around its kills, and the transitions
    // (about 4 to 5 s per kill in the montage, slow motion included)
    private const double Pre = 2.4, Post = 1.5, MaxSpan = 7, StreakGap = 4.5;
    // on the beat: the lead-in may grow by up to 1 s or shrink by up to 0.9 s (at least 1.2 s before the first kill);
    // after the last kill at least MinTail s are shown (the last picture is held if the clip ends sooner)
    private const double BeatLonger = 1.0, BeatShorter = 0.9, MinLead = 1.2, MinTail = 0.8;
    public const double TIn = 0.5, TClip = 0.4, TOut = 0.6, IntroLen = 2.0, OutroLen = 2.4;

    public sealed class Part
    {
        public Source Src = null!;
        /// <summary>Shown: from A to D seconds of the clip.</summary>
        public double A, D;
        /// <summary>Relative to A: ramp into slow motion from P1 to B, slow motion from B to C, ramp out from C to P4.</summary>
        public double P1, B, C, P4;
        /// <summary>Slow motion factor (1 = none), and the kill it is on (relative to A).</summary>
        public double K = 1, KX;
        /// <summary>Seconds in the montage, and where it starts there (the transition into it begins then).</summary>
        public double Len, At;
        /// <summary>Seconds the last picture is held at the end (a kill right at the end of a clip still gets its tag).</summary>
        public double Hold;
        public readonly List<Kill> Kills = new();

        /// <summary>Where a moment x seconds after A lands in this part of the montage (the speed ramp).</summary>
        public double Out(double x)
        {
            if (K <= 1 || x <= P1) return x;
            double rin = B - P1, rout = P4 - C;
            double fb = P1 + rin * (K + 1) / 2, fc = fb + K * (C - B), f4 = fc + rout * (K + 1) / 2;
            if (x < B) { double u = x - P1; return P1 + u + (K - 1) * u * u / (2 * rin); }
            if (x < C) return fb + K * (x - B);
            if (x < P4) { double u = x - C; return fc + K * u - (K - 1) * u * u / (2 * rout); }
            return f4 + (x - P4);
        }

        /// <summary>The same as an ffmpeg setpts expression (T = seconds of the frame from the start of the part).</summary>
        public string SetPts()
        {
            // (the time of the first picture after the seek is kept here too: fps fills the start, else the kill came
            // up to one source frame early)
            if (K <= 1) return "PTS";
            double rin = B - P1, rout = P4 - C;
            double fb = Out(B), fc = Out(C), f4 = Out(P4);
            string k1 = N((K - 1) / (2 * rin)), k2 = N((K - 1) / (2 * rout));
            return $"(if(lt(T,{N(P1)}),T,if(lt(T,{N(B)}),T+{k1}*(T-{N(P1)})*(T-{N(P1)}),if(lt(T,{N(C)}),{N(fb)}+{N(K)}*(T-{N(B)}),"
                 + $"if(lt(T,{N(P4)}),{N(fc)}+{N(K)}*(T-{N(C)})-{k2}*(T-{N(C)})*(T-{N(C)}),{N(f4)}+(T-{N(P4)}))))))/TB";
        }

        public double SlowFrom => Out(P1);
        public double SlowTo => Out(P4);
        public double SlowKill => Out(KX);
    }

    public sealed class Plan
    {
        public int W, H;
        public readonly List<Part> Parts = new();
        public int Kills;
        public bool Intro;
        public double Total;
        /// <summary>The beat of the music (null = none known), and the seconds into the music where the montage music starts.</summary>
        public MontageBeat.Grid? Beat;
        public double MusicFrom;
    }

    /// <summary>Kill times of a clip without marks: the clipper keeps max(2, after) seconds after the last kill.</summary>
    public static double Guess(double dur, double after) => Math.Clamp(dur - Math.Max(2, after) - 0.55, 1.0, Math.Max(1.0, dur - 0.6));

    /// <summary>
    /// The part of a clip that a montage or highlight video without ffmpeg keeps, as seconds before the end of the clip
    /// (from &gt; to &gt;= 0; the clip's length is not needed): "lead" seconds before its first kill up to "post" seconds
    /// after its last one, at most maxLen long (then the last kills are kept). marks = seconds before the end at which
    /// the kills happened (ClipMarks); without them the kill is guessed from the seconds kept after a kill.
    /// </summary>
    public static (double from, double to) Window(IReadOnlyCollection<double> marks, double after, double lead, double post, double maxLen)
    {
        var ks = marks.Where(m => m >= 0 && m < 600).ToList();
        if (ks.Count == 0) ks.Add(Math.Max(2, after) + 0.55);
        double last = ks.Min(), first = ks.Max();
        double to = Math.Max(0, last - post);
        double from = Math.Min(first + lead, to + Math.Max(maxLen, lead + post));
        from = Math.Max(from, last + lead);
        return (Math.Round(from, 2), Math.Round(to, 2));
    }

    /// <summary>Picture size of a montage: 1080p, or 720p if all clips are smaller (4K, vertical and odd sizes are scaled to it).</summary>
    public static (int w, int h) Size(IEnumerable<Source> clips)
    {
        int maxH = clips.Select(c => c.H).DefaultIfEmpty(1080).Max();
        int h = maxH >= 1000 || maxH <= 0 ? 1080 : 720;
        return (h * 16 / 9 / 2 * 2, h);
    }

    /// <summary>The kill times of a clip that the montage uses (guessed from "after" if it has none).</summary>
    private static List<double> KillTimes(Source s, Options o)
    {
        var ks = s.Kills.Where(k => k > 0.2 && k < s.Dur - 0.1).OrderBy(k => k).ToList();
        if (ks.Count == 0) ks.Add(Guess(s.Dur, o.After));
        return ks;
    }

    /// <summary>Lays out a part of the montage that shows its clip from part.A on; number = kills before it. Returns the kills after it.</summary>
    private static int Shape(Part part, List<double> ks, Options o, int number)
    {
        var s = part.Src;
        int extra = Math.Max(0, s.Count - ks.Count);   // kills the clip has but no time for: they land on the last one
        double last = ks[^1];
        part.D = Math.Min(s.Dur - 0.05, last + Post);
        double len = part.D - part.A, x = last - part.A;
        part.P1 = part.B = part.C = part.P4 = part.KX = 0; part.K = 1; part.Hold = 0; part.Kills.Clear();
        // the slow motion: from shortly before the last kill to shortly after it, with a ramp on both sides
        if (o.Slow > 1.05)
        {
            double b = x - 0.45, c = x + 0.55, p1 = b - 0.22, p4 = c + 0.28;
            if (p1 < 0.3) { double sh = 0.3 - p1; p1 += sh; b += sh; }
            if (p4 > len - 0.1) { p4 = len - 0.1; c = Math.Min(c, p4 - 0.2); }
            if (b - p1 >= 0.1 && c - b >= 0.4 && p4 - c >= 0.1) { part.P1 = p1; part.B = b; part.C = c; part.P4 = p4; part.K = o.Slow; part.KX = x; }
        }
        part.Len = Math.Round(part.Out(len) * Fps) / Fps;
        // kill moments: kills before the shown part count with the first one that is shown
        int carry = 0, streak = 0; double prev = double.NegativeInfinity;
        for (int i = 0; i < ks.Count; i++)
        {
            int jump = 1 + (i == ks.Count - 1 ? extra : 0);
            streak = ks[i] - prev <= StreakGap ? streak + jump : jump;
            prev = ks[i];
            if (ks[i] < part.A + 0.3) { carry += jump; continue; }
            number += carry + jump; carry = 0;
            part.Kills.Add(new Kill(Math.Max(0.25, part.Out(ks[i] - part.A)), number, streak));
        }
        if (carry > 0) { number += carry; part.Kills.Add(new Kill(0.25, number, carry)); }
        // a kill right at the end of the clip: the last picture is held a moment, so its tag can be read
        double tail = part.Len - part.Kills[^1].T;
        if (tail < MinTail) { part.Hold = Math.Ceiling((MinTail - tail) * Fps) / Fps; part.Len += part.Hold; }
        return number;
    }

    public static Plan Make(IReadOnlyList<Source> clips, Options o, MontageBeat.Grid? beat = null)
    {
        var p = new Plan { Intro = o.Intro, Beat = beat, MusicFrom = beat?.Start ?? 0 };
        (p.W, p.H) = Size(clips);
        int number = 0;
        double acc = p.Intro ? IntroLen : 0;
        foreach (var s in clips)
        {
            var part = new Part { Src = s };
            var ks = KillTimes(s, o);
            double first = ks[0], last = ks[^1], d = Math.Min(s.Dur - 0.05, last + Post);
            double a0 = Math.Max(0, Math.Max(first - Pre, last - MaxSpan - Pre));
            if (d - a0 < 2) a0 = Math.Max(0, d - 2);
            // where the part starts in the montage (the transition into it begins then)
            part.At = acc - (p.Parts.Count == 0 ? (p.Intro ? TIn : 0) : TClip);
            part.A = a0;
            int next = Shape(part, ks, o, number);
            if (beat is { Period: > 0.2 })
            {
                // the (last) kill onto the nearest beat it can reach: a longer lead-in (smaller A) moves it later.
                // At least MinLead s before the first kill shown; if no beat is in reach so, a quicker cut in.
                double shown = ks.Where(k => k >= a0 + 0.3).DefaultIfEmpty(last).First(), kill = part.At + part.Kills[^1].T;
                double KillAt(double a) { part.A = a; Shape(part, ks, o, number); return part.At + part.Kills[^1].T; }
                double lo = Math.Max(0, a0 - BeatLonger), hi = a0, target = double.NaN;
                foreach (var lead in new[] { MinLead, MinLead / 2 })
                {
                    hi = Math.Max(a0, Math.Min(Math.Min(a0 + BeatShorter, shown - lead), d - 2));
                    double early = KillAt(hi), late = KillAt(lo);
                    for (double m = Math.Ceiling(early / beat.Period); m * beat.Period <= late; m++)
                    {
                        double bt = m * beat.Period;
                        if (bt >= 0 && bt <= beat.Span - 0.2 && (double.IsNaN(target) || Math.Abs(bt - kill) < Math.Abs(target - kill))) target = bt;
                    }
                    if (!double.IsNaN(target)) break;
                }
                if (!double.IsNaN(target))
                {
                    // the kill moves steadily with A (not quite one to one: the speed ramp): halving the range
                    for (int step = 0; step < 40 && hi - lo > 1e-5; step++)
                    {
                        double m = (lo + hi) / 2;
                        if (KillAt(m) > target) lo = m; else hi = m;
                    }
                    part.A = (lo + hi) / 2;
                }
                else part.A = a0;
                next = Shape(part, ks, o, number);
            }
            number = next;
            acc = part.At + part.Len;
            p.Parts.Add(part);
        }
        p.Kills = number;
        p.Total = p.Intro ? acc - TOut + OutroLen : acc;
        return p;
    }

    /// <summary>
    /// The pictures drawn by the app for a plan (null = leave it out): title cards, kill chips and streak tags keyed by
    /// kill number, and Shade, the dark corners laid over every clip (full picture size, transparent in the middle).
    /// </summary>
    public sealed record Art(string? Intro, string? Outro, IReadOnlyDictionary<int, string> Chips, IReadOnlyDictionary<int, string> Tags, string? Shade = null);

    /// <summary>"DOUBLE KILL" and so on for the n-th kill of a streak inside one clip; null for a single kill.</summary>
    public static string? StreakName(int streak) => streak switch { < 2 => null, 2 => "DOUBLE KILL", 3 => "TRIPLE KILL", 4 => "QUAD KILL", _ => "MULTI KILL" };

    // small expression helpers (ffmpeg expression syntax)
    private static string Clip01(string x) => $"clip({x},0,1)";
    private static string Smooth(string x) => $"(pow({Clip01(x)},2)*(3-2*{Clip01(x)}))";
    /// <summary>0 before t0, up to 1 within "rise" seconds, then fading with time constant "fall".</summary>
    private static string Pulse(string v, double t0, double rise, double fall)
        => $"if(gte({v},{N(t0)}),if(lt({v},{N(t0 + rise)}),({v}-{N(t0)})/{N(rise)},exp(-({v}-{N(t0 + rise)})/{N(fall)})),0)";
    /// <summary>Like Pulse, but eased in (no sudden jump at the start): for the zoom, where a jump would be seen.</summary>
    private static string Ease(string v, double t0, double rise, double fall)
        => $"if(gte({v},{N(t0)}),if(lt({v},{N(t0 + rise)}),{Smooth($"({v}-{N(t0)})/{N(rise)}")},exp(-({v}-{N(t0 + rise)})/{N(fall)})),0)";

    /// <summary>
    /// The ffmpeg arguments (inputs, mapping, encoding) and the filter graph (to be written to graphFile, which the
    /// arguments point at). newSyntax: ffmpeg 7 and newer read the graph file with "-/filter_complex".
    /// </summary>
    public static (string args, string graph) Build(Plan p, Art art, string? music, string graphFile, string target, bool newSyntax)
    {
        int W = p.W, H = p.H; double u = H / 1080.0;
        var args = new StringBuilder("-hide_banner -nostdin -y ");
        var g = new StringBuilder();
        int inputs = 0;
        int Input(string spec) { args.Append(spec).Append(' '); return inputs++; }
        // a picture is read once and repeated by the loop filter (decoding a PNG for every frame costs more than the rest)
        // (converted to YUV with alpha once, before it is repeated, so the overlay has nothing to convert per frame)
        string Still(int idx, double len) => $"[{idx}:v]format=yuva420p,loop=loop={(int)Math.Ceiling(len * Fps)}:size=1,setpts=N/{Fps}/TB";
        const string AFmt = "aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo";
        const string Silence = "anullsrc=r=48000:cl=stereo";
        string Std(int w, int h)
        {
            // a clip of another shape: the middle cut out to 16:9 (21:9, 16:10, windowed); very different shapes keep all and get bars
            double ar = (double)w / Math.Max(1, h), want = 16.0 / 9;
            if (w <= 0 || h <= 0 || Math.Abs(ar / want - 1) < 0.01) return "";
            if (ar > want && want / ar >= 0.7) return $"crop={(int)(h * want) / 2 * 2}:{h / 2 * 2},";
            if (ar < want && ar / want >= 0.7) return $"crop={w / 2 * 2}:{(int)(w / want) / 2 * 2},";
            return $"pad='max(iw,ih*16/9)':'max(ih,iw*9/16)':(ow-iw)/2:(oh-ih)/2,";
        }
        // the soft colour grade of the clips, all in YUV (curves, colour balance and sharpening would each need a
        // round trip through RGB and made the whole cut five times slower); "flash" brightens for a moment at kills
        string Grade(string flash) => $"eq=contrast=1.05:saturation=1.08:gamma=0.97:brightness='{flash}':eval=frame";
        // the dark corners: one picture, overlaid on every clip and the cards (the vignette filter costs as much as the rest)
        int shadeAt = art.Shade != null ? Input($"-i \"{art.Shade}\"") : -1;
        int shades = 0;
        string Shade(string from, string to) => shadeAt < 0 ? $"[{from}]null[{to}];" : $"[{from}][sh{shades++}]overlay=0:0:eof_action=repeat[{to}];";
        string Zoom(string z, int w = 0, int h = 0) => $"zoompan=z='{z}':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':d=1:s={(w > 0 ? w : W)}x{(h > 0 ? h : H)}:fps={Fps}";

        // ---------- the clips ----------
        int bh = (int)Math.Round(H * 0.07 / 2) * 2;
        int chipX = (int)Math.Round(46 * u), chipY = (int)Math.Round(92 * u), slide = (int)Math.Round(34 * u);
        for (int i = 0; i < p.Parts.Count; i++)
        {
            var c = p.Parts[i]; var s = c.Src; double len = c.D - c.A;
            int iv = Input($"{(c.A > 0.01 ? $"-ss {N(c.A)} " : "")}-t {N(len + 0.15)} -i \"{s.File}\"");
            // zoom: a quick punch-in at every kill, a slow push-in during the slow motion
            var z = new StringBuilder("1");
            foreach (var k in c.Kills) z.Append("+0.07*").Append(Ease("it", k.T - 0.05, 0.1, 0.3));
            if (c.K > 1) z.Append($"+0.085*{Smooth($"(it-{N(c.SlowFrom)})/{N(Math.Max(0.2, c.SlowKill - c.SlowFrom))}")}*(1-{Smooth($"(it-{N(c.SlowTo - 0.1)})/0.45")})");
            var flash = new StringBuilder("0");
            foreach (var k in c.Kills) flash.Append("+0.075*").Append(Pulse("t", k.T - 0.02, 0.04, 0.14));
            bool big = s.H > H * 1.25;   // 4K and the like: made smaller first, the blending of frames is cheaper then
            // (the first picture after the seek comes up to one frame after A: its time is kept, and the start filled
            // up by fps before zoompan, which numbers its pictures from 0; else every kill came up to two frames early)
            g.Append($"[{iv}:v]trim=end={N(len)},{Std(s.W, s.H)}{(big ? $"scale={W}:{H}," : "")}setsar=1,"
                   + $"setpts='{c.SetPts()}',framerate=fps={Fps}:interp_start=0:interp_end=255:scene=100,fps=fps={Fps}:start_time=0,{Zoom(z.ToString())},"
                   + $"tpad=stop_mode=clone:stop_duration={N(0.5 + c.Hold)},trim=duration={N(c.Len)},{Grade(flash.ToString())}[c{i}e];{Shade($"c{i}e", $"c{i}g")}\n");
            string cur = $"c{i}g";
            // cinema bars sliding in with the slow motion
            if (c.K > 1)
            {
                string bars = $"{Smooth($"(t-{N(c.SlowFrom)})/0.3")}*(1-{Smooth($"(t-{N(c.SlowTo + 0.05)})/0.35")})";
                g.Append($"color=c=black:s={W}x{bh}:r={Fps},split[bt{i}][bb{i}];\n");
                g.Append($"[{cur}][bt{i}]overlay=x=0:y='-h+h*{bars}':shortest=1[c{i}t];[c{i}t][bb{i}]overlay=x=0:y='H-h*{bars}':shortest=1[c{i}b];\n");
                cur = $"c{i}b";
            }
            // kill chips and streak tags
            for (int j = 0; j < c.Kills.Count; j++)
            {
                var k = c.Kills[j]; double until = j + 1 < c.Kills.Count ? c.Kills[j + 1].T : c.Len + 1;
                if (art.Chips.TryGetValue(k.Number, out var chip))
                {
                    int ic = Input($"-i \"{chip}\"");
                    string e = Smooth($"(t-{N(k.T)})/0.22");
                    g.Append($"{Still(ic, c.Len)},fade=t=in:st={N(k.T)}:d=0.18:alpha=1[k{i}_{j}];");
                    g.Append($"[{cur}][k{i}_{j}]overlay=x='{chipX}-{slide}*(1-{e})':y={chipY}:enable='between(t,{N(k.T)},{N(until)})'[c{i}k{j}];\n");
                    cur = $"c{i}k{j}";
                }
                if (art.Tags.TryGetValue(k.Number, out var tag))
                {
                    // (shown at least 0.6 s, also for a kill right at the end of the clip)
                    double t1 = Math.Min(c.Len - 0.05, k.T + 1.75), t0 = Math.Max(0, Math.Min(k.T + 0.05, t1 - 0.6));
                    int it = Input($"-i \"{tag}\"");
                    g.Append($"{Still(it, c.Len)},fade=t=in:st={N(t0)}:d=0.15:alpha=1,fade=t=out:st={N(t1 - 0.3)}:d=0.3:alpha=1[s{i}_{j}];");
                    g.Append($"[{cur}][s{i}_{j}]overlay=x=(W-w)/2:y='H*0.73+{N(10 * u)}*(1-{Smooth($"(t-{N(t0)})/0.25")})':enable='between(t,{N(t0)},{N(t1)})'[c{i}s{j}];\n");
                    cur = $"c{i}s{j}";
                }
            }
            // (trim again after fps: zoompan hands on the end of the clip with a time in the wrong time base, and fps
            // would fill up to it with copies of the last picture, hundreds of seconds of them)
            g.Append($"[{cur}]fps={Fps},settb=1/{Fps},trim=duration={N(c.Len)},format=yuv420p,setsar=1[v{i}];\n");
            // sound: normal, slowed (and so lower) through the slow motion, normal again
            if (s.Audio)
            {
                g.Append($"[{iv}:a]asetpts=PTS-STARTPTS,atrim=duration={N(len)},aresample=48000,{AFmt}");
                if (c.K > 1)
                {
                    double outLen = c.Out(c.P4) - c.Out(c.P1); int rate = (int)Math.Round(48000 * (c.P4 - c.P1) / outLen);
                    g.Append($",asplit=3[y{i}a][y{i}b][y{i}c];\n");
                    g.Append($"[y{i}a]atrim=0:{N(c.P1)},asetpts=PTS-STARTPTS[z{i}a];");
                    g.Append($"[y{i}b]atrim={N(c.P1)}:{N(c.P4)},asetpts=PTS-STARTPTS,asetrate={rate},aresample=48000,afade=t=in:d=0.03,afade=t=out:st={N(Math.Max(0, outLen - 0.05))}:d=0.05,volume=1.15[z{i}b];");
                    g.Append($"[y{i}c]atrim=start={N(c.P4)},asetpts=PTS-STARTPTS[z{i}c];\n");
                    g.Append($"[z{i}a][z{i}b][z{i}c]concat=n=3:v=0:a=1,{AFmt}");
                }
                g.Append($",apad=whole_dur={N(c.Len)},atrim=duration={N(c.Len)}[a{i}];\n");
            }
            else g.Append($"{Silence},atrim=duration={N(c.Len)}[a{i}];\n");
        }

        // ---------- intro and outro: the first / last clip blurred and dark, the title over it ----------
        bool hasMusic = !string.IsNullOrEmpty(music);
        // (blurred small, slowly zoomed at half size: nothing of it is sharp, so nobody sees the difference)
        string Backdrop(int idx, Source s, double len, string zoom) =>
            $"[{idx}:v]setpts=PTS-STARTPTS,{Std(s.W, s.H)}scale={W / 8}:{H / 8},boxblur=6:2,scale={W / 2}:{H / 2},setsar=1,"
            + $"eq=brightness=-0.16:saturation=0.75:contrast=0.95,fps={Fps},tpad=stop_mode=clone:stop_duration={N(len)},trim=duration={N(len)},"
            + $"{Zoom(zoom, W / 2 / 2 * 2, H / 2 / 2 * 2)},scale={W}:{H},setsar=1";
        if (p.Intro && p.Parts.Count > 0)
        {
            var f = p.Parts[0];
            double from = Math.Max(0, f.A - IntroLen);
            int ib = Input($"{(from > 0.01 ? $"-ss {N(from)} " : "")}-t {N(IntroLen + 0.2)} -i \"{f.Src.File}\"");
            g.Append(Backdrop(ib, f.Src, IntroLen, $"1.0+0.05*it/{N(IntroLen)}")).Append("[ib0];").Append(Shade("ib0", "ib"));
            if (art.Intro != null)
            {
                int it = Input($"-i \"{art.Intro}\"");
                g.Append($"{Still(it, IntroLen)},fade=t=in:st=0.15:d=0.5:alpha=1[it];[ib][it]overlay=x=0:y='{N(28 * u)}*pow(1-{Clip01("(t-0.15)/0.6")},3)':shortest=1");
            }
            else g.Append("[ib]null");
            g.Append($",fade=t=in:st=0:d=0.25,fps={Fps},settb=1/{Fps},trim=duration={N(IntroLen)},format=yuv420p,setsar=1[vin];\n");
            if (!hasMusic && f.Src.Audio)
                g.Append($"[{ib}:a]asetpts=PTS-STARTPTS,aresample=48000,{AFmt},lowpass=f=700,volume=0.55,afade=t=in:d=0.8,apad=whole_dur={N(IntroLen)},atrim=duration={N(IntroLen)}[ain];\n");
            else g.Append($"{Silence},atrim=duration={N(IntroLen)}[ain];\n");

            var l = p.Parts[^1];
            int ob = Input($"-ss {N(Math.Max(0, l.D - 0.6))} -t {N(OutroLen + 0.2)} -i \"{l.Src.File}\"");
            g.Append(Backdrop(ob, l.Src, OutroLen, $"1.06-0.05*it/{N(OutroLen)}")).Append("[ob0];").Append(Shade("ob0", "ob"));
            if (art.Outro != null)
            {
                int ot = Input($"-i \"{art.Outro}\"");
                g.Append($"{Still(ot, OutroLen)},fade=t=in:st=0.25:d=0.5:alpha=1[ot];[ob][ot]overlay=x=0:y='{N(28 * u)}*pow(1-{Clip01("(t-0.25)/0.6")},3)':shortest=1");
            }
            else g.Append("[ob]null");
            g.Append($",fade=t=out:st={N(OutroLen - 0.7)}:d=0.7,fps={Fps},settb=1/{Fps},trim=duration={N(OutroLen)},format=yuv420p,setsar=1[vout];\n");
            if (!hasMusic && l.Src.Audio)
                g.Append($"[{ob}:a]asetpts=PTS-STARTPTS,aresample=48000,{AFmt},lowpass=f=900,volume=0.5,afade=t=out:st={N(OutroLen - 1.2)}:d=1.2,apad=whole_dur={N(OutroLen)},atrim=duration={N(OutroLen)}[aout];\n");
            else g.Append($"{Silence},atrim=duration={N(OutroLen)}[aout];\n");
        }

        // ---------- everything in a row, with cross transitions ----------
        string[] moves = { "smoothleft", "smoothright", "zoomin", "smoothup" };
        string v = p.Intro ? "vin" : "v0", a = p.Intro ? "ain" : "a0";
        for (int i = p.Intro ? 0 : 1; i < p.Parts.Count; i++)
        {
            var c = p.Parts[i]; double t = i == 0 ? TIn : TClip;
            string tr = i == 0 ? "fade" : moves[(i - 1) % moves.Length];
            g.Append($"[{v}][v{i}]xfade=transition={tr}:duration={N(t)}:offset={N(c.At)}[xv{i}];[{a}][a{i}]acrossfade=d={N(t)}:c1=tri:c2=tri[xa{i}];\n");
            v = $"xv{i}"; a = $"xa{i}";
        }
        if (p.Intro)
        {
            var l = p.Parts[^1];
            g.Append($"[{v}][vout]xfade=transition=fade:duration={N(TOut)}:offset={N(l.At + l.Len - TOut)}[xvo];[{a}][aout]acrossfade=d={N(TOut)}:c1=tri:c2=tri[xao];\n");
            v = "xvo"; a = "xao";
        }
        else g.Append($"[{v}]fade=t=in:d=0.3,fade=t=out:st={N(p.Total - 0.5)}:d=0.5[xvf];\n");
        string vEnd = p.Intro ? v : "xvf";

        // ---------- music: under everything, lower while the game is loud and through the slow motion ----------
        if (hasMusic)
        {
            int im = Input($"-stream_loop -1 -i \"{music}\"");
            var dip = new StringBuilder("0");
            foreach (var c in p.Parts.Where(x => x.K > 1))
                dip.Append($"+{Smooth($"(t-{N(c.At + c.SlowFrom)})/0.25")}*(1-{Smooth($"(t-{N(c.At + c.SlowTo)})/0.4")})");
            // (on the beat it starts right on its first strong hit, without a slow fade)
            g.Append($"[{im}:a]atrim=start={N(p.MusicFrom)}:duration={N(p.Total)},asetpts=PTS-STARTPTS,aresample=48000,{AFmt},afade=t=in:d={(p.Beat != null ? "0.02" : "1")},afade=t=out:st={N(Math.Max(0, p.Total - 2.5))}:d=2.5,"
                   + $"volume='0.85*(1-0.4*min(1,{dip}))':eval=frame[mu];\n");
            g.Append($"[{a}]asplit[ga][gk];[mu][gk]sidechaincompress=threshold=0.06:ratio=4:attack=8:release=300[md];");
            g.Append($"[ga]volume=0.8[gl];[gl][md]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.95:level=0[aend]");
        }
        else g.Append($"[{a}]afade=t=out:st={N(Math.Max(0, p.Total - 0.6))}:d=0.6,alimiter=limit=0.95:level=0[aend]");

        if (shades > 0) g.Insert(0, $"[{shadeAt}:v]format=yuva420p,split={shades}{string.Concat(Enumerable.Range(0, shades).Select(k => $"[sh{k}]"))};\n");
        args.Append(newSyntax ? $"-/filter_complex \"{graphFile}\" " : $"-filter_complex_script \"{graphFile}\" ");
        // a steady 60 frames a second (ffmpeg before 7 knows -vsync only)
        args.Append($"-map \"[{vEnd}]\" -map \"[aend]\" -r {Fps} {(newSyntax ? "-fps_mode cfr" : "-vsync cfr")} -c:v libx264 -preset veryfast -crf 19 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart -t {N(p.Total)} -progress pipe:1 -nostats \"{target}\"");
        return (args.ToString(), g.ToString());
    }
}
