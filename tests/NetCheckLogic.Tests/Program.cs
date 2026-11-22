using OrbitTweak.Core;

// Checks of the Netz-Check's pure logic. Exit code 0 = all fine.
//   dotnet run --project tests/NetCheckLogic.Tests

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }
void Eq<T>(T got, T want, string what) => Check(EqualityComparer<T>.Default.Equals(got, want), $"{what}: got [{got}], want [{want}]");
void Has(string text, string part, string what) => Check(text.Contains(part, StringComparison.Ordinal), $"{what}: [{part}] not in [{text}]");
void Hasnt(string text, string part, string what) => Check(!text.Contains(part, StringComparison.Ordinal), $"{what}: [{part}] should not be in [{text}]");

// samples: a base ping with a small deterministic wobble, spikes and losses at given positions
List<NcSample> Gen(double baseMs, double wobble = 0.6, int[]? spikes = null, double spikeMs = 190, int[]? lost = null, int n = 120, int interval = 500, int seed = 7)
{
    var r = new Random(seed); var l = new List<NcSample>();
    for (int i = 0; i < n; i++)
    {
        double at = i * interval / 1000.0;
        double v = Math.Max(0.3, baseMs + (r.NextDouble() * 2 - 1) * wobble);
        if (lost != null && lost.Contains(i)) { l.Add(new(at, null)); continue; }
        if (spikes != null && spikes.Contains(i)) v = spikeMs + baseMs;
        l.Add(new(at, v));
    }
    return l;
}
List<NcSample> Seq(params double?[] v) => v.Select((x, i) => new NcSample(i * 0.5, x)).ToList();
List<NcSample> Silent(int n = 120, int interval = 500) => Enumerable.Range(0, n).Select(i => new NcSample(i * interval / 1000.0, null)).ToList();
NcTarget Gw(List<NcSample> s, bool measured = true) => new() { Id = "gw", Label = "Router", Name = "Router", Host = "192.168.178.1", Samples = s, Measured = measured };
NcTarget Net(List<NcSample> s) => new() { Id = "net", Label = "Internet", Name = "Internet", Host = NetCheckLogic.Anchor, Samples = s };
NcTarget Srv(List<NcSample> s, string mode = "icmp", int interval = 500, string name = "Nova City", bool measured = true)
    => new() { Id = "srv", Label = "Server", Name = name, Host = "46.203.182.16", Port = 30120, Mode = mode, IntervalMs = interval, Samples = s, Measured = measured };
var when = new DateTime(2026, 11, 22, 14, 5, 0);
NcResult Run(NcTarget gw, NcTarget net, NcTarget? srv, bool? wifi, Func<NcInput, NcInput>? more = null)
{
    var x = new NcInput { Gw = gw, Net = net, Srv = srv, Wifi = wifi, When = when, User = "Max", ServerAddr = srv != null ? "cfx.re/join/abc123" : "" };
    return NetCheckLogic.Evaluate(more != null ? more(x) : x);
}
var all = new List<(string name, NcResult r)>();
NcResult Keep(string name, NcResult r) { all.Add((name, r)); return r; }

// ---------- numbers ----------
Eq(NetCheckLogic.Median(new double[] { 1, 3, 9 }), 3.0, "median of an odd count");
Eq(NetCheckLogic.Median(new double[] { 1, 3, 5, 9 }), 4.0, "median of an even count");
Eq(NetCheckLogic.Median(Array.Empty<double>()), 0.0, "median of nothing");
Eq(NetCheckLogic.Percentile(Enumerable.Range(1, 20).Select(i => (double)i).ToList(), 95), 19.0, "p95 of 1..20 (nearest rank)");
Eq(NetCheckLogic.Percentile(Enumerable.Range(1, 100).Select(i => (double)i).ToList(), 95), 95.0, "p95 of 1..100");
Eq(NetCheckLogic.Percentile(new double[] { 7 }, 95), 7.0, "p95 of one value");
Check(Math.Abs(NetCheckLogic.Jitter(new double[] { 10, 12, 11, 15 }) - 7 / 3.0) < 1e-9, "jitter = mean absolute difference of consecutive pings");
Eq(NetCheckLogic.Jitter(new double[] { 10 }), 0.0, "jitter of one ping");
Eq(NetCheckLogic.SpikeLimit(4), 34.0, "spike limit: at least 30 ms above a small median");
Eq(NetCheckLogic.SpikeLimit(40), 80.0, "spike limit: double a large median");
Eq(NetCheckLogic.Spikes(new double[] { 5, 5, 50, 60, 5, 5, 70, 5 }, 5), 2, "consecutive high pings count as one spike");
Eq(NetCheckLogic.Spikes(new double[] { 5, 34, 5 }, 4), 0, "the limit itself is no spike");
Eq(NetCheckLogic.Spikes(new double[] { 5, 35, 5 }, 4), 1, "above the limit is a spike");

var st = NetCheckLogic.Stats(Seq(10, 12, null, 11, 15));
Eq(st.Sent, 5, "stats: sent"); Eq(st.Answered, 4, "stats: answered"); Eq(st.Loss, 20.0, "stats: loss in percent");
Eq(st.Median, (double?)11.5, "stats: median of the answers"); Eq(st.Max, (double?)15.0, "stats: max");
Eq(st.Jitter, (double?)2.3, "stats: jitter skips the lost ping and is rounded");
Eq(st.Seconds, 2.5, "stats: five pings at 2 per second are 2.5 s");
var none = NetCheckLogic.Stats(new List<NcSample>());
Check(none.Sent == 0 && none.Median == null && none.Jitter == null && none.Loss == 0, "stats of nothing");
var dead = NetCheckLogic.Stats(Silent(10));
Check(dead.Answered == 0 && dead.Loss == 100 && dead.Median == null && dead.P95 == null, "stats without any answer");
Eq(NetCheckLogic.Stats(Seq(5, double.NaN, -1, 5)).Answered, 2, "NaN and negative values are no answers");
Eq(NetCheckLogic.Stats(Seq(5)).Jitter, (double?)null, "one answer has no jitter");
Eq(NetCheckLogic.Stats(Gen(30, lost: new[] { 1, 2, 3, 4 }), 500).Loss, 3.3, "4 of 120 lost = 3.3 %");
Eq(NetCheckLogic.Stats(Gen(30, n: 57, interval: 1000), 1000).Seconds, 57.0, "port measurements once per second");

// ---------- one stage ----------
var calm = NetCheckLogic.Stats(Gen(3));
Eq(NetCheckLogic.Level("gw", calm), ("ok", ""), "a calm router");
var zig = NetCheckLogic.Stats(Seq(Enumerable.Range(0, 60).Select(i => (double?)(i % 2 == 0 ? 2 : 14)).ToArray()));
var (zl, zw) = NetCheckLogic.Level("gw", zig);
Eq(zl, "bad", "router jitter 12 ms is bad"); Has(zw, "einen Jitter von 12 ms", "why names the jitter");
Eq(NetCheckLogic.Level("srv", NetCheckLogic.Stats(Seq(Enumerable.Range(0, 60).Select(i => (double?)(i % 2 == 0 ? 30 : 38)).ToArray()))).level, "warn", "server jitter 8 ms is a wobble");
Eq(NetCheckLogic.Level("gw", NetCheckLogic.Stats(Seq(Enumerable.Range(0, 60).Select(i => (double?)(i % 2 == 0 ? 2 : 7)).ToArray()))).level, "warn", "router jitter 5 ms is a wobble");
Eq(NetCheckLogic.Level("srv", NetCheckLogic.Stats(Seq(Enumerable.Range(0, 60).Select(i => (double?)(i % 2 == 0 ? 30 : 35)).ToArray()))).level, "ok", "server jitter 5 ms is fine");
var (l2, w2) = NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(3, lost: new[] { 10, 70 })));
Eq(l2, "warn", "2 of 120 lost is a wobble"); Has(w2, "1,7 % Verlust", "why names the loss in German");
Eq(NetCheckLogic.Level("srv", NetCheckLogic.Stats(Gen(30, lost: new[] { 1 }))).level, "ok", "one lost ping in a minute is fine");
Eq(NetCheckLogic.Level("srv", NetCheckLogic.Stats(Gen(30, lost: new[] { 5, 25, 45, 65 }))).level, "bad", "3.3 % loss is bad");
var sp6 = NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(3, spikes: new[] { 5, 25, 45, 65, 85, 105 })));
Eq(sp6.level, "bad", "six spikes a minute are bad"); Has(sp6.why, "6 Ping-Spitzen bis 193 ms", "why names the spikes and the highest");
Eq(NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(3, spikes: new[] { 5, 45, 85 }, spikeMs: 40))).level, "warn", "three small spikes a minute are a wobble");
Eq(NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(3, spikes: new[] { 5, 45, 85 }))).level, "bad", "three big spikes a minute drive the jitter up: bad");
Eq(NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(3, spikes: new[] { 50 }))).level, "ok", "one spike a minute is fine");
Has(NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(3, spikes: new[] { 5, 45, 85 }, spikeMs: 40))).why, "3 Ping-Spitzen bis 43 ms", "plural");
Has(NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(3, spikes: new[] { 5 }, spikeMs: 40, n: 30))).why, "eine Ping-Spitze bis 43 ms", "singular (one spike in 15 s)");
var bloat = NetCheckLogic.Stats(Seq(Enumerable.Range(0, 120).Select(i => (double?)(i >= 24 && i <= 40 ? 94 : 14)).ToArray()));
Eq(bloat.Spikes, 1, "a long high stretch is one spike"); Eq(bloat.High, 17, "but 17 high answers");
var bl = NetCheckLogic.Level("net", bloat);
Eq(bl.level, "bad", "a full line for 8.5 s in a minute is bad"); Eq(bl.why, "9 Sekunden lang stark erhöhten Ping bis 94 ms", "why names the stretch, not the one spike");
Eq(NetCheckLogic.Level("net", NetCheckLogic.Stats(Seq(Enumerable.Range(0, 120).Select(i => (double?)(i >= 24 && i <= 28 ? 94 : 14)).ToArray()))).level, "warn", "2.5 s high is a wobble");
Hasnt(NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(3, spikes: new[] { 5 }, spikeMs: 40, n: 30))).why, "stark erhöht", "one short spike is named once, as a spike");
Eq(NetCheckLogic.Sec(1), "eine Sekunde", "one second"); Eq(NetCheckLogic.Sec(8.5), "9 Sekunden", "seconds round half up"); Eq(NetCheckLogic.Sec(0.2), "eine Sekunde", "at least one");
Eq(NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(15))).level, "warn", "15 ms to the router is a busy WLAN");
var med35 = NetCheckLogic.Level("gw", NetCheckLogic.Stats(Gen(35)));
Eq(med35.level, "bad", "35 ms to the router is bad"); Has(med35.why, "im Mittel 35 ms Ping", "why names the plain ping");
Eq(NetCheckLogic.Level("srv", NetCheckLogic.Stats(Gen(150))).level, "ok", "a far but calm server is no problem (distance)");
Eq(NetCheckLogic.Level("net", NetCheckLogic.Stats(Gen(150))).level, "ok", "a far but calm anchor is no problem");
Eq(NetCheckLogic.Level("srv", NetCheckLogic.Stats(Silent(10))), ("silent", "keine Antwort"), "no answer after 10 tries");
Eq(NetCheckLogic.Level("srv", NetCheckLogic.Stats(Silent(3))).level, "none", "three tries are too few to say");
Eq(NetCheckLogic.Level("srv", calm, measured: false).level, "none", "not measured");
var both = NetCheckLogic.Level("srv", NetCheckLogic.Stats(Gen(30, spikes: new[] { 5, 25, 45, 65, 85, 105 }, lost: new[] { 2, 22, 42, 62, 82 })));
Eq(both.level, "bad", "loss and spikes"); Has(both.why, " und ", "two worst reasons are joined");
Eq(NetCheckLogic.Rank("gw", "silent"), (int?)null, "a router that ignores pings is unknown, not bad");
Eq(NetCheckLogic.Rank("srv", "silent"), (int?)2, "a server that does not answer is bad");
Eq(NetCheckLogic.Rank("net", "warn"), (int?)1, "warn ranks 1");
Eq(NetCheckLogic.Rank("srv", "none"), (int?)null, "not measured ranks nothing");

// ---------- switching a silent server to its port ----------
Check(NetCheckLogic.SwitchToTcp(6, 0, 3, 30120), "6 silent pings while the internet answers: measure the port");
Check(!NetCheckLogic.SwitchToTcp(5, 0, 3, 30120), "5 tries are too few");
Check(!NetCheckLogic.SwitchToTcp(6, 1, 3, 30120), "one answer: stay with ping");
Check(!NetCheckLogic.SwitchToTcp(6, 0, 0, 30120), "nothing answers at all: no switch (no internet)");
Check(!NetCheckLogic.SwitchToTcp(6, 0, 3, 0) && !NetCheckLogic.SwitchToTcp(6, 0, 3, 70000), "no port: no switch");

// ---------- verdicts ----------
int[] wSp = { 8, 9, 30, 31, 52, 70, 71, 92, 110 }, wLost = { 20, 60, 100, 101 };
var wGw = Gen(3, 1.5, wSp, 180, wLost, seed: 1); var wNet = Gen(15, 1.5, wSp, 180, wLost, seed: 2); var wSrv = Gen(33, 2, wSp, 180, wLost, seed: 3);

var home = Keep("home wifi", Run(Gw(wGw), Net(wNet), Srv(wSrv), true));
Eq(home.Kind, "home", "WLAN spikes on every stage: it starts at home"); Eq(home.Start, "gw", "start = router"); Check(!home.Mild, "not mild");
Eq(home.Title, "Das Problem fängt in deinem WLAN an", "WLAN title");
Has(home.Text, "Schon zwischen deinem PC und deinem Router gab es", "home text"); Has(home.Text, "bis zum Server durch", "home text names the server");
Check(home.Advice.Any(a => a.Contains("LAN-Kabel")) && home.Advice.Any(a => a.Contains("5-GHz")) && home.Advice.Any(a => a.Contains("näher an den Router")), "WLAN advice: cable, 5 GHz, closer");
Check(home.Targets[0].Start && !home.Targets[1].Start && !home.Targets[2].Start, "only the router row is marked");
Eq(string.Join(",", home.Targets.Select(t => t.Level)), "bad,bad,bad", "all three bad");
Eq(home.Targets[0].Trace.Count, 120, "trace carries every sample");
Check(home.Targets[0].Trace[20][1] == null && home.Targets[0].Trace[21][1] != null && home.Targets[0].Trace[1][0] == 0.5, "trace: [at, ms], lost = null");
Eq(home.Seconds, 60.0, "a full minute");
Eq(home.Notes.Count, 0, "no notes for a plain WLAN problem");
Eq(home.Wifi, (bool?)true, "wifi passed through");

var cable = Keep("home cable", Run(Gw(wGw), Net(wNet), Srv(wSrv), false));
Eq(cable.Kind, "home", "cable: still home"); Eq(cable.Title, "Das Problem fängt zwischen PC und Router an", "cable title");
Has(cable.Text, "Über Kabel ist das ungewöhnlich.", "cable text");
Check(cable.Advice.Any(a => a.Contains("anderes LAN-Kabel")) && !cable.Advice.Any(a => a.Contains("5-GHz")), "cable advice: no WLAN tips");
Hasnt(Run(Gw(wGw), Net(wNet), Srv(wSrv), null).Text, "Kabel", "unknown link: no cable claim");

var prov = Keep("provider", Run(Gw(Gen(2.5, seed: 4)), Net(wNet), Srv(wSrv), false));
Eq(prov.Kind, "provider", "calm router, wobbly internet and server: provider"); Eq(prov.Start, "net", "start = internet");
Eq(prov.Title, "Das Problem fängt hinter deinem Router an", "provider title");
Has(prov.Text, "Bis zu deinem Router ist alles ruhig, erst dahinter gab es", "provider text");
Check(prov.Advice.Any(a => a.Contains("anderen Uhrzeit")) && prov.Advice.Any(a => a.Contains("Anbieter")), "provider advice: other time, contact the provider");

var srvP = Keep("server", Run(Gw(Gen(2.5, seed: 4)), Net(Gen(14, seed: 5)), Srv(Gen(35, 1, lost: new[] { 3, 20, 40, 41, 70, 90 }, seed: 6)), false));
Eq(srvP.Kind, "server", "only the server loses packets: server or route"); Eq(srvP.Start, "srv", "start = server");
Eq(srvP.Title, "Das Problem liegt beim Server oder auf dem Weg dorthin", "server title");
Has(srvP.Text, "5 % Verlust", "server text names the loss");
Check(srvP.Advice.Any(a => a.Contains("nichts ändern")) && srvP.Advice.Any(a => a.Contains("Server-Admins")), "server advice: nothing to fix here, tell the admins");

var ok = Keep("ok cable", Run(Gw(Gen(1.2, 0.3, seed: 8)), Net(Gen(12, seed: 9)), Srv(Gen(28, seed: 10)), false));
Eq(ok.Kind, "ok", "all calm"); Eq(ok.Start, "", "no start"); Eq(ok.Title, "Deine Verbindung war ruhig", "ok title");
Has(ok.Text, "In diesen 60 Sekunden gab es bis zum Server keinen nennenswerten Verlust", "ok text");
Eq(ok.Notes.Count, 0, "calm over cable: no notes");
Check(ok.Advice.Any(a => a.Contains("FPS-Einbrüchen")), "ok advice says what the test does not show");
var okW = Keep("ok wifi", Run(Gw(Gen(3, seed: 8)), Net(Gen(12, seed: 9)), Srv(Gen(28, seed: 10)), true));
Check(okW.Kind == "ok" && okW.Notes.Any(n => n.Contains("per WLAN verbunden")), "calm over WLAN: cable hint as a note");

var noisy = Keep("router noisy", Run(Gw(Gen(3, 1, new[] { 5, 15, 25, 35, 45, 55, 65 }, 120, seed: 11)), Net(Gen(12, seed: 9)), Srv(Gen(28, seed: 10)), true));
Eq(noisy.Kind, "ok", "a router that answers pings unevenly while everything behind it is calm: no problem");
Check(noisy.Notes.Any(n => n.Contains("Router beantwortet Pings unruhig") && n.Contains("Ping-Spitzen")), "router noise is explained");
Eq(noisy.Targets[0].Level, "bad", "the router row still shows its own numbers honestly");
Eq(noisy.Targets[0].Holds, "ok", "but nothing of it holds up to the server (the row's chip follows the verdict)");
Eq(string.Join(",", home.Targets.Select(t => t.Holds)), "bad,bad,bad", "a WLAN problem holds on every stage");
Eq(string.Join(",", prov.Targets.Select(t => t.Holds)), "ok,bad,bad", "provider: holds from the internet on");

var netOnly = Keep("anchor only", Run(Gw(Gen(2, seed: 4)), Net(Gen(12, 1, new[] { 5, 25, 45, 65, 85, 105 }, 150, seed: 12)), Srv(Gen(28, seed: 10)), false));
Eq(netOnly.Kind, "ok", "only 1.1.1.1 wobbles, the server is calm: ok for FiveM");
Check(netOnly.Notes.Any(n => n.StartsWith("Nur bis 1.1.1.1 gab es")), "anchor note");
Check(netOnly.Targets[1].Level == "bad" && netOnly.Targets[1].Holds == "ok", "anchor row: bad on its own, holds nothing");
var netDead = Keep("anchor silent", Run(Gw(Gen(2, seed: 4)), Net(Silent()), Srv(Gen(28, seed: 10)), false));
Eq(netDead.Kind, "ok", "1.1.1.1 blocked but the server answers: ok");
Check(netDead.Notes.Any(n => n.Contains("antwortet in deinem Netz nicht auf Ping")), "blocked anchor note");

var gwSil = Keep("router silent", Run(Gw(Silent()), Net(wNet), Srv(wSrv), true));
Eq(gwSil.Kind, "provider", "router ignores pings, internet and server bad: from the line on");
Eq(gwSil.Title, "Das Problem liegt bei deinem Anschluss", "title without the router");
Has(gwSil.Text, "nicht trennen", "says it cannot tell home from provider");
Check(gwSil.Notes.Any(n => n.Contains("Dein Router antwortet nicht auf Ping")), "silent router note");
Check(gwSil.Advice.Any(a => a.Contains("mit LAN-Kabel")), "WLAN: suggests a cable test to tell them apart");
Eq(gwSil.Targets[0].Level, "silent", "router row: silent");

var offline = Keep("offline", Run(Gw(Gen(2, seed: 4)), Net(Silent()), Srv(Silent()), true));
Eq(offline.Kind, "provider", "router answers, nothing behind it"); Eq(offline.Title, "Hinter deinem Router kommt nichts an", "offline title");
Check(offline.Advice.Any(a => a.Contains("Störung")), "offline advice: outage");
var nothing = Keep("nothing", Run(Gw(Silent()), Net(Silent()), Srv(Silent()), true));
Eq(nothing.Kind, "none", "nothing answers"); Eq(nothing.Title, "Keine Verbindung", "none title");
Check(!nothing.Notes.Any(n => n.Contains("Router antwortet nicht")), "no router note when nothing answers");

var srvDead = Keep("server silent", Run(Gw(Gen(2, seed: 4)), Net(Gen(12, seed: 9)), Srv(Silent(57, 1000), "tcp", 1000), false, x => new NcInput { Gw = x.Gw, Net = x.Net, Srv = x.Srv, Wifi = x.Wifi, When = x.When, User = x.User, ServerAddr = x.ServerAddr, SrvSwitched = true }));
Eq(srvDead.Kind, "server", "server answers neither ping nor port"); Eq(srvDead.Title, "Dein Server antwortet nicht", "silent server title");
Has(srvDead.Text, "Port 30120", "names the port");
Check(srvDead.Notes.Any(n => n.Contains("Verbindungsaufbau zu Port 30120, einmal pro Sekunde")), "tcp note");
Has(srvDead.Report, "keine Antwort auf 57 Verbindungsaufbauten zu Port 30120", "report: silent server line");

var tcp = Keep("server tcp", Run(Gw(Gen(2, seed: 4)), Net(Gen(12, seed: 9)), Srv(Gen(31, n: 57, interval: 1000, seed: 13), "tcp", 1000), false, x => new NcInput { Gw = x.Gw, Net = x.Net, Srv = x.Srv, Wifi = x.Wifi, When = x.When, User = x.User, ServerAddr = x.ServerAddr, SrvSwitched = true }));
Eq(tcp.Kind, "ok", "calm over the port"); Eq(tcp.Seconds, 60.0, "the run is a minute even if the port started later");
Eq(tcp.Targets[2].Stats.Seconds, 57.0, "the port was measured 57 s"); Eq(tcp.Targets[2].Mode, "tcp", "mode tcp");
Has(tcp.Report, "gemessen über Port 30120", "report says how the server was measured");

var shortR = Keep("short", Run(Gw(Gen(3, n: 20)), Net(Gen(12, n: 20)), Srv(Gen(30, n: 20)), true));
Eq(shortR.Kind, "short", "10 s is too short"); Eq(shortR.Title, "Zu kurz für ein Urteil", "short title");
Eq(Run(Gw(Gen(3, n: 30)), Net(Gen(12, n: 30)), Srv(Gen(30, n: 30)), true).Kind, "ok", "15 s are enough");

List<NcSample> Wob(double b, int seed) => Seq(Enumerable.Range(0, 120).Select(i => (double?)(b + (i % 2 == 0 ? 0 : 7) + new Random(seed + i).NextDouble() * 0.2)).ToArray());
var mild = Keep("home mild", Run(Gw(Wob(2, 1)), Net(Wob(12, 2)), Srv(Wob(30, 3)), true));
Eq(mild.Kind, "home", "a wobble on every stage starts at home"); Check(mild.Mild, "mild");
Eq(mild.Title, "Dein WLAN schwankt leicht", "mild WLAN title"); Has(mild.Text, "noch kein großes Problem", "mild text");
var provPlus = Keep("provider + router wobble", Run(Gw(Wob(2, 1)), Net(wNet), Srv(wSrv), true));
Eq(provPlus.Kind, "provider", "bad from the internet on, a wobble before");
Check(provPlus.Notes.Any(n => n.StartsWith("Leicht schwankt es schon bis zum Router")), "the smaller home wobble is noted");
Eq(provPlus.Targets[0].Holds, "warn", "the router's wobble holds up to the server");

var noSrv = Keep("no server", Run(Gw(wGw), Net(wNet), null, true, x => new NcInput { Gw = x.Gw, Net = x.Net, Wifi = x.Wifi, When = x.When, User = x.User, NoServer = true }));
Eq(noSrv.Kind, "home", "WLAN problem without a server"); Has(noSrv.Text, "bis ins Internet durch", "names the internet as the end");
Check(noSrv.Notes.Any(n => n.StartsWith("Ohne Server gemessen")), "no server note"); Eq(noSrv.Targets.Count, 2, "two rows without a server");
var hidden = Keep("hidden", Run(Gw(Gen(2, seed: 4)), Net(Gen(12, seed: 9)), Srv(new List<NcSample>(), measured: false), true, x => new NcInput { Gw = x.Gw, Net = x.Net, Srv = x.Srv, Wifi = x.Wifi, When = x.When, User = x.User, SrvHidden = true }));
Check(hidden.Notes.Any(n => n.Contains("hält seine Adresse geheim")), "hidden server note"); Eq(hidden.Targets[2].Level, "none", "server row not measured");
Has(hidden.Text, "bis ins Internet", "hidden: judged up to the internet");
Check(hidden.Advice.Any(a => a.Contains("ohne Server nicht")) && !hidden.Advice.Any(a => a.StartsWith("Ruckelt FiveM trotzdem, liegt es eher am Server")), "calm without a server: does not blame the server");
Has(hidden.Report, "nicht gemessen", "report: server not measured"); Eq(hidden.Targets[2].Holds, "none", "a server that was not measured holds nothing");
var skipped = Keep("skipped", Run(Gw(Gen(2, seed: 4)), Net(Gen(12, seed: 9)), Srv(new List<NcSample>(), measured: false), false, x => new NcInput { Gw = x.Gw, Net = x.Net, Srv = x.Srv, Wifi = x.Wifi, When = x.When, User = x.User, SrvSkipped = "Cfx.re kennt diesen Server nicht" }));
Check(skipped.Notes.Contains("Cfx.re kennt diesen Server nicht, deshalb wurden nur Router und Internet gemessen."), "skip reason note");
var proxy = Keep("proxy", Run(Gw(Gen(2, seed: 4)), Net(Gen(12, seed: 9)), Srv(Gen(20, seed: 10)), false, x => new NcInput { Gw = x.Gw, Net = x.Net, Srv = x.Srv, Wifi = x.Wifi, When = x.When, User = x.User, SrvProxy = true, SrvProxyHost = "play.example.com" }));
Check(proxy.Notes.Any(n => n.StartsWith("Der Server ist über play.example.com erreichbar")), "proxy note");
var far = Keep("far", Run(Gw(Gen(2, seed: 4)), Net(Gen(12, seed: 9)), Srv(Gen(140, seed: 10)), false));
Check(far.Kind == "ok" && far.Notes.Any(n => n.Contains("im Mittel 140 ms") && n.Contains("Entfernung")), "far server: distance note");
var noGw = Keep("no gateway", Run(Gw(new List<NcSample>(), measured: false), Net(Gen(12, seed: 9)), Srv(Gen(28, seed: 10)), false));
Check(noGw.Kind == "ok" && noGw.Notes.Any(n => n.StartsWith("Kein Router gefunden")), "no gateway note");

// 1.1.1.1 blocked in this network while the server answers: the anchor says nothing, provider and server cannot be told apart
var srvBad = Gen(33, 2, wSp, 180, wLost, seed: 3);
var blind = Keep("anchor blind, server bad", Run(Gw(Gen(2, seed: 4)), Net(Silent()), Srv(srvBad), false));
Eq(blind.Kind, "server", "anchor blocked, server bad: judged from the server on"); Eq(blind.Start, "srv", "start = the first stage that shows it");
Eq(blind.Title, "Das Problem fängt hinter deinem Router an", "anchor blocked: behind the router, nothing more");
Has(blind.Text, "lässt sich nicht trennen, ob es an der Strecke deines Anbieters oder am Server liegt", "says provider and server cannot be told apart");
Hasnt(blind.Text, "das ist die Strecke deines Internetanbieters", "does not blame the provider");
Hasnt(blind.Text, "Bis ins Internet ist alles ruhig", "does not claim the internet stage was calm");
Check(blind.Advice.Any(a => a.Contains("andere Spieler")) && blind.Advice.Any(a => a.Contains("Server-Admins oder deinem Anbieter")), "anchor blocked: advice for both sides");
Check(!blind.Notes.Any(n => n.Contains("antwortet in deinem Netz nicht auf Ping")), "the text says it, no second note");
Eq(blind.Targets[1].Level, "silent", "internet row: no answer"); Eq(blind.Targets[1].Holds, "silent", "a blocked anchor holds nothing it could say"); Check(!blind.Targets[1].Start, "the silent anchor is not marked as the start");
var blindW = Keep("anchor and router blind", Run(Gw(Silent()), Net(Silent()), Srv(srvBad), true));
Eq(blindW.Kind, "server", "router and anchor blocked, server bad"); Eq(blindW.Title, "Auf dem Weg zum Server hakt es", "nothing before the server answers: no place named");
Has(blindW.Text, "lässt sich nicht sagen, wo auf dem Weg es anfängt", "router and anchor blocked: where it starts is unknown");
Check(blindW.Advice.Any(a => a.Contains("mit LAN-Kabel")), "WLAN: a cable test tells the home part apart");
var blindM = Keep("anchor blind, server wobbles", Run(Gw(Gen(2, seed: 4)), Net(Silent()), Srv(Wob(30, 3)), false));
Check(blindM.Kind == "server" && blindM.Mild && blindM.Title == "Leichte Schwankungen hinter deinem Router", "anchor blocked, a wobble at the server: mild");
var blindH = Keep("anchor blind, wifi bad", Run(Gw(wGw), Net(Silent()), Srv(srvBad), true));
Eq(blindH.Kind, "home", "anchor blocked, WLAN bad up to the server: still the WLAN");
Check(blindH.Notes.Any(n => n.StartsWith("1.1.1.1 antwortet in deinem Netz nicht auf Ping")), "home verdict: the blocked anchor is a note");
Eq(Run(Gw(Gen(2, seed: 4)), Net(Silent()), null, false).Title, "Hinter deinem Router kommt nichts an", "anchor silent without a server: nothing gets through");

// a server measured by ping is the last stage: nothing behind it confirms it, so the verdict says so
Check(srvP.Notes.Any(n => n.StartsWith("Dein Server wurde per Ping gemessen")), "server verdict by ping: caveat note");
var tcpBad = Keep("server tcp bad", Run(Gw(Gen(2, seed: 4)), Net(Gen(12, seed: 9)), Srv(Gen(35, 1, lost: new[] { 3, 20, 40 }, n: 57, interval: 1000, seed: 6), "tcp", 1000), false, x => new NcInput { Gw = x.Gw, Net = x.Net, Srv = x.Srv, Wifi = x.Wifi, When = x.When, User = x.User, ServerAddr = x.ServerAddr, SrvSwitched = true }));
Eq(tcpBad.Kind, "server", "port measurements with loss: server");
Check(!tcpBad.Notes.Any(n => n.StartsWith("Dein Server wurde per Ping gemessen")), "measured by its port: no ping caveat");
Check(!srvDead.Notes.Any(n => n.StartsWith("Dein Server wurde per Ping gemessen")) && !ok.Notes.Any(n => n.StartsWith("Dein Server wurde per Ping")), "no caveat for a silent server or a calm verdict");

// the way to the internet through another connection (a VPN)
var vpn = Keep("vpn provider", Run(Gw(Gen(2.5, seed: 4)), Net(wNet), Srv(wSrv), false, x => new NcInput { Gw = x.Gw, Net = x.Net, Srv = x.Srv, Wifi = x.Wifi, When = x.When, User = x.User, ServerAddr = x.ServerAddr, Tunnel = true }));
Eq(vpn.Kind, "provider", "VPN: the verdict stays the same"); Has(vpn.Text, "die Strecke deines Anbieters und deines VPN.", "VPN: the provider text names the VPN too");
Check(vpn.Notes.Any(n => n.Contains("zum Beispiel ein VPN") && n.Contains("teste einmal ohne VPN")), "VPN note");
Check(!prov.Notes.Any(n => n.Contains("VPN")), "no VPN note without a tunnel");
Check(!Run(Gw(Silent()), Net(Silent()), Srv(Silent()), false, x => new NcInput { Gw = x.Gw, Net = x.Net, Srv = x.Srv, Wifi = x.Wifi, When = x.When, User = x.User, Tunnel = true }).Notes.Any(n => n.Contains("VPN")), "no VPN note without any answer");

// every verdict: honest copy, no placebo, German sentences
foreach (var (name, r) in all)
{
    var words = string.Join(" ", new[] { r.Title, r.Text }.Concat(r.Advice).Concat(r.Notes));
    Check(!words.Contains("DNS") && !words.Contains("Tweak") && !words.Contains("Boost") && !words.Contains("optimier", StringComparison.OrdinalIgnoreCase), $"{name}: no placebo promises");
    Check(r.Text.EndsWith('.') && r.Advice.All(a => a.EndsWith('.') || a.EndsWith(')')) && r.Notes.All(n => n.EndsWith('.')), $"{name}: full sentences");
    Check(!r.Title.EndsWith('.') && r.Title.Length < 60, $"{name}: short title without a dot");
    Check(r.Advice.Count is >= 1 and <= 4, $"{name}: one to four pieces of advice");
    Check(!words.Contains("  ") && !words.Contains(" ,") && !words.Contains(" ."), $"{name}: no broken spacing");
    Hasnt(r.Report, "Max", $"{name}: report without the user name");
    Has(r.Report, "OrbitTweak Netz-Check · 22.11.2026, 14:05 Uhr", $"{name}: report head");
}

// ---------- the copied text ----------
Has(home.Report, "Verbindung: WLAN", "report: link"); Has(cable.Report, "Verbindung: Kabel", "report: cable");
Has(home.Report, "Ergebnis: Das Problem fängt in deinem WLAN an", "report: verdict");
Has(home.Report, "Router (192.168.178.1): Ping ", "report: router line");
Has(home.Report, "Internet (1.1.1.1): Ping ", "report: internet line");
Has(home.Report, "Server Nova City (cfx.re/join/abc123 · 46.203.182.16): Ping ", "report: server line with the join address and the IP");
Has(home.Report, "(4 von 120)", "report: lost count"); Has(home.Report, "← hier fängt es an", "report marks the start");
Eq(home.Report.Split('\n').Count(l => l.Contains("← hier fängt es an")), 1, "one start mark");
Has(home.Report, "Gemessen: zwei Pings pro Sekunde je Strecke", "report explains the method");
var named = Run(Gw(Gen(2, seed: 4)), Net(Gen(12, seed: 9)), Srv(Gen(28, seed: 10), name: "Max RP"), false);
Has(named.Report, "Server <Nutzer> RP", "user name in a server name is replaced");
Eq(NetCheckLogic.Redact("Maximal MAX max. C:\\Users\\Max\\x", "Max"), "Maximal <Nutzer> <Nutzer>. C:\\Users\\<Nutzer>\\x", "redact: whole words, any case");
// the fixed wording is never touched: only server names, addresses and host names are the user's to share
var pcUser = NetCheckLogic.Evaluate(new NcInput { Gw = Gw(wGw), Net = Net(wNet), Srv = Srv(wSrv, name: "PC Arena"), Wifi = true, When = when, User = "PC", ServerAddr = "pc.example.com:30120" });
Has(pcUser.Report, "Schon zwischen deinem PC und deinem Router", "a user called PC keeps the sentence");
Has(pcUser.Report, "Server <Nutzer> Arena (<Nutzer>.example.com:30120 · 46.203.182.16)", "but not in the server name and address");
var proxyUser = NetCheckLogic.Evaluate(new NcInput { Gw = Gw(Gen(2, seed: 4)), Net = Net(Gen(12, seed: 9)), Srv = Srv(Gen(20, seed: 10)), Wifi = false, When = when, User = "kevin", SrvProxy = true, SrvProxyHost = "kevin-rp.example.com" });
Check(proxyUser.Notes.Any(n => n.Contains("über <Nutzer>-rp.example.com erreichbar")) && !proxyUser.Report.Contains("kevin"), "a host name in a note is redacted too");
var skipUser = NetCheckLogic.Evaluate(new NcInput { Gw = Gw(Gen(2, seed: 4)), Net = Net(Gen(12, seed: 9)), Srv = Srv(new List<NcSample>(), measured: false), Wifi = false, When = when, User = "kevin", SrvSkipped = "Die Adresse kevin.example.com ließ sich nicht auflösen" });
Check(!skipUser.Report.Contains("kevin") && skipUser.Notes.Any(n => n.Contains("<Nutzer>.example.com")), "a host name in the skip reason is redacted");
Eq(NetCheckLogic.Redact("Mr. X", "X"), "Mr. X", "redact: a one-letter user name is left alone");
Eq(NetCheckLogic.Redact("a (b) c", "(b)"), "a <Nutzer> c", "redact: regex characters are escaped");
Eq(NetCheckLogic.Redact("Jörg spielt", "jörg"), "<Nutzer> spielt", "redact: umlauts");

// ---------- addresses ----------
Eq(NetCheckLogic.ParseEndpoint("46.203.182.16:30120"), new NcEndpoint("46.203.182.16", 30120, false, false), "ip:port");
Eq(NetCheckLogic.ParseEndpoint("220.48.96.137:30320")?.Port, (int?)30320, "another port");
Eq(NetCheckLogic.ParseEndpoint("https://play.hazerp.pt/"), new NcEndpoint("play.hazerp.pt", 443, true, false), "https endpoint = proxy, port 443");
Eq(NetCheckLogic.ParseEndpoint("https://private-placeholder.cfx.re/"), new NcEndpoint("private-placeholder.cfx.re", 443, true, true), "hidden address");
Eq(NetCheckLogic.ParseEndpoint("http://x.example.com:8080/")?.Port, (int?)8080, "http with a port");
Eq(NetCheckLogic.ParseEndpoint("[2001:db8::1]:30120"), new NcEndpoint("2001:db8::1", 30120, false, false), "[v6]:port");
Eq(NetCheckLogic.ParseEndpoint("2001:db8::1")?.Host, "2001:db8::1", "bare v6");
Eq(NetCheckLogic.ParseEndpoint("myserver.de"), new NcEndpoint("myserver.de", 30120, false, false), "host without port: 30120");
Eq(NetCheckLogic.ParseEndpoint(" 1.2.3.4:30120 ")?.Host, "1.2.3.4", "trimmed");
foreach (var bad in new[] { "", null, "host:0", "host:70000", "bad host:30120", "a/b:1", "-x.de:1", "https://", "host:12a", "[::1", "[]:30120", "x.de:", "javascript:alert(1)" })
    Eq(NetCheckLogic.ParseEndpoint(bad), (NcEndpoint?)null, $"no address: [{bad}]");
Eq(NetCheckLogic.CleanName("^1Nova^7City ^3| Roleplay"), "NovaCity | Roleplay", "colour codes out");
Eq(NetCheckLogic.CleanName("  a \t\n b  "), "a b", "spaces squeezed");
Eq(NetCheckLogic.CleanName(new string('x', 80)).Length, 60, "long names are cut to 60");
Check(NetCheckLogic.CleanName(new string('x', 80)).EndsWith('…'), "cut names end with an ellipsis");
Eq(NetCheckLogic.CleanName(null), "", "no name");
var emo = NetCheckLogic.CleanName(new string('x', 58) + "🔥🔥 Serious RP");
Check(!emo.Any(char.IsSurrogate) || emo.Where(char.IsHighSurrogate).Count() == emo.Where(char.IsLowSurrogate).Count(), "a cut never splits an emoji");
Check(emo.EndsWith('…') && !char.IsHighSurrogate(emo[^2]), "a cut name with an emoji at the cut ends cleanly");
Eq(NetCheckLogic.CleanName(new string('x', 57) + "🔥🔥 Serious RP"), new string('x', 57) + "🔥…", "an emoji before the cut stays whole");
Eq(NetCheckLogic.CleanName("a\u0007b"), "ab", "control characters out");

// ---------- German numbers ----------
Eq(NetCheckLogic.Ms(0.4), "<1 ms", "below 1 ms"); Eq(NetCheckLogic.Ms(4.06), "4,1 ms", "one decimal"); Eq(NetCheckLogic.Ms(4.0), "4 ms", "no ,0");
Eq(NetCheckLogic.Ms(42.4), "42 ms", "whole ms above 10"); Eq(NetCheckLogic.Ms(9.96), "10 ms", "rounding up to 10");
Eq(NetCheckLogic.Pct(0), "0 %", "no loss"); Eq(NetCheckLogic.Pct(2.5), "2,5 %", "loss with a comma"); Eq(NetCheckLogic.Pct(33.3), "33 %", "whole percent above 10");
Eq(NetCheckLogic.Pct(100), "100 %", "all lost");

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
