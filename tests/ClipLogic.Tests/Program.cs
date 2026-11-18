using System.Diagnostics;
using System.Globalization;
using OrbitTweak.Core;

// Checks of the clipper's pure logic. Exit code 0 = all fine.
//   dotnet run --project tests/ClipLogic.Tests
//   dotnet run --project tests/ClipLogic.Tests -- --render <dir> [--art <dir>] [--music <file>] [--plain]
//     cuts a montage from made-up clips (ffmpeg on PATH); --art takes intro.png, outro.png, shade.png, chip<n>.png, tag<n>.png from there;
//     --plain: no intro/outro and no slow motion; --music: its beat is looked for and the kills are put on it

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
        ("You killed Young_Bob", "KILLED"), ("KILLED Bob", "KILLED"), ("KILLED Kyle", "KILLED"), ("KILLED Lou", "KILLED"),
        ("HEADSHOT KILLED Max", "KILLED"), ("ELIMINATED Bob", "ELIMINATED"), ("KILLED 8Y Max", "DEATH"), ("K1LLED BY Max", "DEATH"),
        ("KILLER", null), ("Top Killer: Max", null), ("Skilled_Sniper", null), ("SKILL", null), ("FILLED", null), ("BILLED", null),
        ("KILLS 12", null), ("Overkill", null),
        ("KILLED BY Max", "DEATH"), ("You have been eliminated", "DEATH"), ("You have been killed", "DEATH"), ("Max killed you", "DEATH"),
        ("Max killed you!", "DEATH"), ("WASTED", "DEATH"), ("Du wurdest getötet", "DEATH"),
        // a death phrase counts only where a word ends after it, and never inside the name after a kill word
        ("ELIMINATED Byron", "ELIMINATED"), ("GETÖTET Vonda", "GETOTET"), ("KILLED Wasted_Max", "KILLED"), ("KILLED Wasted Max", "KILLED"),
        ("KILLED Max_Wasted", "KILLED"), ("KILLED Youssef", "KILLED"), ("ERLEDIGT Vongola", "ERLEDIGT"), ("ELIMINATEO Byron", "ELIMINATED"),
        ("KILLED Bylo", "KILLED"), ("ELIMINATED BY Byron", "DEATH"), ("GETÖTET VON Vonda", "DEATH"), ("KILLEDBY Max", "DEATH"),
        ("HEADSHOT! KILLED BY Max", "DEATH"), ("KILLCAM You died", "DEATH"), ("You were killed by Wasted_Max", "DEATH"),
        ("Max hat dich getötet", "DEATH"), ("Du hast Max getötet", "GETOTET"),
    })
        Check(K(line) == want, $"\"{line}\" is {want ?? "nothing"} (got {K(line) ?? "nothing"})");
    // a word of the user's own that starts like a look-alike still works
    Check(KillText.Classify(new[] { "KILLERSTREAK 3" }, "KILLERSTREAK").kind == KillText.Kind.Kill, "own word KILLERSTREAK counts");
}

// ---------- a death message is reported once ----------
{
    var k = new KillMessages(); int deaths = 0, kills = 0;
    k.Read(T(0), false, false);
    // a kill, then "KILLED BY …" on screen for 2 s, then again after a respawn
    for (double s = 0.2; s < 12; s += 0.2)
    {
        bool kill = s >= 1 && s < 2, death = (s >= 3 && s < 5) || (s >= 10 && s < 11);
        if (k.Read(T(s), kill, death, kill ? "KILLED Kronos77" : death ? "KILLED BY Max" : "", kill ? "KILLED" : "")) kills++;
        if (k.NewDeath) deaths++;
    }
    Check(kills == 1 && deaths == 2, $"one kill, two deaths, each once ({kills}, {deaths})");
}

// ---------- three ways of reading in turns ----------
{
    var r = new ReadTurns();
    var ways = Enumerable.Range(0, 6).Select(_ => r.Next(false)).ToList();
    Check(ways.Select(x => x.first).SequenceEqual(new[] { 0, 1, 2, 0, 1, 2 }) && ways.All(x => x.second < 0), "one way per reading, in turns");
    r.Found(2);
    var (a, b) = r.Next(true);
    Check(a == 2 && b >= 0 && b != 2, $"with a message on screen the way that found it goes first, then another ({a}, {b})");
}

// ---------- masks for white and coloured kill text ----------
{
    // a strip with red text, white text, a pale sky, a blue sky, grey road and dark scenery (BGRA, one pixel each)
    var px = new byte[] { 50, 40, 235, 255,   245, 245, 245, 255,   225, 205, 175, 255,   200, 120, 60, 255,   128, 128, 128, 255,   30, 40, 35, 255,   0, 215, 255, 255 };
    var white = KillText.WhiteOnly(px); var bright = KillText.BrightOnly(px);
    byte W(int i) => white[i * 4];
    byte B(int i) => bright[i * 4];
    Check(W(0) == 255 && B(0) < 40, $"red text: dropped by the white look, kept by the bright one ({W(0)}, {B(0)})");
    Check(B(6) < 40, $"yellow text kept by the bright look ({B(6)})");
    Check(W(1) < 40 && B(1) < 40, "white text kept by both");
    Check(B(2) > 200 && B(4) == 255 && B(5) == 255, $"pale sky, grey road and dark scenery dropped ({B(2)}, {B(4)}, {B(5)})");
    // the share of text pixels in a picture: 2 of 7 pixels are no text in any way; the blue sky (3) counts as coloured
    double share = KillText.TextShare(px, 7, 1, 1);
    Check(Math.Abs(share - 4 / 7.0) < 1e-9, $"share of bright text pixels ({share:0.000})");
}

// ---------- which pictures of a fight the AI gets ----------
{
    // 100 crosshair pictures (40 s), a little bright noise, a kill message on four of them (1.6 s)
    var share = Enumerable.Range(0, 100).Select(i => 0.010 + 0.0004 * ((i * 7) % 5) + (i >= 40 && i <= 43 ? 0.04 : 0)).ToList();
    var even = Enumerable.Range(0, 14).Select(i => (int)Math.Round(i * 99.0 / 13)).ToList();
    var pick = KillLogic.PickFrames(share, 14);
    Check(!even.Any(i => i >= 40 && i <= 43), "spread evenly, the message is missed (the old way)");
    Check(pick.Any(i => i >= 40 && i <= 43) && pick.Contains(40), $"the message and its first picture are among the chosen ones ({string.Join(",", pick)})");
    Check(pick.Count == 14 && pick.SequenceEqual(pick.OrderBy(x => x).Distinct()) && pick[0] == 0 && pick[^1] == 99, "14 pictures in time order, first and last included");
    int maxGap = pick.Zip(pick.Skip(1), (x, y) => y - x).Max();
    Check(maxGap <= 20, $"no long stretch without a picture (longest gap {maxGap})");
    Check(KillLogic.PickFrames(share.Take(10).ToList(), 14).Count == 10, "short fights: every picture");
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
    // zoompan ends a stream with a time in the wrong time base: without a trim after fps the montage never finished
    Check(System.Text.RegularExpressions.Regex.Matches(graph, @"fps=60,settb=1/60,trim=duration=").Count == p.Parts.Count + 2, "every clip, intro and outro end where planned");
    Check(!graph.Contains("NaN") && !graph.Contains("Infinity") && !graph.Contains("∞"), "no broken numbers in the graph");
    // 4K and vertical clips: still 1080p (even sizes)
    var big = MontageCut.Make(new List<MontageCut.Source> { new("4k.mp4", 20, true, 3840, 2160, new[] { 15.0 }), new("v.mp4", 20, true, 1080, 1920, new[] { 15.0 }) }, new MontageCut.Options());
    Check(big.W == 1920 && big.H == 1080, $"4K and vertical clips make a 1080p montage ({big.W}x{big.H})");
    Check(MontageCut.Size(new List<MontageCut.Source> { new("s.mp4", 20, true, 1366, 768, new[] { 15.0 }) }) == (1280, 720), "small clips: 720p");
    // a triple kill right at the end of its clip: the last picture is held, so the tag is shown (it had no time at all)
    var end = MontageCut.Make(new List<MontageCut.Source> { src[0], new("end.mp4", 8, true, 1920, 1080, new[] { 5.5, 6.6, 7.85 }, 3) }, new MontageCut.Options(After: 5));
    var e = end.Parts[1]; var ek = e.Kills[^1];
    Check(ek.Streak == 3 && e.Hold > 0 && e.Len - ek.T >= 0.8 - 1e-9, $"a kill at the very end gets a moment after it (hold {e.Hold:0.###}, {e.Len - ek.T:0.###} s after it)");
    var (_, eg) = MontageCut.Build(end, new MontageCut.Art(null, null, new Dictionary<int, string>(), new Dictionary<int, string> { [ek.Number] = "t.png" }), null, "g.txt", "out.mp4", true);
    var tm = System.Text.RegularExpressions.Regex.Match(eg, @"\[s1_\d+\]overlay=.*?enable='between\(t,([\d.]+),([\d.]+)\)'");
    Check(tm.Success && double.Parse(tm.Groups[2].Value, CultureInfo.InvariantCulture) - double.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture) >= 0.6 - 1e-9
          && double.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture) >= ek.T, $"the TRIPLE KILL tag at the end of a clip is shown ({tm.Groups[1].Value}..{tm.Groups[2].Value})");
    Check(eg.Contains($"stop_duration={(0.5 + e.Hold).ToString("0.####", CultureInfo.InvariantCulture)}"), "the held picture is in the graph");
}

// ---------- beat of the music ----------
{
    // made-up click track: quiet hum, then clicks (a short high burst every beat, a lower one on every 4th), a bit of noise
    short[] Clicks(double bpm, double first, double secs, bool offbeats = false, int seed = 1)
    {
        int rate = MontageBeat.Rate; var rnd = new Random(seed); var x = new double[(int)(secs * rate)];
        for (int i = 0; i < x.Length; i++) x[i] = 0.01 * (rnd.NextDouble() * 2 - 1) + 0.02 * Math.Sin(2 * Math.PI * 110 * i / rate);
        void Burst(double t, double amp, double f)
        {
            int s0 = (int)Math.Round(t * rate);
            for (int i = 0; i < rate * 0.04 && s0 + i < x.Length; i++) if (s0 + i >= 0) x[s0 + i] += amp * Math.Exp(-i / (rate * 0.008)) * Math.Sin(2 * Math.PI * f * i / rate);
        }
        double p = 60 / bpm; int k = 0;
        for (double t = first; t < secs; t += p, k++)
        {
            Burst(t, k % 4 == 0 ? 0.7 : 0.5, k % 4 == 0 ? 180 : 2500);
            if (offbeats) Burst(t + p / 2, 0.12, 6000);
        }
        return x.Select(v => (short)Math.Clamp(v * 32767, -32768, 32767)).ToArray();
    }
    double Worst(MontageBeat.Grid g, double bpm, double first, double secs)
    {
        double w = 0, p = 60 / bpm;
        for (double t = first; t < secs - 1; t += p) { double m = g.Start + Math.Round((t - g.Start) / g.Period) * g.Period; w = Math.Max(w, Math.Abs(m - t)); }
        return w;
    }
    var g120 = MontageBeat.Find(Clicks(120, 1.37, 90));
    Check(g120 != null && Math.Abs(g120.Bpm - 120) < 0.2, $"click track: 120 BPM ({g120?.Bpm:0.00})");
    Check(g120 != null && Math.Abs(g120.Start - 1.37) < 0.015, $"the music starts on the first click ({g120?.Start:0.000} s)");
    Check(g120 != null && Worst(g120, 120, 1.37, 90) < 0.015, $"every beat of 90 s within 15 ms ({(g120 == null ? -1 : Worst(g120, 120, 1.37, 90) * 1000):0.0} ms)");
    var g95 = MontageBeat.Find(Clicks(95, 3.1, 120, true, 2));
    Check(g95 != null && (Math.Abs(g95.Bpm - 95) < 0.2 || Math.Abs(g95.Bpm - 190) < 0.4), $"95 BPM with hats in between ({g95?.Bpm:0.00})");
    Check(g95 != null && Math.Abs(g95.Start - 3.1) < 0.015 && Worst(g95, 95, 3.1, 120) < 0.015, $"95 BPM: start and beats on the clicks ({g95?.Start:0.000} s, {(g95 == null ? -1 : Worst(g95, 95, 3.1, 120) * 1000):0.0} ms)");
    var g150 = MontageBeat.Find(Clicks(150, 0.2, 60, false, 3));
    Check(g150 != null && Math.Abs(g150.Bpm - 150) < 0.3 && Worst(g150, 150, 0.2, 60) < 0.015, $"150 BPM ({g150?.Bpm:0.00})");
    var noise = new Random(4); var hiss = Enumerable.Range(0, MontageBeat.Rate * 30).Select(_ => (short)noise.Next(-3000, 3000)).ToArray();
    Check(MontageBeat.Find(hiss) == null, "noise has no beat");
    Check(MontageBeat.Find(new short[MontageBeat.Rate * 20]) == null && MontageBeat.Find(new short[100]) == null, "silence and nothing have no beat");

    // the plan on the beat: every clip's (last) kill lands on a beat, the lead-in stays within limits
    var src = new List<MontageCut.Source>
    {
        new("a.mp4", 25, true, 1920, 1080, new[] { 20.0 }),
        new("b.mp4", 25, true, 2560, 1080, new[] { 17.5, 19.0 }, 2),
        new("c.mp4", 25, false, 1280, 720, Array.Empty<double>()),
        new("d.mp4", 4, true, 1920, 1080, new[] { 3.6 }),
        new("e.mp4", 25, true, 1920, 1080, new[] { 1.4 }),
        new("f.mp4", 9, true, 1920, 1080, new[] { 8.8 }),
    };
    foreach (var (bpm, slow) in new[] { (120.0, 2.0), (87.0, 2.0), (174.0, 1.0) })
    {
        var beat = new MontageBeat.Grid(bpm, 60 / bpm, 0.37, 300);
        var plain = MontageCut.Make(src, new MontageCut.Options(After: 5, Slow: slow));
        var p = MontageCut.Make(src, new MontageCut.Options(After: 5, Slow: slow), beat);
        double worst = 0, moved = 0; bool inside = true;
        for (int i = 0; i < p.Parts.Count; i++)
        {
            var c = p.Parts[i]; double k = c.At + c.Kills[^1].T, off = k / beat.Period - Math.Round(k / beat.Period);
            worst = Math.Max(worst, Math.Abs(off) * beat.Period);
            moved = Math.Max(moved, Math.Abs(c.A - plain.Parts[i].A));
            inside &= c.A >= 0 && c.A <= c.Src.Kills.DefaultIfEmpty(c.Src.Dur).Max() && c.D - c.A >= 1.99;
        }
        Check(worst < 0.004, $"{bpm} BPM, slow {slow}: every kill on a beat (worst {worst * 1000:0.0} ms off)");
        Check(moved <= 1.0 + 1e-9 && inside, $"{bpm} BPM: lead-ins moved at most 1 s ({moved:0.00} s) and inside the clips");
        Check(p.MusicFrom == 0.37 && p.Kills == plain.Kills, "music from the first hit, same kills");
        double sum = MontageCut.IntroLen + p.Parts.Sum(x => x.Len) + MontageCut.OutroLen - MontageCut.TIn - MontageCut.TOut - MontageCut.TClip * (p.Parts.Count - 1);
        Check(Math.Abs(p.Total - sum) < 1e-9, "total length on the beat");
    }
    // music of 8 s: the clips after it keep their usual lead-in
    var shortBeat = new MontageBeat.Grid(120, 0.5, 0.37, 8);
    var short1 = MontageCut.Make(src, new MontageCut.Options(After: 5), shortBeat); var usual = MontageCut.Make(src, new MontageCut.Options(After: 5));
    var late = short1.Parts.Select((c, i) => (c, i)).Where(x => x.c.At + x.c.Kills[^1].T > shortBeat.Span + 1.2).ToList();
    Check(late.Count >= 3 && late.All(x => x.c.A == usual.Parts[x.i].A), $"no beats past the end of the music ({late.Count} clips after it)");
    var (_, bg) = MontageCut.Build(MontageCut.Make(src, new MontageCut.Options(After: 5), new MontageBeat.Grid(120, 0.5, 0.37, 300)), new MontageCut.Art(null, null, new Dictionary<int, string>(), new Dictionary<int, string>()), "m.mp3", "g.txt", "out.mp4", true);
    Check(bg.Contains("atrim=start=0.37:duration=") && bg.Contains("afade=t=in:d=0.02"), "the music starts on its first hit");
    // without slow motion the time of the first picture after the seek is kept as well (else the kill came a frame early)
    var (_, pg) = MontageCut.Build(MontageCut.Make(src, new MontageCut.Options(After: 5, Intro: false, Slow: 1), new MontageBeat.Grid(120, 0.5, 0.37, 300)), new MontageCut.Art(null, null, new Dictionary<int, string>(), new Dictionary<int, string>()), "m.mp3", "g.txt", "out.mp4", true);
    Check(System.Text.RegularExpressions.Regex.Matches(pg, @"setpts='PTS',framerate=").Count == src.Count && !pg.Contains("setpts='PTS-STARTPTS'"), "no slow motion: the clips keep their picture times");
}

// ---------- tweaks: per-app graphics fields ----------
{
    // only the own field changes; Windows' other fields (Auto HDR, windowed optimizations) stay as they were
    const string pre = "SwapEffectUpgradeEnable=1;AutoHDREnable=1;";
    Check(TweakLogic.WithField(pre, "GpuPreference", "2") == "SwapEffectUpgradeEnable=1;AutoHDREnable=1;GpuPreference=2;", "GPU field is added next to the others");
    Check(TweakLogic.WithField("GpuPreference=1;AutoHDREnable=1;", "GpuPreference", "2") == "GpuPreference=2;AutoHDREnable=1;", "GPU field is changed in place");
    Check(TweakLogic.WithField("GpuPreference=2;AutoHDREnable=1;", "GpuPreference", null) == "AutoHDREnable=1;", "removing the GPU field keeps Auto HDR");
    Check(TweakLogic.WithField("GpuPreference=2;", "GpuPreference", null) == null, "nothing left: the value goes");
    Check(TweakLogic.WithField(null, "SwapEffectUpgradeEnable", "1") == "SwapEffectUpgradeEnable=1;", "field on an exe without a value");
    Check(TweakLogic.Field("GpuPreference=2;SwapEffectUpgradeEnable=0;", "SwapEffectUpgradeEnable") == "0", "a field is read among others");
    Check(TweakLogic.Field("AutoHDREnable=1;", "GpuPreference") == null, "a missing field is null");
    Check(TweakLogic.Field("gpupreference=2", "GpuPreference") == "2", "field names ignore case, the last ; may be missing");
    // set then restore with the backup gives back exactly the old string (except the order of the own field)
    var before = "AutoHDREnable=1;GpuPreference=1;";
    var saved = TweakLogic.Field(before, "GpuPreference");
    var after = TweakLogic.WithField(TweakLogic.WithField(before, "GpuPreference", "2"), "GpuPreference", saved);
    Check(after == before, "apply and revert of the GPU field round-trip");
}

// ---------- tweaks: accessibility shortcut bits ----------
{
    // STICKYKEYS default 510 (hotkey on), the old tweak wrote 506; a user with sticky keys on has 511
    Check(TweakLogic.WithoutHotkey(510) == 506, "only the shortcut bit goes (sticky keys 510 → 506)");
    Check(TweakLogic.WithoutHotkey(62) == 58, "only the shortcut bit goes (toggle keys 62 → 58)");
    // revert: the shortcut bits come back, a change the user made to the feature meanwhile stays
    Check(TweakLogic.RestoreHotkey(506, 510) == 510, "shortcut back from the backup");
    Check(TweakLogic.RestoreHotkey(507, 510) == 511, "the feature switched on meanwhile stays on");
    Check(TweakLogic.RestoreHotkey(506 & ~8u, 510) == 510, "the confirm bit comes back too");
}

// ---------- tweaks: page file ----------
{
    Check(TweakLogic.PagefileOk(new[] { @"?:\pagefile.sys" }, 16384), "Windows-managed for all drives is fine");
    Check(TweakLogic.PagefileOk(new[] { @"C:\pagefile.sys 0 0" }, 16384), "system-managed size on C: is fine");
    Check(!TweakLogic.PagefileOk(Array.Empty<string>(), 16384), "no page file at all is not");
    Check(!TweakLogic.PagefileOk(new[] { "" }, 16384), "an empty entry is not");
    Check(!TweakLogic.PagefileOk(null, 16384), "a missing value is not");
    Check(!TweakLogic.PagefileOk(new[] { @"C:\pagefile.sys 1024 2048" }, 16384), "16 GB RAM with a 2 GB page file is too small");
    Check(TweakLogic.PagefileOk(new[] { @"C:\pagefile.sys 8192 8192" }, 32768), "a fixed 8 GB page file on a 32 GB PC is kept");
    Check(TweakLogic.PagefileOk(new[] { @"C:\pagefile.sys 4096 8192", @"D:\pagefile.sys 4096 8192" }, 16384), "two page files count together");
    // a real 16 GB PC: Windows reports about 16 250 MB usable, the installed RAM counts
    Check(TweakLogic.InstalledRamMb(16 * 1024 * 1024, 16250) == 16384, "the installed RAM comes from the firmware");
    Check(TweakLogic.InstalledRamMb(0, 16250) == 16384, "without it the usable RAM is rounded to whole GB");
    Check(TweakLogic.InstalledRamMb(0, 7900) == 8192, "an 8 GB PC is 8 GB");
    Check(TweakLogic.PagefileOk(new[] { @"C:\pagefile.sys 16384 16384" }, TweakLogic.InstalledRamMb(0, 16250)), "a 16 GB PC with a fixed 16 GB page file is fine");
    Check(TweakLogic.PagefileOk(new[] { @"C:\pagefile.sys 16384 16384" }, TweakLogic.InstalledRamMb(16 * 1024 * 1024, 16250)), "the same with the firmware's figure");
    Check(!TweakLogic.PagefileOk(new[] { @"C:\pagefile.sys 1024 2048" }, TweakLogic.InstalledRamMb(0, 16250)), "a 16 GB PC with a 2 GB page file is not");
}

// ---------- tweaks: the short line of every row, and the browser preview's copy of the list ----------
TweakTexts.Run(Check);

// ---------- tweaks: backups and CPU detection ----------
{
    Check(TweakLogic.ShouldBackup("1", "0"), "a value that differs from the target is saved");
    Check(!TweakLogic.ShouldBackup("0", "0"), "a value that already is the target keeps the old backup");
    Check(TweakLogic.ShouldBackup(null, "0"), "a missing value is saved as missing");
    Check(TweakLogic.Ints3("6,10,1") is [6, 10, 1], "mouse values are read back");
    Check(TweakLogic.Ints3("6,10") == null && TweakLogic.Ints3("a,b,c") == null && TweakLogic.Ints3(null) == null, "broken mouse backups are ignored");
    Check(TweakLogic.IsDualCcdX3D("AMD Ryzen 9 7950X3D 16-Core Processor"), "7950X3D has two chiplets");
    Check(TweakLogic.IsDualCcdX3D("AMD Ryzen 9 7945HX3D with Radeon Graphics"), "7945HX3D has two chiplets");
    Check(TweakLogic.IsDualCcdX3D("AMD Ryzen 9 9900X3D 12-Core Processor"), "9900X3D has two chiplets");
    Check(!TweakLogic.IsDualCcdX3D("AMD Ryzen 7 7800X3D 8-Core Processor"), "7800X3D has one chiplet");
    Check(!TweakLogic.IsDualCcdX3D("AMD Ryzen 9 7950X 16-Core Processor"), "7950X has no 3D cache");
    Check(!TweakLogic.IsDualCcdX3D("13th Gen Intel(R) Core(TM) i7-13700K"), "Intel is not X3D");
}

// ---------- tweaks: communication ducking (UserDuckingPreference) ----------
{
    Check(TweakLogic.DuckingMode(null) == TweakLogic.DUCK_80, "no value is Windows' default: other sounds 80 % quieter");
    Check(TweakLogic.DuckingMode(0) == 0 && TweakLogic.DuckingMode(1) == 1 && TweakLogic.DuckingMode(2) == 2 && TweakLogic.DuckingMode(3) == 3, "the four modes are read as they are");
    Check(TweakLogic.DuckingMode(7) == TweakLogic.DUCK_80 && TweakLogic.DuckingMode(-1) == TweakLogic.DUCK_80, "a value outside 0 to 3 is the default");
    Check(TweakLogic.DuckingMode("3") == TweakLogic.DUCK_80, "a text value is not the DWORD Windows reads: default");
    Check(TweakLogic.DuckingMode(3) == TweakLogic.DUCK_NONE && TweakLogic.DuckingMode(null) != TweakLogic.DUCK_NONE, "only a DWORD 3 is the tweak's state");
    Check(TweakLogic.DuckingHint(TweakLogic.DUCK_MUTE)?.Contains("stumm") == true && TweakLogic.DuckingHint(TweakLogic.DUCK_50)?.Contains("50 %") == true,
        "a choice of the user's own (mute, 50 %) is named before the tweak replaces it");
    Check(TweakLogic.DuckingHint(TweakLogic.DUCK_80) == null && TweakLogic.DuckingHint(TweakLogic.DUCK_NONE) == null, "no note for the default or for the tweak's own state");
    // the backup: the value as it was, "-" (missing) when there was none, nothing new when it already is 3
    Check(TweakLogic.ShouldBackup(null, "3") && TweakLogic.ShouldBackup("0", "3") && !TweakLogic.ShouldBackup("3", "3"), "the value before the tweak is saved, also a missing one");
}

// ---------- diagnose: score and memory integrity ----------
{
    Check(HealthLogic.Score(new[] { "ok", "ok", "warn" }) == 83 && HealthLogic.Score(new[] { "ok", "warn", "bad" }) == 50, "ok 100, warn 50, bad 0, averaged and rounded");
    Check(HealthLogic.Score(new[] { "ok", "warn", "info" }) == HealthLogic.Score(new[] { "ok", "warn" }), "an info row does not change the score");
    Check(HealthLogic.Score(new[] { "ok", "info" }) == 100, "an info row is no fault");
    Check(HealthLogic.Score(new[] { "info" }) == 100 && HealthLogic.Score(Array.Empty<string>()) == 100, "no rated row is 100");
    Check(HealthLogic.Score(new[] { "ok", "ok", "ok", "ok", "ok", "warn", "warn", "bad", "info" }) == 75, "the browser preview's rows give its 75");

    const string on = "running=2\r\nconfigured=2\r\nprops=1,2,3,5,7\r\n";
    Check(HealthLogic.ParseHvci(on) == HealthLogic.Hvci.On, "SecurityServicesRunning with 2: memory integrity runs");
    Check(HealthLogic.ParseHvci("running=1,2,3\nconfigured=1,2\nprops=1,2") == HealthLogic.Hvci.On, "2 among other services counts");
    Check(HealthLogic.ParseHvci("running=0\nconfigured=2\nprops=1,2,3") == HealthLogic.Hvci.Pending, "switched on but not running yet: after a restart");
    Check(HealthLogic.ParseHvci("running=0\nconfigured=0\nprops=1,2,3,5") == HealthLogic.Hvci.Off, "neither running nor switched on: off");
    Check(HealthLogic.ParseHvci("running=0\nconfigured=0\nprops=") == HealthLogic.Hvci.Off, "no properties listed: plain off, not \"not available\"");
    Check(HealthLogic.ParseHvci("running=0\nconfigured=0\nprops=0") == HealthLogic.Hvci.Unsupported, "no hypervisor support: not available");
    Check(HealthLogic.ParseHvci("running=0\nconfigured=2\nprops=2,5") == HealthLogic.Hvci.Unsupported, "switched on without hypervisor support still cannot run");
    Check(HealthLogic.ParseHvci("running=3\nconfigured=3\nprops=1,2") == HealthLogic.Hvci.Off, "Secure Launch alone is not memory integrity");
    Check(HealthLogic.ParseHvci("running=12\nconfigured=12\nprops=1") == HealthLogic.Hvci.Off, "a 12 is not a 2");
    Check(HealthLogic.ParseHvci(null) == HealthLogic.Hvci.Unknown && HealthLogic.ParseHvci("") == HealthLogic.Hvci.Unknown, "no output: unknown");
    Check(HealthLogic.ParseHvci("Timeout") == HealthLogic.Hvci.Unknown, "a timeout: unknown");
    Check(HealthLogic.ParseHvci("Get-CimInstance : Ungültige Klasse\r\n+ CategoryInfo : ObjectNotFound") == HealthLogic.Hvci.Unknown, "a PowerShell error: unknown");
    Check(HealthLogic.ParseHvci("running=\nconfigured=\nprops=") == HealthLogic.Hvci.Unknown, "no instance (empty fields): unknown");
    Check(HealthLogic.ParseHvci("running=x\nconfigured=2") == HealthLogic.Hvci.Unknown, "a field that is not a list of numbers: unknown");

    var row = HealthLogic.HvciRow(HealthLogic.Hvci.On);
    Check(row is { st: "info", fix: "coreisolation" } r && r.value.StartsWith("An") && r.value.Contains("5 %") && r.value.Contains("10 %"),
        "on: a note with the trade-off and the way to Windows Security, not a fault");
    foreach (var h in new[] { HealthLogic.Hvci.On, HealthLogic.Hvci.Pending, HealthLogic.Hvci.Off, HealthLogic.Hvci.Unsupported })
        Check(HealthLogic.HvciRow(h) is { st: "info" or "ok" }, $"{h}: never warn or bad");
    Check(HealthLogic.HvciRow(HealthLogic.Hvci.Off) is ("Aus", "ok", null), "off: \"Aus\", fine, no button");
    Check(HealthLogic.HvciRow(HealthLogic.Hvci.Unsupported) is ("Nicht verfügbar", "ok", null), "not possible on this PC: \"Nicht verfügbar\", fine");
    Check(HealthLogic.HvciRow(HealthLogic.Hvci.Pending) is { st: "info", fix: "coreisolation" } p && p.value.StartsWith("Eingeschaltet, läuft aber nicht")
        && p.value.Contains("meist fehlt ein Neustart") && p.value.Contains("Hypervisor") && p.value.Contains("5 %"),
        "switched on, not running: says only what WMI knows (usually a restart, else e.g. the hypervisor is off), the same note and way to Windows Security");
    Check(HealthLogic.HvciRow(HealthLogic.Hvci.Pending) is { } pw && !pw.value.Contains("wartet"), "pending does not claim it only waits for a restart");
    Check(HealthLogic.HvciRow(HealthLogic.Hvci.Unknown) == null, "unknown: the row is left out");
    Check(HealthLogic.HvciQuery.Contains("root\\Microsoft\\Windows\\DeviceGuard") && HealthLogic.HvciQuery.Contains("Win32_DeviceGuard") && !HealthLogic.HvciQuery.Contains('"'),
        "the query reads Win32_DeviceGuard and has no double quote that would end the -Command argument");
}

// ---------- graphics: the settings file FiveM really reads ----------
{
    DateTime U(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);
    // FiveM's IsNewInstall(): crashes folder created after 2020-06-03 07:30:25 UTC, or the marker file
    Check(GfxFile.IsNewInstall(false, U(2026, 7, 1)) == true, "a crashes folder from 2026 is a new install");
    Check(GfxFile.IsNewInstall(false, U(2019, 3, 1)) == false, "a crashes folder from 2019 is an old install");
    Check(GfxFile.IsNewInstall(false, U(2020, 6, 3, 7, 30, 25)) == false && GfxFile.IsNewInstall(false, U(2020, 6, 3, 7, 30, 26)) == true,
        "the cutoff is FiveM's own moment, and only later counts as new");
    Check(DateTime.FromFileTimeUtc(GfxFile.NewInstallCutoff) == U(2020, 6, 3, 7, 30, 25), "the cutoff FILETIME is 2020-06-03 07:30:25 UTC");
    Check(GfxFile.IsNewInstall(true, U(2019, 3, 1)) == true && GfxFile.IsNewInstall(true, null) == true, "the marker file always means new");
    Check(GfxFile.IsNewInstall(false, null), "no crashes folder: FiveM makes it on its next start, so that start counts as new");
    Check(GfxFile.Pick(GfxFile.IsNewInstall(false, null), false, true) == GfxFile.FiveMKind,
        "FiveM.app without crashes folder and only the old settings.xml: FiveM's file (FiveM makes it from that one on start)");

    // which file
    Check(GfxFile.Pick(true, true, true) == GfxFile.FiveMKind, "new install with both files: FiveM's gta5_settings.xml");
    Check(GfxFile.Pick(true, false, true) == GfxFile.FiveMKind, "new install that has not started yet still uses FiveM's file (it is made from the old one)");
    Check(GfxFile.Pick(false, true, true) == GfxFile.GtaKind, "old install keeps settings.xml (AppData\\Local for FiveM), even with a stray gta5_settings.xml");
    Check(GfxFile.Pick(false, false, true) == GfxFile.GtaKind && GfxFile.Pick(false, false, false) == GfxFile.GtaKind, "old install: settings.xml");
    Check(GfxFile.Pick(null, true, true) == GfxFile.FiveMKind && GfxFile.Pick(null, true, false) == GfxFile.FiveMKind, "unknown install: FiveM's file when it exists");
    Check(GfxFile.Pick(null, false, true) == GfxFile.GtaKind, "unknown install with only the old settings.xml: that one");
    Check(GfxFile.Pick(null, false, false) == GfxFile.FiveMKind, "no file at all: FiveM's file, it appears after the first start");

    // backups sit next to their own file
    var fx = Path.Combine("C:", "Users", "Max", "AppData", "Roaming", "CitizenFX", "gta5_settings.xml");
    var gta = Path.Combine("C:", "Users", "Max", "AppData", "Local", "Rockstar Games", "GTA V", "settings.xml");
    Check(Path.GetDirectoryName(GfxFile.BackupOf(fx)) == Path.GetDirectoryName(fx) && Path.GetDirectoryName(GfxFile.BackupOf(gta)) == Path.GetDirectoryName(gta),
        "each backup lies next to its file");
    Check(GfxFile.BackupOf(fx) != GfxFile.BackupOf(gta) && GfxFile.BackupOf(gta).EndsWith("settings.xml.orbit-backup"), "two files, two backups; the old backup name stays");
}

// ---------- graphics: FPS limit in the NVIDIA driver profile ----------
{
    // NVAPI structs as nvapi.h lays them out (MAKE_NVAPI_VERSION = size | version << 16)
    Check(FpsLimitLogic.SettingSize == 12320 && FpsLimitLogic.SettingVer == 0x13020u, "NVDRS_SETTING_V1 is 12320 bytes, version 0x13020");
    Check(FpsLimitLogic.SetId == 4100 && FpsLimitLogic.SetLocation == 4108 && FpsLimitLogic.SetIsCurPredefined == 4112 && FpsLimitLogic.SetCurrent == 8220,
        "NVDRS_SETTING_V1: settingId after the name, the current value after the 4100-byte predefined union");
    Check(FpsLimitLogic.AppSize == 20492 && FpsLimitLogic.AppVer == 0x4500Cu && FpsLimitLogic.AppFriendly == 4104, "NVDRS_APPLICATION_V4 is 20492 bytes, version 4");
    Check(FpsLimitLogic.ProfileSize == 4116 && FpsLimitLogic.ProfileVer == 0x11014u, "NVDRS_PROFILE_V1 is 4116 bytes, version 1");
    Check(FpsLimitLogic.ProfNumOfApps == 4108, "NVDRS_PROFILE_V1.numOfApps after version, the name, gpuSupport and isPredefined");
    Check(FpsLimitLogic.IdEnumApplications == 0x7FA2173Au && FpsLimitLogic.EndEnumeration == -7, "NvAPI_DRS_EnumApplications and NVAPI_END_ENUMERATION as nvapi_interface.h and nvapi_lite_common.h give them");
    Check(FpsLimitLogic.FrlFpsId == 0x10835002u, "the limit is FRL_FPS, the setting behind the Control Panel's Max Frame Rate");

    // values
    Check(FpsLimitLogic.Valid(0) && FpsLimitLogic.Valid(30) && FpsLimitLogic.Valid(141) && FpsLimitLogic.Valid(500), "off and 30 to 500 FPS are allowed");
    Check(!FpsLimitLogic.Valid(29) && !FpsLimitLogic.Valid(501) && !FpsLimitLogic.Valid(-1) && !FpsLimitLogic.Valid(1), "below 30, above 500 and negative are not");
    Check(FpsLimitLogic.Suggestion(144) == 141 && FpsLimitLogic.Suggestion(60) == 57 && FpsLimitLogic.Suggestion(240) == 237, "suggestion: 3 below the refresh rate");
    Check(FpsLimitLogic.Suggestion(540) == 500 && FpsLimitLogic.Suggestion(0) == 0 && FpsLimitLogic.Suggestion(1) == 0 && FpsLimitLogic.Suggestion(30) == 0,
        "suggestion stays inside 30 to 500, none for an unknown refresh rate");

    // which exes: FiveM's game processes, one name per build
    string Sub(string n) => Path.Combine("C:", "FiveM", "FiveM.app", "data", "cache", "subprocess", n);
    var exes = FpsLimitLogic.GameExes(new[] { Path.Combine("C:", "FiveM", "FiveM.exe"), Sub("FiveM_b3095_GTAProcess.exe"), Sub("FiveM_b2802_GTAProcess.exe"),
        Sub("FiveM_ChromeBrowser.exe"), Sub("FiveM_GTAProcess.exe"), Sub("fivem_b3095_gtaprocess.exe") }, "FiveM_b3258_GTAProcess");
    Check(exes.SequenceEqual(new[] { "FiveM_b2802_GTAProcess.exe", "FiveM_b3095_GTAProcess.exe", "FiveM_b3258_GTAProcess.exe", "FiveM_GTAProcess.exe" }),
        $"game exes: every build once, the running one too, not FiveM.exe or the browser ({string.Join(", ", exes)})");
    Check(FpsLimitLogic.GameExes(Array.Empty<string>(), "FiveM_ChromeBrowser").Count == 0 && FpsLimitLogic.GameExes(Array.Empty<string>(), "FiveM_b3095_GTAProcess.exe").Count == 1,
        "a running process counts only when it is the game, with or without .exe");

    // a new build is the only reason to ask the driver again
    var known = new[] { "FiveM_b3095_GTAProcess.exe" };
    Check(FpsLimitLogic.NeedsSync(known, null), "first look since the start: the profiles are checked once");
    Check(!FpsLimitLogic.NeedsSync(new[] { "fivem_b3095_gtaprocess.exe" }, known), "the same exes: nothing to do (names are case-insensitive)");
    Check(FpsLimitLogic.NeedsSync(new[] { "FiveM_b3095_GTAProcess.exe", "FiveM_b3258_GTAProcess.exe" }, known), "a new build: the profiles are checked");
    Check(!FpsLimitLogic.NeedsSync(Array.Empty<string>(), null) && !FpsLimitLogic.NeedsSync(Array.Empty<string>(), known), "no exe: nothing to check");

    // where the limit goes
    var plan = FpsLimitLogic.Decide(new (string, string?)[] { ("FiveM_b3095_GTAProcess.exe", null), ("FiveM_b2802_GTAProcess.exe", "Grand Theft Auto V"),
        ("FiveM_b3258_GTAProcess.exe", "grand theft auto v"), ("FiveM_b2699_GTAProcess.exe", FpsLimitLogic.OwnProfile), ("FiveM_b3095_GTAProcess.exe", null) });
    Check(plan.AddToOwn.SequenceEqual(new[] { "FiveM_b3095_GTAProcess.exe" }) && plan.Own, "an exe in no profile goes into OrbitTweak's own profile, once");
    Check(plan.Foreign.Count == 1 && plan.Foreign[0] == "Grand Theft Auto V", "an exe of someone else's profile stays there; that profile once");
    var mine = FpsLimitLogic.Decide(new (string, string?)[] { ("FiveM_b3095_GTAProcess.exe", "orbittweak fivem") });
    Check(mine.Own && mine.AddToOwn.Count == 0 && mine.Foreign.Count == 0, "an exe already in the own profile: only the limit is set");
    var theirs = FpsLimitLogic.Decide(new (string, string?)[] { ("FiveM_b3095_GTAProcess.exe", "FiveM") });
    Check(!theirs.Own && theirs.AddToOwn.Count == 0, "all exes in someone else's profile: no own profile is made");
    Check(FpsLimitLogic.FriendlyName("FiveM_b3095_GTAProcess.exe") == "FiveM b3095" && FpsLimitLogic.FriendlyName("FiveM_GTAProcess.exe") == "FiveM", "names in the Control Panel");

    // backup of someone else's profile: its own value, or "-" (delete gives back the predefined or inherited value)
    Check(FpsLimitLogic.Backup(true, 0, false, 100) == "100", "a profile's own limit is kept as it was");
    Check(FpsLimitLogic.Backup(false, 0, false, 0) == "-" && FpsLimitLogic.Backup(true, 1, false, 60) == "-" && FpsLimitLogic.Backup(true, 2, false, 0) == "-",
        "no setting of its own (not found, from the global or base profile): absent");
    Check(FpsLimitLogic.Backup(true, 0, true, 0) == "-", "NVIDIA's predefined value: absent, deleting the setting brings it back");
    Check(FpsLimitLogic.RestoreValue("100") == 100u && FpsLimitLogic.RestoreValue("0") == 0u, "a kept value is set back");
    Check(FpsLimitLogic.RestoreValue("-") == null && FpsLimitLogic.RestoreValue(null) == null && FpsLimitLogic.RestoreValue("-5") == null, "absent: the setting is deleted");
    Check(FpsLimitLogic.ProfileLimit(true, 0, 141) == 141 && FpsLimitLogic.ProfileLimit(true, 1, 141) == 0 && FpsLimitLogic.ProfileLimit(false, 0, 141) == 0,
        "the limit a profile has itself");
    Check(FpsLimitLogic.ShouldRestore(141, 141) && !FpsLimitLogic.ShouldRestore(90, 141) && FpsLimitLogic.ShouldRestore(90, 0),
        "Aus puts a profile back while it still has OrbitTweak's limit, a value the user changed since stays");

    // the entry per profile of someone else: "<backup>|<written>"
    Check(FpsLimitLogic.Entry("-", 141) == "-|141" && FpsLimitLogic.Entry("100", 60) == "100|60", "entry: the backup, then what OrbitTweak wrote");
    Check(FpsLimitLogic.ParseEntry("-|141", 120) == ("-", 141) && FpsLimitLogic.ParseEntry("100|60", 0) == ("100", 60), "entry read back, the stored limit does not count");
    Check(FpsLimitLogic.ParseEntry("100", 120) == ("100", 120) && FpsLimitLogic.ParseEntry("-", 141) == ("-", 141), "an entry without '|' was written with the stored limit");
    Check(FpsLimitLogic.ParseEntry("-|x", 120) == ("-", 120) && FpsLimitLogic.ParseEntry(null, 90) == ("-", 90), "an unreadable written value or no entry: the stored limit");
    foreach (var (b, w) in new[] { ("-", 141), ("100", 30), ("0", 500) })
        Check(FpsLimitLogic.ParseEntry(FpsLimitLogic.Entry(b, w), 7) == (b, w), $"entry round trip {b}|{w}");
    Check(FpsLimitLogic.RestoreValue(FpsLimitLogic.ParseEntry("100|141", 0).backup) == 100u && FpsLimitLogic.RestoreValue(FpsLimitLogic.ParseEntry("-|141", 0).backup) == null,
        "the backup part restores as before");
    // pending: the backup goes in before the driver writes, the written value only once it saved
    Check(FpsLimitLogic.PendingEntry("-") == "-|0" && FpsLimitLogic.PendingEntry("100") == "100|0", "pending entry: the backup, nothing written yet");
    Check(FpsLimitLogic.Written("-|0", 141) == 0 && FpsLimitLogic.Written("100|0", 0) == 0 && FpsLimitLogic.Written(null, 141) == 0,
        "pending entry or none: OrbitTweak wrote nothing there");
    Check(FpsLimitLogic.Written("-|141", 0) == 141 && FpsLimitLogic.Written("-", 120) == 120, "written: from the entry, an old one without '|' the stored limit");
    Check(FpsLimitLogic.CardState("-|0", 141) == (0, true), "card: a pending entry while a limit is kept comes with the next start, not changed outside OrbitTweak");
    Check(FpsLimitLogic.CardState(null, 141) == (0, true) && FpsLimitLogic.CardState(null, 0) == (0, false) && FpsLimitLogic.CardState("-|0", 0) == (0, false),
        "card: no entry while a limit is kept is pending; without a kept limit nothing is");
    Check(FpsLimitLogic.CardState("-|141", 141) == (141, false) && FpsLimitLogic.CardState("-|141", 120) == (141, false),
        "card: a written entry is set, its written value is what the card compares");

    // "Aus" per profile of someone else
    var R = FpsLimitLogic.OffStep.Restore; var D = FpsLimitLogic.OffStep.Drop; var K = FpsLimitLogic.OffStep.Keep;
    Check(FpsLimitLogic.OffDecision(true, 141, "-|141", 0) == R, "still has what OrbitTweak wrote: put back");
    Check(FpsLimitLogic.OffDecision(true, 141, "-|141", 120) == R,
        "the exe left FiveM's cache, the limit changed to 120 elsewhere: the profile still has OrbitTweak's 141 and is put back (not left capped)");
    Check(FpsLimitLogic.OffDecision(true, 100, "-|141", 141) == K, "changed to 100 outside OrbitTweak: stays as the user set it");
    Check(FpsLimitLogic.OffDecision(true, 0, "-|141", 141) == D && FpsLimitLogic.OffDecision(true, 60, "60|141", 141) == D,
        "already back to its value from before: only the entry goes");
    Check(FpsLimitLogic.OffDecision(false, 0, "-|141", 141) == D, "the profile is gone: nothing to put back, the entry goes");
    Check(FpsLimitLogic.OffDecision(true, 141, "-", 141) == R && FpsLimitLogic.OffDecision(true, 100, "-", 141) == K,
        "an entry without '|': OrbitTweak wrote the stored limit");
    Check(FpsLimitLogic.OffDecision(true, 100, "-", 0) == R, "no record of OrbitTweak's limit at all: the backup alone counts, as before");
    // a pending entry (backup taken, the driver never saved): Aus sets the backup, which such a profile still has
    Check(FpsLimitLogic.OffDecision(true, 100, "100|0", 141) == R && FpsLimitLogic.RestoreValue(FpsLimitLogic.ParseEntry("100|0", 141).backup) == 100u,
        "pending entry, profile still at its 100: Aus writes the same 100 (harmless), never reported as changed");
    Check(FpsLimitLogic.OffDecision(true, 0, "-|0", 141) == R && FpsLimitLogic.RestoreValue(FpsLimitLogic.ParseEntry("-|0", 141).backup) == null,
        "pending entry, no limit of its own: Aus deletes a setting the profile does not have (harmless)");
    Check(FpsLimitLogic.OffDecision(false, 0, "-|0", 141) == D, "pending entry of a profile that is gone: only the entry goes");

    // Aus over all profiles: every entry goes, also of a profile it leaves as the user set it
    var off = FpsLimitLogic.OffPlan(new (string, bool, int, string?)[] { ("Grand Theft Auto V", true, 100, "-|141"), ("FiveM", true, 141, "-|141"),
        ("Gone", false, 0, "-|141"), ("Before", true, 60, "60|141"), ("Pending", true, 0, "-|0") }, 141);
    Check(off.steps.Select(x => x.step).SequenceEqual(new[] { K, R, D, D, R }), "Aus: kept, put back, gone, already as before, pending");
    Check(off.stayed.SequenceEqual(new[] { ("Grand Theft Auto V", 100) }), "Aus: only the profile changed outside OrbitTweak is named, with its limit");
    Check(off.drop.SequenceEqual(new[] { "Grand Theft Auto V", "FiveM", "Gone", "Before", "Pending" }),
        "Aus: every entry goes, the kept profile's too (it is the user's again)");
    // why: the user changed P from OrbitTweak's 141 to 100, then Aus. With its entry gone the next limit backs up the 100,
    // so a later Aus gives the 100 back instead of reusing the stale "-" and wiping it
    Check(FpsLimitLogic.Written(null, 141) == 0 && FpsLimitLogic.Backup(true, 0, false, 100) == "100" && FpsLimitLogic.PendingEntry("100") == "100|0",
        "after Aus the next limit takes a fresh backup of the user's 100, stored as pending first");
    Check(FpsLimitLogic.OffDecision(true, 141, FpsLimitLogic.Entry("100", 141), 141) == R && FpsLimitLogic.RestoreValue(FpsLimitLogic.ParseEntry(FpsLimitLogic.Entry("100", 141), 141).backup) == 100u,
        "and the Aus after that puts the user's 100 back");
    Check(FpsLimitLogic.RestoreValue(FpsLimitLogic.ParseEntry("-|141", 141).backup) == null, "(a stale \"-|141\" would have deleted it)");

    // a new limit follows to profiles that left the cache only while they still have OrbitTweak's value
    Check(FpsLimitLogic.Follows(141, 141) && !FpsLimitLogic.Follows(100, 141) && !FpsLimitLogic.Follows(0, 0), "a profile with OrbitTweak's value follows a new limit, a changed one not");

    // the FiveM probe only adds what is new
    var up = FpsLimitLogic.UpkeepPlan(new (string, string?)[] { ("FiveM_b3258_GTAProcess.exe", null), ("FiveM_b3095_GTAProcess.exe", FpsLimitLogic.OwnProfile),
        ("FiveM_b2802_GTAProcess.exe", "FiveM"), ("FiveM_b2699_GTAProcess.exe", "Grand Theft Auto V") }, new (string, string?)[] { ("fivem", "-|141") }, 141, ownExists: true);
    Check(up.AddToOwn.SequenceEqual(new[] { "FiveM_b3258_GTAProcess.exe" }), "upkeep: an exe in no profile goes into the own profile");
    Check(!up.Own, "upkeep: the own profile exists, its limit is not written again (a value changed in the Control Panel stays)");
    Check(up.Foreign.SequenceEqual(new[] { "Grand Theft Auto V" }), "upkeep: a profile with an entry is not written again, a new one gets the limit");
    var fresh = FpsLimitLogic.UpkeepPlan(new (string, string?)[] { ("FiveM_b3258_GTAProcess.exe", null) }, Array.Empty<(string, string?)>(), 141, ownExists: false);
    Check(fresh.Own && fresh.AddToOwn.Count == 1, "upkeep: the own profile made right now gets the kept limit");
    var none = FpsLimitLogic.UpkeepPlan(new (string, string?)[] { ("FiveM_b3095_GTAProcess.exe", FpsLimitLogic.OwnProfile), ("FiveM_b2802_GTAProcess.exe", "FiveM") },
        new (string, string?)[] { ("FiveM", "-|141") }, 141, ownExists: true);
    Check(none.AddToOwn.Count == 0 && !none.Own && none.Foreign.Count == 0, "upkeep after the app starts with nothing new: nothing is written");
    var changedUp = FpsLimitLogic.UpkeepPlan(new (string, string?)[] { ("FiveM_b2802_GTAProcess.exe", "FiveM") }, new (string, string?)[] { ("FiveM", "-|141") }, 120, ownExists: true);
    Check(changedUp.Foreign.Count == 0, "upkeep: a profile OrbitTweak wrote stays as it is, also after the kept limit changed");
    var oldUp = FpsLimitLogic.UpkeepPlan(new (string, string?)[] { ("FiveM_b2802_GTAProcess.exe", "FiveM") }, new (string, string?)[] { ("FiveM", "-") }, 141, ownExists: true);
    Check(oldUp.Foreign.Count == 0, "upkeep: an entry without '|' counts as written with the stored limit");
    var retry = FpsLimitLogic.UpkeepPlan(new (string, string?)[] { ("FiveM_b2802_GTAProcess.exe", "Grand Theft Auto V"), ("FiveM_b2699_GTAProcess.exe", "FiveM") },
        new (string, string?)[] { ("grand theft auto v", "-|0"), ("FiveM", "60|141") }, 141, ownExists: true);
    Check(retry.Foreign.SequenceEqual(new[] { "Grand Theft Auto V" }), "upkeep: a pending entry (the driver refused to save) is written again, a written one not");

    // the card names every profile of someone else a limit writes, not only the newest build's
    var fo = FpsLimitLogic.ForeignOf(new (string, string?)[] { ("FiveM_b2802_GTAProcess.exe", "Grand Theft Auto V"), ("FiveM_b3095_GTAProcess.exe", null),
        ("FiveM_GTAProcess.exe", "grand theft auto v"), ("FiveM_b3258_GTAProcess.exe", FpsLimitLogic.OwnProfile) }, "FiveM_b3095_GTAProcess.exe");
    Check(fo.Count == 1 && fo[0].profile == "Grand Theft Auto V" && fo[0].exes.SequenceEqual(new[] { "FiveM_b2802_GTAProcess.exe", "FiveM_GTAProcess.exe" }),
        "an older build in NVIDIA's GTA V profile is named although the newest build is in no profile");
    var fo2 = FpsLimitLogic.ForeignOf(new (string, string?)[] { ("FiveM_b2802_GTAProcess.exe", "Grand Theft Auto V"), ("FiveM_b3095_GTAProcess.exe", "FiveM"),
        ("FiveM_b2699_GTAProcess.exe", "FiveM") }, "fivem_b3095_gtaprocess.exe");
    Check(fo2.Select(x => x.profile).SequenceEqual(new[] { "FiveM", "Grand Theft Auto V" }), "the newest build's profile first, then the others");
    Check(fo2.Select(x => x.profile).SequenceEqual(FpsLimitLogic.Decide(new (string, string?)[] { ("FiveM_b2802_GTAProcess.exe", "Grand Theft Auto V"),
        ("FiveM_b3095_GTAProcess.exe", "FiveM"), ("FiveM_b2699_GTAProcess.exe", "FiveM") }).Foreign.OrderBy(x => x != "FiveM")), "the same profiles the card's set writes");
    Check(FpsLimitLogic.ForeignOf(new (string, string?)[] { ("FiveM_b3095_GTAProcess.exe", FpsLimitLogic.OwnProfile), ("FiveM_b2802_GTAProcess.exe", null) }, null).Count == 0,
        "only the own profile or none: nothing of someone else");

    // what else is in the profile gets the limit too
    Check(FpsLimitLogic.OtherApps(new[] { "FiveM_b3095_GTAProcess.exe", "GTA5.exe", @"C:\Games\GTAV\PlayGTAV.exe", "gta5.exe", "FiveM.exe", "FiveM_ChromeBrowser.exe" })
        .SequenceEqual(new[] { "GTA5.exe", "PlayGTAV.exe" }), "other apps: file names once, FiveM's own exes are not others");
    Check(FpsLimitLogic.OtherApps(new[] { "FiveM_b3095_GTAProcess.exe", "FiveM_b2802_GTAProcess.exe" }).Count == 0, "only FiveM's exes: no others");
    Check(FpsLimitLogic.IsFiveMApp("FiveM.exe") && FpsLimitLogic.IsFiveMApp("fivem_b3095_gtaprocess.exe") && !FpsLimitLogic.IsFiveMApp("FiveMods.exe") && !FpsLimitLogic.IsFiveMApp("GTA5.exe"),
        "FiveM's own exes by name");
    Check(FpsLimitLogic.OthersFromCount(1) == 0 && FpsLimitLogic.OthersFromCount(0) == 0 && FpsLimitLogic.OthersFromCount(3) == null,
        "without the list: one app is FiveM alone, more is unknown");
}

// ---------- graphics: texture budget in fivem.cfg ----------
{
    Check(TexBudgetLogic.Multiplier(0) == 1 && TexBudgetLogic.Multiplier(12) == 2 && Math.Abs(TexBudgetLogic.Multiplier(8) - 5 / 3.0) < 1e-9, "multiplier value / 12 + 1: 0 default, 12 double");
    Check(TexBudgetLogic.Percent(8) == 40 && TexBudgetLogic.Percent(TexBudgetLogic.Max) == 100, "the slider: 8 is 40 %, 20 is 100 %");
    Check(TexBudgetLogic.CapMb(8) == 6575 && TexBudgetLogic.CapMb(0) == 3945, "texture cap estimate: 6575 MB at 8 as Cfx.re staff gave it");
    Check(TexBudgetLogic.Valid(0) && TexBudgetLogic.Valid(20) && !TexBudgetLogic.Valid(-1) && !TexBudgetLogic.Valid(21), "0 to 20");

    // VRAM → suggestion: the texture cap stays within half of the card
    Check(TexBudgetLogic.Suggestion(0) == 0 && TexBudgetLogic.Suggestion(4096) == 0 && TexBudgetLogic.Suggestion(6144) == 0 && TexBudgetLogic.Suggestion(8192) == 0,
        "up to 8 GB: FiveM's default 0");
    Check(TexBudgetLogic.Suggestion(10240) == 3 && TexBudgetLogic.Suggestion(12288) == 6 && TexBudgetLogic.Suggestion(12079) == 6, "10 GB: 15 %, 12 GB: 30 %");
    Check(TexBudgetLogic.Suggestion(16384) == 12 && TexBudgetLogic.Suggestion(24576) == 20 && TexBudgetLogic.Suggestion(49152) == 20, "16 GB: double, 24 GB and more: 100 %");
    for (long mb = 0; mb <= 40000; mb += 256)
    {
        int s = TexBudgetLogic.Suggestion(mb);
        if (TexBudgetLogic.CapMb(s) * 2 > Math.Max(mb, 7890) || s < 0 || s > TexBudgetLogic.Max || (mb > 0 && TexBudgetLogic.Suggestion(mb + 256) < s))
        { Check(false, $"suggestion for {mb} MB ({s}) keeps half the VRAM free, stays in range and grows with the VRAM"); break; }
    }

    // reading the file FiveM writes
    const string cfg = "// generated by CitizenFX\r\nbind keyboard \"F8\" \"toggleconsole\"\r\nseta \"cl_drawfps\" \"true\"\r\nseta \"vid_budgetScale\" \"8\"\r\nseta \"voice_mode\" \"2\"\r\n";
    var r = TexBudgetLogic.Read(cfg);
    Check(r.present && r.raw == "8" && r.value == 8, "the value FiveM wrote is read");
    Check(TexBudgetLogic.Read("// generated by CitizenFX\r\nseta \"cl_drawfps\" \"true\"\r\n") == (false, null, 0), "no line: FiveM's default 0");
    Check(TexBudgetLogic.Read("seta vid_budgetScale 4\nset \"VID_BUDGETSCALE\" \"6\" // mine\n").value == 6, "unquoted, other case, set and a comment: the last line wins");
    Check(TexBudgetLogic.Read("// seta \"vid_budgetScale\" \"9\"\nseta \"vid_budgetScale2\" \"9\"\nseta \"vid_budgetScale\" \"x\"\n") == (true, "x", 0),
        "comments and other convars do not count, a value that is no number is 0");
    Check(TexBudgetLogic.Words("seta \"a b\" c // d").SequenceEqual(new[] { "seta", "a b", "c" }), "console words: quotes keep spaces, // ends the line");

    // writing: only the one line changes
    var w = TexBudgetLogic.Write(cfg, "4");
    Check(w == cfg.Replace("\"vid_budgetScale\" \"8\"", "\"vid_budgetScale\" \"4\""), "the value is replaced in place, every other line and line end as before");
    Check(TexBudgetLogic.Line("12") == "seta \"vid_budgetScale\" \"12\"", "the line as FiveM writes it");
    var noLine = "// generated by CitizenFX\r\nseta \"cl_drawfps\" \"true\"\r\n";
    Check(TexBudgetLogic.Write(noLine, "6") == noLine + "seta \"vid_budgetScale\" \"6\"\r\n", "a file without the line gets it at the end, with FiveM's \\r\\n");
    Check(TexBudgetLogic.Write("seta \"a\" \"1\"", "6") == "seta \"a\" \"1\"\r\nseta \"vid_budgetScale\" \"6\"\r\n" && TexBudgetLogic.Write("x\ny\n", "2") == "x\ny\nseta \"vid_budgetScale\" \"2\"\n",
        "a last line without line end gets one first; a file with \\n keeps \\n");
    Check(TexBudgetLogic.Write(TexBudgetLogic.Write(noLine, "6"), null) == noLine, "back to before: the added line goes again, the file is as it was");
    Check(TexBudgetLogic.Write(noLine, null) == noLine && TexBudgetLogic.Write("", "3") == "seta \"vid_budgetScale\" \"3\"\r\n", "nothing to take out; an empty file gets the line");
    var twice = "seta \"vid_budgetScale\" \"2\"\nbind x y\n  seta \"vid_budgetScale\" \"5\"\nz";
    Check(TexBudgetLogic.Write(twice, "9") == "seta \"vid_budgetScale\" \"2\"\nbind x y\n  seta \"vid_budgetScale\" \"9\"\nz", "two lines: the last one (the one that counts) changes, its indent stays");
    Check(TexBudgetLogic.Write("a\rseta vid_budgetScale 1\rb\r", "7") == "a\rseta \"vid_budgetScale\" \"7\"\rb\r", "old Mac line ends stay too");
}

// ---------- play time (Spielzeit) ----------
{
    var cet = TimeSpan.FromHours(1);
    DateTimeOffset L(int d, int h, int m = 0, int s = 0) => new(2026, 11, d, h, m, s, cet);   // November 2026, German winter time
    PlayRound R(DateTimeOffset a, DateTimeOffset b, string? key = null, string? name = null, int kills = 0) => new() { Start = a, End = b, Key = key, Server = name, Kills = kills };

    // days and hours
    var sd = PlayStats.SplitDays(L(16, 23, 30), L(17, 1, 15));
    Check(sd.Count == 2 && sd[0].day == new DateTime(2026, 11, 16) && sd[0].secs == 1800 && sd[1].day == new DateTime(2026, 11, 17) && sd[1].secs == 4500, "a round over midnight is split at midnight");
    var dst = R(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1)));
    Check(dst.Secs == 7200 && PlayStats.SplitDays(dst.Start, dst.End).Sum(x => x.secs) == 7200, "a round over the clock change keeps its real length");
    var hrs = PlayStats.Hours(new[] { R(L(16, 20, 30), L(16, 22, 15)) }, new DateTime(2026, 11, 1));
    Check(hrs[20] == 1800 && hrs[21] == 3600 && hrs[22] == 900 && hrs.Sum() == 6300, "seconds per hour of the day");
    Check(PlayStats.Hours(new[] { R(L(16, 20, 30), L(16, 22, 15)) }, new DateTime(2026, 11, 17)).Sum() == 0, "hours before the window do not count");
    Check(PlayStats.WeekStart(new DateTime(2026, 11, 17)) == new DateTime(2026, 11, 16) && PlayStats.WeekStart(new DateTime(2026, 11, 22)) == new DateTime(2026, 11, 16)
        && PlayStats.WeekStart(new DateTime(2026, 11, 23)) == new DateTime(2026, 11, 23), "weeks start on Monday");

    // days in a row: 10, 11, 12 played, 14 only 30 s (does not count), 15 and 16 played
    var pd = PlayStats.PerDay(new[] { R(L(10, 20), L(10, 21)), R(L(11, 20), L(11, 21)), R(L(12, 20), L(12, 21)), R(L(14, 20), L(14, 20, 0, 30)), R(L(15, 20), L(15, 21)), R(L(16, 20), L(16, 21)) });
    var st = PlayStats.Streak(pd, new DateTime(2026, 11, 17));
    Check(st == (2, 3), $"days in a row: 2 up to yesterday while today has none yet, best 3 ({st})");
    Check(PlayStats.Streak(pd, new DateTime(2026, 11, 18)).current == 0, "a whole day without play breaks the run");
    var dl = PlayStats.Days(pd, new DateTime(2026, 11, 17), 14);
    Check(dl.Count == 14 && dl[^1].Day == "2026-11-17" && dl[^1].Secs == 0 && dl[^2].Secs == 3600 && dl[^2].Rounds == 1 && dl[0].Day == "2026-11-04", "14 days up to today, oldest first");

    // servers
    var srv = PlayStats.Servers(new[] { R(L(1, 20), L(1, 23), "cfx:nova", "NovaCity"), R(L(2, 20), L(2, 21), "cfx:nova", "NovaCity RP"), R(L(3, 20), L(3, 22), "ip:1.2.3.4:30120"), R(L(4, 20), L(4, 21)) }, 5);
    Check(srv.Count == 3 && srv[0].Name == "NovaCity RP" && srv[0].Addr == "cfx.re/join/nova" && srv[0].Secs == 4 * 3600 && srv[0].Rounds == 2 && Math.Abs(srv[0].Share - 4 / 7.0) < 1e-3,
        "servers: grouped by address, with the newest name and the share of all play time");
    Check(srv[1].Addr == "1.2.3.4:30120" && srv[1].Name == null && srv[2].Addr == null && srv[2].Name == null, "a server without a name keeps its address, unknown ones are one entry");

    // what the page gets: Monday 16, today is Tuesday 17, 21:00, a round runs since 20:00
    var now = L(17, 21);
    var open = new PlayRound { Start = L(17, 20), End = now, Open = true, Pid = 42 };
    var all = new List<PlayRound> { R(L(9, 18), L(9, 19), kills: 3), R(L(16, 20), L(16, 22, 30), "cfx:nova", "NovaCity", 7), R(L(17, 18), L(17, 18, 45)), open };
    var sum = PlayStats.Summarize(all, open, now);
    Check(sum.Today == 6300 && sum.TodayRounds == 2, $"today counts the open round ({sum.Today})");
    Check(sum.Week == 15300 && sum.LastWeek == 3600, $"this week from Monday, last week ({sum.Week}, {sum.LastWeek})");
    Check(sum.Total == 18900 && sum.Rounds == 4 && sum.Since == L(9, 18).ToUnixTimeMilliseconds(), "total");
    Check(sum.Current != null && sum.Current.Secs == 3600 && sum.Recent.Count == 3 && sum.Recent[0].Start == L(17, 18).ToUnixTimeMilliseconds(), "the open round is current, the list holds the others, newest first");
    Check(sum.Records.Longest!.Secs == 9000 && sum.Records.Longest.Server == "NovaCity" && sum.Records.MostKills!.Kills == 7 && sum.Records.Avg == 18900 / 4, "records");
    Check(sum.Records.Streak == 2 && sum.Days.Count == 30 && sum.Days[^1].Secs == 6300 && sum.Hours[20] == 3600 + 3600, "days in a row, days and hours");
    Check(PlayStats.Summarize(new List<PlayRound>(), null, now).Records.MostKills == null, "no kills, no kill record");

    // the recorder follows the game
    var log = new PlayLog(); var rec = new PlayRecorder(log);
    var ended = new List<PlayRound>(); rec.Ended = ended.Add;
    var t = L(17, 20); var gs = L(17, 19, 59);
    rec.Tick(0, null, null, t, 0, 0, 0);
    Check(rec.Open == null && log.Rounds.Count == 0, "no game, no round");
    rec.Tick(100, gs, "b3095", t, 0, 0, 0);
    Check(rec.Open != null && rec.Open.Start == gs && rec.Open.Build == "b3095" && rec.Open.Open, "a round starts with the game process, at its start time");
    rec.Tick(100, gs, "b3095", t.AddSeconds(2), 100, 1, 100);
    rec.Tick(100, gs, "b3095", t.AddSeconds(4), 100, 3, 100);
    Check(rec.Open!.Kills == 3, "kills of the round come from the clipper's count");
    rec.Clip(); rec.Clip();
    rec.Server(100, "cfx:nova", "NovaCity", t.AddSeconds(30), t.AddSeconds(40));
    Check(rec.Open!.Key == "cfx:nova" && rec.Open.Server == "NovaCity" && rec.Open.Start == gs, "the first server found is the server of the whole round");
    for (int m = 1; m <= 30; m++) rec.Tick(100, gs, "b3095", t.AddMinutes(m), 100, 3, 100);   // looked at all the time
    Check(ended.Count == 0 && rec.Open!.Start == gs, "a round that is looked at all the time goes on");
    bool split = rec.Server(100, "cfx:blue", "BlueLine", t.AddMinutes(20), t.AddMinutes(30));
    Check(split && ended.Count == 1 && ended[0].End == t.AddMinutes(20) && ended[0].Kills == 3 && ended[0].Clips == 2 && !ended[0].Open
        && rec.Open!.Key == "cfx:blue" && rec.Open.Start == t.AddMinutes(20) && rec.Open.Kills == 0 && rec.Open.Clips == 0, "a server change splits the round where the new connect was noted");
    rec.Tick(100, gs, "b3095", t.AddMinutes(31), 100, 5, 100);
    Check(rec.Open!.Kills == 2, "kills after the split belong to the new round");
    bool save = rec.Tick(0, null, null, t.AddMinutes(32), 100, 5, 0);
    Check(save && rec.Open == null && ended.Count == 2 && ended[1].End == t.AddMinutes(31) && ended[1].Pid == 0 && log.Rounds.Count == 2, "the round ends with the game, at the last look");
    rec.Tick(200, L(17, 21), null, L(17, 21, 0, 30), 0, 0, 0);
    rec.Tick(0, null, null, L(17, 21, 0, 40), 0, 0, 0);
    Check(log.Rounds.Count == 2 && ended.Count == 2, "a round under a minute is dropped (crash while loading)");
    rec.Tick(300, L(17, 22), null, L(17, 22, 1), 100, 5, 0);
    rec.Tick(300, L(17, 22), null, L(17, 22, 2), 300, 1, 300);
    Check(rec.Open!.Kills == 1, "a new game process starts again at zero kills");
    rec.Tick(301, L(17, 23), null, L(17, 23, 30), 300, 1, 0);
    Check(ended.Count == 3 && ended[2].End == L(17, 22, 2) && rec.Open!.Pid == 301, "another game process ends the round and starts a new one");

    // the PC slept with the game open: that time is not play time
    var gl = new PlayLog(); var gr = new PlayRecorder(gl); var gEnded = new List<PlayRound>(); gr.Ended = gEnded.Add;
    var g0 = L(18, 20);
    gr.Tick(700, g0, "b3095", g0, 700, 2, 700);
    gr.Server(700, "cfx:nova", "NovaCity", g0.AddSeconds(20), g0.AddSeconds(30));
    for (int m = 1; m <= 40; m++) gr.Tick(700, g0, "b3095", g0.AddMinutes(m), 700, 2, 700);
    bool woke = gr.Tick(700, g0, "b3095", g0.AddHours(9), 700, 2, 700);   // the next morning, the game still open
    Check(woke && gEnded.Count == 0 && gl.Rounds.Count == 2 && gl.Rounds[0].End == g0.AddMinutes(40) && !gl.Rounds[0].Open && gl.Rounds[0].Pid == 0
        && gr.Open!.Start == g0.AddHours(9) && gr.Open.Key == "cfx:nova" && gr.Open.Server == "NovaCity" && gr.Open.Pid == 700 && gr.Open.Build == "b3095",
        "a sleeping PC cuts the round quietly there, the hours asleep do not count");
    gr.Tick(700, g0, "b3095", g0.AddHours(9).AddMinutes(2), 700, 3, 700);
    gr.Tick(700, g0, "b3095", g0.AddHours(9).AddMinutes(4), 700, 3, 700);
    Check(gl.Rounds.Count == 2 && gr.Open!.Kills == 1 && gl.Rounds[0].Kills == 0 && gr.Open.Secs == 240, "after the pause the new round counts on, with its own kills");

    // one round removed, the history cleared: the round being played stays
    var rc = new PlayRecorder(new PlayLog());
    rc.Tick(1, L(17, 10), null, L(17, 12), 0, 0, 0);
    rc.Log.Add(R(L(16, 10), L(16, 11)));
    var rid = rc.Log.Rounds.First(r => r != rc.Open).Id;
    Check(!rc.Remove(rc.Open!.Id) && rc.Remove(rid) && rc.Log.Rounds.Count == 1, "one round can be removed, not the one being played");
    rc.Log.Add(R(L(15, 10), L(15, 11)));
    Check(rc.Clear() == 1 && rc.Log.Rounds.Single() == rc.Open, "clearing the history keeps the round being played");

    // the file
    var dir = Path.Combine(Path.GetTempPath(), "orbit-play-" + Guid.NewGuid().ToString("N"));
    var file = Path.Combine(dir, "playtime.json");
    try
    {
        var l1 = new PlayLog(file); var r1 = new PlayRecorder(l1);
        r1.Tick(500, L(17, 18), null, L(17, 19, 29), 0, 0, 0);
        r1.Tick(500, L(17, 18), null, L(17, 19, 30), 500, 4, 500);
        r1.Close(L(17, 19, 31));
        PlayLog.Write(file, l1.Serialize());
        Check(File.Exists(file) && !File.Exists(file + ".tmp"), "written in one piece, no temp file left");
        var r2 = new PlayRecorder(PlayLog.Load(file));
        r2.Tick(500, L(17, 18), null, L(17, 20), 0, 0, 0);
        Check(r2.Open != null && r2.Open.Start == L(17, 18) && r2.Open.Kills == 4 && r2.Log.Rounds.Count == 1, "a restart of OrbitTweak alone takes the running round up again");
        r2.Tick(500, L(17, 18), null, L(17, 20, 0, 2), 500, 1, 500);
        Check(r2.Open!.Kills == 5, "and keeps counting its kills");
        var r3 = new PlayRecorder(PlayLog.Load(file));
        var ended3 = new List<PlayRound>(); r3.Ended = ended3.Add;
        bool s3 = r3.Tick(0, null, null, L(18, 9), 0, 0, 0);
        var only = r3.Log.Rounds.Single();
        Check(s3 && r3.Open == null && !only.Open && only.End == L(17, 19, 31) && only.Pid == 0 && ended3.Count == 0, "after a crash an unfinished round is closed at its last-seen time");
        var closedJson = System.Text.Encoding.UTF8.GetString(r3.Log.Serialize());
        Check(!closedJson.Contains("\"Pid\"") && !closedJson.Contains("\"Open\"") && !closedJson.Contains("\"GameStart\"") && closedJson.Contains("\"Kills\":4"), "a closed round keeps no process data in the file");
        var r4 = new PlayRecorder(PlayLog.Load(file));
        r4.Tick(500, L(17, 18, 0, 30), null, L(18, 9), 0, 0, 0);
        Check(r4.Log.Rounds.Count == 2 && r4.Open!.Start == L(17, 18, 0, 30) && !r4.Log.Rounds[0].Open, "a new game with the same process id is a new round");
        File.WriteAllText(file, "{\"V\":1,\"Rounds\":[{\"Start\":");
        var l5 = PlayLog.Load(file);
        Check(l5.Rounds.Count == 0 && File.Exists(file + ".bad") && !l5.Unreadable, "a broken file is kept aside as .bad");
        PlayLog.Write(file, l1.Serialize()); File.Delete(file + ".bad");
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))   // another program holds it
        {
            var l7 = PlayLog.Load(file);
            Check(l7.Unreadable && l7.Rounds.Count == 0 && !File.Exists(file + ".bad"), "a file that cannot be opened is marked, so it is never written over");
        }
        Check(PlayLog.Load(file) is { Unreadable: false, Rounds.Count: 1 }, "once free it reads again with the whole history");
        var l6 = new PlayLog();
        for (int i = 0; i < PlayLog.Keep + 7; i++) l6.Add(R(L(1, 0).AddMinutes(i * 3), L(1, 0).AddMinutes(i * 3 + 2)));
        Check(l6.Rounds.Count == PlayLog.Keep && l6.Rounds[0].Start == L(1, 0).AddMinutes(21) && l6.Rounds.Select(r => r.Id).Distinct().Count() == PlayLog.Keep, "only the last 5000 rounds are kept, each with its own id");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
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
        var opts = plain ? new MontageCut.Options(After: 2, Intro: false, Slow: 1) : new MontageCut.Options(After: 2);
        var plan = MontageCut.Make(src, opts);
        if (music != null)
        {
            // the music as the app reads it: mono 16-bit samples from ffmpeg
            var p = Process.Start(new ProcessStartInfo("ffmpeg", $"-v error -i \"{music}\" -t {N(60 + plan.Total * 1.3)} -vn -f s16le -ac 1 -ar {MontageBeat.Rate} pipe:1") { RedirectStandardOutput = true, UseShellExecute = false })!;
            using var ms = new MemoryStream(); p.StandardOutput.BaseStream.CopyTo(ms); p.WaitForExit();
            var pcm = new short[ms.Length / 2]; Buffer.BlockCopy(ms.GetBuffer(), 0, pcm, 0, pcm.Length * 2);
            var beat = MontageBeat.Find(pcm, MontageBeat.Rate, plan.Total + 30);
            Console.WriteLine(beat == null ? "no clear beat" : $"beat: {beat.Bpm:0.00} BPM, music from {beat.Start:0.000} s");
            if (beat != null) plan = MontageCut.Make(src, opts, beat);
        }
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
        foreach (var p in plan.Parts) Console.WriteLine($"  {Path.GetFileName(p.Src.File)}: {N(p.A)}..{N(p.D)} at {N(p.At)} len {N(p.Len)} slow {N(p.SlowFrom)}..{N(p.SlowTo)} kills " + string.Join(" ", p.Kills.Select(k => $"#{k.Number}@{N(k.T)} (montage {N(p.At + k.T)})")));
        var sw = Stopwatch.StartNew();
        Ff(a.Replace("-progress pipe:1 -nostats ", ""));
        Console.WriteLine($"rendered in {sw.Elapsed.TotalSeconds:0.0} s -> {target}");
        return 0;
    }
}
