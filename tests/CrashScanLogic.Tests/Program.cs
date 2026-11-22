using System.Text;
using OrbitTweak.Core;

// Checks of the crash analysis' pure logic. Exit code 0 = all fine.
//   dotnet run --project tests/CrashScanLogic.Tests
// The samples follow what FiveM writes (see the comment at the top of Core/CrashScanLogic.cs): the line format of
// Console.Logging.cpp, the dialog text of MiniDump.cpp and the error texts of ErrorHandler.cpp, RenderHooks.cpp,
// ResourceTimeWarnings.cpp and PoolManagement.cpp, several of them copied from real logs players posted on forum.cfx.re
// (user names replaced). The GTA error descriptions after "RAGE error:" come from GTA's own errorcodes file; their wording
// here is made up.

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }
void Eq<T>(T got, T want, string what) => Check(EqualityComparer<T>.Default.Equals(got, want), $"{what}: got [{got}], want [{want}]");

// a log line as FiveM writes it: "[%10lld] [%14s] %20s/ %s"
string L(long tick, string proc, string thread, string text) => $"[{tick,10}] [{proc,14}] {thread,20}/ {text}";
const string D = "DumpServer";
var pcNone = new CrashPc();
CrashReport One(params string[] lines) { var log = CrashScanLogic.ScanLog(lines); return CrashScanLogic.FromBlock(log.Blocks[0], log, "CitizenFX_log_x.log"); }
CrashReport Dialog(string thread, params string[] dialog)
{
    var l = new List<string> { L(563, D, "3684", "DumpServer is active and waiting."), L(563, "FiveM", "MainThrd", "--- BEGIN LOGGING AT Sat Nov 21 19:30:15 2026 ---"), L(9000, D, thread, CrashScanLogic.CaptureMarker) };
    l.AddRange(dialog.Select(x => L(9000, D, thread, x)));
    return One(l.ToArray());
}

// ---------- log lines ----------
var p1 = CrashScanLogic.ParseLine("[     10531] [    DumpServer]                17420/ Process crash captured. Crash dialog content:");
Check(p1 != null && p1.Tick == 10531 && p1.Proc == "DumpServer" && p1.Thread == "17420" && p1.Text == CrashScanLogic.CaptureMarker, "a DumpServer line");
var p2 = CrashScanLogic.ParseLine("[      6953] [    GTAProcess] [Mumble] Audio Input/ MumbleAudioInput::InitializeAudioDevice: Initialized audio capture device.");
Check(p2 != null && p2.Thread == "[Mumble] Audio Input" && p2.Text.StartsWith("MumbleAudioInput"), "a thread name with brackets and spaces");
var p3 = CrashScanLogic.ParseLine("[     10531] [    DumpServer]                17420/ \r");
Check(p3 != null && p3.Text == "", "an empty dialog line (with the CR of CRLF)");
var p4 = CrashScanLogic.ParseLine("[         0] Process crash captured. Crash dialog content:");
Check(p4 != null && p4.Tick == 0 && p4.Proc == "" && p4.Text == CrashScanLogic.CaptureMarker, "the old format without process and thread");
var p5 = CrashScanLogic.ParseLine("[     48016] [b3258_GTAProce]             MainThrd/ pre-gameload component fx");
Check(p5 != null && p5.Proc == "b3258_GTAProce" && p5.Thread == "MainThrd", "a game process line with its build");
Check(CrashScanLogic.ParseLine("") == null && CrashScanLogic.ParseLine(null) == null && CrashScanLogic.ParseLine("no tick here") == null, "no line, no result");
Eq(CrashScanLogic.ParseLine(L(12, "FiveM", "MainThrd", "hello"))?.Text, "hello", "the test helper writes FiveM's format");

// ---------- session start ----------
Eq(CrashScanLogic.StartFromName("CitizenFX_log_2026-11-21T193015.log"), (DateTime?)new DateTime(2026, 11, 21, 19, 30, 15, DateTimeKind.Utc), "start from the file name (UTC)");
Eq(CrashScanLogic.StartFromName("citizenfx_log_2026-11-21T193015.log")?.Kind, (DateTimeKind?)DateTimeKind.Utc, "file name case does not matter, the time is UTC");
Check(CrashScanLogic.StartFromName("CitizenFX_log_2026-13-21T193015.log") == null && CrashScanLogic.StartFromName("CitizenFX.log") == null && CrashScanLogic.StartFromName(null) == null, "a broken name has no start");
Eq(CrashScanLogic.ParseBegin("--- BEGIN LOGGING AT Tue Sep  1 05:12:11 2026 ---"), (DateTime?)new DateTime(2026, 9, 1, 5, 12, 11, DateTimeKind.Utc), "BEGIN line with a padded day");
Eq(CrashScanLogic.ParseBegin("--- BEGIN LOGGING AT Sat Nov 21 19:30:15 2026 ---"), (DateTime?)new DateTime(2026, 11, 21, 19, 30, 15, DateTimeKind.Utc), "BEGIN line with a two-digit day");
Check(CrashScanLogic.ParseBegin("--- BEGIN LOGGING AT Sat Foo 21 19:30:15 2026 ---") == null && CrashScanLogic.ParseBegin("hello") == null, "a broken BEGIN line");

// ---------- one full session log with two crashes, interleaved lines and the report ID ----------
string[] session =
{
    L(563, D, "3684", "DumpServer is active and waiting."),
    L(563, "FiveM", "MainThrd", "--- BEGIN LOGGING AT Sat Nov 21 19:30:15 2026 ---"),
    L(2297, "FiveM", "MainThrd", @"hello from ""C:\Users\Max\AppData\Local\FiveM\FiveM.exe"" "),
    L(45125, "b3258_GTAProce", "MainThrd", @"hello from ""C:\Users\Max\AppData\Local\FiveM\FiveM.exe"" "),
    L(48110, "b3258_GTAProce", "MainThrd", ">16 GiB of system RAM, increasing streaming allocator size to 2 GiB"),
    L(3601523, D, "17420", CrashScanLogic.CaptureMarker),
    L(3601523, D, "17420", "nvgpucomp64.dll+2A4B10"),
    L(3601523, "b3258_GTAProce", "MainThrd", "this line of another process lands in between"),
    L(3601524, D, "17420", "An error at nvgpucomp64.dll+2A4B10 caused FiveM to stop working. A crash report is being uploaded to the FiveM developers."),
    L(3601524, D, "17420", "Stack trace:"),
    L(3601524, D, "17420", "  nvgpucomp64.dll+2A4B10"),
    L(3601524, D, "17420", "  nvgpucomp64.dll+1F0C02"),
    L(3601524, D, "17420", "  nvwgf2umx.dll+9F1E20"),
    L(3601525, D, "17420", "  d3d11.dll!CContext::TID3D11DeviceContext_Draw_<1> (0x5e)"),
    L(3601525, D, "17420", "  GTA5_b3258.exe!sub_1415D7E8C (0x1c4)"),
    L(3601525, D, "17420", ""),
    L(3602100, "b3258_GTAProce", "MainThrd", "still logging"),
    L(3609476, D, "17420", "Crash report service returned si-923407810a0e40deba6e61b554b7a499"),
};
var log = CrashScanLogic.ScanLog(session, CrashScanLogic.StartFromName("CitizenFX_log_2026-11-21T193015.log"));
Eq(log.Build, "b3258", "build from the game process tag");
Eq(log.StartTick, 563L, "the BEGIN line's tick");
Eq(log.Blocks.Count, 1, "one crash dialog");
var blk = log.Blocks[0];
Eq(blk.Lines.Count, 8, "dialog lines without the other process and without the trailing blank");
Eq(blk.Lines[0], "nvgpucomp64.dll+2A4B10", "headline");
Eq(blk.ReportId, "si-923407810a0e40deba6e61b554b7a499", "report ID read eight seconds later");
Eq(log.TimeOf(blk), (DateTime?)new DateTime(2026, 11, 21, 20, 30, 15, 960, DateTimeKind.Utc), "crash time = session start + ticks");

var r1 = CrashScanLogic.FromBlock(blk, log, "CitizenFX_log_2026-11-21T193015.log");
Eq(r1.Signature, "nvgpucomp64.dll+2A4B10", "signature");
Eq(r1.Module, "nvgpucomp64.dll", "module");
Eq(r1.Stack.Count, 5, "five stack frames");
Eq(r1.Stack[3], "d3d11.dll!CContext::TID3D11DeviceContext_Draw_<1> (0x5e)", "frames are trimmed");
Check(r1.Text == null, "a plain crash has no error text (the 'An error at' sentence is boilerplate)");
Eq(r1.Build, "b3258", "build from the log");
Eq(r1.ReportId, "si-923407810a0e40deba6e61b554b7a499", "report ID on the report");
Eq(r1.Log, "CitizenFX_log_2026-11-21T193015.log", "log name kept");
var c1 = CrashScanLogic.Analyze(r1, new CrashPc("nvidia"));
Eq(c1.Key, "gpu-driver", "nvgpucomp64 is the NVIDIA driver");
Eq(c1.Sure, "klar", "the crash happened right in the driver");
Check(c1.Fixes.Any(f => f.Id == "driver:nvidia" && f.Kind == "host" && f.Arg == "driver:nvidia"), "offers the NVIDIA driver page");
Check(c1.Why.Contains("nvgpucomp64.dll"), "names the module");
var c1b = CrashScanLogic.Analyze(r1, new CrashPc("nvidia", HagsOn: true));
Check(c1b.Fixes.Any(f => f.Id == "hags" && f.Kind == "tweakoff" && f.Arg == "hags"), "GPU scheduling off is offered only when it is on");
Check(!c1.Fixes.Any(f => f.Id == "hags"), "…and not when it is off");

// a log without a BEGIN line takes the file name's start
var noBegin = CrashScanLogic.ScanLog(new[] { L(1000, D, "1", CrashScanLogic.CaptureMarker), L(1000, D, "1", "GTA5_b3095.exe+11E2E0D") }, new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc));
Eq(noBegin.TimeOf(noBegin.Blocks[0]), (DateTime?)new DateTime(2026, 11, 1, 10, 0, 1, DateTimeKind.Utc), "time from the file name when BEGIN is missing");
var noStart = CrashScanLogic.ScanLog(new[] { L(1000, D, "1", CrashScanLogic.CaptureMarker), L(1000, D, "1", "GTA5+654BB2D") });
Check(noStart.TimeOf(noStart.Blocks[0]) == null, "no start, no time");

// two crashes in one session (the first one was a crash of the NUI browser the game survived)
var two = CrashScanLogic.ScanLog(new[]
{
    L(0, "FiveM", "MainThrd", "--- BEGIN LOGGING AT Sun Nov 22 08:00:00 2026 ---"),
    L(50000, D, "200", CrashScanLogic.CaptureMarker), L(50000, D, "200", "libcef.dll+3C1A2F"), L(50000, D, "200", "An error at libcef.dll+3C1A2F caused FiveM to stop working. A crash report is being uploaded to the FiveM developers."),
    L(900000, D, "201", CrashScanLogic.CaptureMarker), L(900000, D, "201", "GTA5_b3095.exe!sub_14109D8D8 (0x2b)"),
    L(900001, D, "201", "An error at GTA5_b3095.exe!sub_14109D8D8 (0x2b) caused FiveM to stop working. A crash report is being uploaded to the FiveM developers."),
    L(900001, D, "201", ""), L(900001, D, "201", "Legacy crash hash: summer-uncle-pip"), L(900001, D, "201", "Stack trace:"), L(900001, D, "201", "  GTA5_b3095.exe!sub_14109D8D8 (0x2b)"), L(900001, D, "201", ""),
    L(905000, D, "201", "Error uploading crash: HTTP 0"),
});
Eq(two.Blocks.Count, 2, "two crashes in one session");
Check(two.Blocks.All(b => b.ReportId == null), "a failed upload has no report ID");
var rGame = CrashScanLogic.FromBlock(two.Blocks[1], two);
Eq(rGame.Build, "b3095", "build from the signature when the log has none");
Eq(rGame.LegacyHash, "summer-uncle-pip", "legacy crash hash");
Eq(rGame.Module, "GTA5_b3095.exe", "game module");
var cGame = CrashScanLogic.Analyze(rGame, pcNone);
Eq(cGame.Key, "game", "a crash in game code");
Eq(cGame.Sure, "unklar", "game code is no clear cause");
Check(cGame.Fixes.Any(f => f.Id == "mods" && f.Kind == "page"), "points to the own mods");
var cNui = CrashScanLogic.Analyze(CrashScanLogic.FromBlock(two.Blocks[0], two), new CrashPc(PagefileOk: false));
Eq(cNui.Key, "nui", "libcef is the NUI browser");
Check(cNui.Fixes.Any(f => f.Id == "pagefile" && f.Kind == "health" && f.Arg == "tweak:pagefile"), "NUI crashes often come from full memory: page file offered when it is not fine");
Check(!CrashScanLogic.Analyze(CrashScanLogic.FromBlock(two.Blocks[0], two), pcNone).Fixes.Any(f => f.Id == "pagefile"), "…and not when it is fine");

// the old format (2019) and the old game module name
var old = One("[         0] Process crash captured. Crash dialog content:", "[         0] GTA5+654BB2D",
    "[         0] An error at GTA5+654BB2D caused FiveM to stop working. A crash report is being uploaded to the FiveM developers. If you require immediate support, please visit <A HREF=\"https://forum.fivem.net/\">FiveM.net</A> and mention the details below.",
    "[         0] ", "[         0] Legacy crash hash: charlie-uranus-mockingbird", "[      2016] Crash report service returned si-cb413ac3233f4d3fa2e557d39d487581");
Eq(old.Signature, "GTA5+654BB2D", "old signature");
Eq(old.Module, "GTA5", "old game module");
Eq(old.LegacyHash, "charlie-uranus-mockingbird", "old legacy hash");
Eq(old.ReportId, "si-cb413ac3233f4d3fa2e557d39d487581", "old report ID");
Eq(old.Build, (string?)null, "old logs have no build");
Eq(CrashScanLogic.Analyze(old, pcNone).Key, "game", "old game crash");

// ---------- FiveM's own words ----------
var nvBlame = One(
    "[         0] [    DumpServer]                18548/ DumpServer is active and waiting.",
    "[         0] [         FiveM]             MainThrd/ --- BEGIN LOGGING AT Sun Apr 12 01:24:31 2020 ---",
    "[     50422] [    DumpServer]                13664/ Process crash captured. Crash dialog content:",
    "[     50437] [    DumpServer]                13664/ NVIDIA GPU drivers encountered an error",
    "[     50468] [    DumpServer]                13664/ FiveM crashed due to NVIDIA GPU drivers.",
    "[     50484] [    DumpServer]                13664/ This is not the fault of the FiveM developers, and can not be resolved by them. NVIDIA does not provide any error reporting contacts to use to report this problem, nor do they provide debugging information that the developers can use to resolve this issue.",
    "[     50500] [    DumpServer]                13664/ ",
    "[     50500] [    DumpServer]                13664/ If you require immediate support, please visit <A HREF=\"https://forum.fivem.net/\">FiveM.net</A> and mention the details in this window.");
Eq(nvBlame.Signature, (string?)null, "a blame headline is no signature");
Eq(nvBlame.Text, "NVIDIA GPU drivers encountered an error – FiveM crashed due to NVIDIA GPU drivers.", "blame text without boilerplate");
Eq(nvBlame.TimeUtc, (DateTime?)new DateTime(2020, 4, 12, 1, 25, 21, 422, DateTimeKind.Utc), "time of the 2020 sample");
var cNv = CrashScanLogic.Analyze(nvBlame, pcNone);
Eq(cNv.Key, "gpu-driver", "FiveM blames the NVIDIA driver");
Eq(cNv.Sure, "klar", "FiveM's blame is clear");
Check(cNv.Fixes.Any(f => f.Id == "driver:nvidia"), "NVIDIA driver page even without knowing the PC's GPU");

var amd = Dialog("1", "AMD GPU drivers encountered an error", "FiveM crashed due to AMD GPU drivers.", "Please try updating your Radeon Software, restarting your PC and then starting the game again.");
var cAmd = CrashScanLogic.Analyze(amd, pcNone);
Check(cAmd.Key == "gpu-driver" && cAmd.Title.StartsWith("AMD") && cAmd.Fixes.Any(f => f.Id == "driver:amd"), "AMD blame");
var filt = CrashScanLogic.Analyze(Dialog("1", "NVIDIA Game Filters (nvppex.dll) encountered an error", "FiveM crashed due to NVIDIA Game Filters (nvppex.dll).", "Please disable NVIDIA Game Filters (Freestyle) in the NVIDIA App and restart the game."), pcNone);
Check(filt.Key == "overlay" && filt.Title == "NVIDIA-Spielfilter" && filt.Steps[0].Contains("NVIDIA App"), "NVIDIA game filter blame");
var comodo = CrashScanLogic.Analyze(Dialog("1", "Comodo Internet Security encountered an error", "FiveM crashed due to Comodo Internet Security.", "Please uninstall Comodo Internet Security and try again, or report the issue on the Comodo forums."), pcNone);
Eq(comodo.Key, "av", "Comodo blame");
var asi = CrashScanLogic.Analyze(Dialog("1", "a third-party game plugin (scripthookv.asi+1A2B) encountered an error", "FiveM crashed due to a third-party game plugin (scripthookv.asi+1A2B).", "Please try removing the above file from the \"plugins\" folder in your FiveM installation and restarting the game."), new CrashPc(HasPlugins: true));
Check(asi.Key == "asi" && asi.Fixes.Any(f => f.Id == "plugins" && f.Kind == "host"), "an .asi plugin with its folder");
Check(!CrashScanLogic.Analyze(Dialog("1", "a third-party game plugin (x.asi+1) encountered an error", "FiveM crashed due to a third-party game plugin (x.asi+1)."), pcNone).Fixes.Any(), "no plugins folder, no button");
var asiMod = CrashScanLogic.Analyze(Dialog("1", "menu.asi+3F00", "An error at menu.asi+3F00 caused FiveM to stop working. A crash report is being uploaded to the FiveM developers."), pcNone);
Eq(asiMod.Key, "asi", "a crash inside an .asi module");

var known = Dialog("1", "FiveM crashed... but we're on it!", "A crash in vehicle sound code occurred. This appears to be caused by bad cloth data in a car mod that is loaded.", "",
    "An error at GTA5_b2944.exe!sub_1413A8C10 (0x4c) caused FiveM to stop working. A crash report is being uploaded to the FiveM developers.", "", "Legacy crash hash: tennis-stairway-seven");
Eq(known.Signature, "GTA5_b2944.exe!sub_1413A8C10 (0x4c)", "signature out of 'An error at'");
Eq(known.Build, "b2944", "build from that signature");
Check(known.Text != null && known.Text.Contains("bad cloth data"), "FiveM's explanation is the error text");
Eq(CrashScanLogic.Analyze(known, pcNone).Key, "known", "known FiveM crash");
Check(known.Known && !known.Text!.Contains("caused FiveM"), "known flag; the caused sentence is no error text");
Check(!r1.Known, "a plain crash is no known crash");

var botan = One(
    "[      1344] [    DumpServer]                 5984/ Process crash captured. Crash dialog content:",
    "[      1344] [    DumpServer]                 5984/ Exception, unhandled!",
    "[      1344] [    DumpServer]                 5984/ An unhandled exception (of type Botan::Decoding_Error) caused FiveM to stop working. A crash report is being uploaded to the FiveM developers.",
    "[      1344] [    DumpServer]                 5984/ Stack trace:",
    "[      1344] [    DumpServer]                 5984/   KERNELBASE.dll!RaiseException (0x8a)",
    "[      1344] [    DumpServer]                 5984/   VCRUNTIME140.dll!_CxxThrowException (0x8f) (throw.cpp:74)",
    "[      1344] [    DumpServer]                 5984/   legitimacy.dll+6B9A7",
    "[      1344] [    DumpServer]                 5984/   legitimacy.dll!GetAccountID (0x153db)",
    "[      1344] [    DumpServer]                 5984/ ",
    "[      4391] [    DumpServer]                 5984/ Crash report service returned si-6f2424cb9eb64950aa4976c0a48f5a56");
Eq(botan.ExceptionType, "Botan::Decoding_Error", "exception type");
Check(!botan.Known, "an exception is no known crash");
Eq(botan.Signature, "legitimacy.dll+6B9A7", "signature: the first frame outside Windows");
Eq(botan.Text, "Unhandled exception: Botan::Decoding_Error", "exception as error text");
var cBotan = CrashScanLogic.Analyze(botan, pcNone);
Eq(cBotan.Key, "fivem", "legitimacy.dll is FiveM");
Check(cBotan.Fixes.Length == 0, "nothing to fix for a FiveM-internal error");

var badAlloc = Dialog("1", "Exception, unhandled!", "An unhandled exception (of type std::bad_alloc) caused FiveM to stop working. A crash report is being uploaded to the FiveM developers.", "", "Exception details: bad allocation");
Eq(badAlloc.Text, "Unhandled exception: std::bad_alloc – Exception details: bad allocation", "exception with details");
var cAlloc = CrashScanLogic.Analyze(badAlloc, new CrashPc(PagefileOk: false, TexRaised: true));
Eq(cAlloc.Key, "memory", "std::bad_alloc is full memory");
Check(cAlloc.Fixes.Select(f => f.Id).SequenceEqual(new[] { "pagefile", "tex" }), "page file and texture budget offered");
Check(cAlloc.Fixes.Any(f => f.Kind == "page" && f.Arg == "gfx#texCard"), "the texture budget card on the graphics page");

var adhesive = One(
    "[      3531] [    DumpServer]                14440/ Process crash captured. Crash dialog content:",
    "[      3531] [    DumpServer]                14440/ adhesive.dll!CreateComponent (0x61e8b1)",
    "[      3531] [    DumpServer]                14440/ An error at adhesive.dll!CreateComponent (0x61e8b1) caused FiveM to stop working. A crash report is being uploaded to the FiveM developers.",
    "[      3531] [    DumpServer]                14440/ Stack trace:",
    "[      3531] [    DumpServer]                14440/   adhesive.dll!CreateComponent (0x61e8b1)",
    "[      3546] [    DumpServer]                14440/   CoreRT.dll!EarlyMode_Init (0x206) (EarlyMode.Win32.cpp:37)",
    "[      3546] [    DumpServer]                14440/   FiveM.exe!RealMain (0x3179) (Main.cpp:1027)",
    "[      3546] [    DumpServer]                14440/ ",
    "[      4906] [    DumpServer]                14440/ Crash report service returned si-197a20f1523f409aadbbcd3e575aedcf");
Eq(CrashScanLogic.Analyze(adhesive, pcNone).Key, "fivem", "adhesive.dll is FiveM");
Eq(adhesive.Stack.Count, 3, "three frames");

var mono = One(
    "[   1005735] [    DumpServer]                   68/ Process crash captured. Crash dialog content:",
    "[   1005735] [    DumpServer]                   68/ mono-2.0-sgen.dll+43439E",
    "[   1005735] [    DumpServer]                   68/ An error at mono-2.0-sgen.dll+43439E caused FiveM to stop working. A crash report is being uploaded to the FiveM developers. If you require immediate support, please visit <A HREF=\"https://forum.fivem.net/\">FiveM.net</A> and mention the details below.",
    "[   1005735] [    DumpServer]                   68/ ",
    "[   1005735] [    DumpServer]                   68/ Legacy crash hash: may-five-batman");
Eq(CrashScanLogic.Analyze(mono, pcNone).Key, "script", "C# script runtime");
Eq(CrashScanLogic.Analyze(Dialog("1", "citizen-scripting-lua54.dll!lua_settop (0x1f)", "An error at citizen-scripting-lua54.dll!lua_settop (0x1f) caused FiveM to stop working. A crash report is being uploaded to the FiveM developers."), pcNone).Key, "script", "Lua runtime is a script, not FiveM itself");

// no crash hash: FiveM's default headline and "An error caused …" (MiniDump.cpp: cuz = "An error")
var plain = Dialog("1", "FiveM has stopped working", "An error caused FiveM to stop working. A crash report is being uploaded to the FiveM developers.");
Check(plain.Signature == null && !plain.Known && plain.Text == "FiveM has stopped working", "default headline: no signature, the caused sentence is boilerplate");
Eq(CrashScanLogic.Analyze(plain, pcNone).Key, "unknown", "default headline without a stack stays unknown");

// ---------- dialogs in FiveM's other languages (exact strings of data/client/citizen/locales/<lang>/cfx.po) ----------
var deSig = Dialog("1", "GTA5_b3258.exe!sub_141322F74 (0x1a)",
    "Ein Fehler bei GTA5_b3258.exe!sub_141322F74 (0x1a) hat verursacht, dass FiveM nicht mehr funktioniert. Ein Absturzbericht wird an die FiveM Entwickler hochgeladen.",
    "", "Alter crash hash: violet-network-nitrogen", "Stack trace:", "  GTA5_b3258.exe!sub_141322F74 (0x1a)", "  GTA5_b3258.exe!sub_14132A0B8 (0x6d)", "");
Eq(deSig.Signature, "GTA5_b3258.exe!sub_141322F74 (0x1a)", "German: signature");
Eq(deSig.LegacyHash, "violet-network-nitrogen", "German: 'Alter crash hash'");
Eq(deSig.Stack.Count, 2, "German: stack");
Check(deSig.Text == null, "German: the caused sentence is boilerplate, no error text: " + deSig.Text);
Eq(CrashScanLogic.Analyze(deSig, pcNone).Key, "game", "German: game code");
var deKnown = Dialog("1", "FiveM ist abgestürzt... aber wir sind dran!", "A crash in vehicle sound code occurred. This appears to be caused by bad cloth data in a car mod that is loaded.", "",
    "Ein Fehler bei GTA5_b3258.exe!sub_1413A8C10 (0x4c) hat verursacht, dass FiveM nicht mehr funktioniert. Ein Absturzbericht wird an die FiveM Entwickler hochgeladen.", "", "Alter crash hash: tennis-stairway-seven",
    "Stack trace:", "  GTA5_b3258.exe!sub_1413A8C10 (0x4c)", "");
Check(deKnown.Known && deKnown.Signature == "GTA5_b3258.exe!sub_1413A8C10 (0x4c)" && deKnown.LegacyHash == "tennis-stairway-seven", "German known crash: flag, signature, hash");
Check(deKnown.Text != null && deKnown.Text.Contains("bad cloth data") && !deKnown.Text.Contains("hat verursacht"), "German known crash: FiveM's explanation without the caused sentence");
Eq(CrashScanLogic.Analyze(deKnown, pcNone).Key, "known", "German known crash");
var deEx = Dialog("1", "Exception, unhandled!", "An unhandled exception (of type std::bad_alloc) hat verursacht, dass FiveM nicht mehr funktioniert. Ein Absturzbericht wird an die FiveM Entwickler hochgeladen.", "", "Fehlerinformationen: bad allocation");
Check(deEx.ExceptionType == "std::bad_alloc" && deEx.Text == "Unhandled exception: std::bad_alloc – Fehlerinformationen: bad allocation" && deEx.LegacyHash == null, "German exception: " + deEx.Text);
Eq(CrashScanLogic.Analyze(deEx, pcNone).Key, "memory", "German bad_alloc");
var deBotan = One(L(0, D, "5984", CrashScanLogic.CaptureMarker), L(0, D, "5984", "Exception, unhandled!"),
    L(0, D, "5984", "An unhandled exception (of type Botan::Decoding_Error) hat verursacht, dass FiveM nicht mehr funktioniert. Ein Absturzbericht wird an die FiveM Entwickler hochgeladen."),
    L(0, D, "5984", "Stack trace:"), L(0, D, "5984", "  KERNELBASE.dll!RaiseException (0x8a)"), L(0, D, "5984", "  VCRUNTIME140.dll!_CxxThrowException (0x8f) (<A HREF=\"https://sourcegraph.com/x\">throw.cpp:74</A>)"),
    L(0, D, "5984", "  CoreRT.dll!fx::ResourceManagerImpl::Tick (0x5d) (ResourceManager.cpp:206)"), L(0, D, "5984", ""));
Eq(deBotan.Signature, "CoreRT.dll!fx::ResourceManagerImpl::Tick (0x5d)", "exception signature: the first frame outside Windows, without its source line");
Eq(deBotan.Stack[1], "VCRUNTIME140.dll!_CxxThrowException (0x8f) (throw.cpp:74)", "a frame's source link becomes plain text");
var frKnown = Dialog("1", "FiveM a cessé de fonctionner... mais nous sommes dessus !", "A crash in network object cloning occurred (missing R* null pointer check). This only seems to happen after game reloading.", "",
    "Une erreur à GTA5_b3258.exe!sub_14109D8D8 (0x2b) a provoqué l'arrêt de FiveM. Un rapport de crash est en cours de téléchargement vers les développeurs FiveM.", "",
    "Héritage du hash de crash : asparagus-king-thirteen", "Stack trace:", "  GTA5_b3258.exe!sub_14109D8D8 (0x2b)", "  GTA5_b3258.exe!sub_1410A0000 (0x11)", "");
Check(frKnown.Known && frKnown.Signature == "GTA5_b3258.exe!sub_14109D8D8 (0x2b)" && frKnown.LegacyHash == "asparagus-king-thirteen", "French known crash by shape: flag, signature from the first frame, hash");
Check(frKnown.Text != null && frKnown.Text.Contains("network object cloning") && !frKnown.Text.Contains("provoqué"), "French: explanation without the caused sentence");
var huSig = Dialog("1", "nvwgf2umx.dll+A3C0F1",
    "Egy hiba a(z) nvwgf2umx.dll+A3C0F1 okozott FiveM a folyamat leállásában. Az összeomlási jelentést feltöltjük a FiveM fejlesztőknek.", "", "Memória lefoglalása:", "  nvwgf2umx.dll+A3C0F1", "  d3d11.dll!CContext::Draw (0x41)", "");
Check(huSig.Stack.Count == 2 && huSig.Text == null, "Hungarian: translated stack header found by shape, caused sentence dropped");
Eq(CrashScanLogic.Analyze(huSig, new CrashPc("nvidia")).Key, "gpu-driver", "Hungarian: NVIDIA driver");
var frSigOnly = Dialog("1", "FiveM a cessé de fonctionner... mais nous sommes dessus !", "Some explanation.", "", "Une erreur à menu.asi+3F00 a provoqué l'arrêt de FiveM. Un rapport de crash est en cours de téléchargement vers les développeurs FiveM.");
Eq(frSigOnly.Signature, "menu.asi+3F00", "no stack: the signature-shaped word of the caused sentence");
var asiBlame = Dialog("1", "a third-party game plugin (scripthookv.asi+1A2B) encountered an error", "FiveM crashed due to a third-party game plugin (scripthookv.asi+1A2B).", "Please try removing the above file from the \"plugins\" folder in your FiveM installation and restarting the game.");
Check(!asiBlame.Known && asiBlame.Signature == null && asiBlame.Text!.Contains("FiveM crashed due to"), "a blame names FiveM once per line: no caused sentence, its text is kept");
var discHash = Dialog("1", "Oшибка (Error)", "Kicked: alpha-beta-gamma");
Check(discHash.LegacyHash == null && discHash.Text!.Contains("alpha-beta-gamma"), "a disconnect text is never read as a crash hash");

// ---------- RAGE errors and DirectX ----------
var gfxInit = One(
    L(0, "FiveM", "MainThrd", "--- BEGIN LOGGING AT Fri Nov 20 18:00:00 2026 ---"),
    L(61234, "b3095_GTAProce", "MainThrd", "Error: RAGE error: ERR_GFX_D3D_INIT"),
    L(61234, "b3095_GTAProce", "MainThrd", "A game error (at 0000000141196c2e) caused the game to stop working."),
    L(61890, D, "9292", CrashScanLogic.CaptureMarker),
    L(61890, D, "9292", "RAGE error: ERR_GFX_D3D_INIT"),
    L(61890, D, "9292", "A game error (at 0000000141196c2e) caused the game to stop working."),
    L(61890, D, "9292", ""),
    L(61890, D, "9292", "Failed to start the graphics device."),
    L(61890, D, "9292", ""));
Eq(gfxInit.Code, "ERR_GFX_D3D_INIT", "RAGE error code");
Eq(gfxInit.Signature, (string?)null, "a RAGE error has no signature in the dialog");
Eq(gfxInit.Text, "RAGE error: ERR_GFX_D3D_INIT – A game error (at 0000000141196c2e) caused the game to stop working. Failed to start the graphics device.", "RAGE error text");
Eq(gfxInit.Build, "b3095", "build from the game's own Error: line");
var cInit = CrashScanLogic.Analyze(gfxInit, new CrashPc("amd"));
Eq(cInit.Key, "gpu-init", "ERR_GFX_D3D_INIT");
Check(cInit.Why.Contains("Overlays") && cInit.Steps[0].Contains("NVIDIA-Overlay") && cInit.Fixes.Any(f => f.Id == "driver:amd") && cInit.Fixes.Any(f => f.Id == "reshade"), "D3D init: overlays named, the PC's driver and ReShade offered");
Check(!cInit.Why.Contains("FiveM nennt"), "D3D init does not claim FiveM says so (only a source comment does, and FiveM blocks nvspcap)");

var memErr = Dialog("1", "RAGE error: ERR_MEM_EMBEDDEDALLOC_ALLOC", "A game error (at 00000001400a2b44) caused the game to stop working.", "", "Out of memory in the game heap.");
var cMem = CrashScanLogic.Analyze(memErr, pcNone);
Check(cMem.Key == "memory" && cMem.Sure == "klar" && cMem.Why.Contains("ERR_MEM_EMBEDDEDALLOC_ALLOC"), "ERR_MEM_* is memory");
Check(cMem.Fixes.Length == 0 && cMem.Steps[0].Contains("in Ordnung"), "page file fine and budget default: no button, honest step");
var cVram = CrashScanLogic.Analyze(Dialog("1", "RAGE error: ERR_GFX_D3D_DEFERRED_MEM", "A game error (at 0000000140f3c2a1) caused the game to stop working."), new CrashPc(TexRaised: true));
Check(cVram.Key == "vram" && cVram.Fixes.Any(f => f.Id == "tex"), "ERR_GFX_*MEM is video memory with the texture budget");
Eq(CrashScanLogic.Analyze(Dialog("1", "RAGE error: ERR_GFX_DRAW_DATA", "A game error (at 0000000140f3c2a1) caused the game to stop working."), pcNone).Key, "assets", "broken draw data");
Eq(CrashScanLogic.Analyze(Dialog("1", "RAGE error: ERR_GFX_STATE", "A game error (at 0000000140f3c2a1) caused the game to stop working."), pcNone).Key, "gpu-device", "other ERR_GFX_*");
var cStr = CrashScanLogic.Analyze(Dialog("1", "RAGE error: ERR_STR_FAILURE", "A game error (at 0000000140f3c2a1) caused the game to stop working."), pcNone);
Check(cStr.Key == "streaming" && cStr.Fixes.Any(f => f.Id == "cache" && f.Kind == "action" && f.Arg == "fmcache"), "streaming error offers the FiveM cache");
Eq(CrashScanLogic.Analyze(Dialog("1", "RAGE error: ERR_FIL_PACK_1", "A game error (at 0000000140f3c2a1) caused the game to stop working."), pcNone).Key, "gamefiles", "pack file error");
var pack = Dialog("1", "FiveM has encountered an error", "Invalid rage::fiPackfile encryption type specified. If you have any modified game files, please remove or verify them. See <A HREF=\"http://rsg.ms/verify\">http://rsg.ms/verify</A> for more information.");
Check(pack.Text != null && pack.Text.Contains("See http://rsg.ms/verify for") && !pack.Text.Contains("<A"), "links become plain text");
Eq(CrashScanLogic.Analyze(pack, pcNone).Key, "gamefiles", "ERR_GEN_INVALID's own message");
Eq(CrashScanLogic.Analyze(Dialog("1", "RAGE error: ERR_SYS_PURE", "A game error (at 0000000140f3c2a1) caused the game to stop working."), pcNone).Key, "unknown", "an ERR_ code without known meaning stays unknown");

var removed = Dialog("1", "DirectX encountered an unrecoverable error: DXGI_ERROR_DEVICE_REMOVED - The video card has been physically removed from the system, or a driver upgrade for the video card has occurred. ",
    "GetDeviceRemovedReason returned DXGI_ERROR_DEVICE_HUNG - The application's device failed due to badly formed commands sent by the application. This is an design-time issue that should be investigated and fixed. ");
Eq(removed.Code, "DXGI_ERROR_DEVICE_REMOVED", "the first DXGI code");
var cRem = CrashScanLogic.Analyze(removed, new CrashPc("nvidia", HagsOn: true));
Check(cRem.Key == "gpu-device" && cRem.Sure == "wahrscheinlich" && cRem.Fixes.Select(f => f.Id).SequenceEqual(new[] { "driver:nvidia", "hags" }), "device removed: driver and GPU scheduling");
Check(cRem.Steps.Any(s => s.Contains("Übertaktung")), "device removed names overclocking");
var rs = Dialog("1", "DirectX encountered an unrecoverable error (R): DXGI_ERROR_DEVICE_HUNG - The GPU will not respond to more commands.", "", "The following graphics mods were found in your GTA V installation:", "- ReShade 6.3.3.1915", "", "Please contact the author of these mods to see if the issue may be related to them.");
var cRs = CrashScanLogic.Analyze(rs, pcNone);
Check(cRs.Key == "gfxmod" && cRs.Fixes.Any(f => f.Id == "reshade" && f.Arg == "mods#rs"), "graphics mod marker (R)");
var oomDev = Dialog("1", "DirectX encountered an unrecoverable error: DXGI_ERROR_DEVICE_REMOVED - The video card has been physically removed from the system, or a driver upgrade for the video card has occurred. ",
    "GetDeviceRemovedReason returned E_OUTOFMEMORY - Ran out of memory ");
var cOom = CrashScanLogic.Analyze(oomDev, new CrashPc(TexRaised: true));
Check(cOom.Key == "vram" && cOom.Fixes.Any(f => f.Id == "tex"), "device removed for lack of memory is video memory");

// ---------- freezes, pools, launcher, disconnects, integrity ----------
var dead = Dialog("1", "FiveM has stopped responding (script deadloop)", "The game stopped responding for too long and needs to be restarted. When asking for help, please click 'Save information' and upload the file that is saved when you click the button.", "", "Scripts were running: myresource: event esx:playerLoaded");
Eq(CrashScanLogic.Analyze(dead, pcNone).Key, "freeze-script", "script deadloop");
Check(dead.Text != null && !dead.Text.Contains("When asking for help") && dead.Text.Contains("myresource"), "freeze text keeps the reasoning, drops the boilerplate");
Eq(CrashScanLogic.Analyze(Dialog("1", "FiveM has stopped responding (DirectX query)", "The game stopped responding for too long and needs to be restarted."), pcNone).Key, "freeze-gpu", "GPU wait");
Eq(CrashScanLogic.Analyze(Dialog("1", "FiveM has stopped responding (NVIDIA drivers)", "The game stopped responding for too long and needs to be restarted."), pcNone).Key, "gpu-driver", "NVIDIA freeze");
Eq(CrashScanLogic.Analyze(Dialog("1", "FiveM has stopped responding ", "The game stopped responding for too long and needs to be restarted."), pcNone).Key, "freeze", "plain freeze");
Eq(CrashScanLogic.Analyze(Dialog("1", "CVehicle Pool Full, Size == 300", " "), pcNone).Key, "pool", "pool full");
var ros = One(
    "[     53531] [   ROSLauncher]             MainThrd/ Error: 127. Could not locate the Rockstar Games Launcher Process! If you are running it as administrator, please launch it as regular user.",
    "[     54328] [    DumpServer]                 8816/ Process crash captured. Crash dialog content:",
    "[     54328] [    DumpServer]                 8816/ 127. Could not locate the Rockstar Games Launcher Process! If you are running it as administrator, please launch it as regular user.",
    "[     54328] [    DumpServer]                 8816/  ",
    "[     55000] [    DumpServer]                 8816/ Crash report service returned si-d5491ad4d40c46f980ec35e2d9f13703");
Eq(ros.Text, "127. Could not locate the Rockstar Games Launcher Process! If you are running it as administrator, please launch it as regular user.", "launcher text");
Eq(CrashScanLogic.Analyze(ros, pcNone).Key, "launcher", "Rockstar launcher");
Eq(CrashScanLogic.Analyze(Dialog("1", "506. Failure connecting to the Rockstar Games Launcher! (ExitCode = 506)", " "), pcNone).Key, "launcher", "launcher exit code");
var disc = Dialog("1", "O\u0448\u0438\u0431\u043a\u0430 (Error)", "Server shutting down: Server restarting (in 0 seconds).");
Eq(CrashScanLogic.Analyze(disc, pcNone).Key, "disconnect", "disconnect dialog");
Check(disc.Text != null && disc.Text.Contains("Server shutting down"), "disconnect message kept");
Eq(CrashScanLogic.Analyze(Dialog("1", "Early-exit trap", "An error occurred while running FiveM, triggering an early-exit trap."), pcNone).Key, "integrity", "early-exit trap");
Eq(CrashScanLogic.Analyze(Dialog("1", "Game integrity check failed", "A FiveM integrity check failed and the game had to be terminated."), pcNone).Key, "integrity", "integrity check");

// ---------- overlays and modules ----------
var rtss = CrashScanLogic.Analyze(Dialog("1", "rtsshooks64.dll+1F2A", "An error at rtsshooks64.dll+1F2A caused FiveM to stop working. A crash report is being uploaded to the FiveM developers."), pcNone);
Check(rtss.Key == "overlay" && rtss.Title.Contains("RivaTuner") && rtss.Fixes.Any(f => f.Id == "autostart" && f.Arg == "autostart"), "RivaTuner hook with the autostart page");
var unl = Dialog("1", "rtsshooks64.dll_unloaded+1F20", "An error at rtsshooks64.dll_unloaded+1F20 caused FiveM to stop working.");
Eq(unl.Module, "rtsshooks64.dll", "an unloaded module keeps its name");
Eq(CrashScanLogic.Analyze(unl, pcNone).Key, "overlay", "unloaded overlay");
var steamStack = Dialog("1", "ntdll.dll!RtlpWaitOnCriticalSection (0xb1)", "An error at ntdll.dll!RtlpWaitOnCriticalSection (0xb1) caused FiveM to stop working.", "Stack trace:", "  ntdll.dll!RtlpWaitOnCriticalSection (0xb1)", "  KERNELBASE.dll!WaitForSingleObjectEx (0x8e)", "  gameoverlayrenderer64.dll+8C1F0", "  GTA5_b3258.exe!sub_14002A3B4 (0x30)");
var cSteam = CrashScanLogic.Analyze(steamStack, pcNone);
Check(cSteam.Key == "overlay" && cSteam.Title == "Steam-Overlay" && cSteam.Sure == "wahrscheinlich", "an overlay in the stack under a Windows frame");
var dx = Dialog("1", "d3d11.dll!CContext::Draw (0x41)", "An error at d3d11.dll!CContext::Draw (0x41) caused FiveM to stop working.", "Stack trace:", "  d3d11.dll!CContext::Draw (0x41)", "  GTA5_b3258.exe!sub_1415D7E8C (0x1c4)");
var cDx = CrashScanLogic.Analyze(dx, new CrashPc("intel"));
Check(cDx.Key == "gfxapi" && cDx.Fixes.Any(f => f.Id == "driver:intel"), "DirectX itself: driver or a d3d11.dll mod");
foreach (var (sig, want) in new[] {
    ("graphics-hook64.dll+3A21", "OBS-Spielaufnahme"), ("DiscordHook64.dll+21C3", "Discord-Overlay"), ("nvspcap64.dll+9A11", "NVIDIA-Overlay (ShadowPlay)"),
    ("ow-graphics-hook64.dll+1021", "Overwolf"), ("ReShade64.dll+1A2B3", "ReShade") })
{
    var c = CrashScanLogic.Analyze(Dialog("1", sig, $"An error at {sig} caused FiveM to stop working."), pcNone);
    Check(c.Key == "overlay" && c.Title == want, $"overlay {sig} → {want} (got {c.Key}/{c.Title})");
}
foreach (var (sig, vendor) in new[] { ("atidxx64.dll+5C1B0", "amd"), ("amdxx64.dll+1100", "amd"), ("igd10iumd64.dll+77A1", "intel"), ("nvwgf2umx.dll+A3C0F1", "nvidia") })
{
    var c = CrashScanLogic.Analyze(Dialog("1", sig, $"An error at {sig} caused FiveM to stop working."), pcNone);
    Check(c.Key == "gpu-driver" && c.Fixes.Any(f => f.Id == "driver:" + vendor), $"driver {sig} → {vendor}");
}
Eq(CrashScanLogic.Analyze(Dialog("1", "guard64.dll+1A2B", "An error at guard64.dll+1A2B caused FiveM to stop working."), pcNone).Key, "av", "Comodo module");
Eq(CrashScanLogic.Analyze(Dialog("1", "FiveM+1C2D", "An error at FiveM+1C2D caused FiveM to stop working."), pcNone).Key, "fivem", "old FiveM signature");
var unknown = Dialog("1", "0x00007ff812345678", "An error at 0x00007ff812345678 caused FiveM to stop working.");
var cUnk = CrashScanLogic.Analyze(unknown, pcNone);
Check(cUnk.Key == "unknown" && cUnk.Title == "Ursache unbekannt" && cUnk.Sure == "unklar" && cUnk.Fixes.Length == 0, "an address alone is honestly unknown");
Eq(unknown.Signature, "0x00007ff812345678", "the raw signature is kept for display");
Eq(CrashScanLogic.Analyze(Dialog("1", "somevendor.dll+1A2B", "An error at somevendor.dll+1A2B caused FiveM to stop working."), pcNone).Key, "unknown", "an unknown DLL stays unknown");
Eq(CrashScanLogic.Analyze(Dialog("1", "ntdll.dll!RtlFreeHeap (0x3a)", "An error at ntdll.dll!RtlFreeHeap (0x3a) caused FiveM to stop working."), pcNone).Key, "unknown", "Windows alone is no cause");

// ---------- module helpers ----------
Eq(CrashScanLogic.ModuleOf("GTA5_b3258.exe!sub_141322F74 (0x1a)"), "GTA5_b3258.exe", "module of a symbol");
Eq(CrashScanLogic.ModuleOf("  nvwgf2umx.dll+A3C0F1"), "nvwgf2umx.dll", "module of an indented frame");
Eq(CrashScanLogic.ModuleOf("0x7ff8"), (string?)null, "no module in an address");
Eq(CrashScanLogic.ModuleOf(null), (string?)null, "no module in nothing");
Check(CrashScanLogic.LooksLikeSignature("GTA5_b3095.exe+11E2E0D") && CrashScanLogic.LooksLikeSignature("citizen-resources-core.dll!fx::ResourceManagerImpl::Tick (0x5d)") && !CrashScanLogic.LooksLikeSignature("RAGE error: ERR_GFX_D3D_INIT") && !CrashScanLogic.LooksLikeSignature("Exception, unhandled!"), "signature shapes");
Eq(CrashScanLogic.ModuleKind("FiveM_b3258_GTAProcess.exe"), "game", "the game process exe");
Eq(CrashScanLogic.ModuleKind("CoreRT.dll"), "fivem", "CoreRT is FiveM");
Eq(CrashScanLogic.VendorOf("NVIDIA GeForce RTX 4070 SUPER"), "nvidia", "NVIDIA name");
Eq(CrashScanLogic.VendorOf("AMD Radeon RX 7800 XT"), "amd", "AMD name");
Eq(CrashScanLogic.VendorOf("Intel(R) Arc(TM) A770 Graphics"), "intel", "Intel name");
Eq(CrashScanLogic.VendorOf("Microsoft Basic Display Adapter"), (string?)null, "unknown GPU");
foreach (var v in new[] { "nvidia", "amd", "intel" }) Check(CrashScanLogic.DriverUrl(v)?.StartsWith("https://") == true, $"driver page of {v} is https");
Check(CrashScanLogic.DriverUrl("matrox") == null && CrashScanLogic.DriverUrl(null) == null, "no page for an unknown vendor");

// ---------- logs and dumps together ----------
DateTime T(int day, int h, int m, int s = 0) => new(2026, 11, day, h, m, s, DateTimeKind.Utc);
CrashReport Logged(DateTime t, string sig) => new() { TimeUtc = t, Title = sig, Signature = sig };
var merged = CrashScanLogic.Merge(
    new[] { Logged(T(21, 20, 30, 16), "a.dll+1"), Logged(T(20, 18, 1), "b.dll+1"), Logged(T(19, 9, 0), "c.dll+1") },
    new[] { ("0f8b3c1e-aaaa-4c6e-9d1b-1c2d3e4f5a6b.dmp", T(21, 20, 30, 2)), ("0f8b3c1e-aaaa-4c6e-9d1b-1c2d3e4f5a6b-full.dmp", T(21, 20, 30, 3)),
            ("11111111-2222-3333-4444-555555555555.dmp", T(20, 18, 3)), ("99999999-2222-3333-4444-555555555555.dmp", T(15, 12, 0)) });
Eq(merged.Count, 4, "three logged crashes and one dump without a log; the full dump is no crash of its own");
Eq(merged[0].Dump, "0f8b3c1e-aaaa-4c6e-9d1b-1c2d3e4f5a6b.dmp", "dump 14 s before the dialog belongs to it");
Eq(merged[1].Dump, "11111111-2222-3333-4444-555555555555.dmp", "dump 2 min away still belongs to it");
Eq(merged[2].Dump, (string?)null, "no dump near the third crash");
Check(!merged[3].FromLog && merged[3].Dump == "99999999-2222-3333-4444-555555555555.dmp", "a dump without a log is a crash of its own, at the end (oldest)");
Eq(CrashScanLogic.Analyze(merged[3], pcNone).Key, "nolog", "a dump alone: no details");
var nologOld = CrashScanLogic.Analyze(merged[3], new CrashPc(NowUtc: T(15, 12, 0).AddDays(9)));
var nologNew = CrashScanLogic.Analyze(merged[3], new CrashPc(NowUtc: T(15, 12, 0).AddDays(2)));
Check(nologOld.Why.Contains("nach sieben Tagen") && !nologOld.Why.Contains("Entweder"), "a dump older than a week: FiveM deleted its log (Console.Logging.cpp)");
Check(nologNew.Why.Contains("Entweder") && !nologNew.Why.Contains("nach sieben Tagen"), "a recent dump: no claim about the seven days");
var many = CrashScanLogic.Merge(Enumerable.Range(0, 14).Select(i => Logged(T(1, 0, 0).AddHours(i), "x.dll+" + i)), Array.Empty<(string, DateTime)>());
Eq(many.Count, CrashScanLogic.MaxCrashes, "at most ten");
Eq(many[0].Signature, "x.dll+13", "newest first");
Check(CrashScanLogic.IdOf(merged[0]) != CrashScanLogic.IdOf(merged[1]) && CrashScanLogic.IdOf(merged[2]) == CrashScanLogic.IdOf(merged[2]), "ids are distinct and stable");
Check(!CrashScanLogic.IdOf(merged[2]).Contains('\\') && !CrashScanLogic.IdOf(merged[2]).Contains('/'), "ids carry no path");

// ---------- crashometry ----------
byte[] Rec(string k, string v) { var kb = Encoding.UTF8.GetBytes(k); var vb = Encoding.UTF8.GetBytes(v); return BitConverter.GetBytes((uint)kb.Length).Concat(BitConverter.GetBytes((uint)vb.Length)).Concat(kb).Concat(vb).ToArray(); }
var cm = Rec("RockstarId", "123456789").Concat(Rec("did_render_mrt", "1")).Concat(Rec("gpu_name", "NVIDIA GeForce RTX 4070 SUPER")).Concat(Rec("last_server_url", "cfx.re/join/abc123"))
    .Concat(Rec("last_server", "203.0.113.24:30120")).Concat(Rec("did_render_mrt", "0")).Concat(Rec("onesync_enabled", "true")).ToArray();
var cmd = CrashScanLogic.ParseCrashometry(cm);
Check(!cmd.ContainsKey("RockstarId"), "the Rockstar ID is never read out");
Eq(cmd["did_render_mrt"], "1", "did_render_mrt keeps its first value (as FiveM does)");
Eq(cmd["gpu_name"], "NVIDIA GeForce RTX 4070 SUPER", "values read");
Eq(CrashScanLogic.ServerOf(cmd), "cfx.re/join/abc123", "join link first");
Eq(CrashScanLogic.ServerOf(new Dictionary<string, string> { ["last_server"] = "203.0.113.24:30120" }), "203.0.113.24:30120", "IP:Port");
Eq(CrashScanLogic.ServerOf(new Dictionary<string, string> { ["last_server_url"] = "https://cfx.re/join/abc123/" }), "cfx.re/join/abc123", "a web link becomes the join code");
Eq(CrashScanLogic.ServerOf(new Dictionary<string, string> { ["last_server_url"] = "javascript:alert(1)" }), (string?)null, "junk is no server");
Eq(CrashScanLogic.ServerOf(new Dictionary<string, string>()), (string?)null, "no server");
var cut = cm.Take(cm.Length - 3).ToArray();
Check(CrashScanLogic.ParseCrashometry(cut).ContainsKey("last_server") && !CrashScanLogic.ParseCrashometry(cut).ContainsKey("onesync_enabled"), "a cut-off file gives what was readable");
Eq(CrashScanLogic.ParseCrashometry(new byte[] { 255, 255, 255, 255, 1, 0, 0, 0 }).Count, 0, "a broken length stops");
Eq(CrashScanLogic.ParseCrashometry(null).Count, 0, "no file");
var cmEmpty = Rec("last_server_ver", "").Concat(Rec("last_server", "203.0.113.24:30120")).ToArray();
Eq(cmEmpty.Length, 8 + 15 + 8 + 11 + 18, "an empty value is written as length 0 (AddCrashometry)");
var cmE = CrashScanLogic.ParseCrashometry(cmEmpty);
Check(!cmE.ContainsKey("last_server_ver") && cmE.GetValueOrDefault("last_server") == "203.0.113.24:30120", "an empty value is skipped, the next record is still read");

// ---------- privacy ----------
Eq(CrashScanLogic.Redact(@"Could not load C:\Users\Max\AppData\Local\FiveM\FiveM.app\plugins\x.asi", "Max"), @"Could not load C:\Users\<Nutzer>\AppData\Local\FiveM\FiveM.app\plugins\x.asi", "user folder in a path");
Eq(CrashScanLogic.Redact(@"d:\users\max.mustermann\Desktop", "max.mustermann"), @"d:\users\<Nutzer>\Desktop", "other drive, other case");
Eq(CrashScanLogic.Redact(@"C:\Benutzer\Lena\x", "Lena"), @"C:\Benutzer\<Nutzer>\x", "German folder name");
Eq(CrashScanLogic.Redact(@"E:\Profile\kai_x\FiveM", "kai_x", @"E:\Profile\kai_x"), @"%USERPROFILE%\FiveM", "a moved profile folder becomes %USERPROFILE%");
Eq(CrashScanLogic.Redact(@"C:\Users\Max\AppData\Local\FiveM", "Max", @"C:\Users\Max\"), @"%USERPROFILE%\AppData\Local\FiveM", "the profile path itself");
Eq(CrashScanLogic.Redact("Player Lena joined, MaxLodScale 1.0", "Lena"), "Player <Nutzer> joined, MaxLodScale 1.0", "the bare name, not inside other words");
Eq(CrashScanLogic.Redact("PC crashed", "PC"), "PC crashed", "names shorter than three letters are left (paths still are)");
Eq(CrashScanLogic.Redact(null, "Max"), "", "no text");

// ---------- text for support ----------
var sup = CrashScanLogic.SupportText(r1, c1, new DateTime(2026, 11, 21, 21, 30, 15), "Max");
Check(sup.StartsWith("**FiveM-Absturz** · 21.11.2026, 21:30 Uhr\n"), "headline with local time: " + sup.Split('\n')[0]);
Check(sup.Contains("Build: b3258\n") && sup.Contains("Signatur: `nvgpucomp64.dll+2A4B10`\n") && sup.Contains("Report-ID: si-923407810a0e40deba6e61b554b7a499\n") && sup.Contains("Vermutete Ursache: NVIDIA-Grafiktreiber – laut OrbitTweak"), "support text fields");
Check(!sup.Contains("Fehler:"), "a plain crash has no error line");
var supInit = CrashScanLogic.SupportText(gfxInit, cInit, null, "Max");
Check(supInit.Contains("Fehler: RAGE error: ERR_GFX_D3D_INIT") && !supInit.Contains("Fehlercode:") && supInit.Contains("Build: b3095"), "error text carries the code once");
var withUser = new CrashReport { Title = "x", Text = @"Assertion failure: x File: C:\Users\Max\fivem\a.cpp, Line: 12", Signature = "GTA5_b3095.exe+1`2" };
var supUser = CrashScanLogic.SupportText(withUser, cUnk, null, "Max");
Check(!supUser.Contains("Max") && supUser.Contains(@"C:\Users\<Nutzer>\fivem"), "the user name never goes into the support text");
Check(supUser.Contains("`GTA5_b3095.exe+1'2`") && supUser.Contains("Build: unbekannt") && supUser.Contains("(unsicher)"), "backticks cannot break the code span; unknown build and unclear cause said so");
var supNo = CrashScanLogic.SupportText(merged[3], CrashScanLogic.Analyze(merged[3], pcNone), null, "Max");
Check(supNo.Contains("Fehler: keine Details im Log"), "a dump without log says so");
Check(sup.Split('\n').Length <= 9 && sup.Length < 600, "short enough for one Discord message");

// ---------- pattern ----------
var now = T(22, 12, 0);
var pat = CrashScanLogic.Pattern(new (DateTime?, CrashCause)[] { (T(21, 20, 30), c1), (T(20, 18, 0), cNv), (T(19, 9, 0), cGame), (T(1, 9, 0), c1) }, now);
Check(pat is { } pp && pp.key == "gpu-driver" && pp.count == 2 && pp.of == 3, "two of the last three (14 days) share the driver");
Check(CrashScanLogic.Pattern(new (DateTime?, CrashCause)[] { (T(21, 20, 30), cGame), (T(20, 18, 0), cGame) }, now) == null, "an unclear cause is no pattern");
Check(CrashScanLogic.Pattern(new (DateTime?, CrashCause)[] { (T(21, 20, 30), c1) }, now) == null, "one crash is no pattern");

// ---------- every cause: complete, German, honest, fixes the page knows ----------
var allCauses = new[] { c1, c1b, cGame, cNui, cNv, cAmd, filt, comodo, asi, asiMod, CrashScanLogic.Analyze(known, pcNone), cBotan, cAlloc, cInit, cMem, cVram, cStr, cRem, cRs, rtss, cSteam, cDx, cUnk,
    CrashScanLogic.Analyze(dead, pcNone), CrashScanLogic.Analyze(disc, pcNone), CrashScanLogic.Analyze(ros, pcNone), CrashScanLogic.Analyze(merged[3], pcNone), CrashScanLogic.Analyze(pack, pcNone),
    CrashScanLogic.Analyze(mono, pcNone), CrashScanLogic.Analyze(Dialog("1", "CVehicle Pool Full, Size == 300", " "), pcNone),
    CrashScanLogic.Analyze(Dialog("1", "Early-exit trap", "x"), pcNone), CrashScanLogic.Analyze(Dialog("1", "FiveM has stopped responding (DirectX query)", "x"), new CrashPc("nvidia", HagsOn: true)) };
var pages = new HashSet<string> { "mods", "gfx", "autostart" };
foreach (var c in allCauses)
{
    Check(c.Title.Length is > 3 and < 40, $"{c.Key}: short title");
    Check(c.Why.Length > 40 && c.Why.Length < 420, $"{c.Key}: why says something, briefly ({c.Why.Length})");
    Check(c.Steps.Length is >= 1 and <= 3 && c.Steps.All(s => s.Length > 8), $"{c.Key}: one to three steps");
    Check(c.Sure is "klar" or "wahrscheinlich" or "unklar", $"{c.Key}: sure");
    Check(c.Icon.Length > 1, $"{c.Key}: icon");
    var words = c.Title + " " + c.Why + " " + string.Join(" ", c.Steps);
    Check(!System.Text.RegularExpressions.Regex.IsMatch(words, @"\b(garantiert|100 ?%|mehr FPS|nie wieder|immer behoben)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase), $"{c.Key}: no promises");
    Check(!System.Text.RegularExpressions.Regex.IsMatch(words, @"\b(the|crashed|please|your)\b"), $"{c.Key}: German, not English");
    Check(c.Fixes.Select(f => f.Id).Distinct().Count() == c.Fixes.Length && c.Fixes.Length <= 3, $"{c.Key}: at most three distinct fixes");
    foreach (var f in c.Fixes)
    {
        Check(f.Label.Length is > 3 and < 32, $"{c.Key}/{f.Id}: label length");
        Check(f.Kind switch
        {
            "health" => f.Arg == "tweak:pagefile",
            "action" => f.Arg == "fmcache",
            "tweakoff" => f.Arg == "hags",
            "host" => f.Arg is "plugins" or "driver:nvidia" or "driver:amd" or "driver:intel",
            "page" => pages.Contains(f.Arg.Split('#')[0]),
            _ => false,
        }, $"{c.Key}/{f.Id}: kind {f.Kind} with arg {f.Arg} is one the page and host know");
    }
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
