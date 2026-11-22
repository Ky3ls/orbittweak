using System.Buffers.Binary;
using System.Text;
using OrbitTweak.Core;
using static OrbitTweak.Core.ServerWatchLogic;

// Checks of the pure logic of Platz-Alarm, Freunde online and Server auf den Desktop. Exit code 0 = all fine.
//   dotnet run -c Release --project tests/ServerWatchLogic.Tests

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }
void Eq<T>(T got, T want, string what) => Check(EqualityComparer<T>.Default.Equals(got, want), $"{what}: got [{got}], want [{want}]");
void Near(double got, double want, double tol, string what) => Check(Math.Abs(got - want) <= tol, $"{what}: got [{got}], want [{want}] ± {tol}");

// =====================================================================
//  Platz-Alarm: full, kinds, decisions
// =====================================================================
Check(IsFull(256, 256), "256 of 256 is full");
Check(IsFull(260, 256), "more players than slots (queue servers report that) is full");
Check(!IsFull(255, 256), "255 of 256 is not full");
Check(!IsFull(0, 0), "no slot count: never full");
Check(!IsFull(10, 0), "players but no slot count: never full");
Check(!IsFull(0, 32), "an empty server is not full");

Eq(KindFor(false, 0, 0), (WatchKind?)WatchKind.Online, "offline: wait for it to come back");
Eq(KindFor(false, 256, 256), (WatchKind?)WatchKind.Online, "offline wins even with old full numbers");
Eq(KindFor(true, 256, 256), (WatchKind?)WatchKind.Slot, "full: wait for a slot");
Eq(KindFor(true, 100, 256), (WatchKind?)null, "room: nothing to wait for");
Eq(KindFor(true, 5, 0), (WatchKind?)null, "no slot count: nothing to wait for");

var ok = (int p, int m) => new Probe(ProbeKind.Ok, p, m);
Eq(Decide(WatchKind.Slot, ok(255, 256)), Step.Fire, "slot watch: one free slot fires");
Eq(Decide(WatchKind.Slot, ok(0, 64)), Step.Fire, "slot watch: an emptied server fires");
Eq(Decide(WatchKind.Slot, ok(256, 256)), Step.Keep, "slot watch: still full keeps watching");
Eq(Decide(WatchKind.Slot, ok(300, 256)), Step.Keep, "slot watch: over full keeps watching");
Eq(Decide(WatchKind.Slot, ok(10, 0)), Step.Keep, "slot watch: no slot count cannot fire");
Eq(Decide(WatchKind.Slot, new Probe(ProbeKind.NotFound)), Step.Keep, "slot watch: server gone keeps watching");
Eq(Decide(WatchKind.Slot, new Probe(ProbeKind.Error)), Step.Retry, "slot watch: error retries");
Eq(Decide(WatchKind.Slot, new Probe(ProbeKind.RateLimited, RetryAfter: 30)), Step.Retry, "slot watch: 429 retries");
Eq(Decide(WatchKind.Online, ok(256, 256)), Step.Fire, "online watch: back but full still fires (the server is back)");
Eq(Decide(WatchKind.Online, ok(3, 64)), Step.Fire, "online watch: back with room fires");
Eq(Decide(WatchKind.Online, ok(0, 0)), Step.Fire, "online watch: back without numbers fires");
Eq(Decide(WatchKind.Online, new Probe(ProbeKind.NotFound)), Step.Keep, "online watch: still offline keeps watching");
Eq(Decide(WatchKind.Online, new Probe(ProbeKind.Error)), Step.Retry, "online watch: error retries");
Eq(Decide(WatchKind.Online, new Probe(ProbeKind.RateLimited)), Step.Retry, "online watch: 429 retries");

// =====================================================================
//  backoff schedule
// =====================================================================
Near(NextDelay(0, false, null, 0), 17, .001, "normal, jitter 0: 17 s");
Near(NextDelay(0, false, null, 1), 23, .001, "normal, jitter 1: 23 s");
Near(NextDelay(0, false, null, .5), 20, .001, "normal, jitter .5: 20 s");
for (double j = 0; j <= 1; j += .1) { var d = NextDelay(0, false, null, j); Check(d >= 17 && d <= 23, $"normal delay stays in 17–23 s (j={j:0.0}: {d})"); }
Near(NextDelay(1, false, null, .5), 40, .001, "one error: 40 s");
Near(NextDelay(2, false, null, .5), 80, .001, "two errors: 80 s");
Near(NextDelay(3, false, null, .5), 160, .001, "three errors: 160 s");
Near(NextDelay(4, false, null, .5), 300, .001, "four errors: capped at 300 s");
Near(NextDelay(50, false, null, .5), 300, .001, "many errors: still 300 s");
Check(NextDelay(4, false, null, 1) <= 300 * 1.15 + .001, "error delay with jitter stays near the cap");
for (int e = 0; e < 6; e++) Check(NextDelay(e + 1, false, null, .5) >= NextDelay(e, false, null, .5), $"error delay never shrinks ({e} → {e + 1})");
Near(NextDelay(1, true, null, 0), 120, .001, "first 429 without Retry-After: 2 minutes");
Near(NextDelay(2, true, null, 0), 240, .001, "second 429 in a row: 4 minutes");
Near(NextDelay(3, true, null, 0), 480, .001, "third 429 in a row: 8 minutes");
Near(NextDelay(4, true, null, 0), 600, .001, "429s keep at 10 minutes at most");
Near(NextDelay(1, true, 300, 0), 300, .001, "Retry-After 300 is honoured");
Near(NextDelay(1, true, 30, 0), 120, .001, "a short Retry-After still waits 2 minutes");
Near(NextDelay(1, true, 5000, 0), 600, .001, "a huge Retry-After is capped at 10 minutes");
Near(NextDelay(1, true, -50, 0), 120, .001, "a negative Retry-After counts as none");
Near(NextDelay(1, true, null, 1), 135, .001, "429 jitter adds up to 15 s");
Check(NextDelay(0, true, null, .3) >= 120, "a 429 with an error count of 0 still waits 2 minutes");
Check(!double.IsNaN(NextDelay(0, false, null, double.NaN)) && NextDelay(0, false, null, double.NaN) is >= 17 and <= 23, "NaN jitter is harmless");
Near(NextDelay(0, false, null, 7), 23, .001, "jitter above 1 is clamped");
Near(NextDelay(0, false, null, -3), 17, .001, "jitter below 0 is clamped");

var nowUtc = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
Eq(RetryAfter("120", nowUtc), (int?)120, "Retry-After in seconds");
Eq(RetryAfter(" 45 ", nowUtc), (int?)45, "Retry-After with spaces");
Eq(RetryAfter("0", nowUtc), (int?)0, "Retry-After 0");
Eq(RetryAfter("999999", nowUtc), (int?)3600, "Retry-After capped at an hour");
Eq(RetryAfter("Sun, 22 Nov 2026 12:03:00 GMT", nowUtc), (int?)180, "Retry-After as HTTP date");
Eq(RetryAfter("Sun, 22 Nov 2026 11:00:00 GMT", nowUtc), (int?)0, "Retry-After date in the past: 0");
Eq(RetryAfter(null, nowUtc), (int?)null, "no Retry-After");
Eq(RetryAfter("", nowUtc), (int?)null, "empty Retry-After");
Eq(RetryAfter("soon", nowUtc), (int?)null, "nonsense Retry-After");
Eq(RetryAfter("-5", nowUtc), (int?)null, "negative Retry-After is unreadable");
Eq(RetryAfter("1.5", nowUtc), (int?)null, "fractional Retry-After is unreadable");

var t0 = new DateTime(2026, 11, 22, 20, 0, 0);
Check(!Expired(t0, t0), "a new watch is not expired");
Check(!Expired(t0, t0.AddMinutes(119)), "after 1:59 h not expired");
Check(Expired(t0, t0.AddHours(2)), "after 2 h expired");
Check(Expired(t0, t0.AddHours(5)), "after 5 h expired");
Eq(WatchFor, TimeSpan.FromHours(2), "watches last two hours");
Eq(MaxWatches, 3, "three watches at most");

Check(!EndsForFiveM(false, false, false), "FiveM closed: the watch goes on");
Check(EndsForFiveM(false, true, false), "FiveM started during the watch: ends");
Check(EndsForFiveM(false, true, true), "game started: ends");
Check(!EndsForFiveM(true, true, false), "launcher was already open: the watch goes on");
Check(EndsForFiveM(true, true, true), "launcher was open, then the game started: ends");
Check(EndsForFiveM(true, false, true), "game seen even without the launcher: ends");

Check(MayAutoConnect(true, false, true), "auto connect: FiveM not running");
Check(!MayAutoConnect(true, true, true), "auto connect never while FiveM runs");
Check(!MayAutoConnect(false, false, true), "no auto connect when it is off");
Check(!MayAutoConnect(true, false, false), "no auto connect without a valid key");
Check(Active("watching") && Active("retry"), "watching and retry count as active");
Check(!Active("fired") && !Active("ended") && !Active(""), "fired and ended do not count");

// =====================================================================
//  names
// =====================================================================
Eq(Plain("^1Max^7Power"), "MaxPower", "colour codes out");
Eq(Plain("  ^2 Max  "), "Max", "outer whitespace out, also after a code");
Eq(Plain("Max ^^1 Power"), "Max ^ Power", "a lone caret stays");
Eq(Plain("^0^1^2^3^4^5^6^7^8^9X"), "X", "all ten codes");
Eq(Plain(null), "", "null is empty");
Eq(Plain("Max^"), "Max^", "a caret at the end stays");
Eq(Key("^3MAX"), "max", "key is lower case");
Eq(Key(" Max "), Key("max"), "key ignores outer spaces and case");
Eq(Key("Jose\u0301"), Key("José"), "key: decomposed accent equals precomposed");
Eq(Key("ISTANBUL"), "istanbul", "key lower case is invariant (I → i, never a dotless ı)");
Eq(Key("Max  Power"), "max  power", "inner spaces stay (exact match)");
Check(Key("Max Power") != Key("MaxPower"), "a missing space is a different name");

Eq(ServerName("^8Nova^7City ~r~RP", "x"), "NovaCity RP", "server name: FiveM and GTA codes out");
Eq(ServerName("  A\tB\n\nC ", "x"), "A B C", "server name: whitespace runs as one space");
Eq(ServerName("", "Fallback"), "Fallback", "server name: empty → fallback");
Eq(ServerName("^1^2", "Fallback"), "Fallback", "server name: only codes → fallback");
Check(ServerName(new string('a', 200), "x").Length == 65 && ServerName(new string('a', 200), "x").EndsWith("…"), "server name: cut to 64 + …");
Eq(ServerName("A\u0007B", "x"), "AB", "server name: control characters out");

Eq(CleanFriend("  ^1Max  "), "Max", "friend: cleaned");
Eq(CleanFriend(""), (string?)null, "friend: empty refused");
Eq(CleanFriend("   "), (string?)null, "friend: spaces refused");
Eq(CleanFriend("^1^2"), (string?)null, "friend: only codes refused");
Eq(CleanFriend(new string('x', 40)), new string('x', 40), "friend: 40 characters ok");
Eq(CleanFriend(new string('x', 41)), (string?)null, "friend: 41 characters refused");
Eq(CleanFriend("Max\u0000"), (string?)null, "friend: NUL refused");
Eq(CleanFriend("Max\nPower"), (string?)null, "friend: line break refused");
Eq(CleanFriend("Max Power"), "Max Power", "friend: inner space ok");
Eq(CleanFriend("Ünal_99"), "Ünal_99", "friend: umlauts and underscore ok");
Eq(CleanFriend(null), (string?)null, "friend: null refused");

var fl = new List<string>();
Eq(AddFriend(fl, "Max"), (string?)null, "add Max");
Eq(AddFriend(fl, "^1MAX "), "Der Name steht schon in deiner Liste.", "same name again (case, codes) refused");
Eq(AddFriend(fl, ""), "Gib einen Namen ein.", "empty name refused with a note");
Eq(AddFriend(fl, new string('y', 41)), $"Der Name ist zu lang (höchstens {MaxFriendLen} Zeichen).", "too long refused with a note");
Eq(AddFriend(fl, "Lena"), (string?)null, "add Lena");
Eq(fl.Count, 2, "two friends");
Eq(fl[0], "Max", "stored as typed (cleaned)");
for (int i = 0; i < 48; i++) AddFriend(fl, "F" + i);
Eq(fl.Count, 50, "fifty friends");
Eq(AddFriend(fl, "Number51"), $"Höchstens {MaxFriends} Namen.", "the 51st is refused");
Eq(fl.Count, 50, "still fifty");
Check(RemoveFriend(fl, " max"), "remove Max (other case and spaces)");
Check(!RemoveFriend(fl, "max"), "Max is gone");
Check(!RemoveFriend(fl, ""), "removing nothing");
Check(!RemoveFriend(fl, null), "removing null");
Eq(fl.Count, 49, "forty-nine left");
Eq(AddFriend(fl, "Number51"), (string?)null, "room again after removing");

Check(IsPlaceholder("Player"), "Player is a stand-in");
Check(IsPlaceholder("Anon0") && IsPlaceholder("Anon1724"), "AnonN is a stand-in");
Check(IsPlaceholder("^1Player "), "a stand-in with codes and spaces");
Check(!IsPlaceholder("player"), "lower-case player is a real name");
Check(!IsPlaceholder("Anon"), "Anon without a number is a real name");
Check(!IsPlaceholder("PlayerOne") && !IsPlaceholder("Anonymous"), "names that only start like a stand-in are real");
Check(!IsPlaceholder("Anon12x"), "Anon with more after the number is real");

string[] Many(string n, int c) => Enumerable.Repeat(n, c).ToArray();
Eq(Classify(0, Array.Empty<string>()), Listing.Empty, "nobody on: empty");
Eq(Classify(25, Array.Empty<string>()), Listing.Hidden, "players on but no list: hidden");
Eq(Classify(1045, Many("Player", 1045)), Listing.Anonymous, "everyone Player: anonymous (seen live)");
Eq(Classify(3, new[] { "Anon0", "Anon1", "Anon2" }), Listing.Anonymous, "AnonN: anonymous (seen live)");
Eq(Classify(26, new[] { "Shadow", "Quinn", "Max" }), Listing.Names, "real names");
Eq(Classify(2, new[] { "Player", "Max" }), Listing.Names, "a few players, one called Player: names");
Eq(Classify(10, Many("Player", 9).Append("Max").ToArray()), Listing.Anonymous, "9 of 10 stand-ins: anonymous");
Eq(Classify(10, Many("Player", 8).Concat(new[] { "Max", "Lena" }).ToArray()), Listing.Names, "8 of 10 stand-ins: still names");
Eq(Classify(1, new[] { "Player" }), Listing.Anonymous, "a single Player: anonymous");

Check(Matches("max", "max", false), "exact match");
Check(!Matches("max", "maxpower", false), "exact by default: no partial");
Check(Matches("max", "maxpower", true), "partial: inside");
Check(!Matches("ma", "maxpower", true), "partial needs three characters");
Check(!Matches("", "", true), "an empty friend matches nothing");
Check(!Matches("lena", "max", true), "no match");

var players = new[] { "^1Max^7Power", "Lena", "  shadow  ", "Quinn", "Max" };
var found = Find(new[] { "max", "SHADOW", "Tom" }, players, false);
Eq(found.Count, 2, "two friends found exactly");
Check(found.Any(f => f.friend == "max" && f.player == "Max"), "max matches Max exactly, not MaxPower");
Check(found.Any(f => f.friend == "SHADOW" && f.player == "shadow"), "SHADOW matches '  shadow  '");
var foundP = Find(new[] { "maxpow", "Len" }, players, true);
Check(foundP.Any(f => f.friend == "maxpow" && f.player == "MaxPower"), "partial: maxpow in MaxPower (codes out)");
Check(foundP.Any(f => f.friend == "Len" && f.player == "Lena"), "partial: Len in Lena");
var foundE = Find(new[] { "max" }, players, true);
Eq(foundE.Count, 1, "each friend once");
Eq(foundE[0].player, "Max", "the exact hit wins over a partial one");
Eq(Find(new[] { "max", "^1MAX" }, players, false).Count, 1, "the same friend twice counts once");
Eq(Find(new[] { "Player" }, Many("Player", 40), false).Count, 0, "a friend called Player is never found among stand-ins");
Eq(Find(new[] { "Anon3" }, new[] { "Anon1", "Anon2", "Anon3" }, false).Count, 0, "AnonN never matches");
Eq(Find(new[] { "Player" }, new[] { "Player", "Max", "Lena" }, false).Count, 0, "a stand-in name never matches, also on a named list");
Eq(Find(new[] { "max" }, Array.Empty<string>(), false).Count, 0, "an empty list finds nobody");
Eq(Find(Array.Empty<string>(), players, false).Count, 0, "no friends, no hits");

// rotation
var (p1, n1) = Rotate(4, 10, 0);
Eq(string.Join(",", p1), "0,1,2,3", "fewer than 10: all");
Eq(n1, 0, "fewer than 10: start stays 0");
var (p2, n2) = Rotate(25, 10, 0);
Eq(string.Join(",", p2), "0,1,2,3,4,5,6,7,8,9", "first ten of 25");
Eq(n2, 10, "next starts at 10");
var (p3, n3) = Rotate(25, 10, n2);
Eq(string.Join(",", p3), "10,11,12,13,14,15,16,17,18,19", "second ten");
var (p4, n4) = Rotate(25, 10, n3);
Eq(string.Join(",", p4), "20,21,22,23,24,0,1,2,3,4", "wraps around");
Eq(n4, 5, "next after the wrap");
Eq(Rotate(0, 10, 3).pick.Length, 0, "no favourites, nothing");
Eq(Rotate(12, 10, 99).pick[0], 99 % 12, "a start past the end wraps");
Eq(Rotate(12, 10, -1).pick[0], 11, "a negative start wraps");
var seenAll = new HashSet<int>(); int st = 0;
for (int r = 0; r < 3; r++) { var (pk, nx) = Rotate(27, ServersPerRound, st); foreach (var i in pk) seenAll.Add(i); st = nx; Check(pk.Length == 10 && pk.Distinct().Count() == 10, $"round {r}: ten different servers"); }
Eq(seenAll.Count, 27, "three rounds cover 27 favourites");

var tm = new DateTime(2026, 11, 22, 20, 0, 0);
Check(MayRound(null, tm, FriendEvery), "the first round may run");
Check(!MayRound(tm, tm.AddSeconds(59), FriendEvery), "not again within 60 s");
Check(MayRound(tm, tm.AddSeconds(60), FriendEvery), "after 60 s again");
Check(MayRound(tm, tm.AddSeconds(-30), FriendEvery), "a clock that went back does not block for ever");
Eq(FriendEvery, TimeSpan.FromSeconds(60), "page rounds at most every 60 s");
Eq(ServersPerRound, 10, "ten servers per round");
Check(FriendBackgroundEvery >= FriendEvery, "background rounds are not more often than page rounds");

// friends coming online
var fs = new FriendSeen();
Eq(fs.Round(new[] { "max" }).Count, 0, "first round only learns (Max was already on)");
Check(fs.Primed, "primed after the first round");
Eq(fs.Round(new[] { "max" }).Count, 0, "Max still on: no note");
Eq(string.Join(",", fs.Round(new[] { "max", "lena" })), "lena", "Lena came online");
Eq(fs.Round(new[] { "max" }).Count, 0, "Lena missed once");
Eq(fs.Round(new[] { "max", "lena" }).Count, 0, "Lena back after one miss: no second note");
fs.Round(new[] { "max" }); fs.Round(new[] { "max" });
Eq(string.Join(",", fs.Round(new[] { "max", "lena" })), "lena", "Lena gone for two rounds, then back: a new note");
Eq(fs.Round(new[] { "max", "max", "" }).Count, 0, "duplicates and empty keys are harmless");
fs.Reset();
Check(!fs.Primed, "reset un-primes");
Eq(fs.Round(new[] { "tom" }).Count, 0, "after a reset the first round only learns again");
Eq(string.Join(",", fs.Round(new[] { "tom", "a", "b" })), "a,b", "two at once, in order");

// =====================================================================
//  codes and the command line
// =====================================================================
Check(ValidCode("67lzxd") && ValidCode("vqqvm75") && ValidCode("abcd") && ValidCode("abcd1234"), "codes of 4 to 8 a-z0-9 (6 and 7 seen live)");
Check(!ValidCode("abc"), "3 characters refused");
Check(!ValidCode("abcdefghi"), "9 characters refused");
Check(!ValidCode("ABC123"), "upper case refused");
Check(!ValidCode("abc 12"), "space refused");
Check(!ValidCode("abc123\n"), "a trailing line break refused");
Check(!ValidCode("abc-12"), "dash refused");
Check(!ValidCode("äbc123"), "umlaut refused");
Check(!ValidCode("abc１２３"), "full-width digits refused");
Check(!ValidCode("../../x"), "path refused");
Check(!ValidCode("") && !ValidCode(null), "empty and null refused");
Check(!ValidCode("abc12\0"), "NUL refused");
Eq(CodeOfKey("cfx:67lzxd"), "67lzxd", "code of a favourite key");
Eq(CodeOfKey("ip:1.2.3.4:30120"), (string?)null, "IP favourites have no code");
Eq(CodeOfKey("cfx:BAD"), (string?)null, "a bad code in a key");
Eq(CodeOfKey("67lzxd"), (string?)null, "a bare code is no key");
Eq(CodeOfKey(null), (string?)null, "null key");

Eq(ConnectArg(new[] { "--connect", "67lzxd" }), "67lzxd", "--connect code");
Eq(ConnectArg(new[] { "--updated", "--connect", "abc123" }), "abc123", "--connect after another flag");
Eq(ConnectArg(new[] { "--connect" }), (string?)null, "--connect without a code");
Eq(ConnectArg(new[] { "--connect", "ABC123" }), (string?)null, "--connect with upper case");
Eq(ConnectArg(new[] { "--connect", "cfx.re/join/abc123" }), (string?)null, "--connect with an address is refused");
Eq(ConnectArg(new[] { "--connect", "abc123 +exec evil" }), (string?)null, "--connect with extra text is refused");
Eq(ConnectArg(new[] { "--connect", "bad!", "--connect", "abc123" }), (string?)null, "only the first --connect counts");
Eq(ConnectArg(new[] { "--CONNECT", "abc123" }), (string?)null, "the flag is case-sensitive");
Eq(ConnectArg(new[] { "abc123" }), (string?)null, "a code without the flag");
Eq(ConnectArg(Array.Empty<string>()), (string?)null, "no arguments");
Eq(ConnectArg(null), (string?)null, "null arguments");
Eq(ShortcutArgs("abc123"), "--connect abc123", "shortcut arguments");
Check(ConnectArg(ShortcutArgs("vqqvm75").Split(' ')) == "vqqvm75", "the shortcut arguments read back");
try { ShortcutArgs("abc 1; x"); Check(false, "ShortcutArgs must refuse a bad code"); } catch (ArgumentException) { Check(true, "ShortcutArgs refuses a bad code"); }
Eq(ConnectAddress("abc123"), "cfx.re/join/abc123", "connect address");

// WM_COPYDATA
var cb = CopyBytes("abc123");
Check(cb != null && cb.Length == 12, "copy bytes: UTF-16");
Eq(CodeFromCopy(CopyTag, cb), "abc123", "copy bytes read back");
Eq(CodeFromCopy(CopyTag + 1, cb), (string?)null, "wrong tag refused");
Eq(CodeFromCopy(CopyTag, Encoding.Unicode.GetBytes("ABC123")), (string?)null, "upper case refused");
Eq(CodeFromCopy(CopyTag, Encoding.Unicode.GetBytes("abc")), (string?)null, "too short refused");
Eq(CodeFromCopy(CopyTag, Encoding.Unicode.GetBytes("abcdefghi")), (string?)null, "too long refused");
Eq(CodeFromCopy(CopyTag, new byte[] { 0x61, 0, 0x62, 0, 0x63, 0, 0x64, 0, 0x65 }), (string?)null, "odd length refused");
Eq(CodeFromCopy(CopyTag, Encoding.Unicode.GetBytes("ab c1")), (string?)null, "space refused");
Eq(CodeFromCopy(CopyTag, ReadOnlySpan<byte>.Empty), (string?)null, "empty refused");
Eq(CopyBytes("BAD"), (byte[]?)null, "nothing is sent for a bad code");
Eq(CodeFromCopy(CopyTag, Encoding.Unicode.GetBytes("vqqvm75")), "vqqvm75", "seven characters");
Eq(CodeFromCopy(CopyTag, Encoding.Unicode.GetBytes("abcd1234")), "abcd1234", "eight characters");

// =====================================================================
//  shortcut file names
// =====================================================================
Eq(FileName("^8Nova^7City ^1| ^7Roleplay ^3[GER]", "abc123"), "NovaCity Roleplay [GER]", "colour codes and | out, spaces collapsed");
Eq(FileName("A<B>C:D\"E/F\\G|H?I*J", "abc123"), "A B C D E F G H I J", "every refused character becomes a space");
Eq(FileName("Server...", "abc123"), "Server", "dots at the end out");
Eq(FileName("Server .  ", "abc123"), "Server", "dots and spaces at the end out");
Eq(FileName("  Server", "abc123"), "Server", "spaces at the start out");
Eq(FileName("", "abc123"), "FiveM-Server abc123", "empty → fallback");
Eq(FileName(null, "abc123"), "FiveM-Server abc123", "null → fallback");
Eq(FileName("^1^2^3", "abc123"), "FiveM-Server abc123", "only codes → fallback");
Eq(FileName("???", "abc123"), "FiveM-Server abc123", "only refused characters → fallback");
Eq(FileName("...", "abc123"), "FiveM-Server abc123", "only dots → fallback");
Eq(FileName("CON", "abc123"), "FiveM-Server abc123", "reserved CON → fallback");
Eq(FileName("nul", "abc123"), "FiveM-Server abc123", "reserved nul (any case) → fallback");
Eq(FileName("COM1.server", "abc123"), "FiveM-Server abc123", "reserved stem with an extension → fallback");
Eq(FileName("LPT9 ", "abc123"), "FiveM-Server abc123", "reserved with a trailing space → fallback");
Eq(FileName("Console RP", "abc123"), "Console RP", "a name that only starts like CON is fine");
Eq(FileName("Evil\u202Eknl.exe", "abc123"), "Evilknl.exe", "right-to-left override out");
Eq(FileName("Zero\u200BWidth", "abc123"), "ZeroWidth", "zero-width space out");
Eq(FileName("Tab\tLine\nBreak", "abc123"), "Tab Line Break", "control characters become spaces");
Eq(FileName("~r~Red ~w~White", "abc123"), "Red White", "GTA colour codes out");
Eq(FileName("🔥 Fire City 🔥", "abc123"), "🔥 Fire City 🔥", "emoji stay");
Eq(FileName("Städte & Straßen", "abc123"), "Städte & Straßen", "umlauts and & stay");
var longName = FileName(new string('x', 59) + "🔥🔥", "abc123");
Check(longName.Length <= MaxFileName && !char.IsHighSurrogate(longName[^1]), "cut at 60 without splitting an emoji");
Eq(FileName(new string('a', 100), "abc123").Length, 60, "cut at 60 characters");
Eq(FileName(new string('a', 59) + " b", "abc123"), new string('a', 59), "a cut that ends in a space is trimmed");
Eq(FileName("bad\uD800surrogate", "abc123"), "bad surrogate", "a lone surrogate becomes a space");
Eq(FileName("\uE000private", "abc123"), "private", "private-use characters out");
Check(!FileName("x/../../Windows/System32", "abc123").Contains('/') && !FileName("..\\..\\x", "abc123").Contains('\\'), "no path separators survive");
Eq(Numbered("Nova", 1), "Nova", "first name unnumbered");
Eq(Numbered("Nova", 2), "Nova (2)", "second name numbered");

// =====================================================================
//  icon bytes
// =====================================================================
byte[] FakePng(int w, int h, int extra = 20)
{
    var b = new byte[33 + extra];
    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
    BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), 13);
    "IHDR"u8.CopyTo(b.AsSpan(12));
    BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w);
    BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h);
    b[24] = 8; b[25] = 6;
    for (int i = 33; i < b.Length; i++) b[i] = (byte)i;
    return b;
}
Eq(PngSize(FakePng(256, 256)), ((int, int)?)(256, 256), "PNG size read");
Eq(PngSize(FakePng(48, 32)), ((int, int)?)(48, 32), "PNG size not square");
Eq(PngSize(new byte[10]), ((int, int)?)null, "too short is no PNG");
Eq(PngSize(Encoding.ASCII.GetBytes("RIFF....WEBPVP8 xxxxxxxxxxxxxxxxxxxx")), ((int, int)?)null, "WebP is no PNG");
var notIhdr = FakePng(16, 16); notIhdr[12] = (byte)'X';
Eq(PngSize(notIhdr), ((int, int)?)null, "first chunk must be IHDR");
Eq(PngSize(FakePng(0, 16)), ((int, int)?)null, "zero width is no image");
Eq(PngSize(FakePng(-5, 16)), ((int, int)?)null, "negative width is no image");

var p256 = FakePng(256, 256, 100); var p48 = FakePng(48, 48, 40); var p16 = FakePng(16, 16, 10);
var ico = Ico(new[] { p256, p48, p16 });
Check(ico != null, "ICO made");
if (ico != null)
{
    Eq(BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0)), (ushort)0, "ICONDIR reserved 0");
    Eq(BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)), (ushort)1, "ICONDIR type 1 = icon");
    Eq(BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)), (ushort)3, "ICONDIR count 3");
    Eq(ico.Length, 6 + 16 * 3 + p256.Length + p48.Length + p16.Length, "ICO length");
    int off = 6 + 48;
    var list = new[] { (p256, 0), (p48, 48), (p16, 16) };
    for (int i = 0; i < 3; i++)
    {
        var e = ico.AsSpan(6 + 16 * i);
        var (png, wb) = list[i];
        Eq(e[0], (byte)wb, $"entry {i} width byte");
        Eq(e[1], (byte)wb, $"entry {i} height byte");
        Eq(e[2], (byte)0, $"entry {i} no palette");
        Eq(e[3], (byte)0, $"entry {i} reserved");
        Eq(BinaryPrimitives.ReadUInt16LittleEndian(e[4..]), (ushort)1, $"entry {i} planes 1");
        Eq(BinaryPrimitives.ReadUInt16LittleEndian(e[6..]), (ushort)32, $"entry {i} 32 bits");
        Eq(BinaryPrimitives.ReadInt32LittleEndian(e[8..]), png.Length, $"entry {i} size");
        Eq(BinaryPrimitives.ReadInt32LittleEndian(e[12..]), off, $"entry {i} offset");
        Check(ico.AsSpan(off, png.Length).SequenceEqual(png), $"entry {i} PNG bytes in place");
        off += png.Length;
    }
}
Eq(Ico(Array.Empty<byte[]>()), (byte[]?)null, "no images, no ICO");
Eq(Ico(new[] { FakePng(512, 512) }), (byte[]?)null, "512 px refused (ICO holds 256 at most)");
Eq(Ico(new[] { FakePng(48, 48), FakePng(48, 48) }), (byte[]?)null, "the same size twice refused");
Eq(Ico(new[] { new byte[40] }), (byte[]?)null, "no PNG refused");
var one = Ico(new[] { FakePng(32, 32) });
Check(one != null && one[6] == 32 && one[7] == 32 && BinaryPrimitives.ReadInt32LittleEndian(one.AsSpan(18)) == 22, "single 32 px entry at offset 22");

// icon address: only Cfx.re's own hosts
Eq(IconUrl("https://cfx-server-metadata.cfx-services.net/images/30d37ea7-bb0c-4a32-afc7-c776df64db9b"), "https://cfx-server-metadata.cfx-services.net/images/30d37ea7-bb0c-4a32-afc7-c776df64db9b", "the Cfx.re metadata host (seen live)");
Eq(IconUrl("https://servers-live.fivem.net/servers/icon/abc123/1234.png"), "https://servers-live.fivem.net/servers/icon/abc123/1234.png", "the old icon host");
Eq(IconUrl("http://servers-live.fivem.net/servers/icon/abc123/1.png"), (string?)null, "plain http refused");
Eq(IconUrl("https://evil.example/icon.png"), (string?)null, "another host refused");
Eq(IconUrl("https://servers-live.fivem.net.evil.example/x.png"), (string?)null, "a look-alike host refused");
Eq(IconUrl("https://user@servers-live.fivem.net/x.png"), (string?)null, "user info refused");
Eq(IconUrl("https://servers-live.fivem.net:8443/x.png"), (string?)null, "another port refused");
Eq(IconUrl("file:///C:/x.png"), (string?)null, "file URL refused");
Eq(IconUrl(""), (string?)null, "empty refused");
Eq(IconUrl(null), (string?)null, "null refused");

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
