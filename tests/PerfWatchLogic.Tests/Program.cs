using System.Text;
using System.Text.Json;
using OrbitTweak.Core;

// Checks of the Engpass-Analyse's pure logic. Exit code 0 = all fine.
//   dotnet run --project tests/PerfWatchLogic.Tests

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }
void Eq<T>(T got, T want, string what) => Check(EqualityComparer<T>.Default.Equals(got, want), $"{what}: got [{got}], want [{want}]");
void Near(double? got, double want, string what, double eps = 1e-6) => Check(got is double g && Math.Abs(g - want) <= eps, $"{what}: got [{got}], want [{want}]");

const string NB = " ";
PerfSample S(double? gpu, double core, double cpu, double? vramMb = 8192, double? vramUsed = 4096, double ramMb = 32768, double ramUsed = 12000, bool own = true)
    => new(gpu, own, core, cpu, vramMb, vramUsed, ramMb, ramUsed);
PerfKind K(double? gpu, double core, double cpu) => PerfWatchLogic.Classify(S(gpu, core, cpu));

// ---------- one look: the thresholds ----------
Eq(K(95, 30, 20), PerfKind.Gpu, "3D at 95 % is the graphics card");
Eq(K(100, 99, 99), PerfKind.Gpu, "3D at 100 % is the card even with a busy processor");
Eq(K(94.9, 30, 20), PerfKind.Mixed, "3D just under 95 % but over 85 %: no clear limit");
Eq(K(84.9, 90, 40), PerfKind.Cpu, "3D under 85 % and a core at 90 %: the processor");
Eq(K(60, 50, 90), PerfKind.Cpu, "3D under 85 % and the whole processor at 90 %: the processor");
Eq(K(85, 99, 60), PerfKind.Mixed, "3D at exactly 85 % does not count as waiting on the processor");
Eq(K(84.9, 89.9, 50), PerfKind.Mixed, "a core between 80 and 90 %: no clear limit");
Eq(K(84.9, 79.9, 79.9), PerfKind.Cap, "both with room: capped");
Eq(K(40, 30, 10), PerfKind.Cap, "a calm look: capped");
Eq(K(40, 80, 20), PerfKind.Mixed, "a core at exactly 80 % is no longer room");
Eq(K(40, 50, 80), PerfKind.Mixed, "the whole processor at exactly 80 % is no longer room");
Eq(K(null, 99, 99), PerfKind.Unknown, "without a GPU value there is no verdict");
Eq(PerfWatchLogic.Classify(S(double.NaN, 99, 10)), PerfKind.Unknown, "a NaN GPU value is no value");
Eq(K(250, 10, 10), PerfKind.Gpu, "values over 100 count as 100");
Eq(K(-5, 95, 10), PerfKind.Cpu, "negative values count as 0");

// ---------- one look: cleaning ----------
var c = PerfWatchLogic.Clean(new PerfSample(130, true, 104, -3, 8192, 9000, 16384, 20000));
Near(c.Gpu, 100, "GPU clamped"); Near(c.Core, 100, "core clamped"); Near(c.Cpu, 0, "CPU clamped");
Near(c.VramUsedMb, 9000, "VRAM in use is kept (over the size: the % is clamped)");
Near(PerfWatchLogic.VramPct(c), 100, "VRAM % clamped");
Near(c.RamUsedMb, 16384, "RAM in use is never more than there is");
var c2 = PerfWatchLogic.Clean(new PerfSample(null, true, 10, 10, null, 3000, 0, 500));
Check(!c2.Own, "no GPU value: not the game's own");
Check(c2.VramMb == null && c2.VramUsedMb == null, "VRAM in use without the card's size: unknown");
Near(PerfWatchLogic.RamPct(c2), 0, "RAM % without a total");
var c3 = PerfWatchLogic.Clean(new PerfSample(50, true, 10, 10, 8192, null, 100, 50));
Check(c3.VramMb == null && c3.VramUsedMb == null, "the card's size without what is in use: unknown");
var c4 = PerfWatchLogic.Clean(new PerfSample(50, true, 10, 10, double.PositiveInfinity, 100, double.NaN, double.NaN));
Check(c4.VramMb == null && c4.RamMb == 0 && c4.RamUsedMb == 0, "infinite and NaN sizes are dropped");
Near(PerfWatchLogic.VramPct(S(50, 10, 10, 8192, 7782.4)), 95, "VRAM %");
Check(PerfWatchLogic.VramFull(S(50, 10, 10, 8192, 7782.4)), "VRAM at 95 % is full");
Check(!PerfWatchLogic.VramFull(S(50, 10, 10, 8192, 7780)), "VRAM just under 95 % is not full");
Check(!PerfWatchLogic.VramFull(S(50, 10, 10, null, null)), "unknown VRAM is never full");
Check(PerfWatchLogic.RamTight(S(50, 10, 10, ramMb: 1000, ramUsed: 900)), "RAM at 90 % is tight");
Check(!PerfWatchLogic.RamTight(S(50, 10, 10, ramMb: 1000, ramUsed: 899)), "RAM just under 90 % is not tight");
Eq(PerfWatchLogic.Key(PerfKind.Gpu) + PerfWatchLogic.Key(PerfKind.Cpu) + PerfWatchLogic.Key(PerfKind.Cap) + PerfWatchLogic.Key(PerfKind.Mixed) + PerfWatchLogic.Key(PerfKind.Unknown),
    "gpucpucapmixedunknown", "kind keys for the page");

// ---------- counter instances ----------
var e = PerfWatchLogic.ParseEngine("pid_1234_luid_0x00000000_0x0000D1B6_phys_0_eng_0_engtype_3D");
Check(e is { pid: 1234, luid: "0x00000000_0x0000d1b6", phys: 0, eng: 0, type: "3D" }, "an engine instance is read (luid in lower case)");
Check(PerfWatchLogic.ParseEngine("PID_7_LUID_0x00000000_0x0001ABCD_PHYS_1_ENG_12_ENGTYPE_VideoDecode") is { pid: 7, phys: 1, eng: 12, type: "VideoDecode" }, "upper case and other engine types");
Check(PerfWatchLogic.ParseEngine("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_Compute_0") is { type: "Compute_0" }, "engine types with an underscore");
Check(PerfWatchLogic.ParseEngine("luid_0x00000000_0x0000D1B6_phys_0") == null, "an adapter instance is no engine");
Check(PerfWatchLogic.ParseEngine("pid_x_luid_0x0_0x1_phys_0_eng_0_engtype_3D") == null && PerfWatchLogic.ParseEngine("") == null && PerfWatchLogic.ParseEngine(null) == null, "broken names");
Check(PerfWatchLogic.ParseEngine("pid_99999999999_luid_0x0_0x1_phys_0_eng_0_engtype_3D") == null, "a pid out of range is refused");
Eq(PerfWatchLogic.Luid(0x0000D1B6, 0), "0x00000000_0x0000d1b6", "LUID as the counters write it");
Eq(PerfWatchLogic.Luid(0xFFFFFFFF, -1), "0xffffffff_0xffffffff", "a negative high part");

const string L1 = "0x00000000_0x0000d1b6", L2 = "0x00000000_0x0000e2c7";
string Eng(int pid, string luid, int eng, string type = "3D", int phys = 0) => $"pid_{pid}_luid_{luid}_phys_{phys}_eng_{eng}_engtype_{type}";
var items = new List<(string, double)>
{
    (Eng(4000, L1, 0), 71), (Eng(4000, L1, 1, "Copy"), 30), (Eng(512, L1, 0), 12.5), (Eng(800, L1, 0), 30),
    (Eng(4000, L2, 0), 3), (Eng(900, L2, 0), 20), ("_Total", 99), ("junk", 50),
};
var g = PerfWatchLogic.GpuLoad(items, 4000);
Near(g.own, 71, "the game's own 3D load");
Near(g.total, 100, "the busiest engine over all processes adds up and is capped at 100");
Eq(g.luid, L1, "the adapter of the game's busiest engine");
var g2 = PerfWatchLogic.GpuLoad(items, 900);
Near(g2.own, 20, "another process's load"); Eq(g2.luid, L2, "and its adapter");
var g3 = PerfWatchLogic.GpuLoad(items, 1);
Check(g3.own == null && g3.total != null && g3.luid == L1, "a process without 3D: no own value, the busiest adapter");
var g4 = PerfWatchLogic.GpuLoad(new List<(string, double)>(), 1);
Check(g4.own == null && g4.total == null && g4.luid == null, "nothing at all");
var g5 = PerfWatchLogic.GpuLoad(new List<(string, double)> { (Eng(5, L1, 0), 40), (Eng(5, L2, 0), 62), (Eng(5, L2, 1, "3D", 1), 61) }, 5);
Near(g5.own, 62, "the game on two adapters: its busiest engine"); Eq(g5.luid, L2, "the adapter it uses most");
var g6 = PerfWatchLogic.GpuLoad(new List<(string, double)> { (Eng(5, L1, 0), double.NaN), (Eng(5, L1, 0), -4) }, 5);
Near(g6.own, 0, "NaN is skipped, negative counts as 0");

var mem = new List<(string, double)> { ("luid_0x00000000_0x0000D1B6_phys_0", 6.0 * (1 << 30)), ("luid_0x00000000_0x0000d1b6_phys_1", 1.0 * (1 << 30)), ("luid_0x00000000_0x0000E2C7_phys_0", 0.5 * (1 << 30)), ("garbage", 1) };
var m1 = PerfWatchLogic.AdapterUse(mem, L1);
Check(m1 is { } a1 && Math.Abs(a1.bytes - 7.0 * (1 << 30)) < 1 && a1.luid == L1, "VRAM of the game's adapter, all its physical GPUs");
var m2 = PerfWatchLogic.AdapterUse(mem, "0x00000000_0x0000E2C7");
Check(m2 is { } a2 && Math.Abs(a2.bytes - 0.5 * (1 << 30)) < 1 && a2.luid == L2, "the luid in any case");
var m3 = PerfWatchLogic.AdapterUse(mem, null);
Check(m3 is { luid: L1 }, "without a luid: the adapter that uses most");
var m4 = PerfWatchLogic.AdapterUse(mem, "0x00000000_0x00000001");
Check(m4 is { luid: L1 }, "an unknown luid: the adapter that uses most");
Check(PerfWatchLogic.AdapterUse(new List<(string, double)> { ("x", 1) }, null) == null, "no adapter");

var cpuInfo = new List<(string, double)> { ("0,0", 40), ("0,1", 97.5), ("0,2", 10), ("0,_Total", 49), ("1,0", 12), ("1,_Total", 12), ("_Total", 39.8) };
var cl = PerfWatchLogic.CpuLoad(cpuInfo);
Check(cl is { } q1 && Math.Abs(q1.total - 39.8) < 1e-9 && Math.Abs(q1.core - 97.5) < 1e-9, "Processor Information: total and busiest core, group totals left out");
var cl2 = PerfWatchLogic.CpuLoad(new List<(string, double)> { ("0", 20), ("1", 60) });
Check(cl2 is { total: 40, core: 60 }, "no _Total: the average of the cores");
var cl3 = PerfWatchLogic.CpuLoad(new List<(string, double)> { ("_Total", 120) });
Check(cl3 is { total: 100, core: 100 }, "only _Total: it stands for the core too, clamped");
Check(PerfWatchLogic.CpuLoad(new List<(string, double)>()) == null, "no values at all");
Check(PerfWatchLogic.CpuLoad(new List<(string, double)> { ("0", double.NaN), ("", 5) }) == null, "only broken values");

// ---------- formats ----------
Eq(PerfWatchLogic.PctText(.62), "62" + NB + "%", "share");
Eq(PerfWatchLogic.PctText(.004), "<" + NB + "1" + NB + "%", "a tiny share");
Eq(PerfWatchLogic.PctText(0), "0" + NB + "%", "no share");
Eq(PerfWatchLogic.PctText(1), "100" + NB + "%", "all of it");
Eq(PerfWatchLogic.LoadText(86.6), "87" + NB + "%", "a load");
Eq(PerfWatchLogic.LoadText(null), "–", "an unknown load");
Eq(PerfWatchLogic.GbText(7270), "7,1" + NB + "GB", "GB with a comma");
Eq(PerfWatchLogic.GbText(12288), "12,0" + NB + "GB", "a whole number of GB");
Eq(PerfWatchLogic.Mix(new PerfShares(.62, .21, .09, .08, 0)), $"Grafikkarte am Limit 62{NB}%, Prozessor 21{NB}%, gedeckelt 9{NB}%, gemischt 8{NB}%", "the mix");
Eq(PerfWatchLogic.Mix(new PerfShares(.995, .005, 0, 0, 0)), $"Grafikkarte am Limit 100{NB}%", "under 1 % is left out of the mix");

// ---------- a round: totals ----------
PerfRound Round(int gpu, int cpu, int cap, int mixed, int unknown = 0, double vramUsed = 4096, double ramUsed = 12000, long id = 1, double secsEach = 2, double ramMb = 32768, bool own = true)
{
    var r = new PerfRound { Id = id, Start = new DateTimeOffset(2026, 11, 22, 18, 0, 0, TimeSpan.FromHours(1)) };
    r.End = r.Start;
    void Add(PerfSample s) { r.Add(PerfWatchLogic.Clean(s), secsEach); r.End += TimeSpan.FromSeconds(secsEach); }
    for (int i = 0; i < gpu; i++) Add(S(98, 40, 25, 8192, vramUsed, ramMb, ramUsed, own));
    for (int i = 0; i < cpu; i++) Add(S(60, 96, 45, 8192, vramUsed, ramMb, ramUsed, own));
    for (int i = 0; i < cap; i++) Add(S(45, 40, 20, 8192, vramUsed, ramMb, ramUsed, own));
    for (int i = 0; i < mixed; i++) Add(S(90, 70, 40, 8192, vramUsed, ramMb, ramUsed, own));
    for (int i = 0; i < unknown; i++) Add(S(null, 70, 40, 8192, vramUsed, ramMb, ramUsed, own));
    return r;
}
var r1 = Round(60, 20, 10, 10);
Eq(r1.N, 100, "looks counted"); Near(r1.Secs, 200, "seconds counted");
Check(r1.Gpu == 60 && r1.Cpu == 20 && r1.Cap == 10 && r1.Mixed == 10 && r1.Unknown == 0, "looks per kind");
Eq(r1.GpuN, 100, "looks with a GPU value"); Eq(r1.OwnN, 100, "all the game's own");
var sh = PerfWatchLogic.Shares(r1);
Check(sh == new PerfShares(.6, .2, .1, .1, 0), "shares of the time");
var it1 = PerfWatchLogic.Item(r1, "NovaCity");
Near(it1.Avg.Gpu, (60 * 98 + 20 * 60 + 10 * 45 + 10 * 90) / 100.0, "average GPU", .05);
Near(it1.Peak.Gpu, 98, "peak GPU"); Near(it1.Peak.Core, 96, "peak core"); Near(it1.Peak.Cpu, 45, "peak CPU");
Near(it1.VramGb, 8, "card VRAM in GB"); Near(it1.PeakVramGb, 4, "VRAM in use at its peak"); Near(it1.RamGb, 32, "RAM in GB");
Check(it1.Own && it1.Server == "NovaCity" && it1.Id == 1, "own flag, server, id");
Eq(it1.End - it1.Start, 200_000L, "start and end in ms");

var unk = Round(10, 0, 0, 0, unknown: 30);
var itU = PerfWatchLogic.Item(unk, null);
Near(itU.Avg.Gpu, 98, "the GPU average only over looks with a value");
Eq(unk.Unknown, 30, "looks without a GPU value");

var a = Round(10, 0, 0, 0, id: 7); var b = Round(0, 5, 0, 0, vramUsed: 8000, ramUsed: 31000, id: 7);
b.Start = a.Start.AddMinutes(-5); b.End = a.End.AddMinutes(30);
a.Merge(b);
Check(a.N == 15 && a.Gpu == 10 && a.Cpu == 5, "two parts of a round add up");
Check(a.Start == b.Start && a.End == b.End, "merged round spans both parts");
Near(a.PeakVramMb, 8000, "merged peaks"); Near(a.PeakRamMb, 31000, "merged RAM peak");
Near(a.SumGpu, 10 * 98 + 5 * 60, "merged sums");
var t = new PerfRound { SumGpu = 1.23456, Secs = 2.0000001, VramMb = 8191.6 }; t.Tidy();
Check(t.SumGpu == 1.2 && t.Secs == 2 && t.VramMb == 8192, "tidy rounds the sums");

// ---------- the verdict ----------
var goes = new HashSet<string?> { null, "presets", "tuning", "fps", "tex", "bgbrake", "cleaner", "autostart", "diag" };
void Sound(PerfVerdict v, string what)
{
    Check(v.Help.Count > 0, $"{what}: something to try");
    Check(v.Help.All(h => goes.Contains(h.Go) && (h.Go == null) == (h.Label == null)), $"{what}: every link leads to a real place and has a label");
    foreach (var s in new[] { v.Title, v.Text, v.NoHelp ?? "" }.Concat(v.Help.Select(h => h.Text)).Concat(v.Notes))
    {
        Check(!s.Contains("  ") && !s.Contains("NaN") && !s.Contains("∞") && !s.Contains("{") && !s.Contains('.' + "%"), $"{what}: clean text [{s}]");
        Check(s.Length == 0 || s == v.Title || s.EndsWith('.') , $"{what}: a sentence ends with a full stop [{s}]");
        Check(!System.Text.RegularExpressions.Regex.IsMatch(s, @"\d %|\d GB"), $"{what}: a number never parts from its unit [{s}]");
        Check(!System.Text.RegularExpressions.Regex.IsMatch(s, @"\b(garantiert|immer mehr FPS|verdoppelt)\b"), $"{what}: no promises [{s}]");
    }
    Check(char.IsUpper(v.Title[0]) && !v.Title.EndsWith('.'), $"{what}: title in sentence case without a full stop");
}
var vg = PerfWatchLogic.Verdict(Round(170, 10, 10, 10));
Eq(vg.Key, "gpu", "mostly at 95 %+ 3D: the graphics card"); Sound(vg, "gpu");
Check(vg.Text.Contains("85" + NB + "%") && vg.Help.Any(h => h.Go == "presets") && vg.Help.Any(h => h.Go == "fps"), "gpu: share, presets, FPS limit");
Check(vg.NoHelp!.Contains("Cleaner") && vg.NoHelp.Contains("Tweaks"), "gpu: says cleaner and tweaks do not help");
Check(vg.Help.Any(h => h.Text.Contains("keine FPS")), "gpu: the FPS limit is not sold as more FPS");
Eq(vg.Short, "Grafikkarte", "gpu: chip");
Check(vg.Notes.Count == 0, "gpu: a long clean round has no notes");

var vc = PerfWatchLogic.Verdict(Round(20, 140, 20, 20));
Eq(vc.Key, "cpu", "mostly waiting on the processor"); Sound(vc, "cpu");
Check(vc.Help[0].Go == "bgbrake" && vc.Help.Any(h => h.Go == "tuning"), "cpu: the tweak first, then the tuning");
Check(vc.NoHelp!.Contains("Auflösung"), "cpu: says a lower resolution does not help");

var vcap = PerfWatchLogic.Verdict(Round(10, 10, 150, 30));
Eq(vcap.Key, "cap", "mostly with room: capped"); Sound(vcap, "cap");
Check(vcap.Text.Contains("gut so") && vcap.Help[0].Text.Contains("reißen"), "cap: says it is fine and names the trade-off of V-Sync off");
Check(vcap.Notes.Any(n => n.Contains("Menüs")), "cap: menus and loading screens look the same");

var vm = PerfWatchLogic.Verdict(Round(70, 60, 40, 30));
Eq(vm.Key, "mixed", "no kind over half the time: mixed"); Sound(vm, "mixed spread");
Check(vm.Text.Contains("Grafikkarte am Limit 35" + NB + "%"), "mixed: names the mix");
var vmb = PerfWatchLogic.Verdict(Round(20, 20, 20, 140));
Eq(vmb.Key, "mixed", "mostly busy without a limit: mixed"); Check(vmb.Text.Contains("Gleichgewicht"), "mixed busy: its own text");
var vmt = PerfWatchLogic.Verdict(Round(52, 45, 30, 63));
Check(vmt.Key == "mixed" && vmt.Text.StartsWith("Mal war es") && vmt.Text.Contains("gemischt 33" + NB + "%"), "mixed on top but under half the time: the spread, not the busy text");
var edge = PerfWatchLogic.Verdict(Round(100, 50, 30, 20));
Eq(edge.Key, "gpu", "exactly half the time is a clear limit");

var vv = PerfWatchLogic.Verdict(Round(150, 20, 20, 10, vramUsed: 8000));
Eq(vv.Key, "vram", "VRAM full comes before the graphics card"); Sound(vv, "vram");
Check(vv.Text.Contains("7,8" + NB + "GB von 8,0" + NB + "GB") && vv.Help[0].Go == "tex", "vram: peak of size, texture budget first");
Check(vv.NoHelp!.Contains("RAM freigeben"), "vram: says the RAM cleaner does not empty VRAM");
Check(vv.Notes.Any(n => n.StartsWith("Daneben: Grafikkarte am Limit 75" + NB + "%")), "vram: what held the frame rate besides");
var vvSmall = Round(150, 20, 20, 10);
for (int i = 0; i < 12; i++) vvSmall.Add(PerfWatchLogic.Clean(S(98, 40, 25, 8192, 8100)), 2);
var vvs = PerfWatchLogic.Verdict(vvSmall);
Eq(vvs.Key, "gpu", "VRAM full 6 % of the time is not the headline");
Check(vvs.Notes.Any(n => n.Contains("Grafikspeicher war zeitweise voll")), "but it is mentioned");

var vr = PerfWatchLogic.Verdict(Round(150, 20, 20, 10, ramUsed: 15000, ramMb: 16000));
Eq(vr.Key, "ram", "RAM tight is the headline"); Sound(vr, "ram");
Check(vr.Help.Any(h => h.Go == "cleaner" && h.Text.Contains("nicht lange")) && vr.Help.Any(h => h.Go == "autostart"), "ram: the cleaner with its limit, the autostart");
Check(!vr.Help.Any(h => h.Text.Contains("16" + NB + "GB")), "ram: 16 GB machines are not told to buy RAM");
var vr8 = PerfWatchLogic.Verdict(Round(150, 20, 20, 10, ramUsed: 7600, ramMb: 8000));
Check(vr8.Help.Any(h => h.Text.Contains("Mit 7,8" + NB + "GB") && h.Go == null), "ram: an 8 GB PC is told plainly");
var vvr = PerfWatchLogic.Verdict(Round(150, 20, 20, 10, vramUsed: 8000, ramUsed: 31000));
Eq(vvr.Key, "vram", "both full: VRAM first"); Check(vvr.Notes.Any(n => n.Contains("Arbeitsspeicher war zeitweise knapp")), "and RAM mentioned");

var vn = PerfWatchLogic.Verdict(Round(10, 0, 0, 0, unknown: 50));
Eq(vn.Key, "nogpu", "mostly without GPU values: no verdict"); Sound(vn, "nogpu");
Check(vn.NoHelp == null && vn.Help[0].Go == "diag", "nogpu: a driver hint, nothing else");
var vn2 = PerfWatchLogic.Verdict(Round(0, 0, 0, 0, unknown: 40));
Eq(vn2.Key, "nogpu", "no GPU value at all");

var vs = PerfWatchLogic.Verdict(Round(40, 0, 0, 0));
Check(vs.Notes.Any(n => n.StartsWith("Kurze Runde")), "a short round is marked as rough");
var vw = PerfWatchLogic.Verdict(Round(170, 10, 10, 10, own: false));
Check(vw.Notes.Any(n => n.Contains("ganze Karte")), "GPU values of the whole card are named so");
Check(!PerfWatchLogic.Item(Round(170, 10, 10, 10, own: false), null).Own, "and the item says so");

// every verdict text for every kind of round stays clean
foreach (var (gp, cp, ca, mx, un, vu, ru) in new[] { (150, 30, 10, 10, 0, 4096.0, 12000.0), (0, 200, 0, 0, 0, 8190.0, 32000.0), (0, 0, 200, 0, 0, 0.0, 0.0), (1, 1, 1, 1, 196, 4096.0, 12000.0) })
    Sound(PerfWatchLogic.Verdict(Round(gp, cp, ca, mx, un, vu, ru)), $"mixed set {gp}/{cp}/{ca}/{mx}/{un}");

// ---------- following the game ----------
var t0 = new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.FromHours(1));
var rec = new PerfRecorder(new PerfLog());
Check(rec.Look(11, t0, null, true) == null && rec.Open == null, "the first look brings no values: nothing opened");
Eq(PerfWatchLogic.Live(rec)!.State, "warmup", "live: warming up");
var gpuS = S(99, 40, 30);
for (int i = 1; i <= 40; i++) rec.Look(11, t0.AddSeconds(2 * i), gpuS, true);
Check(rec.Open is { N: 40, Id: 11 }, "40 looks in the round");
Near(rec.Open!.Secs, 80, "two seconds each");
Eq(rec.Now, PerfKind.Gpu, "right now: the graphics card");
var live = PerfWatchLogic.Live(rec)!;
Check(live.State == "live" && live.Kind == "gpu" && live.Title == "Grafikkarte am Limit" && live.Round == 11, "live: state, kind, title, round");
Check(live.Now is { Gpu: 99, Core: 40, Cpu: 30, Own: true, Vram: 50, VramGb: 8, VramUsedGb: 4, RamGb: 32 } && !live.Now.VramFull && !live.Now.RamTight, "live: the values");
Check(live.Shares is { Gpu: 1 } && live.Secs == 80 && live.Since == rec.Open.Start.ToUnixTimeMilliseconds(), "live: the round so far");
// three looks of another kind do not flip "now" at once, a majority does
rec.Look(11, t0.AddSeconds(82), S(50, 96, 40), true);
rec.Look(11, t0.AddSeconds(84), S(50, 96, 40), true);
Eq(rec.Now, PerfKind.Gpu, "two looks of the processor among five: still the graphics card");
rec.Look(11, t0.AddSeconds(86), S(50, 96, 40), true);
Eq(rec.Now, PerfKind.Cpu, "three of five: the processor");
// in the background: not counted
int before = rec.Open.N;
rec.Look(11, t0.AddSeconds(88), gpuS, false);
Check(rec.Open.N == before, "a look in the background is not counted");
Eq(PerfWatchLogic.Live(rec)!.State, "paused", "live: paused"); Eq(PerfWatchLogic.Live(rec)!.Kind, null, "live: no kind while paused");
Eq(rec.Now, null, "right now: nothing while in the background");
rec.Look(11, t0.AddSeconds(90), null, true);   // back in front: the first look only primes the counters
Eq(rec.Open.N, before, "the first look after a pause brings no values");
rec.Look(11, t0.AddSeconds(92), gpuS, true);
Near(rec.Open.Secs, 80 + 6 + 2, "after a pause a look counts its 2 s, not the pause");
rec.Look(11, t0.AddSeconds(400), gpuS, true);
Near(rec.Open.Secs, 80 + 6 + 2 + 2, "a long gap counts as one look");
// a new Spielzeit round (server changed): the old one is kept
var kept = rec.Look(12, t0.AddSeconds(402), gpuS, true);
Check(kept is { Id: 11 } && rec.Log.Rounds.Count == 1 && rec.Open is { Id: 12, N: 1 }, "a new round id finishes the old one and starts the next");
// too short: dropped
for (int i = 0; i < 10; i++) rec.Look(12, t0.AddSeconds(404 + 2 * i), gpuS, true);
var gone = rec.Stop();
Check(gone == null && rec.Log.Rounds.Count == 1 && rec.Open == null && rec.RoundId == 0 && rec.Last == null, "a round under a minute is not kept, stop clears everything");
Eq(PerfWatchLogic.Live(rec), null, "no game: nothing live");
// round id 0: the Spielzeit round is not known, nothing is counted
var rec0 = new PerfRecorder(new PerfLog());
for (int i = 0; i < 40; i++) rec0.Look(0, t0.AddSeconds(2 * i), gpuS, true);
Check(rec0.Open == null && rec0.Stop() == null, "without a Spielzeit round nothing is counted");
// clock going backwards
var recB = new PerfRecorder(new PerfLog());
recB.Look(3, t0, gpuS, true); recB.Look(3, t0.AddSeconds(-30), gpuS, true);
Near(recB.Open!.Secs, 4, "a clock that jumps back counts one look");
// the same round measured twice (OrbitTweak restarted): one entry
var log2 = new PerfLog();
var rA = new PerfRecorder(log2);
for (int i = 0; i < 35; i++) rA.Look(21, t0.AddSeconds(2 * i), gpuS, true);
rA.Stop();
var rB = new PerfRecorder(log2);
for (int i = 0; i < 35; i++) rB.Look(21, t0.AddSeconds(600 + 2 * i), S(50, 97, 30), true);
rB.Stop();
Check(log2.Rounds.Count == 1 && log2.Rounds[0] is { N: 70, Gpu: 35, Cpu: 35 }, "two parts of one round become one entry");

// ---------- perf.json ----------
var dir = Path.Combine(Path.GetTempPath(), "pwtest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    var file = Path.Combine(dir, "perf.json");
    var log = PerfLog.Load(file);
    Check(log.Rounds.Count == 0 && !log.Off && !log.Unreadable, "no file: an empty log");
    for (int i = 0; i < 60; i++) { var r = Round(40, 0, 0, 0, id: 1000 + i); r.Start = t0.AddHours(i); r.End = r.Start.AddMinutes(30); log.Put(r); }
    Eq(log.Rounds.Count, PerfLog.Keep, "only the last 50 stay");
    Check(log.Rounds.Min(r => r.Id) == 1010, "the oldest went");
    log.Off = true;
    var open = Round(40, 0, 0, 0, id: 5000); open.Start = t0.AddHours(100); open.End = open.Start.AddMinutes(2);
    PerfLog.Write(file, log.Serialize(open));
    Check(!File.Exists(file + ".tmp"), "no temp file left");
    var back = PerfLog.Load(file);
    Check(back.Off, "the switch is kept");
    Eq(back.Rounds.Count, PerfLog.Keep, "the open round is written along, the oldest drops");
    Check(back.Rounds.Any(r => r.Id == 5000 && r.N == 40) && back.Rounds.All(r => r.Id != 1010), "the open round in the file, read back as a finished one");
    var json = Encoding.UTF8.GetString(File.ReadAllBytes(file));
    Check(json.Contains("\"Rounds\"") && json.Contains("\"SumGpu\"") && !json.Contains("E-"), "readable property names, tidy numbers");
    // the open round's id is already in the log (an earlier part): written merged, not twice
    var log3 = new PerfLog(); var p1 = Round(40, 0, 0, 0, id: 77); log3.Put(p1);
    var p2 = Round(0, 40, 0, 0, id: 77);
    PerfLog.Write(file, log3.Serialize(p2));
    var back3 = PerfLog.Load(file);
    Check(back3.Rounds.Count == 1 && back3.Rounds[0] is { N: 80, Gpu: 40, Cpu: 40 }, "an open part joins its round in the file");
    Check(log3.Rounds[0].N == 40, "writing does not change the log in memory");
    // too short to be written along
    PerfLog.Write(file, new PerfLog().Serialize(Round(5, 0, 0, 0, id: 9)));
    Eq(PerfLog.Load(file).Rounds.Count, 0, "an open round under a minute is not written");
    // the same id twice in a file (written by hand or an old bug): merged on reading
    var twice = new PerfLog(); twice.Rounds.Add(Round(40, 0, 0, 0, id: 3)); twice.Rounds.Add(Round(0, 40, 0, 0, id: 3));
    PerfLog.Write(file, twice.Serialize());
    var back4 = PerfLog.Load(file);
    Check(back4.Rounds.Count == 1 && back4.Rounds[0].N == 80, "the same id twice is merged on reading");
    // entries that make no sense are dropped
    File.WriteAllText(file, "{\"V\":1,\"Rounds\":[{\"Id\":1,\"N\":0,\"Start\":\"2026-11-22T18:00:00+01:00\",\"End\":\"2026-11-22T18:30:00+01:00\"},{\"Id\":2,\"N\":40,\"Start\":\"2026-11-22T19:00:00+01:00\",\"End\":\"2026-11-22T18:00:00+01:00\"},null,{\"Id\":3,\"N\":40,\"Gpu\":40,\"Start\":\"2026-11-22T19:00:00+01:00\",\"End\":\"2026-11-22T19:30:00+01:00\"}]}");
    var back5 = PerfLog.Load(file);
    Check(back5.Rounds.Count == 1 && back5.Rounds[0].Id == 3, "empty, backwards and null entries are dropped");
    // a broken file is kept aside
    File.WriteAllText(file, "{ broken");
    var bad = PerfLog.Load(file);
    Check(bad.Rounds.Count == 0 && !bad.Unreadable && File.Exists(file + ".bad"), "a broken file starts empty and is kept as .bad");
    // a first save cut off before it was put in place
    File.Delete(file);
    PerfLog.Write(file + ".tmp.src", log.Serialize());
    File.Move(file + ".tmp.src", file + ".tmp");
    var fromTmp = PerfLog.Load(file);
    Eq(fromTmp.Rounds.Count, PerfLog.Keep, "only the temp file: read from it");
    // pruning: rounds removed from the Spielzeit history go too, the one being measured stays
    var pl = new PerfLog(); foreach (var id in new long[] { 1, 2, 3, 4 }) pl.Put(Round(40, 0, 0, 0, id: id));
    Eq(pl.Prune(new HashSet<long> { 1, 3 }, 4), 1, "one round without its Spielzeit round");
    Check(pl.Rounds.Select(r => r.Id).OrderBy(x => x).SequenceEqual(new long[] { 1, 3, 4 }), "the measured one is kept");
    Eq(pl.Clear(), 3, "clear"); Eq(pl.Rounds.Count, 0, "cleared");
    // camelCase for the page
    var pageJson = JsonSerializer.Serialize(PerfWatchLogic.Item(Round(170, 10, 10, 10), "X"), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    Check(pageJson.Contains("\"verdict\":{\"key\":\"gpu\"") && pageJson.Contains("\"shares\":{\"gpu\":0.85") && pageJson.Contains("\"avg\":{\"gpu\""), "the page gets camelCase shapes");
}
finally { try { Directory.Delete(dir, true); } catch { } }

// ---------- texts right now ----------
foreach (var st in new[] { "warmup", "paused" }) Check(PerfWatchLogic.NowText(st, null).title.Length > 0, $"now: {st} has a title");
foreach (var k in Enum.GetValues<PerfKind>())
{
    var (ti, tx) = PerfWatchLogic.NowText("live", k);
    Check(ti.Length > 0 && tx.EndsWith('.') && !tx.Contains("  "), $"now: {k} has a title and one line");
}
Eq(PerfWatchLogic.NowText("live", PerfKind.Cap).title, "Gedeckelt", "now: capped");

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
