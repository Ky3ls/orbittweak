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
foreach (var f in new[] { "Spotify.lnk", "Start.url", "tool.exe", "run.bat", "sync.vbs", "boot.ps1", "notes.txt" })
    Check(AutostartLogic.StartupFile(f), $"{f} in the startup folder is opened at sign-in");
foreach (var f in new[] { "desktop.ini", "Desktop.INI", "Thumbs.db", "", @"..\x.lnk" })
    Check(!AutostartLogic.StartupFile(f), $"\"{f}\" is not a startup entry");
foreach (var h in new[] { @"C:\Windows\System32\cmd.exe", "powershell.exe", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", "pwsh", "wscript.exe", "MSHTA.EXE", "javaw.exe", "rundll32.exe" })
    Check(AutostartLogic.ScriptHost(h), $"{h} only runs something else");
foreach (var h in new[] { @"C:\Steam\steam.exe", "Discord.exe", "cmdtool.exe", @"C:\x\powershell-helper.exe", "" })
    Check(!AutostartLogic.ScriptHost(h), $"\"{h}\" is a program of its own");
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
Eq(AutostartLogic.DisplayName("Updater", @"C:\x\GHelper.exe", "G-Helper", "G-Helper"), "G-Helper", "a generic value name gives way to the description");
Eq(AutostartLogic.DisplayName("Launcher", @"C:\x\launcher.exe", "", ""), "Launcher", "a generic value name stays when nothing better is known");
Eq(AutostartLogic.DisplayName("nightly-backup", @"C:\Windows\System32\cmd.exe", "", ""), "nightly-backup", "a script entry without version info keeps its own name");

// ---------- advice ----------
string? Rec(string entry, string exe, string pub = "", string args = "", bool missing = false, string? name = null)
    => AutostartLogic.Recommend(entry, name ?? entry, exe, pub, args, missing)?.Rec;
Eq(Rec("Steam", @"C:\Steam\steam.exe", "Valve Corporation", "-silent"), "off", "Steam can go");
Check(AutostartLogic.Recommend("Steam", "Steam", @"C:\Steam\steam.exe", "Valve Corporation", "-silent", false)!.Why.Contains("FiveM"), "Steam says to open it before FiveM");
Check(!AutostartLogic.Recommend("EpicGamesLauncher", "Epic Games Launcher", @"C:\x\EpicGamesLauncher.exe", "", "", false)!.Why.Contains("Steam"), "other launchers do not talk about Steam");
Eq(Rec("SteamVR", @"C:\x\vrmonitor.exe", "Valve Corporation"), null, "SteamVR is not Steam");
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

// ---------- Store apps (2.28) ----------
var store = AutostartLogic.Store;
Check(!AutostartLogic.Sources.Contains(store), "the Store source is not one of the Run/folder sources");
Check(store.ApprovedKey == @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData", "Store state lives under SystemAppData of the user");
Check(!store.AllUsers && !store.Folder, "Store apps are per user and no folder");
var teamsEntry = AutostartLogic.StoreEntry("MSTeams_8wekyb3d8bbwe", "TeamsTfwStartupTask");
Eq(teamsEntry, "MSTeams_8wekyb3d8bbwe/TeamsTfwStartupTask", "Store entry");
var sid = AutostartLogic.Id(store, teamsEntry);
Eq(sid, "store|MSTeams_8wekyb3d8bbwe/TeamsTfwStartupTask", "Store id");
Check(AutostartLogic.ParseId(sid) is { } sp && sp.src == store && sp.entry == teamsEntry, "a Store id is read back");
Eq(AutostartLogic.ParseStoreEntry(teamsEntry), (("MSTeams_8wekyb3d8bbwe", "TeamsTfwStartupTask") as (string, string)?), "a Store entry is split");
Eq(AutostartLogic.ParseStoreEntry("5319275A.WhatsAppDesktop_cv1g1gvanyjgm/WhatsAppStartupTask")?.family, "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "WhatsApp's family is valid");
Eq(AutostartLogic.ParseStoreEntry("SpotifyAB.SpotifyMusic_zpdnekdrzrea0/Spotify")?.taskId, "Spotify", "Spotify's task");
foreach (var bad in new[] { "", "/x", "MSTeams_8wekyb3d8bbwe/", "MSTeams/Task", @"..\..\Run_8wekyb3d8bbwe/x", "MSTeams_8wekyb3d8bbwe/a\\b", "MSTeams_8wekyb3d8bbwe/..", "MSTeams_8wekyb3d8bbw/x", "MS Teams_8wekyb3d8bbwe/x", "MSTeams_8wekyb3d8bbwe/ x" })
    Check(AutostartLogic.ParseStoreEntry(bad) == null, $"bad Store entry \"{bad}\" is refused");
Check(AutostartLogic.ParseStoreEntry(null) == null, "no Store entry");
Check(AutostartLogic.ParseStoreEntry("MSTeams_8wekyb3d8bbwe/a/b") == null, "a task id with a slash is refused");
Check(AutostartLogic.TaskIdOk("TeamsTfwStartupTask") && AutostartLogic.TaskIdOk("My Task.1") && !AutostartLogic.TaskIdOk("a|b") && !AutostartLogic.TaskIdOk("a\u0001") && !AutostartLogic.TaskIdOk(new string('x', 256)), "task ids");

// state: 0 disabled, 1 by the user, 2 enabled, 3/4 policy; no value = not registered yet (never opened) = off
Check(AutostartLogic.StoreOn(2) && AutostartLogic.StoreOn(4), "2 and 4 are on");
Check(!AutostartLogic.StoreOn(0) && !AutostartLogic.StoreOn(1) && !AutostartLogic.StoreOn(3), "0, 1 and 3 are off");
Check(!AutostartLogic.StoreOn(null), "no value: the app was never opened, Windows does not start it");
Check(!AutostartLogic.StoreOn(9) && !AutostartLogic.StoreOn(-1), "an unknown value is off");
Check(AutostartLogic.StoreLocked(3) && AutostartLogic.StoreLocked(4) && !AutostartLogic.StoreLocked(1) && !AutostartLogic.StoreLocked(2) && !AutostartLogic.StoreLocked(null), "only 3 and 4 are locked");
Eq(AutostartLogic.StoreState(true), 2, "on is written as 2");
Eq(AutostartLogic.StoreState(false), 1, "off is written as 1 (by the user), so the app cannot switch itself on again");
Check(!AutostartLogic.StoreOn(AutostartLogic.StoreState(false)) && AutostartLogic.StoreOn(AutostartLogic.StoreState(true)), "what is written reads back");

// manifests
const string teamsXml = """
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10" IgnorableNamespaces="uap desktop">
  <Identity Name="MSTeams" Publisher="CN=Microsoft Corporation" Version="24295.605.3225.8804" ProcessorArchitecture="x64" />
  <Properties><DisplayName>Microsoft Teams</DisplayName><PublisherDisplayName>Microsoft Corporation</PublisherDisplayName><Logo>Images\StoreLogo.png</Logo></Properties>
  <Applications>
    <Application Id="MSTeams" Executable="ms-teams.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Microsoft Teams" Square150x150Logo="Images\Square150x150Logo.png" Square44x44Logo="Images\Square44x44Logo.png" Description="Teams" BackgroundColor="transparent" />
      <Extensions>
        <uap:Extension Category="windows.protocol"><uap:Protocol Name="msteams" /></uap:Extension>
        <desktop:Extension Category="windows.startupTask" Executable="ms-teams.exe" EntryPoint="Windows.FullTrustApplication">
          <desktop:StartupTask TaskId="TeamsTfwStartupTask" Enabled="true" DisplayName="Microsoft Teams" />
        </desktop:Extension>
      </Extensions>
    </Application>
  </Applications>
</Package>
""";
var tt = AutostartLogic.ParseManifest(teamsXml);
Eq(tt.Count, 1, "Teams declares one startup task");
Check(tt.Count == 1 && tt[0] == new AutostartLogic.StoreTask("MSTeams", "ms-teams.exe", "TeamsTfwStartupTask", true, "Microsoft Teams", @"Images\Square44x44Logo.png"), $"Teams task read: {(tt.Count > 0 ? tt[0].ToString() : "none")}");

const string uwpXml = """
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5">
  <Properties><Logo>Assets\StoreLogo.png</Logo></Properties>
  <Applications>
    <Application Id="App" Executable="Chat.exe" EntryPoint="Chat.App">
      <uap:VisualElements DisplayName="ms-resource:AppName" Square44x44Logo="" />
      <Extensions>
        <uap5:Extension Category="windows.startupTask"><uap5:StartupTask TaskId="ChatStartup" Enabled="false" DisplayName="ms-resource:StartupName" /></uap5:Extension>
        <uap5:Extension Category="windows.startupTask"><uap5:StartupTask TaskId="ChatStartup" Enabled="true" /></uap5:Extension>
        <uap5:Extension Category="windows.startupTask"><uap5:StartupTask TaskId="..\Run" Enabled="true" /></uap5:Extension>
      </Extensions>
    </Application>
    <Application Id="Helper" Executable="Helper\helper.exe">
      <Extensions>
        <uap5:Extension Category="Windows.StartupTask"><uap5:StartupTask TaskId="HelperTask" Enabled="1" /></uap5:Extension>
      </Extensions>
    </Application>
  </Applications>
</Package>
""";
var ut = AutostartLogic.ParseManifest(uwpXml);
Eq(ut.Count, 2, "a doubled task id and an unsafe one are dropped");
Check(ut.Count == 2 && ut[0].TaskId == "ChatStartup" && !ut[0].Enabled && ut[0].Executable == "Chat.exe" && ut[0].Logo == @"Assets\StoreLogo.png", "UWP task: the app's exe, the package logo when the app has none");
Check(ut.Count == 2 && !AutostartLogic.PlainText(ut[0].DisplayName), "an ms-resource name is no plain text");
Check(ut.Count == 2 && ut[1] == new AutostartLogic.StoreTask("Helper", @"Helper\helper.exe", "HelperTask", true, "", @"Assets\StoreLogo.png"), "second app: Enabled=\"1\", category in any case");
Eq(AutostartLogic.ParseManifest("""<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Applications><Application Id="A" Executable="a.exe"/></Applications></Package>""").Count, 0, "no startup task");
Eq(AutostartLogic.ParseManifest("not xml <").Count, 0, "broken XML gives none");
Eq(AutostartLogic.ParseManifest("").Count, 0, "empty manifest gives none");
Eq(AutostartLogic.ParseManifest(null).Count, 0, "no manifest gives none");
Eq(AutostartLogic.ParseManifest("""<!DOCTYPE p [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><Package><Applications><Application Id="A"><Extensions><Extension Category="windows.startupTask"><StartupTask TaskId="&x;"/></Extension></Extensions></Application></Applications></Package>""").Count, 0, "a DTD with an external entity is refused");
Eq(AutostartLogic.ParseManifest("""<!DOCTYPE p [<!ENTITY x "abc">]><Package><Applications><Application Id="A"><Extensions><Extension Category="windows.startupTask"><StartupTask TaskId="&x;"/></Extension></Extensions></Application></Applications></Package>""").Count, 0, "a DTD with an internal entity is refused too");
Check(AutostartLogic.PlainText("WhatsApp") && !AutostartLogic.PlainText("ms-resource:x") && !AutostartLogic.PlainText(" ") && !AutostartLogic.PlainText(null) && !AutostartLogic.PlainText(new string('a', 65)), "plain text");

// logos
var logos = new[] { "Square44x44Logo.scale-100.png", "Square44x44Logo.scale-200.png", "Square44x44Logo.targetsize-48.png", "Square44x44Logo.targetsize-48_altform-unplated.png",
    "Square44x44Logo.targetsize-64_altform-lightunplated.png", "Square44x44Logo.contrast-white_targetsize-64.png", "Square150x150Logo.scale-200.png", "Square44x44Logo.targetsize-16_altform-unplated.png" };
Eq(AutostartLogic.PickLogo(@"Images\Square44x44Logo.png", logos), "Square44x44Logo.targetsize-48_altform-unplated.png", "the unplated 48 px logo wins");
Eq(AutostartLogic.PickLogo("Square44x44Logo.png", new[] { "Square44x44Logo.scale-100.png", "Square44x44Logo.scale-200.png", "Square44x44Logo.scale-400.png" }), "Square44x44Logo.scale-100.png", "scale 100 (44 px) is closest to 64 among 44, 88 and 176");
Eq(AutostartLogic.PickLogo("Logo.png", new[] { "Logo.png" }), "Logo.png", "the plain file when it exists");
Eq(AutostartLogic.PickLogo("Logo.png", new[] { "Logo.contrast-black_scale-100.png", "Logo.theme-light_scale-100.png" }), null, "high contrast and light theme never");
Eq(AutostartLogic.PickLogo("Logo.png", new[] { "LogoBig.png", "Logo.png.bak", "Other.png" }), null, "other files are no variants");
Eq(AutostartLogic.PickLogo("Logo", new[] { "Logo" }), null, "a logo without extension");
Eq(AutostartLogic.PickLogo("StoreLogo.png", new[] { "StoreLogo.scale-100.png", "StoreLogo.targetsize-48_altform-unplated_devicefamily-colorfulunplated.png" }), "StoreLogo.targetsize-48_altform-unplated_devicefamily-colorfulunplated.png", "unknown qualifiers do not hurt");
Eq(AutostartLogic.PickLogo("Logo.png", new[] { "Logo.contrast-standard_scale-100.png", "Logo.contrast-high_scale-100.png", "Logo.contrast-white_targetsize-48.png" }), "Logo.contrast-standard_scale-100.png", "contrast-standard is the normal look");

// advice for Store apps (the entry is family/task id, the name the package's)
string? SRec(string family, string task, string name, string exe = "", string pub = "")
    => AutostartLogic.Recommend(family + "/" + task, name, exe, pub, "", false)?.Rec;
Eq(SRec("MSTeams_8wekyb3d8bbwe", "TeamsTfwStartupTask", "Microsoft Teams", @"C:\Program Files\WindowsApps\MSTeams_x\ms-teams.exe", "Microsoft Corporation"), "off", "new Teams can go");
Eq(SRec("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "WhatsAppStartupTask", "WhatsApp", "", "WhatsApp Inc."), "off", "WhatsApp can go");
Eq(SRec("SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", "Spotify Music", "", "Spotify AB"), "off", "Spotify from the Store can go");
Eq(SRec("Microsoft.GamingApp_8wekyb3d8bbwe", "XboxAppStartupTask", "Xbox", "", "Microsoft Corporation"), "off", "the Xbox app can go");
Eq(SRec("Microsoft.YourPhone_8wekyb3d8bbwe", "YourPhoneStartupTask", "Smartphone-Link", "", "Microsoft Corporation"), "off", "Phone Link can go");
Eq(SRec("Microsoft.YourPhone_8wekyb3d8bbwe", "YourPhoneStartupTask", "Telefon Bağlantısı", @"C:\x\PhoneExperienceHost.exe", "Microsoft Corporation"), "off", "Phone Link in any display language");
Eq(SRec("TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm", "Telegram", "Telegram Desktop"), "off", "Telegram can go");
Eq(SRec("RealtekSemiconductorCorp.RealtekAudioControl_dt26b99r8h8gj", "RtkUWP", "Realtek Audio Console", "", "Realtek Semiconductor Corp"), "on", "Realtek Audio Console stays");
Eq(SRec("DolbyLaboratories.DolbyAccess_rz1tebttyb220", "DolbyStartup", "Dolby Access", "", "Dolby Laboratories"), "on", "Dolby stays");
Eq(SRec("Microsoft.WindowsTerminal_8wekyb3d8bbwe", "StartTerminalOnLoginTask", "Terminal", "", "Microsoft Corporation"), null, "Terminal: no advice");
Check(AutostartLogic.Recommend("AppleInc.iCloud_nzyj5cx40ttqa/iCloudStartup", "iCloud", "", "Apple Inc.", "", false) is { Rec: null } ic && ic.Why.Contains("Synchron"), "iCloud: a sync note, no advice");
Eq(SRec("Microsoft.Copilot_8wekyb3d8bbwe", "CopilotStartupTask", "Microsoft Copilot"), "off", "Copilot can go");

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
