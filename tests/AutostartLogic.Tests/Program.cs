using OrbitTweak.Core;

// Checks of the autostart manager's pure logic. Exit code 0 = all fine.
//   dotnet run --project tests/AutostartLogic.Tests

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }
void Eq<T>(T got, T want, string what) => Check(EqualityComparer<T>.Default.Equals(got, want), $"{what}: got [{got}], want [{want}]");

// ---------- the StartupApproved flag ----------
Check(AutostartLogic.IsOn(null), "a missing value is on");
Check(AutostartLogic.IsOn(Array.Empty<byte>()), "an empty value is on");
Check(AutostartLogic.IsOn(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }), "02 is on");
Check(AutostartLogic.IsOn(new byte[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }), "06 is on");
Check(!AutostartLogic.IsOn(new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }), "03 is off");
Check(!AutostartLogic.IsOn(new byte[] { 7, 0, 0, 0 }), "07 is off, also when short");

var when = new DateTime(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc);
var off = AutostartLogic.Flag(false, when);
Eq(off.Length, 12, "off flag length");
Eq(off[0], (byte)3, "off flag first byte");
Check(off[1] == 0 && off[2] == 0 && off[3] == 0, "off flag: three zeros after the first byte");
Eq(BitConverter.ToInt64(off, 4), when.ToFileTimeUtc(), "off flag carries the FILETIME at offset 4");
Check(!AutostartLogic.IsOn(off), "the written off flag reads as off");
Eq(AutostartLogic.OffSince(off), (DateTime?)when, "the moment it was switched off is read back");

var on = AutostartLogic.Flag(true, when);
Eq(on.Length, 12, "on flag length");
Check(on[0] == 2 && on.Skip(1).All(b => b == 0), "on flag is 02 and zeros");
Check(AutostartLogic.IsOn(on), "the written on flag reads as on");
Eq(AutostartLogic.OffSince(on), (DateTime?)null, "an on flag has no off time");
Eq(AutostartLogic.OffSince(new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }), (DateTime?)null, "off without a time");
Eq(AutostartLogic.OffSince(new byte[] { 3, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }), (DateTime?)null, "a nonsense time is ignored");
var local = new DateTime(2026, 11, 14, 10, 30, 0, DateTimeKind.Local);
Eq(AutostartLogic.OffSince(AutostartLogic.Flag(false, local)), (DateTime?)local.ToUniversalTime(), "a local time is stored as UTC");

// ---------- ids ----------
var hkcu = AutostartLogic.Sources.First(s => s.Key == "hkcu");
var id = AutostartLogic.Id(hkcu, "Steam");
Eq(id, "hkcu|Steam", "id of a Run entry");
var back = AutostartLogic.ParseId(id);
Check(back != null && back.Value.src == hkcu && back.Value.entry == "Steam", "an id is read back");
Check(AutostartLogic.ParseId("hkcu|Name|with|bars")?.entry == "Name|with|bars", "a value name may contain the separator");
Check(AutostartLogic.ParseId("nope|Steam") == null, "an unknown source is refused");
Check(AutostartLogic.ParseId("hkcu|") == null && AutostartLogic.ParseId("|x") == null && AutostartLogic.ParseId("") == null && AutostartLogic.ParseId(null) == null, "broken ids are refused");
Check(AutostartLogic.PlainFileName("Spotify.lnk"), "a file name is plain");
Check(!AutostartLogic.PlainFileName(@"..\..\evil.lnk") && !AutostartLogic.PlainFileName("a/b.lnk") && !AutostartLogic.PlainFileName("..") && !AutostartLogic.PlainFileName(""), "paths are no plain file names");
foreach (var s in AutostartLogic.Sources)
{
    Check(s.ApprovedKey.Contains(@"\Explorer\StartupApproved\"), $"{s.Key}: flag key under StartupApproved");
    Check(s.ApprovedKey.StartsWith(s.AllUsers ? "HKLM" : "HKCU"), $"{s.Key}: flag key in the right hive");
    Check(s.Folder ? s.ApprovedKey.EndsWith("StartupFolder") : s.RunKey.EndsWith(@"\Run"), $"{s.Key}: run key or folder flag key");
}
Check(AutostartLogic.Sources.First(s => s.Key == "hklm32").ApprovedKey.EndsWith(@"\Run32"), "WOW6432Node entries use Run32");
Eq(AutostartLogic.Sources.Select(s => s.Key).Distinct().Count(), AutostartLogic.Sources.Length, "source keys are unique");

// ---------- command lines ----------
Eq(AutostartLogic.SplitCommand(@"""C:\Program Files (x86)\Steam\steam.exe"" -silent"), (@"C:\Program Files (x86)\Steam\steam.exe", "-silent"), "quoted path with arguments");
Eq(AutostartLogic.SplitCommand(@"C:\Program Files\LGHUB\lghub.exe --background"), (@"C:\Program Files\LGHUB\lghub.exe", "--background"), "unquoted path with spaces");
Eq(AutostartLogic.SplitCommand(@"C:\Windows\system32\SecurityHealthSystray.exe"), (@"C:\Windows\system32\SecurityHealthSystray.exe", ""), "a path alone");
Eq(AutostartLogic.SplitCommand(@"ctfmon"), ("ctfmon", ""), "a bare name");
Eq(AutostartLogic.SplitCommand(@"  ""C:\x\a.exe""  "), (@"C:\x\a.exe", ""), "spaces round a quoted path");
Eq(AutostartLogic.SplitCommand(@"""C:\x\a.exe"), (@"C:\x\a.exe", ""), "a quote that is not closed");
Eq(AutostartLogic.SplitCommand(""), ("", ""), "an empty command");
Eq(AutostartLogic.SplitCommand(null), ("", ""), "no command");
Eq(AutostartLogic.SplitCommand(@"C:\Tools\run.bat /min"), (@"C:\Tools\run.bat", "/min"), "a batch file");
Eq(AutostartLogic.SplitCommand(@"C:\My.exe.files\tool.exe -x"), (@"C:\My.exe.files\tool.exe", "-x"), ".exe inside a folder name is not the end");

Eq(AutostartLogic.Rundll32Target(@"C:\Windows\system32\rundll32.exe", @"""C:\Program Files\Old\helper.dll"",Start"), @"C:\Program Files\Old\helper.dll", "rundll32 with a quoted DLL");
Eq(AutostartLogic.Rundll32Target("rundll32.exe", @"C:\Old\helper.dll,Start -x"), @"C:\Old\helper.dll", "rundll32 with an unquoted DLL");
Eq(AutostartLogic.Rundll32Target("RUNDLL32", @"shell32.dll"), "shell32.dll", "rundll32 without an entry point");
Eq(AutostartLogic.Rundll32Target(@"C:\x\steam.exe", "a.dll,b"), null, "not rundll32");
Eq(AutostartLogic.Rundll32Target("rundll32.exe", "  "), null, "rundll32 without arguments");

Eq(AutostartLogic.SquirrelApp(@"C:\Users\Max\AppData\Local\Discord\Update.exe", "--processStart Discord.exe"), "Discord.exe", "Squirrel app");
Eq(AutostartLogic.SquirrelApp(@"C:\Users\Max\AppData\Local\Microsoft\Teams\Update.exe", @"--processStart ""Teams.exe"" --process-start-args ""--system-initiated"""), "Teams.exe", "Squirrel app in quotes");
Eq(AutostartLogic.SquirrelApp(@"C:\x\Update.exe", "--processStartAndWait Slack.exe"), "Slack.exe", "Squirrel processStartAndWait");
Eq(AutostartLogic.SquirrelApp(@"C:\x\Update.exe", @"--processStart ..\..\evil.exe"), null, "Squirrel app must be a plain name");
Eq(AutostartLogic.SquirrelApp(@"C:\x\Updater.exe", "--processStart Discord.exe"), null, "only Update.exe");
Eq(AutostartLogic.FileName(@"C:\a\b\c.exe"), "c.exe", "file name of a Windows path");
Eq(AutostartLogic.Folder(@"C:\a\b\c.exe"), @"C:\a\b", "folder of a Windows path");
Eq(AutostartLogic.Folder("c.exe"), "", "no folder");

// ---------- names ----------
foreach (var n in new[] { "Steam", "Discord", "Spotify", "Logitech G HUB", "Opera GX Browser Assistant" })
    Check(AutostartLogic.Readable(n), $"\"{n}\" reads like a name");
foreach (var n in new[] { "RtkAudUService", "SecurityHealth", "com.squirrel.Teams.Teams", "{6E3C9A6B-1F2D-4E8A-9B7C-0D1E2F3A4B5C}", "MicrosoftEdgeAutoLaunch_0E6C0B3A84F5D1A4", "iTunesHelper", "EADM", "NvBackend", "ab" })
    Check(!AutostartLogic.Readable(n), $"\"{n}\" is an internal name");
Eq(AutostartLogic.DisplayName("Steam", @"C:\Steam\steam.exe", "Steam Client Bootstrapper", "Steam"), "Steam", "a readable entry name stays");
Eq(AutostartLogic.DisplayName("SecurityHealth", @"C:\Windows\system32\SecurityHealthSystray.exe", "Windows Security notification icon", "Microsoft® Windows® Operating System"), "Windows Security notification icon", "the description replaces an internal name");
Eq(AutostartLogic.DisplayName("RtkAudUService", @"C:\Windows\System32\RtkAudUService64.exe", "Realtek® HD Audio Universal Service", "Realtek® HD Audio"), "Realtek HD Audio Universal Service", "® is dropped");
Eq(AutostartLogic.DisplayName("iTunesHelper", @"C:\Program Files\iTunes\iTunesHelper.exe", "iTunesHelper", "iTunes"), "iTunes", "the product when the description is only the file name");
Eq(AutostartLogic.DisplayName("com.squirrel.Teams.Teams", @"C:\x\Teams.exe", "Microsoft Teams", "Microsoft Teams"), "Microsoft Teams", "Squirrel value name");
Eq(AutostartLogic.DisplayName("com.squirrel.app.app", @"C:\x\Update.exe", "Update", "Squirrel"), "Squirrel", "a generic description is skipped");
Eq(AutostartLogic.DisplayName("NvBackend", @"C:\x\NvBackend.exe", "", ""), "NvBackend", "no version info: the entry name");
Eq(AutostartLogic.DisplayName("ctfmon", @"C:\Windows\System32\ctfmon.exe", "CTF Loader", "Microsoft® Windows® Operating System"), "CTF Loader", "a lower-case value name");
Eq(AutostartLogic.DisplayName("Foo", @"C:\x\foo.exe", "", "Microsoft® Windows® Operating System"), "Foo", "the Windows product name is no name");
Eq(AutostartLogic.Tidy("  Realtek®   HD (TM) Audio "), "Realtek HD Audio", "tidy");

// ---------- advice ----------
string? Rec(string entry, string exe, string pub = "", string args = "", bool missing = false, string? name = null)
    => AutostartLogic.Recommend(entry, name ?? entry, exe, pub, args, missing)?.Rec;
Eq(Rec("Steam", @"C:\Steam\steam.exe", "Valve Corporation", "-silent"), "off", "Steam can go");
Eq(Rec("EpicGamesLauncher", @"C:\x\EpicGamesLauncher.exe", "Epic Games, Inc.", "-silent", name: "Epic Games Launcher"), "off", "Epic can go");
Eq(Rec("EADM", @"C:\x\EADesktop.exe", "Electronic Arts", name: "EA app"), "off", "EA app can go");
Eq(Rec("Ubisoft Connect", @"C:\x\upc.exe", "Ubisoft"), "off", "Ubisoft can go");
Eq(Rec("Battle.net", @"C:\x\Battle.net Launcher.exe", "Blizzard Entertainment"), "off", "Battle.net can go");
Eq(Rec("Rockstar Games Launcher", @"C:\x\Launcher.exe", "Rockstar Games"), "off", "Rockstar can go");
Eq(Rec("Spotify", @"C:\x\Spotify.exe", "Spotify AB", "--autostart --minimized"), "off", "Spotify can go");
Eq(Rec("com.squirrel.Teams.Teams", @"C:\x\Update.exe", "Microsoft Corporation", "--processStart Teams.exe", name: "Microsoft Teams"), "off", "Teams can go");
Eq(Rec("Skype for Desktop", @"C:\x\Skype.exe", "Skype Technologies S.A."), "off", "Skype can go");
Eq(Rec("Adobe Creative Cloud", @"C:\x\Creative Cloud.exe", "Adobe Inc."), "off", "Creative Cloud can go");
Eq(Rec("AdobeAAMUpdater-1.0", @"C:\x\AAMUpdater.exe", "Adobe Systems", name: "Adobe Updater"), "off", "Adobe updater can go");
Eq(Rec("CCleaner Smart Cleaning", @"C:\x\CCleaner64.exe", "Piriform Software Ltd", "/MONITOR"), "off", "CCleaner can go");
Eq(Rec("iTunesHelper", @"C:\x\iTunesHelper.exe", "Apple Inc.", name: "iTunes"), "off", "iTunesHelper can go");
Eq(Rec("Opera GX Browser Assistant", @"C:\x\browser_assistant.exe", "Opera Norway AS"), "off", "Opera GX assistant can go");
Eq(Rec("GoogleChromeAutoLaunch_6E9A1B2C3D4E5F60", @"C:\x\chrome.exe", "Google LLC", "--no-startup-window /prefetch:5", name: "Google Chrome"), "off", "Chrome auto launch can go");
Eq(Rec("MicrosoftEdgeAutoLaunch_0E6C0B3A84F5D1A4", @"C:\x\msedge.exe", "Microsoft Corporation", "--win-session-start", name: "Microsoft Edge"), "off", "Edge auto launch can go");
Eq(Rec("SecurityHealth", @"C:\Windows\system32\SecurityHealthSystray.exe", "Microsoft Corporation", name: "Windows Security notification icon"), "on", "Windows Security stays");
Eq(Rec("RtkAudUService", @"C:\x\RtkAudUService64.exe", "Realtek Semiconductor", name: "Realtek HD Audio Universal Service"), "on", "Realtek stays");
Eq(Rec("NvBackend", @"C:\x\NvBackend.exe", "NVIDIA Corporation", name: "NVIDIA GeForce Experience Backend"), "on", "NVIDIA stays");
Eq(Rec("AMD Software", @"C:\x\RadeonSoftware.exe", "Advanced Micro Devices, Inc."), "on", "AMD stays");
Eq(Rec("IgfxTray", @"C:\x\igfxtray.exe", "Intel Corporation"), "on", "Intel graphics stays");
Eq(Rec("Logitech G HUB", @"C:\x\lghub.exe", "Logitech, Inc.", "--background"), "on", "G HUB stays");
Eq(Rec("Razer Synapse", @"C:\x\Razer Synapse 3.exe", "Razer Inc."), "on", "Razer Synapse stays");
Eq(Rec("Razer Cortex", @"C:\x\RazerCortex.exe", "Razer Inc."), "off", "Razer Cortex can go (an exception inside Razer)");
Eq(Rec("SteelSeries GG", @"C:\x\SteelSeriesGG.exe", "SteelSeries ApS"), "on", "SteelSeries stays");
Eq(Rec("Corsair iCUE5 Software", @"C:\x\iCUE.exe", "Corsair Memory, Inc."), "on", "iCUE stays");
Eq(Rec("Avast", @"C:\x\AvastUI.exe", "Avast Software"), "on", "antivirus stays");
Eq(Rec("Discord", @"C:\x\Update.exe", "Discord Inc.", "--processStart Discord.exe"), null, "Discord: no advice");
Eq(Rec("OrbitTweak", @"C:\x\OrbitTweak.exe", "", "", missing: true), null, "OrbitTweak: no advice, not even when gone");
Eq(Rec("OneDrive", @"C:\x\OneDrive.exe", "Microsoft Corporation", "/background", name: "Microsoft OneDrive"), null, "OneDrive: no advice");
Eq(Rec("Some Tool", @"C:\x\tool.exe", "Someone"), null, "unknown: no advice");
Eq(Rec("IntelliJ Toolbox", @"C:\x\jetbrains-toolbox.exe", "JetBrains s.r.o."), null, "IntelliJ is not Intel");
Eq(Rec("Steam", @"C:\x\steam.exe", "Valve Corporation", missing: true), "off", "a missing file can go");
Check(AutostartLogic.Recommend("Steam", "Steam", @"C:\x\steam.exe", "", "", true)!.Why.Contains("fehlt"), "a missing file says so");
Check(AutostartLogic.Recommend("OneDrive", "Microsoft OneDrive", @"C:\x\OneDrive.exe", "", "", false) is { Rec: null } a && a.Why.Length > 0, "OneDrive carries a note without advice");
Check(AutostartLogic.Recommend("Discord", "Discord", @"C:\x\Update.exe", "", "", false) is { Rec: null }, "Discord carries a note without advice");

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
