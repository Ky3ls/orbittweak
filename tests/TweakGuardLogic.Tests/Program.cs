using OrbitTweak.Core;
using static OrbitTweak.Core.TweakGuardLogic;

// Checks of the Tweak-Wächter's pure logic. Exit code 0 = all fine.
//   dotnet run --project tests/TweakGuardLogic.Tests

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }
void Eq<T>(T got, T want, string what) => Check(EqualityComparer<T>.Default.Equals(got, want), $"{what}: got [{got}], want [{want}]");
void Seq(IEnumerable<string> got, string want, string what) => Eq(string.Join(",", got), want, what);

var t0 = new DateTime(2026, 11, 22, 14, 0, 0);
Obs On(string id) => new(id, true, true, Read.On);
Obs Off(string id) => new(id, true, true, Read.Off);
Obs Unk(string id) => new(id, true, true, Read.Unknown);

// ---------- timing ----------
Check(MinGap <= ConfirmAfter, "the planned second look is far enough from the first to count");
Check(MinGap >= TimeSpan.FromSeconds(30), "two looks count only with some time between them");
Check(ConfirmAfter <= TimeSpan.FromMinutes(2), "the second look comes soon, not after the long interval");
Eq(Interval, TimeSpan.FromMinutes(15), "the usual rhythm is about 15 minutes");
Check(FirstDelay > TimeSpan.Zero && FirstDelay < TimeSpan.FromMinutes(1), "the first look comes soon after the start, not at once");
Check(GameRetry > TimeSpan.Zero && GameRetry < Interval, "while a game runs it asks again sooner than the usual rhythm");

// ---------- what is watched ----------
foreach (var id in new[] { "nothrottle", "bgbrake", "timer", "wifiscan", "winkey" })
{
    Check(AppHeld.Contains(id), $"{id} is OrbitTweak's own setting");
    Check(!Applies(id, true, null), $"{id} is never watched");
    Check(!Applies(id, true, true), $"{id} is never watched, also when recommended");
}
Eq(AppHeld.Count, 5, "exactly the five settings of OrbitTweak itself");
foreach (var id in new[] { "gpu", "winopt", "ult", "gm", "dvr", "hags", "vfx", "pagefile", "ducking", "nic", "delopt", "sticky", "accel", "usb", "kbd", "gbbtn", "langkey" })
{
    Check(!AppHeld.Contains(id), $"{id} is a Windows setting");
    Check(Applies(id, true, null), $"{id}: watched without a condition");
    Check(Applies(id, true, true), $"{id}: watched while its condition holds");
    Check(!Applies(id, true, false), $"{id}: not watched once its condition no longer holds");
    Check(!Applies(id, false, null), $"{id}: not watched when it cannot work on this PC");
    Check(!Applies(id, false, true), $"{id}: not usable wins over the condition");
}
Check(!Applies("gpu", true, false), "the GPU tweak with no second GPU any more is skipped");
// the condition: RecWhen, FiveM for FiveM's tweaks, a tweak's own requirement
foreach (bool? rec in new bool?[] { null, true, false })
{
    Eq(Condition(rec, false, false), rec, $"not a FiveM tweak: FiveM does not matter (RecWhen {rec})");
    Eq(Condition(rec, true, true), rec, $"FiveM there: RecWhen decides ({rec})");
    Eq(Condition(rec, true, false), (bool?)false, $"FiveM gone: never watched (RecWhen {rec})");
    Eq(Condition(rec, false, true, false), (bool?)false, $"own requirement gone: never watched (RecWhen {rec})");
    Eq(Condition(rec, false, true, true), rec, $"own requirement there: RecWhen decides ({rec})");
}
Check(!Applies("gpu", true, Condition(true, true, false)), "two GPUs but FiveM uninstalled: the GPU tweak is skipped");
Check(!Applies("nic", true, Condition(null, false, true, false)), "a new network driver without the power modes: skipped");
Check(Applies("nic", true, Condition(null, false, true, true)), "the adapter still has them: watched");
Check(Applies(FpsId, true, null), "the FPS limit is watched");

// ---------- two looks in a row ----------
{
    var s = new State();
    var r = Step(s, new[] { Off("accel") }, t0);
    Eq(r.Confirmed.Count, 0, "one look off: nothing reported yet");
    Check(s.Suspect.ContainsKey("accel") && !s.Pending.ContainsKey("accel"), "one look off: waiting for the second");
    Eq(s.Suspect["accel"], t0, "the first look's time is kept");
    Eq(Next(s, t0), t0 + ConfirmAfter, "the second look is planned a minute later");

    r = Step(s, new[] { Off("accel") }, t0.AddSeconds(10));
    Eq(r.Confirmed.Count, 0, "a second look too soon does not confirm");
    Eq(s.Suspect["accel"], t0, "too soon: the first time stays");
    Eq(Next(s, t0.AddSeconds(10)), t0 + ConfirmAfter, "too soon: the planned second look stays");

    r = Step(s, new[] { Off("accel") }, t0 + ConfirmAfter);
    Seq(r.Confirmed, "accel", "two looks off, a minute apart: reported");
    Check(s.Pending.ContainsKey("accel") && !s.Suspect.ContainsKey("accel"), "now pending, no longer waiting");
    Eq(s.Pending["accel"], t0, "pending since the first look");
    Eq(Next(s, t0 + ConfirmAfter), t0 + ConfirmAfter + Interval, "nothing waiting: the usual rhythm");

    r = Step(s, new[] { Off("accel") }, t0.AddMinutes(20));
    Eq(r.Confirmed.Count, 0, "still off: not reported a second time");
    Check(s.Pending.ContainsKey("accel"), "still off: still pending");
    Eq(s.Pending["accel"], t0, "still off: the time stays");

    r = Step(s, new[] { On("accel") }, t0.AddMinutes(35));
    Seq(r.Cleared, "accel", "on again: cleared");
    Eq(s.Pending.Count + s.Suspect.Count, 0, "on again: nothing left");
}
{
    var s = new State();
    Step(s, new[] { Off("dvr") }, t0);
    var r = Step(s, new[] { On("dvr") }, t0.AddMinutes(1));
    Check(r.Confirmed.Count == 0 && s.Suspect.Count == 0, "off then on: a transient read never nags");
    r = Step(s, new[] { Off("dvr") }, t0.AddMinutes(2));
    Check(r.Confirmed.Count == 0 && s.Suspect["dvr"] == t0.AddMinutes(2), "after on, a new off starts a new streak");
}
{
    var s = new State();
    Step(s, new[] { Off("ult") }, t0);
    Step(s, new[] { Unk("ult") }, t0.AddMinutes(1));
    Check(!s.Suspect.ContainsKey("ult"), "a look that could not read it breaks the streak");
    var r = Step(s, new[] { Off("ult") }, t0.AddMinutes(2));
    Check(r.Confirmed.Count == 0, "off, unreadable, off: not two in a row");
    r = Step(s, new[] { Off("ult") }, t0.AddMinutes(3));
    Seq(r.Confirmed, "ult", "then off once more: two in a row");
    Eq(s.Pending["ult"], t0.AddMinutes(2), "pending since the first look of the streak");
    r = Step(s, new[] { Unk("ult") }, t0.AddMinutes(20));
    Check(s.Pending.ContainsKey("ult") && r.Cleared.Count == 0, "an unreadable look leaves a pending one as it is");
}
{
    // the mark gone (switched off in OrbitTweak, "So lassen", "Alle zurücksetzen") or the condition gone
    var s = new State();
    Step(s, new[] { Off("gpu"), Off("nic"), Off("hags") }, t0);
    Step(s, new[] { Off("gpu"), Off("nic"), Off("hags") }, t0.AddMinutes(1));
    Eq(s.Pending.Count, 3, "three pending");
    var r = Step(s, new[] { new Obs("gpu", true, false, Read.Off), new Obs("nic", false, false, Read.Unknown), Off("hags") }, t0.AddMinutes(16));
    Seq(r.Cleared.OrderBy(x => x), "gpu,nic", "condition gone and mark gone: cleared");
    Seq(s.Pending.Keys, "hags", "the rest stays");
    r = Step(s, Array.Empty<Obs>(), t0.AddMinutes(31));
    Seq(r.Cleared, "hags", "not looked at any more: cleared");
    Eq(s.Pending.Count, 0, "nothing left");
}
{
    var s = new State();
    Step(s, new[] { new Obs("vfx", false, true, Read.Off) }, t0);
    Check(s.Suspect.Count == 0, "not marked: off means nothing (Windows default or the user's choice)");
    Step(s, new[] { new Obs("vfx", true, false, Read.Off) }, t0);
    Check(s.Suspect.Count == 0, "not applicable: off means nothing");
    Step(s, new[] { Off("kbd") }, t0);
    Step(s, new[] { Off("sticky") }, t0.AddSeconds(30));
    Check(!s.Suspect.ContainsKey("kbd") && s.Suspect.ContainsKey("sticky"), "waiting ones not in a look are dropped");
}
{
    var s = new State();
    var r = Step(s, new[] { Off("a"), On("a"), new Obs("", true, true, Read.Off) }, t0);
    Check(s.Suspect.ContainsKey("a") && s.Suspect.Count == 1, "a second look at the same id in one pass and empty ids are ignored");
    Step(s, new[] { Off("a"), Off("b") }, t0.AddMinutes(1));
    Eq(Next(s, t0.AddMinutes(1)), t0.AddMinutes(1) + ConfirmAfter, "a new waiting one: its second look is planned");
    Eq(Next(s, t0.AddMinutes(30)), t0.AddMinutes(30).AddSeconds(5), "a second look that is overdue (game ran): a few seconds later, never in the past");
    r = Step(s, new[] { Off("a"), Off("b") }, t0.AddMinutes(2));
    Seq(r.Confirmed, "b", "only the one that waited long enough is confirmed");
}
{
    // many: a whole day of looks with one tweak that stays off and one that flickers
    var s = new State(); var all = new List<string>(); var now = t0;
    for (int i = 0; i < 40; i++)
    {
        var r = Step(s, new[] { Off("accel"), i % 2 == 0 ? Off("gm") : On("gm") }, now);
        all.AddRange(r.Confirmed);
        now = Next(s, now);
    }
    Seq(all, "accel", "over a day: the one that stays off is reported once, the flickering one never");
}

// ---------- the quick look at the pending ones ----------
{
    var s = new State();
    s.Pending["accel"] = t0; s.Pending["dvr"] = t0; s.Pending["ult"] = t0; s.Pending["nic"] = t0; s.Suspect["kbd"] = t0;
    var c = Recheck(s, new[] { On("accel"), Off("dvr"), Unk("ult"), new Obs("nic", false, false, Read.Unknown), On("kbd"), On("other") });
    Seq(c.OrderBy(x => x), "accel,nic", "on again and mark gone: cleared");
    Seq(s.Pending.Keys.OrderBy(x => x), "dvr,ult", "off and unreadable stay");
    Check(s.Suspect.ContainsKey("kbd"), "the quick look does not touch waiting ones");
    Eq(Recheck(s, new[] { new Obs("dvr", true, false, Read.Off) }).Count, 1, "a condition that no longer holds: cleared");
}

// ---------- what the buttons act on ----------
{
    var s = new State();
    foreach (var id in new[] { "usb", "ult", FpsId, "accel" }) s.Pending[id] = t0;
    var order = new[] { "gpu", "ult", "dvr", "accel", "usb", FpsId };
    Seq(Pick(s, new[] { "usb", "ult" }, order), "ult,usb", "list order: the power plan before USB power saving");
    Seq(Pick(s, new[] { FpsId, "usb", "accel", "ult" }, order), "ult,accel,usb," + FpsId, "all, the FPS limit last");
    Seq(Pick(s, new[] { "dvr", "gpu", "nope" }, order), "", "only pending ones, never others");
    Seq(Pick(s, new[] { "accel", "accel", "accel" }, order), "accel", "each once");
    Seq(Pick(s, null, order), "", "nothing asked, nothing done");
    Seq(Pick(s, new string[] { null!, "" }, order), "", "broken ids are ignored");
    Seq(Pick(s, new[] { FpsId, "accel" }, new[] { "accel" }), "accel," + FpsId, "the FPS limit last also when the order does not name it");
    s.Pending["zzz"] = t0;
    Seq(Pick(s, new[] { FpsId, "zzz" }, Array.Empty<string>()), "zzz," + FpsId, "unknown to the order: after the rest, the FPS limit last");
    Seq(Pick(s, s.Pending.Keys, order), "ult,accel,usb,zzz," + FpsId, "everything pending for the page, in list order");
}

// ---------- games ----------
var others = new[] { "FortniteClient-Win64-Shipping", "cs2", "RainbowSix", "RainbowSix_Vulkan" };
foreach (var n in new[] { "FiveM", "FiveM_b3095_GTAProcess", "FiveM_GTAProcess", "FiveM_ChromeBrowser", "FiveM_SteamChild", "fivem", "FIVEM_b2802_GTAProcess" })
    Check(IsGameProcess(n, others), $"{n} is FiveM");
foreach (var n in new[] { "cs2", "CS2", "RainbowSix", "rainbowsix_vulkan", "FortniteClient-Win64-Shipping" })
    Check(IsGameProcess(n, others), $"{n} is a game");
foreach (var n in new[] { "", null, "explorer", "OrbitTweak", "msedgewebview2", "cs", "cs22", "MyFiveM", "GTA5", "Rainbow" })
    Check(!IsGameProcess(n, others), $"{n ?? "null"} is no game");
Check(!IsGameProcess("cs2", Array.Empty<string>()), "without the list only FiveM counts");

// ---------- the FPS limit ----------
// the card's rule (app.js fpsCard): changed = ours && !pending && written > 0 && limit != written
foreach (var ours in new[] { false, true })
    foreach (var pending in new[] { false, true })
        foreach (var written in new[] { 0, 141 })
            foreach (var limit in new[] { 0, 100, 141 })
            {
                bool changed = ours && !pending && written > 0 && limit != written;
                Eq(FpsRead(ours, pending, written, limit), changed ? Read.Off : Read.On, $"FPS ours={ours} pending={pending} written={written} limit={limit}");
            }
Eq(FpsRead(true, true, 0, 141), Read.On, "a driver profile that is gone (clean install): FiveM's next start puts the limit back by itself");
Eq(FpsRead(true, false, 141, 0), Read.Off, "the limit taken out of the profile: reset");
Eq(FpsDetail(100, 141), "Im Treiberprofil stehen jetzt 100 statt 141 FPS", "detail with another limit");
Eq(FpsDetail(0, 141), "Im Treiberprofil steht jetzt kein Limit statt 141 FPS", "detail without a limit");
// "So lassen": quiet for the kept limit until a new one is set or it is in force again
Eq(FpsQuietNext(null, 141, Read.Off), (int?)null, "no quiet: none");
Eq(FpsQuietNext(141, 141, Read.Off), (int?)141, "quiet for this limit while the profile still differs");
Eq(FpsQuietNext(141, 141, Read.Unknown), (int?)141, "an unreadable look keeps it quiet");
Eq(FpsQuietNext(141, 141, Read.On), (int?)null, "in force again: watched again");
Eq(FpsQuietNext(141, 120, Read.Off), (int?)null, "a new limit on the card: watched again");
Eq(FpsQuietNext(141, 0, Read.Off), (int?)null, "no limit kept: nothing to keep quiet");
Eq(FpsQuietNext(0, 0, Read.Off), (int?)null, "zero is no limit");
{
    // the full cycle with the guard: reset found, "So lassen", then the user sets the limit again
    var s = new State(); int? quiet = null; int kept = 141;
    Obs Fps(Read st) { quiet = FpsQuietNext(quiet, kept, st); return new Obs(FpsId, quiet == null, true, st); }
    Step(s, new[] { Fps(Read.Off) }, t0);
    var r = Step(s, new[] { Fps(Read.Off) }, t0.AddMinutes(1));
    Seq(r.Confirmed, FpsId, "FPS limit changed outside: reported after two looks");
    quiet = kept; s.Pending.Remove(FpsId);   // "So lassen"
    r = Step(s, new[] { Fps(Read.Off) }, t0.AddMinutes(16));
    Check(r.Confirmed.Count == 0 && s.Suspect.Count == 0, "let be: not asked again");
    Step(s, new[] { Fps(Read.On) }, t0.AddMinutes(31));
    Eq(quiet, (int?)null, "set again on the card: watched again");
    Step(s, new[] { Fps(Read.Off) }, t0.AddMinutes(46));
    r = Step(s, new[] { Fps(Read.Off) }, t0.AddMinutes(47));
    Seq(r.Confirmed, FpsId, "changed outside once more: reported again");
}

// ---------- words ----------
Eq(Join(Array.Empty<string>()), "", "no names");
Eq(Join(new[] { "A" }), "A", "one name");
Eq(Join(new[] { "A", "B" }), "A und B", "two names");
Eq(Join(new[] { "A", "B", "C" }), "A, B und C", "three names");
Eq(Title(1), "1 Tweak wurde zurückgesetzt", "title for one");
Eq(Title(2), "2 Tweaks wurden zurückgesetzt", "title for two");
Eq(Title(5), "5 Tweaks wurden zurückgesetzt", "title for five");
Eq(Announce(Array.Empty<string>()), "", "nothing to announce");
Eq(Announce(new[] { "Zeigerbeschleunigung aus" }), "🛡 Nicht mehr aktiv: Zeigerbeschleunigung aus · unter Tweaks wieder anwenden oder so lassen", "activity line for one");
Eq(Announce(new[] { "Zeigerbeschleunigung aus", "Game-Bar-Aufnahme aus" }), "🛡 2 Tweaks nicht mehr aktiv: Zeigerbeschleunigung aus und Game-Bar-Aufnahme aus · unter Tweaks wieder anwenden oder so lassen", "activity line for two");
Eq(KeptLine(Array.Empty<string>()), "", "nothing kept");
Eq(KeptLine(new[] { "Spielmodus" }), "🛡 So gelassen: Spielmodus · der Tweak-Wächter fragt dort nicht mehr nach", "line after So lassen");
foreach (var line in new[] { Announce(new[] { "X" }), Announce(new[] { "X", "Y" }), Title(2) })
    Check(!System.Text.RegularExpressions.Regex.IsMatch(line, "Windows-Update|Treiber|weil|wegen"), $"never guesses the cause: {line}");

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
