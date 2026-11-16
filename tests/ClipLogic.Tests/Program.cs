using System.Diagnostics;
using System.Globalization;
using OrbitTweak.Core;

// Checks of the clipper's pure logic. Exit code 0 = all fine.
//   dotnet run --project tests/ClipLogic.Tests
//   dotnet run --project tests/ClipLogic.Tests -- --render <dir> [--art <dir>] [--music <file>] [--plain]
//     cuts a montage from made-up clips (ffmpeg on PATH); --art takes intro.png, outro.png, shade.png, chip<n>.png, tag<n>.png from there;
//     --plain: no intro/outro and no slow motion

if (args.Length > 0 && args[0] == "--render") return Render.Run(args);

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }
var t0 = new DateTime(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
DateTime T(double s) => t0.AddSeconds(s);

// ---------- red X ----------
byte[] Square(int n, Action<Action<int, int>> draw)
{
    var px = new byte[n * n * 4];
    for (int i = 0; i < px.Length; i += 4) { px[i] = 40; px[i + 1] = 60; px[i + 2] = 70; px[i + 3] = 255; } // dark grey-blue game picture
    draw((x, y) => { if (x < 0 || y < 0 || x >= n || y >= n) return; int i = (y * n + x) * 4; px[i] = 50; px[i + 1] = 50; px[i + 2] = 224; });
    return px;
}
void Cross(Action<int, int> set, int c, int r, int w, int ox = 0, int oy = 0)
{
    for (int k = -r; k <= r; k++) for (int t = 0; t < w; t++) { set(c + ox + k + t, c + oy + k); set(c + ox + k + t, c + oy - k); }
}
int n = 53, mid = n / 2;
foreach (var r in new[] { 4, 8, 14, 22 })
    Check(KillLogic.IsRedX(Square(n, set => Cross(set, mid, r, 2)), n), $"red X of radius {r} is seen");
Check(KillLogic.IsRedX(Square(n, set => Cross(set, mid, 10, 2, 1, 1)), n), "red X a pixel off the middle is seen");
Check(!KillLogic.IsRedX(Square(n, set => Cross(set, mid, 10, 2, 12, 0)), n), "red X far beside the crosshair is not");
Check(!KillLogic.IsRedX(Square(n, set => { for (int y = -3; y <= 3; y++) for (int x = -3; x <= 3; x++) if (x * x + y * y <= 9) set(mid + x, mid + y); }), n), "red dot is not an X");
Check(!KillLogic.IsRedX(Square(n, set => { for (int k = -12; k <= 12; k++) for (int t = 0; t < 2; t++) { set(mid + k, mid + t); set(mid + t, mid + k); } }), n), "red + is not an X");
Check(!KillLogic.IsRedX(Square(n, set => { for (int k = -15; k <= 15; k++) for (int t = 0; t < 2; t++) set(mid + k, mid + t); }), n), "red line is not an X");
Check(!KillLogic.IsRedX(Square(n, set => { for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) if ((x + y) % 3 != 0) set(x, y); }), n), "red area is not an X");
Check(!KillLogic.IsRedX(Square(n, _ => { }), n), "nothing is not an X");

// ---------- red X in time ----------
{
    // one kill mark: 0.4 s on screen after a shot, counted after the quiet time with the time it appeared
    var m = new XMarks(); m.Look(T(0), false, true);
    DateTime? got = null;
    for (double s = 1; s < 3; s += 0.033) got ??= m.Look(T(s), s >= 1.5 && s < 1.9, true);
    Check(got != null && Math.Abs((got.Value - T(1.5)).TotalSeconds) < 0.05, "a kill mark counts once, at the moment it appeared");
}
{
    // the same X but a look misses it for a moment: still one kill
    var m = new XMarks(); m.Look(T(0), false, true); int kills = 0;
    for (double s = 1; s < 3; s += 0.033) if (m.Look(T(s), s >= 1.5 && s < 1.9 && Math.Abs(s - 1.7) > 0.02, true) != null) kills++;
    Check(kills == 1, "a blink inside one X does not make two kills");
}
{
    // a red X on every hit (four hits in a row): no kill, and after three such rows the X is ignored
    var m = new XMarks(); m.Look(T(0), false, true); int kills = 0;
    for (int burst = 0; burst < 3; burst++)
        for (double s = 0; s < 3; s += 0.033)
        {
            double b = burst * 5 + 1 + s;
            bool x = s < 1.2 && (s % 0.3) < 0.15;
            if (m.Look(T(b), x, true) != null) kills++;
        }
    Check(kills == 0, "hit markers in a row are no kills");
    Check(m.HitMarkers, "after three rows the red X counts as hit marker on this server");
}
{
    // an X that is there when the game comes back to the front is not new
    var m = new XMarks(); int kills = 0;
    m.Reset();
    for (double s = 0; s < 2; s += 0.033) if (m.Look(T(s), s < 0.3, true) != null) kills++;
    Check(kills == 0, "an X already there after alt-tab is not a kill");
}
{
    // no shot before: no kill; X that stays: no kill
    var m = new XMarks(); m.Look(T(0), false, false); int kills = 0;
    for (double s = 1; s < 3; s += 0.033) if (m.Look(T(s), s >= 1.5 && s < 1.9, false) != null) kills++;
    for (double s = 3; s < 8; s += 0.033) if (m.Look(T(s), s >= 4 && s < 6.5, true) != null) kills++;
    Check(kills == 0, "no kill without a shot, and none for a red X that stays");
}
{
    // two kills 2 s apart: two
    var m = new XMarks(); m.Look(T(0), false, true); int kills = 0;
    for (double s = 1; s < 6; s += 0.033) if (m.Look(T(s), (s >= 1.5 && s < 1.9) || (s >= 3.5 && s < 3.9), true) != null) kills++;
    Check(kills == 2, "two kill marks two seconds apart are two kills");
}

// ---------- kill messages ----------
{
    var k = new KillMessages(); int kills = 0;
    k.Read(T(0), false, false);
    // one message, 2.5 s on screen, read five times a second with a few failed readings
    for (double s = 0.2; s < 4; s += 0.2)
    {
        bool on = s >= 1 && s < 3.5, miss = on && (Math.Abs(s - 1.8) < 0.05 || Math.Abs(s - 2.0) < 0.05 || Math.Abs(s - 2.2) < 0.05);
        if (k.Read(T(s), on && !miss, false, "KILLED ZM_Blaze", "KILLED")) kills++;
    }
    Check(kills == 1, "one message with failed readings is one kill");
}
{
    var k = new KillMessages(); int kills = 0;
    k.Read(T(0), false, false);
    // a reading gap of a whole second inside one named message: still one kill
    for (double s = 0.2; s < 4; s += 0.2)
    {
        bool on = s >= 1 && s < 3.5 && !(s > 1.5 && s < 2.6);
        if (k.Read(T(s), on, false, s < 2 ? "KILLED ZM_Blaze" : "KILLED ZM_BIaze", "KILLED")) kills++;
    }
    Check(kills == 1, "the same named message after a long reading gap is no second kill");
}
{
    var k = new KillMessages(); int kills = 0;
    k.Read(T(0), false, false);
    // the next kill replaces the message at once
    for (double s = 0.2; s < 4; s += 0.2)
        if (k.Read(T(s), s >= 1, false, s < 2 ? "KILLED ZM_Blaze" : "KILLED Kronos77", "KILLED")) kills++;
    Check(kills == 2, "a message with another name right away is a second kill");
}
{
    var k = new KillMessages(); int kills = 0;
    k.Read(T(0), false, false);
    // a partial reading first, then the whole message: one kill
    for (double s = 0.2; s < 3; s += 0.2)
        if (k.Read(T(s), s >= 1, false, s < 1.5 ? "KILLED" : "KILLED Kronos77", "KILLED")) kills++;
    Check(kills == 1, "a partly read message is not counted twice");
}
{
    var k = new KillMessages(); int kills = 0;
    // a HUD label with a kill word that is always there never counts
    for (double s = 0; s < 10; s += 0.2) if (k.Read(T(s), true, false, "KILLS 12", "KILL")) kills++;
    Check(kills == 0, "a label that is always there never counts");
}
{
    var k = new KillMessages(); int kills = 0;
    k.Read(T(0), false, false);
    // death screen "KILLED BY …" and then readings that miss the "BY": no kill
    for (double s = 0.2; s < 5; s += 0.2)
    {
        bool death = s >= 1 && s < 1.6, kill = s >= 1.8 && s < 3.5;
        if (k.Read(T(s), kill, death, "KILLED Kronos77", "KILLED")) kills++;
    }
    Check(kills == 0, "a kill text right after the own death is the kill cam, no kill");
}
{
    var k = new KillMessages(); int kills = 0;
    k.Read(T(0), false, false);
    // two kills with the same short message and a gap between them
    for (double s = 0.2; s < 6; s += 0.2)
        if (k.Read(T(s), (s >= 1 && s < 2) || (s >= 3.2 && s < 4.2), false, "+1 KILL", "KILL")) kills++;
    Check(kills == 2, "the same unnamed message twice with a gap is two kills");
}
{
    var k = new KillMessages(); int kills = 0;
    k.Read(T(0), false, false);
    for (double s = 0.2; s < 2; s += 0.2) if (k.Read(T(s), s >= 1, false, "KILLED ZM_Blaze", "KILLED")) kills++;
    // the player tabs out; back in the game another message is on screen (an old one): not new
    k.Pause(T(10));
    for (double s = 10.3; s < 12; s += 0.2) if (k.Read(T(s), true, false, "KILLED Kronos77", "KILLED")) kills++;
    Check(kills == 1, "a message already there after alt-tab is not a kill");
}
{
    var mg = new KillMerge();
    Check(mg.New(T(10), "x") && !mg.New(T(10.4), "text") && mg.New(T(13), "text"), "red X and its message are one kill, a later message another");
}

// ---------- text ----------
Check(KillLogic.SameText("ZM Blaze killed Kronos", "|ZM Blaze killed Kronos"), "a leading icon read as a letter is the same line");
Check(KillLogic.SameText("ZM Blaze killed Kronos77", "ZM Blaze kiIled Kronos77"), "one misread letter is the same line");
Check(!KillLogic.SameText("ZM Blaze killed Kronos77", "ZM Blaze killed Viper_12"), "another victim is another line");

// ---------- kill words ----------
{
    string? K(string line) { var r = KillText.Classify(new[] { line }, null); return r.kind == KillText.Kind.Kill ? r.word : r.kind == KillText.Kind.Death ? "DEATH" : null; }
    foreach (var (line, want) in new (string, string?)[]
    {
        ("KILLED ZM_Blaze", "KILLED"), ("KILLEO Kronos77", "KILLED"), ("K1LLED Max", "KILLED"), ("KILLEDZM_Blaze", "KILLED"),
        ("+1 KILL", "KILL"), ("+1KILL", "KILL"), ("ELIMINATED Viper_12", "ELIMINATED"), ("Du hast Max getötet", "GETOTET"),
        ("You killed Young_Bob", "KILLED"),
        ("KILLER", null), ("Top Killer: Max", null), ("Skilled_Sniper", null), ("SKILL", null), ("FILLED", null), ("BILLED", null),
        ("KILLS 12", null), ("Overkill", null),
        ("KILLED BY Max", "DEATH"), ("You have been eliminated", "DEATH"), ("You have been killed", "DEATH"), ("Max killed you", "DEATH"),
        ("Max killed you!", "DEATH"), ("WASTED", "DEATH"), ("Du wurdest getötet", "DEATH"),
    })
        Check(K(line) == want, $"\"{line}\" is {want ?? "nothing"} (got {K(line) ?? "nothing"})");
    // a word of the user's own that starts like a look-alike still works
    Check(KillText.Classify(new[] { "KILLERSTREAK 3" }, "KILLERSTREAK").kind == KillText.Kind.Kill, "own word KILLERSTREAK counts");
}

// ---------- the part of a clip kept without ffmpeg ----------
{
    // one kill 5.35 s before the end (the clipper keeps 5 s after a kill): 3 s before it to 2 s after it
    var (f, t) = MontageCut.Window(new[] { 5.35 }, 5, 3, 2, 9);
    Check(Math.Abs(f - 8.35) < 1e-9 && Math.Abs(t - 3.35) < 1e-9, $"window around one kill ({f}..{t})");
    // no marks: the kill is guessed from "after"
    (f, t) = MontageCut.Window(Array.Empty<double>(), 5, 3, 2, 9);
    Check(Math.Abs(f - 8.55) < 1e-9 && Math.Abs(t - 3.55) < 1e-9, $"window without marks ({f}..{t})");
    // two kills far apart: the last ones are kept, never longer than maxLen
    (f, t) = MontageCut.Window(new[] { 20.0, 6.0 }, 5, 3, 2, 9);
    Check(Math.Abs(f - t - 9) < 1e-9 && t < 6 && f > 6, $"window of a long multi kill ({f}..{t})");
    // a kill at the very end of the clip
    (f, t) = MontageCut.Window(new[] { 0.5 }, 5, 5.5, 2.5, 12);
    Check(t == 0 && Math.Abs(f - 6) < 1e-9, $"window of a kill at the end ({f}..{t})");
}

// ---------- montage plan ----------
{
    var src = new List<MontageCut.Source>
    {
        new("a.mp4", 25, true, 1920, 1080, new[] { 20.0 }),
        new("b.mp4", 25, true, 2560, 1080, new[] { 17.5, 19.0 }, 2),
        new("c.mp4", 25, false, 1280, 720, Array.Empty<double>()),
        new("d.mp4", 4, true, 1920, 1080, new[] { 3.6 }),
    };
    var p = MontageCut.Make(src, new MontageCut.Options(After: 5));
    Check(p.W == 1920 && p.H == 1080, "montage is 1080p");
    Check(p.Kills == 5, $"kills are counted over all clips ({p.Kills})");
    var a = p.Parts[0];
    Check(Math.Abs(a.Out(a.KX) - a.SlowKill) < 1e-9 && a.K > 1, "the slow motion sits on the kill");
    Check(Math.Abs(a.A - 17.6) < 0.01 && Math.Abs(a.D - 21.5) < 0.01, $"part around the kill ({a.A}..{a.D})");
    Check(a.Len >= 4.5 && a.Len <= 5.6, $"about 4 to 5 s per kill ({a.Len})");
    Check(a.K == 2, "slow motion is 2x");
    double prev = -1; bool mono = true;
    for (double x = 0; x <= a.D - a.A; x += 0.01) { double o = a.Out(x); if (o < prev - 1e-9) mono = false; prev = o; }
    Check(mono, "the speed ramp never runs backwards");
    Check(Math.Abs(a.Out(a.P4) - a.Out(a.P1) - ((a.B - a.P1 + a.P4 - a.C) * (a.K + 1) / 2 + (a.C - a.B) * a.K)) < 1e-9, "slow part length");
    var b = p.Parts[1];
    Check(b.Kills.Count == 2 && b.Kills[1].Streak == 2 && MontageCut.StreakName(b.Kills[1].Streak) == "DOUBLE KILL", "double kill in one clip");
    Check(b.Kills[0].Number == 2 && b.Kills[1].Number == 3, "kill numbers run on");
    Check(Math.Abs(p.Parts[2].Kills[0].T - p.Parts[2].Out(MontageCut.Guess(25, 5) - p.Parts[2].A)) < 1e-9, "a clip without marks: the kill is guessed from the time kept after it");
    var d = p.Parts[3];
    Check(d.Len > 1.5 && d.Kills.Count == 1, "a very short clip still works");
    double sum = MontageCut.IntroLen + p.Parts.Sum(x => x.Len) + MontageCut.OutroLen - MontageCut.TIn - MontageCut.TOut - MontageCut.TClip * (p.Parts.Count - 1);
    Check(Math.Abs(p.Total - sum) < 1e-9, "total length");
    var (margs, graph) = MontageCut.Build(p, new MontageCut.Art("i.png", "o.png", new Dictionary<int, string> { [1] = "k1.png", [3] = "k3.png" }, new Dictionary<int, string> { [3] = "t3.png" }, "s.png"), "m.mp3", "g.txt", "out.mp4", true);
    Check(margs.Contains("-/filter_complex \"g.txt\"") && graph.Contains("xfade=") && graph.Contains("sidechaincompress"), "graph has transitions and ducking");
    Check(margs.Contains("-fps_mode cfr") && margs.Contains("-r 60"), "steady 60 fps");
    Check(graph.Contains("crop=1920:1080"), "ultrawide is cropped to 16:9, not padded");
    Check(graph.Contains("interp_start=0") && graph.Contains("settb=1/60"), "blended slow motion, xfade inputs on one time base");
    var (oargs, _) = MontageCut.Build(p, new MontageCut.Art(null, null, new Dictionary<int, string>(), new Dictionary<int, string>()), null, "g.txt", "out.mp4", false);
    Check(oargs.Contains("-filter_complex_script \"g.txt\"") && oargs.Contains("-vsync cfr") && !oargs.Contains("-/filter_complex"), "ffmpeg before 7: the old options");
    Check(!graph.Contains("NaN") && !graph.Contains("Infinity") && !graph.Contains("∞"), "no broken numbers in the graph");
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static class Render
{
    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static void Ff(string a)
    {
        var p = Process.Start(new ProcessStartInfo("ffmpeg", a) { RedirectStandardError = true, UseShellExecute = false })!;
        var err = p.StandardError.ReadToEnd(); p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception("ffmpeg failed: " + string.Join("\n", err.Split('\n').TakeLast(25)));
    }

    public static int Run(string[] args)
    {
        string dir = Path.GetFullPath(args[1]);
        string? art = null, music = null;
        bool plain = args.Contains("--plain");
        for (int i = 2; i + 1 < args.Length; i++) { if (args[i] == "--art") art = args[i + 1]; if (args[i] == "--music") music = args[i + 1]; }
        Directory.CreateDirectory(dir);
        // made-up kill clips: moving test picture, a tone, and a red X on the kill
        var specs = new (string name, int w, int h, int fps, bool audio, double dur, double[] kills)[]
        {
            ("Kill 1", 1920, 1080, 60, true, 10, new[] { 6.5 }),
            ("2 Kills 2", 2560, 1080, 60, true, 11, new[] { 5.5, 7.0 }),
            ("Kill 3", 1280, 720, 30, false, 9, new[] { 6.0 }),
        };
        var src = new List<MontageCut.Source>();
        foreach (var s in specs)
        {
            var f = Path.Combine(dir, s.name + ".mp4");
            if (!File.Exists(f))
            {
                var x = string.Join(",", s.kills.Select(k => $"drawtext=text='X':fontsize={s.h / 6}:fontcolor=red:x=(w-tw)/2:y=(h-th)/2:enable='between(t,{N(k)},{N(k + 0.4)})'"));
                Ff($"-y -f lavfi -i testsrc2=s={s.w}x{s.h}:r={s.fps}:d={N(s.dur)} " + (s.audio ? $"-f lavfi -i sine=f=330:d={N(s.dur)}:beep_factor=4 " : "")
                   + $"-vf \"{x}\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p " + (s.audio ? "-c:a aac -shortest " : "") + $"\"{f}\"");
            }
            src.Add(new MontageCut.Source(f, s.dur, s.audio, s.w, s.h, s.kills, s.kills.Length));
        }
        var plan = MontageCut.Make(src, plain ? new MontageCut.Options(After: 2, Intro: false, Slow: 1) : new MontageCut.Options(After: 2));
        var chips = new Dictionary<int, string>(); var tags = new Dictionary<int, string>();
        string? intro = null, outro = null, shade = null;
        if (art != null)
        {
            string A(string n) => Path.Combine(art, n);
            if (File.Exists(A("intro.png"))) intro = A("intro.png");
            if (File.Exists(A("outro.png"))) outro = A("outro.png");
            if (File.Exists(A("shade.png"))) shade = A("shade.png");
            for (int k = 1; k <= plan.Kills; k++)
            {
                if (File.Exists(A($"chip{k}.png"))) chips[k] = A($"chip{k}.png");
                if (File.Exists(A($"tag{k}.png"))) tags[k] = A($"tag{k}.png");
            }
        }
        var graphFile = Path.Combine(dir, plain ? "graph-plain.txt" : "graph.txt"); var target = Path.Combine(dir, plain ? "montage-plain.mp4" : "montage.mp4");
        var (a, g) = MontageCut.Build(plan, new MontageCut.Art(intro, outro, chips, tags, shade), music, graphFile, target, true);
        File.WriteAllText(graphFile, g);
        File.WriteAllText(Path.Combine(dir, plain ? "args-plain.txt" : "args.txt"), a);
        Console.WriteLine($"{plan.Parts.Count} clips, {plan.Kills} kills, {N(plan.Total)} s, {plan.W}x{plan.H}");
        foreach (var p in plan.Parts) Console.WriteLine($"  {Path.GetFileName(p.Src.File)}: {N(p.A)}..{N(p.D)} at {N(p.At)} len {N(p.Len)} slow {N(p.SlowFrom)}..{N(p.SlowTo)} kills " + string.Join(" ", p.Kills.Select(k => $"#{k.Number}@{N(k.T)}")));
        var sw = Stopwatch.StartNew();
        Ff(a.Replace("-progress pipe:1 -nostats ", ""));
        Console.WriteLine($"rendered in {sw.Elapsed.TotalSeconds:0.0} s -> {target}");
        return 0;
    }
}
