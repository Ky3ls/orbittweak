using System.Text;
using OrbitTweak.Core;
using L = OrbitTweak.Core.CfgBackupLogic;

// Checks of the settings backup's pure logic. Exit code 0 = all fine.
//   dotnet run --project tests/CfgBackupLogic.Tests

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }
void Eq<T>(T got, T want, string what) => Check(EqualityComparer<T>.Default.Equals(got, want), $"{what}: got [{got}], want [{want}]");

const string Roam = @"C:\Users\Max\AppData\Roaming", Loc = @"C:\Users\Max\AppData\Local", App = @"C:\Users\Max\AppData\Local\FiveM\FiveM.app";
var roots = new CfgRoots(Roam, Loc, App);
var noFiveM = new CfgRoots(Roam, Loc, null);

// ---------- the files ----------
Eq(L.Files.Select(f => f.Key).Distinct().Count(), L.Files.Length, "keys are unique");
Eq(L.Files.Select(f => L.Placeholder(f).ToLowerInvariant()).Distinct().Count(), L.Files.Length, "places are unique");
Eq(L.Files.Select(f => L.EntryName(f)).Distinct().Count(), L.Files.Length, "zip names are unique");
foreach (var f in L.Files)
{
    Check(f.Root is L.AppData or L.LocalAppData or L.FiveMApp, $"{f.Key}: a known root");
    Check(!f.Rel.Contains('/') && !f.Rel.StartsWith('\\') && !f.Rel.Split('\\').Any(p => p is "" or "." or ".."), $"{f.Key}: a plain relative path");
    Check(f.Rel.EndsWith(f.Name, StringComparison.Ordinal), $"{f.Key}: the shown name is the file's name");
    Check(L.SafeEntryName(L.EntryName(f)), $"{f.Key}: its zip name is safe");
    Check(L.ByPath(L.Placeholder(f)) == f, $"{f.Key}: found by its place");
    Check(L.ByKey(f.Key) == f, $"{f.Key}: found by its key");
    Check(f.What.Length > 0 && f.What == f.What.Trim(), $"{f.Key}: says what is in it");
    var low = L.Placeholder(f).ToLowerInvariant();
    foreach (var bad in new[] { "cache", "kvs", "nui-storage", "ros_", "crashes", "logs", "\\data\\" })
        Check(!low.Contains(bad), $"{f.Key}: no cache, server storage or sign-in ({bad})");
}
Eq(L.Placeholder(L.ByKey("fivemcfg")!), @"%APPDATA%\CitizenFX\fivem.cfg", "fivem.cfg (key bindings, profile_* convars)");
Eq(L.Placeholder(L.ByKey("gfx")!), @"%APPDATA%\CitizenFX\gta5_settings.xml", "FiveM's graphics file");
Eq(L.Placeholder(L.ByKey("ini")!), @"<FiveM.app>\CitizenFX.ini", "CitizenFX.ini in FiveM.app");
Eq(L.Placeholder(L.ByKey("gtaxml")!), @"%LOCALAPPDATA%\Rockstar Games\GTA V\settings.xml", "the graphics file of old installs");
Check(L.ByKey("nope") == null && L.ByKey(null) == null && L.ByKey("") == null, "unknown keys");

// ---------- places ----------
Check(L.ByPath(@"%appdata%\citizenfx\FIVEM.CFG")?.Key == "fivemcfg", "places ignore case");
Check(L.ByPath("%APPDATA%/CitizenFX/fivem.cfg")?.Key == "fivemcfg", "forward slashes are read as backslashes");
Check(L.ByPath(@"  %APPDATA%\CitizenFX\fivem.cfg ")?.Key == "fivemcfg", "spaces around are ignored");
Check(L.ByPath(@"%APPDATA%\CitizenFX\..\CitizenFX\fivem.cfg") == null, "no way round with ..");
Check(L.ByPath(@"%APPDATA%\CitizenFX\kvs\db") == null, "an unknown file is not found");
Check(L.ByPath(@"C:\Windows\System32\drivers\etc\hosts") == null, "a real path is no place");
Check(L.ByPath(@"%APPDATA%\CitizenFX") == null && L.ByPath("") == null && L.ByPath(null) == null && L.ByPath("   ") == null, "empty and partial places");
Check(L.ByPath(@"%TEMP%\fivem.cfg") == null && L.ByPath(@"%USERPROFILE%\AppData\Roaming\CitizenFX\fivem.cfg") == null, "other placeholders are unknown");

// ---------- safe roots ----------
Check(L.SafeRoot(Roam) && L.SafeRoot(Loc) && L.SafeRoot(App), "the usual folders");
Check(L.SafeRoot(@"D:\") && L.SafeRoot(@"d:\Games\FiveM\FiveM.app"), "other drives");
Check(L.SafeRoot(@"\\nas\home\max\AppData\Roaming"), "a redirected folder on a share");
Check(L.SafeRoot("C:/Users/Max/AppData/Roaming"), "forward slashes");
Check(!L.SafeRoot(null) && !L.SafeRoot("") && !L.SafeRoot("  "), "nothing is no root");
Check(!L.SafeRoot(@"Users\Max") && !L.SafeRoot(@"\Users\Max") && !L.SafeRoot("C:") && !L.SafeRoot("C:Users"), "relative roots");
Check(!L.SafeRoot(@"C:\Users\..\Windows") && !L.SafeRoot(@"C:\Users\Max\.\AppData") && !L.SafeRoot(@"C:\Users\Max\AppData\.."), ". and .. parts");
Check(!L.SafeRoot(@"\\?\C:\Users") && !L.SafeRoot(@"\\.\C:\Users") && !L.SafeRoot(@"\\server"), "device paths and a bare server");
Check(!L.SafeRoot(@"C:\Users\Max|x") && !L.SafeRoot(@"C:\Us*rs") && !L.SafeRoot("C:\\Users\\Max\0") && !L.SafeRoot(@"C:\a:b") && !L.SafeRoot("C:\\Us\"ers"), "characters Windows does not allow");
Check(!L.SafeRoot(@"C:\Users\Max \AppData") && !L.SafeRoot(@"C:\Users\Max.\AppData"), "names ending in a space or a dot");
Check(!L.SafeRoot("C:\\" + new string('a', 260)), "an overlong root");
Check(!L.SafeRoot("1:\\x") && !L.SafeRoot("ä:\\x"), "a drive is a letter");

// ---------- resolve ----------
Eq(L.Resolve(@"%APPDATA%\CitizenFX\fivem.cfg", roots), @"C:\Users\Max\AppData\Roaming\CitizenFX\fivem.cfg", "fivem.cfg");
Eq(L.Resolve(@"%APPDATA%\CitizenFX\gta5_settings.xml", roots), @"C:\Users\Max\AppData\Roaming\CitizenFX\gta5_settings.xml", "gta5_settings.xml");
Eq(L.Resolve(@"<FiveM.app>\CitizenFX.ini", roots), @"C:\Users\Max\AppData\Local\FiveM\FiveM.app\CitizenFX.ini", "CitizenFX.ini");
Eq(L.Resolve(@"%LOCALAPPDATA%\Rockstar Games\GTA V\settings.xml", roots), @"C:\Users\Max\AppData\Local\Rockstar Games\GTA V\settings.xml", "old settings.xml");
Eq(L.Resolve(@"<FiveM.app>\CitizenFX.ini", noFiveM), null, "no FiveM folder: no place for the ini");
Eq(L.Resolve(@"%APPDATA%\CitizenFX\fivem.cfg", noFiveM), @"C:\Users\Max\AppData\Roaming\CitizenFX\fivem.cfg", "the others still resolve without FiveM");
Eq(L.Resolve(@"%APPDATA%\CitizenFX\fivem.cfg", new CfgRoots(Roam + "\\", Loc, App)), @"C:\Users\Max\AppData\Roaming\CitizenFX\fivem.cfg", "a root with a trailing backslash");
Eq(L.Resolve(@"<FiveM.app>\CitizenFX.ini", new CfgRoots(Roam, Loc, "D:/Games/FiveM/FiveM.app/")), @"D:\Games\FiveM\FiveM.app\CitizenFX.ini", "a root with forward slashes");
Eq(L.Resolve(@"%APPDATA%\CitizenFX\fivem.cfg", new CfgRoots(@"C:\Users\..\Windows", Loc, App)), null, "an unsafe root resolves nothing");
Eq(L.Resolve(@"%APPDATA%\CitizenFX\fivem.cfg", new CfgRoots(null, Loc, App)), null, "an unknown root resolves nothing");
Eq(L.Resolve(@"%APPDATA%\CitizenFX\..\..\Local\x.cfg", roots), null, "a made-up place resolves nothing");
Eq(L.Resolve(@"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\x.lnk", roots), null, "only the known files resolve");
Eq(L.Resolve(null, roots), null, "no place");
Eq(L.Resolve(@"%appdata%\citizenfx\fivem.cfg", roots), @"C:\Users\Max\AppData\Roaming\CitizenFX\fivem.cfg", "the known spelling is used whatever the case");

// ---------- real path -> placeholder ----------
Eq(L.ToPlaceholder(@"C:\Users\Max\AppData\Roaming\CitizenFX\fivem.cfg", roots), @"%APPDATA%\CitizenFX\fivem.cfg", "under AppData");
Eq(L.ToPlaceholder(@"C:\Users\Max\AppData\Local\FiveM\FiveM.app\CitizenFX.ini", roots), @"<FiveM.app>\CitizenFX.ini", "FiveM.app wins over LocalAppData");
Eq(L.ToPlaceholder(@"C:\Users\Max\AppData\Local\Rockstar Games\GTA V\settings.xml", roots), @"%LOCALAPPDATA%\Rockstar Games\GTA V\settings.xml", "under LocalAppData");
Eq(L.ToPlaceholder(@"C:\Users\Max\AppData\Roaming\OrbitTweak\backups", roots), @"%APPDATA%\OrbitTweak\backups", "the backup folder without the user name");
Eq(L.ToPlaceholder(@"c:\users\max\appdata\roaming\OrbitTweak", roots), @"%APPDATA%\OrbitTweak", "case does not matter");
Eq(L.ToPlaceholder("C:/Users/Max/AppData/Roaming/CitizenFX/fivem.cfg", roots), @"%APPDATA%\CitizenFX\fivem.cfg", "forward slashes");
Eq(L.ToPlaceholder(Roam, roots), "%APPDATA%", "the root itself");
Eq(L.ToPlaceholder(App, roots), "<FiveM.app>", "FiveM.app itself");
Eq(L.ToPlaceholder(@"C:\Users\Max\AppData\Roaming2\x", roots), null, "a folder that only starts like the root");
Eq(L.ToPlaceholder(@"C:\Users\Max\AppData\Local\FiveM\FiveM.application\x", roots), @"%LOCALAPPDATA%\FiveM\FiveM.application\x", "a neighbour of FiveM.app");
Eq(L.ToPlaceholder(@"C:\Users\Max\AppData\Roaming\..\..\Max2\x", roots), null, ".. is never shown as a place");
Eq(L.ToPlaceholder(@"D:\Games\GTA V", roots), null, "a path outside the roots");
Eq(L.ToPlaceholder(null, roots), null, "no path");
Eq(L.ToPlaceholder(@"C:\Users\Max\AppData\Local\FiveM\FiveM.app\CitizenFX.ini", noFiveM), @"%LOCALAPPDATA%\FiveM\FiveM.app\CitizenFX.ini", "without a FiveM root LocalAppData is used");
foreach (var f in L.Files)
{
    var full = L.Resolve(L.Placeholder(f), roots);
    Eq(L.ToPlaceholder(full, roots), L.Placeholder(f), $"{f.Key}: there and back");
    Check(full != null && !L.Placeholder(f).Contains("Max") && full.Contains("Max"), $"{f.Key}: the user name only in the real path");
}

// ---------- names ----------
var t0 = new DateTime(2026, 11, 22, 14, 2, 5, DateTimeKind.Local);
Eq(L.NameFor(t0, Array.Empty<string>()), "fivem-2026-11-22_140205.zip", "a name from the moment");
Eq(L.NameFor(t0, new[] { "fivem-2026-11-22_140205.zip" }), "fivem-2026-11-22_140205-2.zip", "the same second again");
Eq(L.NameFor(t0, new[] { "FIVEM-2026-11-22_140205.ZIP", "fivem-2026-11-22_140205-2.zip" }), "fivem-2026-11-22_140205-3.zip", "taken names ignore case");
Eq(L.NameFor(new DateTime(2027, 1, 3, 9, 4, 0), Array.Empty<string>()), "fivem-2027-01-03_090400.zip", "leading zeros");
Check(L.ValidName("fivem-2026-11-22_140205.zip") && L.ValidName("fivem-2026-11-22_140205-2.zip") && L.ValidName("fivem-2026-11-22_140205-999.zip"), "valid names");
foreach (var bad in new[] { "", " ", "fivem-2026-11-22_140205.zip ", @"..\fivem-2026-11-22_140205.zip", "../fivem-2026-11-22_140205.zip",
             @"C:\x\fivem-2026-11-22_140205.zip", "fivem-2026-11-22_140205.zip.exe", "fivem-2026-11-22_140205.ZIP", "fivem-2026-13-22_140205.zip",
             "fivem-2026-02-30_140205.zip", "fivem-2026-11-22_250205.zip", "fivem-2026-11-22_140205-1000.zip", "fivem-26-11-22_140205.zip",
             "settings.v2.json", "fivem-2026-11-22_140205", "fivem-2026-11-22_140205-.zip", "fivem-2026-11-22_140205-a.zip", "redm-2026-11-22_140205.zip" })
    Check(!L.ValidName(bad), $"not a backup name: [{bad}]");
Check(!L.ValidName(null), "null is no name");
Eq(L.TimeOf("fivem-2026-11-22_140205-2.zip"), (DateTime?)t0, "the moment is read back");
Eq(L.TimeOf("fivem-2026-11-22_140205.zip")?.Kind, (DateTimeKind?)DateTimeKind.Local, "as local time");
Eq(L.TimeOf("nope.zip"), null, "no moment from another name");
for (int i = 0; i < 40; i++)
{
    var t = t0.AddMinutes(i * 997.3);
    var n = L.NameFor(t, Array.Empty<string>());
    Check(L.ValidName(n) && L.TimeOf(n) == new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second), $"name round trip {n}");
}

// ---------- zip entry names ----------
foreach (var ok in new[] { "files/fivemcfg/fivem.cfg", "manifest.json", "files/gfx/gta5_settings.xml", "a/b-c_d.e" })
    Check(L.SafeEntryName(ok), $"safe entry: {ok}");
foreach (var bad in new[] { "", "../x", "files/../../x", "/x", "files//x", @"files\x", "C:/x", "C:x", "./x", "files/./x", ".hidden", "x/",
             "files/fivemcfg/fivem.cfg.", "a b", "fïle", "x\0", new string('a', 201) })
    Check(!L.SafeEntryName(bad), $"unsafe entry: [{bad}]");
Check(!L.SafeEntryName(null), "null entry");

// ---------- hashes ----------
Eq(L.Sha256(Encoding.ASCII.GetBytes("abc")), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA-256 of abc");
Eq(L.Sha256(ReadOnlySpan<byte>.Empty), "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "SHA-256 of nothing");
Check(L.ValidSha(L.Sha256(new byte[] { 1, 2, 3 })), "a hash is valid");
Check(!L.ValidSha("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"), "upper case is not how we write it");
Check(!L.ValidSha("ba7816bf") && !L.ValidSha("") && !L.ValidSha(null) && !L.ValidSha(new string('g', 64)) && !L.ValidSha(new string('a', 65)), "broken hashes");

// ---------- manifest ----------
string Sha(string s) => L.Sha256(Encoding.UTF8.GetBytes(s));
var created = new DateTimeOffset(2026, 11, 22, 14, 2, 5, TimeSpan.FromHours(1));
var mod = new DateTimeOffset(2026, 11, 20, 21, 0, 0, TimeSpan.FromHours(1));
CfgManifest Make(string kind = L.Manual, params string[] keys)
{
    var ks = keys.Length > 0 ? keys : new[] { "fivemcfg", "gfx", "ini" };
    return L.NewManifest(kind, created, "OrbitTweak 2.31.0", ks.Select(k => (L.ByKey(k)!, (long)(100 + k.Length), Sha(k), (DateTimeOffset?)mod)));
}
var m = Make();
Eq(m.Format, L.Format, "format");
Eq(m.Files.Count, 3, "three files");
Eq(m.Files[0].Path, @"%APPDATA%\CitizenFX\fivem.cfg", "the place is stored with its placeholder");
Eq(m.Files[2].Entry, "files/ini/CitizenFX.ini", "zip name");
Eq(L.Validate(m), null, "a new manifest is valid");
var json = L.Serialize(m);
Check(json.Contains("\"format\": 1") && json.Contains("\"kind\": \"manual\"") && json.Contains("\"sha256\""), "camelCase json");
Check(json.Contains(@"<FiveM.app>\\CitizenFX.ini") && json.Contains(@"%APPDATA%\\CitizenFX\\fivem.cfg"), "placeholders are readable in the file");
Check(!json.Contains("Max") && !json.Contains(@"C:\\"), "no user name and no real path in the manifest");
var back = L.Parse(json, out var perr);
Check(back != null && perr == null, "the manifest is read back");
Check(back != null && back.Kind == L.Manual && back.Created == created && back.App == "OrbitTweak 2.31.0", "kind, moment and app come back");
Check(back != null && back.Files.Select(e => (e.Key, e.Path, e.Entry, e.Size, e.Sha256, e.Modified)).SequenceEqual(m.Files.Select(e => (e.Key, e.Path, e.Entry, e.Size, e.Sha256, e.Modified))), "every entry comes back");
Check(L.Parse(json.Replace("\"format\"", "\"Format\""), out _) != null, "names are read in any case");
Check(L.Parse(null, out var e0) == null && e0 != null, "nothing is no manifest");
Check(L.Parse("{nope", out var e1) == null && e1 != null && e1.Contains("unlesbar"), "broken json");
Check(L.Parse("[]", out var e2) == null && e2 != null, "a list is no manifest");
Check(L.Parse("{\"format\":\"x\"}", out var e3) == null && e3 != null, "a wrong type");
Check(L.Parse("{}", out var e4) == null && e4 != null, "an empty object");

string? Bad(Action<CfgManifest> change) { var x = L.Parse(L.Serialize(Make()), out _)!; change(x); return L.Validate(x); }
Check(Bad(x => x.Format = 2)?.Contains("neueren") == true, "a newer format is refused with its reason");
Check(Bad(x => x.Format = 0) != null, "format 0");
Check(Bad(x => x.Kind = "evil") != null && Bad(x => x.Kind = "") != null && Bad(x => x.Kind = "Manual") != null, "unknown kinds");
Check(Bad(x => x.Kind = L.Auto) == null && Bad(x => x.Kind = L.Safety) == null, "auto and safety are fine");
Check(Bad(x => x.Files.Clear()) != null, "no files");
Check(Bad(x => x.Files = null!) != null, "files missing");
Check(Bad(x => x.Files[0] = null!) != null, "a missing entry");
Check(Bad(x => x.Files[0].Path = @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\evil.bat") != null, "an unknown place");
Check(Bad(x => x.Files[0].Path = @"C:\Windows\System32\evil.dll") != null, "a real path");
Check(Bad(x => x.Files[0].Path = @"%APPDATA%\CitizenFX\..\..\evil.cfg") != null, "a place with ..");
Check(Bad(x => x.Files[0].Key = "gfx") != null, "a key that does not fit its place");
Check(Bad(x => { x.Files[1].Key = "fivemcfg"; x.Files[1].Path = x.Files[0].Path; x.Files[1].Entry = x.Files[0].Entry; })?.Contains("zweimal") == true, "the same file twice");
Check(Bad(x => x.Files[0].Entry = "../../evil.cfg") != null, "a zip name that leaves the folder");
Check(Bad(x => x.Files[0].Entry = "files/gfx/gta5_settings.xml") != null, "the zip name of another file");
Check(Bad(x => x.Files[0].Entry = "files/fivemcfg/FIVEM.CFG") != null, "the zip name exactly as written");
Check(Bad(x => x.Files[0].Size = -1) != null, "a negative size");
Check(Bad(x => x.Files[0].Size = L.MaxFile + 1) != null, "too big for a settings file");
Check(Bad(x => x.Files[0].Size = L.MaxFile) == null, "the biggest allowed size");
Check(Bad(x => x.Files[0].Size = 0) == null, "an empty file is fine");
Check(Bad(x => x.Files[0].Sha256 = "") != null && Bad(x => x.Files[0].Sha256 = x.Files[0].Sha256.ToUpperInvariant()) != null, "broken hashes");
Check(Bad(x => x.Files.Add(new CfgEntry { Key = "gtaxml", Path = L.Placeholder(L.ByKey("gtaxml")!), Entry = L.EntryName(L.ByKey("gtaxml")!), Size = 1, Sha256 = Sha("x") })) == null, "all four files");
Check(Bad(x => { foreach (var f in L.Files) x.Files.Add(new CfgEntry { Key = f.Key, Path = L.Placeholder(f), Entry = L.EntryName(f), Size = 1, Sha256 = Sha("x") }); }) != null, "more files than there are");
Check(Bad(x => x.Files[0].Modified = null) == null, "the file time is optional");
Check(Make(L.Auto, "gtaxml").Files.Single().Path == @"%LOCALAPPDATA%\Rockstar Games\GTA V\settings.xml", "one old file alone");

// ---------- same content ----------
var nowSame = new[] { ("ini", Sha("ini")), ("fivemcfg", Sha("fivemcfg")), ("gfx", Sha("gfx")) };
Check(L.SameContent(m, nowSame), "same files and hashes in another order");
Check(!L.SameContent(m, new[] { ("fivemcfg", Sha("fivemcfg")), ("gfx", Sha("changed")), ("ini", Sha("ini")) }), "one file changed");
Check(!L.SameContent(m, new[] { ("fivemcfg", Sha("fivemcfg")), ("gfx", Sha("gfx")) }), "one file gone");
Check(!L.SameContent(m, nowSame.Append(("gtaxml", Sha("gtaxml")))), "one file more");
Check(!L.SameContent(null, nowSame), "no backup yet");
Check(!L.SameContent(m, Array.Empty<(string, string)>()), "no files now");

// ---------- clean-up ----------
var day = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(1));
CfgStored St(int d, string? kind, int sec = 0) { var t = day.AddDays(d).AddSeconds(sec); return new(L.NameFor(t.DateTime, Array.Empty<string>()), kind, t); }
var stored = Enumerable.Range(0, 15).Select(d => St(d, L.Auto)).ToList();
stored.AddRange(Enumerable.Range(0, 4).Select(d => St(d, L.Manual, 7)));
stored.AddRange(Enumerable.Range(0, 7).Select(d => St(d, L.Safety, 9)));
stored.Add(St(-30, null, 11));
var del = L.Prune(stored);
Eq(del.Count, 5 + 2, "five automatic and two snapshots too many");
Check(del.Take(5).SequenceEqual(Enumerable.Range(0, 5).Reverse().Select(d => St(d, L.Auto).Name)), "the oldest automatic ones go");
Check(del.Skip(5).SequenceEqual(new[] { St(1, L.Safety, 9).Name, St(0, L.Safety, 9).Name }), "the oldest snapshots go");
Check(!del.Any(n => stored.Any(s => s.Name == n && (s.Kind == L.Manual || s.Kind == null))), "manual and unreadable backups stay");
Eq(L.Prune(stored.Where(s => s.Kind != L.Auto || s.Created >= day.AddDays(5))).Count(n => stored.First(s => s.Name == n).Kind == L.Auto), 0, "ten automatic ones are kept");
Eq(L.Prune(Enumerable.Range(0, 30).Select(d => St(d, L.Manual))).Count, 0, "manual backups are never cleaned up");
Eq(L.Prune(Array.Empty<CfgStored>()).Count, 0, "nothing to clean");
var same = Enumerable.Range(0, 12).Select(i => new CfgStored(L.NameFor(day.DateTime, Enumerable.Range(0, i).Select(j => j == 0 ? "fivem-2026-11-01_120000.zip" : $"fivem-2026-11-01_120000-{j + 1}.zip")), L.Auto, day)).ToList();
var delSame = L.Prune(same);
Eq(delSame.Count, 2, "same moment: still only ten kept");
Check(delSame.Contains("fivem-2026-11-01_120000.zip") && delSame.Contains("fivem-2026-11-01_120000-2.zip"), "same moment: the first two made go");
Check(!delSame.Contains("fivem-2026-11-01_120000-12.zip") && !delSame.Contains("fivem-2026-11-01_120000-9.zip"), "same moment: -12 is newer than -9");
Eq(L.SeqOf("fivem-2026-11-01_120000.zip"), 1, "no suffix is the first");
Eq(L.SeqOf("fivem-2026-11-01_120000-12.zip"), 12, "suffix read as a number");
Eq(L.SeqOf("nope"), 1, "another name");
Eq(L.SeqOf(null), 1, "no name");
Eq(L.Prune(new[] { new CfgStored("../evil.zip", L.Auto, day) }.Concat(Enumerable.Range(0, 10).Select(d => St(d, L.Auto)))).Count, 0, "a name that is not ours is never deleted");

// ---------- schedule ----------
var now = new DateTimeOffset(2026, 11, 22, 18, 30, 0, TimeSpan.FromHours(1));
Check(L.AutoDue(null, now), "never backed up: due");
Check(!L.AutoDue(now.AddHours(-10), now), "earlier today: not due");
Check(!L.AutoDue(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.FromHours(1)), now), "at midnight today: not due");
Check(L.AutoDue(new DateTimeOffset(2026, 11, 21, 23, 59, 59, TimeSpan.FromHours(1)), now), "one second before midnight: due");
Check(L.AutoDue(now.AddDays(-3), now), "days ago: due");
Check(!L.AutoDue(new DateTimeOffset(2026, 11, 22, 1, 0, 0, TimeSpan.Zero), now), "the same day stored in UTC");
Check(L.AutoDue(new DateTimeOffset(2026, 11, 21, 23, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 22, 0, 15, 0, TimeSpan.FromHours(1))) == false, "23:30 UTC is 00:30 here: the same day");
Check(L.AutoDue(now.AddDays(2), now), "a clock set back: due again");
Check(!L.AutoDue(now.AddHours(2), now), "later today (clock moved a little): not due");
Check(!L.Started(null, true), "OrbitTweak opened while FiveM runs: no start");
Check(L.Started(false, true), "closed, then open: a start");
Check(!L.Started(true, true) && !L.Started(false, false) && !L.Started(true, false) && !L.Started(null, false), "no start otherwise");

// ---------- restore plan ----------
var plan = L.Plan(m, null, roots, out var pe);
Eq(pe, null, "plan: no error");
Eq(plan.Count, 3, "plan: all files");
Check(plan.All(p => p.Skip == null && p.Target != null), "plan: every place found");
Eq(plan.First(p => p.File.Key == "ini").Target, @"C:\Users\Max\AppData\Local\FiveM\FiveM.app\CitizenFX.ini", "plan: the ini goes into FiveM.app");
Eq(plan.First(p => p.File.Key == "fivemcfg").Target, @"C:\Users\Max\AppData\Roaming\CitizenFX\fivem.cfg", "plan: fivem.cfg");
var part = L.Plan(m, new[] { "gfx" }, roots, out pe);
Check(pe == null && part.Count == 1 && part[0].File.Key == "gfx", "plan: only the chosen file");
Check(L.Plan(m, Array.Empty<string>(), roots, out pe).Count == 3 && pe == null, "plan: an empty choice is all");
Check(L.Plan(m, new[] { "gtaxml" }, roots, out pe).Count == 0 && pe != null, "plan: a file that is not in the backup");
Check(L.Plan(m, new[] { "../../evil" }, roots, out pe).Count == 0 && pe != null, "plan: an unknown key");
Check(L.Plan(m, new[] { "fivemcfg", "nope" }, roots, out pe).Count == 0 && pe != null, "plan: one bad key stops all");
var noApp = L.Plan(m, null, noFiveM, out pe);
Check(pe == null && noApp.Count == 3, "plan without FiveM: still three steps");
Check(noApp.Single(p => p.File.Key == "ini") is { Target: null, Skip: "FiveM-Ordner nicht gefunden" }, "plan without FiveM: the ini is skipped with its reason");
Check(noApp.Where(p => p.File.Key != "ini").All(p => p.Target != null && p.Skip == null), "plan without FiveM: the others are written");
Check(L.Plan(m, null, new CfgRoots(null, null, null), out pe).All(p => p.Target == null && p.Skip != null) && pe == null, "plan with no folders: everything skipped");
var broken = L.Parse(L.Serialize(Make()), out _)!; broken.Files[0].Path = @"C:\Windows\win.ini";
Check(L.Plan(broken, null, roots, out pe).Count == 0 && pe != null, "plan: a broken manifest writes nothing");

var dupKeys = L.Plan(m, new[] { "gfx", "gfx" }, roots, out pe);
Check(pe == null && dupKeys.Count == 1 && dupKeys[0].File.Key == "gfx", "plan: a key chosen twice is one step");

// ---------- the snapshot before a restore covers every file it writes ----------
Check(L.Uncovered(new[] { "fivemcfg", "gfx" }, Array.Empty<string>()).Count == 0, "guard: everything was read, everything may be written");
Check(L.Uncovered(new[] { "fivemcfg", "gfx" }, new[] { "fivemcfg" }).SequenceEqual(new[] { "fivemcfg" }), "guard: an unreadable file that would be written stays");
Check(L.Uncovered(new[] { "gfx" }, new[] { "fivemcfg", "ini" }).Count == 0, "guard: an unreadable file that is not written does not matter");
Check(L.Uncovered(new[] { "ini", "gfx", "fivemcfg" }, new[] { "fivemcfg", "ini" }).SequenceEqual(new[] { "ini", "fivemcfg" }), "guard: in the order of the restore");
Check(L.Uncovered(Array.Empty<string>(), new[] { "gfx" }).Count == 0, "guard: nothing to write");
Check(L.Uncovered(new[] { "gfx", "gfx" }, new[] { "gfx" }).SequenceEqual(new[] { "gfx" }), "guard: each file once");

// ---------- what "Rückgängig" writes back ----------
Check(L.UndoKeys(new[] { "gfx" }, new[] { "fivemcfg", "gfx", "ini" }).SequenceEqual(new[] { "gfx" }), "undo: only the file that was restored, not the ones left alone");
Check(L.UndoKeys(new[] { "ini", "fivemcfg" }, new[] { "fivemcfg", "gfx", "ini" }).SequenceEqual(new[] { "fivemcfg", "ini" }), "undo: in the order of the known files");
Check(L.UndoKeys(new[] { "gtaxml", "fivemcfg" }, new[] { "fivemcfg", "gfx" }).SequenceEqual(new[] { "fivemcfg" }), "undo: a file that was not there before is not in the snapshot");
Check(L.UndoKeys(new[] { "gtaxml" }, new[] { "fivemcfg" }).Count == 0, "undo: nothing to write back (the host sends no empty list: that would be all)");
Check(L.UndoKeys(new[] { "../evil", "fivemcfg" }, new[] { "../evil", "fivemcfg" }).SequenceEqual(new[] { "fivemcfg" }), "undo: only known keys");
Check(L.UndoKeys(Array.Empty<string>(), new[] { "fivemcfg" }).Count == 0, "undo: nothing touched");
// undo keys always form a valid choice for a restore from the snapshot
var snap = Make(L.Safety, "fivemcfg", "gfx");
var undoPlan = L.Plan(snap, L.UndoKeys(new[] { "gfx", "gtaxml" }, snap.Files.Select(f => f.Key)), roots, out pe);
Check(pe == null && undoPlan.Count == 1 && undoPlan[0].File.Key == "gfx", "undo: its keys are a valid restore of the snapshot");

// ---------- more manifest and schedule cases ----------
Check(L.Parse(json.Replace("\"format\": 1,", "\"format\": 1, \"note\": \"from a later 1.x\","), out _) != null, "an unknown extra field is ignored");
Check(L.Parse(json.Replace("\"sha256\"", "\"sha\""), out var e5) == null && e5 != null, "an entry without a hash");
Check(L.Parse(json.Replace("\"kind\": \"manual\"", "\"kind\": null"), out _) == null, "a kind of null");
Check(L.Parse(json.Replace("\"key\": \"fivemcfg\"", "\"key\": \"FIVEMCFG\""), out _) == null, "keys are exact");
Check(!L.SameContent(new CfgManifest { Files = null! }, nowSame), "a manifest without files is never the same");
var withNull = Make(); withNull.Files.Add(null!);
Check(L.SameContent(withNull, nowSame), "a null entry is not counted");
var dstLast = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.FromHours(2));   // before the clocks went back
Check(!L.AutoDue(dstLast, new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.FromHours(1))), "the day the clocks go back is still one day");
Check(L.AutoDue(dstLast, new DateTimeOffset(2026, 10, 26, 0, 5, 0, TimeSpan.FromHours(1))), "the day after it is due");
bool? seen = null; int starts = 0;
foreach (var open in new[] { true, true, false, false, true, true, false, true })
{
    if (L.Started(seen, open)) starts++;
    seen = open;
}
Eq(starts, 2, "probes: running at launch is no start, then two real starts");
var mixed = Enumerable.Range(0, 7).Select(d => St(d, L.Safety)).Append(new CfgStored("fivem-backup-old.zip", L.Safety, day.AddDays(-9))).ToList();
var delMixed = L.Prune(mixed);
Check(delMixed.Count == 2 && !delMixed.Contains("fivem-backup-old.zip"), "a snapshot with a name that is not ours is never deleted");
Eq(L.ToPlaceholder(@"C:\Users\Max\AppData\Local\FiveM\FiveM.app", roots), "<FiveM.app>", "FiveM.app itself, not %LOCALAPPDATA%\\…");

// ---------- labels ----------
Eq(L.KindLabel(L.Manual), "Von dir", "manual label");
Eq(L.KindLabel(L.Auto), "Automatisch", "auto label");
Eq(L.KindLabel(L.Safety), "Vor dem Wiederherstellen", "safety label");
Eq(L.KindLabel(null), "Unbekannt", "unknown label");
Eq(L.KindLabel(""), "Unbekannt", "empty label");

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
