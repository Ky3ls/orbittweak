using OrbitTweak.Core;
using static OrbitTweak.Core.HwCheckLogic;

// Checks of the Monitor-Check's and RAM-Check's pure logic. Exit code 0 = all fine.
//   dotnet run --project tests/HwCheckLogic.Tests

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }
void Eq<T>(T got, T want, string what) => Check(EqualityComparer<T>.Default.Equals(got, want), $"{what}: got [{got}], want [{want}]");

// =====================================================================
//  MONITOR
// =====================================================================
var fhd = new Mode(1920, 1080, 32, 60);
var modes = new List<Mode>
{
    new(1920, 1080, 32, 60), new(1920, 1080, 32, 59), new(1920, 1080, 32, 50), new(1920, 1080, 32, 75), new(1920, 1080, 32, 100),
    new(1920, 1080, 32, 120), new(1920, 1080, 32, 144), new(1920, 1080, 32, 165),
    new(1920, 1080, 32, 240, true),     // interlaced: never counted
    new(1920, 1080, 16, 200),           // another colour depth
    new(2560, 1440, 32, 240),           // another resolution
    new(1280, 720, 32, 240),
};
Eq(BestHz(fhd, modes), 165, "best at the same resolution and depth, interlaced and other depths left out");
Eq(BestHz(new Mode(2560, 1440, 32, 60), modes), 240, "best at 1440p");
Eq(BestHz(new Mode(3840, 2160, 32, 60), modes), 0, "no mode at that resolution");
Eq(BestHz(fhd, Array.Empty<Mode>()), 0, "no modes at all");
Eq(BestHz(new Mode(1080, 1920, 32, 60), new[] { new Mode(1920, 1080, 32, 144), new Mode(1920, 1080, 32, 60) }), 144, "a display turned by 90° matches its modes either way round");
Eq(BestHz(new Mode(1080, 1920, 32, 60), new[] { new Mode(1080, 1920, 32, 120) }), 120, "rotated modes listed rotated");
Eq(BestHz(fhd, new[] { new Mode(1920, 1080, 32, 1), new Mode(1920, 1080, 32, 0) }), 0, "\"hardware default\" rates (0, 1) are no rate");
Eq(BestHz(new Mode(1920, 1080, 32, 60), new[] { new Mode(1920, 1080, 32, 60, true), new Mode(1920, 1080, 32, 30) }), 30, "only progressive modes");

Check(WorthSwitching(60, 144), "60 → 144 is worth it");
Check(WorthSwitching(60, 75), "60 → 75 is worth it");
Check(WorthSwitching(120, 144), "120 → 144 is worth it");
Check(WorthSwitching(144, 165), "144 → 165 is worth it");
Check(WorthSwitching(240, 260), "240 → 260 is worth it");
Check(WorthSwitching(100, 144), "100 → 144 is worth it");
Check(!WorthSwitching(59, 60), "59 → 60 is the same rate");
Check(!WorthSwitching(60, 60), "nothing more");
Check(!WorthSwitching(144, 144), "at the top already");
Check(!WorthSwitching(165, 170), "165 → 170 (an overclock mode) is no point");
Check(!WorthSwitching(360, 380), "360 → 380 is no point");
Check(!WorthSwitching(0, 144) && !WorthSwitching(1, 144), "no current rate, no switch");
Check(!WorthSwitching(144, 120), "lower is never worth it");
Check(!WorthSwitching(60, 69), "9 Hz more is not enough");

Eq(ConnOf(5), Conn.Hdmi, "HDMI");
Eq(ConnOf(10), Conn.DisplayPort, "external DisplayPort");
Eq(ConnOf(11), Conn.Internal, "embedded DisplayPort is the laptop panel");
Eq(ConnOf(6), Conn.Internal, "LVDS is the laptop panel");
Eq(ConnOf(0x80000000), Conn.Internal, "internal");
Eq(ConnOf(4), Conn.Dvi, "DVI");
Eq(ConnOf(0), Conn.Vga, "VGA");
Eq(ConnOf(15), Conn.Wireless, "Miracast");
Eq(ConnOf(0xFFFFFFFF), Conn.Unknown, "unknown (GDI fallback)");
Eq(ConnOf(16), Conn.Unknown, "indirect");

Eq(MonitorName(1, 1, "DELL S2721DGF", Conn.DisplayPort), "Monitor · DELL S2721DGF", "one monitor with its name");
Eq(MonitorName(1, 1, "", Conn.DisplayPort), "Monitor", "one monitor without a name");
Eq(MonitorName(2, 2, "LG ULTRAGEAR", Conn.Hdmi), "Monitor 2 · LG ULTRAGEAR", "the second of two");
Eq(MonitorName(1, 2, null, Conn.Hdmi), "Monitor 1", "the first of two, no name");
Eq(MonitorName(1, 1, "", Conn.Internal), "Laptop-Bildschirm", "a laptop panel has no name");
Eq(MonitorName(1, 2, "", Conn.Internal), "Laptop-Bildschirm 1", "laptop panel and a monitor");
Eq(MonitorName(1, 1, "  AOC 24G2W1G4 \u0001", Conn.Hdmi), "Monitor · AOC 24G2W1G4", "EDID name cleaned");
Check(MonitorName(1, 1, new string('X', 80), Conn.Hdmi).Length == "Monitor · ".Length + 32, "a long name is cut");

const string Dev = @"\\.\DISPLAY1";
var warn = MonitorRow("Monitor", Dev, 60, 144, Conn.DisplayPort, null, null);
Check(warn != null, "a row for 60 of 144 Hz");
Eq(warn!.St, "warn", "60 of 144 Hz is a warning");
Eq(warn.Value, "Läuft mit 60 Hz, kann aber 144 Hz", "the warning names both rates");
Eq(warn.Fix, @"hw:hz:\\.\DISPLAY1", "the fix names the display");
Eq(warn.Act, null, "the fix button is the plain „Beheben“");
Check(!warn.Guide, "switching is a real fix");
var w2 = MonitorRow("Monitor", Dev, 144, 165, Conn.DisplayPort, null, null)!;
Eq(w2.Value, "Läuft mit 144 Hz, kann aber 165 Hz", "144 of 165");
var top = MonitorRow("Monitor", Dev, 144, 144, Conn.DisplayPort, null, null)!;
Eq(top.St, "ok", "at the top is fine");
Eq(top.Value, "144 Hz, das Höchste, was Windows anbietet", "the top rate");
Eq(top.Fix, null, "nothing to fix at the top");
var near = MonitorRow("Monitor", Dev, 59, 60, Conn.DisplayPort, null, null)!;
Eq(near.St, "info", "59 of 60 on an external monitor: the cable note");
var hdmi60 = MonitorRow("Monitor", Dev, 60, 60, Conn.Hdmi, null, null)!;
Eq(hdmi60.St, "info", "60 Hz at most on HDMI: a note, no fault");
Check(hdmi60.Value.StartsWith("60 Hz, mehr bietet Windows hier nicht an") && hdmi60.Value.Contains("HDMI") && hdmi60.Value.Contains("DisplayPort"), "the HDMI note names cable and DisplayPort");
Eq(hdmi60.Fix, null, "no button on the note");
var dp60 = MonitorRow("Monitor", Dev, 60, 60, Conn.DisplayPort, null, null)!;
Check(dp60.St == "info" && dp60.Value.Contains("Kabel oder Anschluss") && !dp60.Value.Contains("HDMI"), "the note without HDMI names cable or port");
var unk75 = MonitorRow("Monitor", Dev, 75, 75, Conn.Unknown, null, null)!;
Eq(unk75.St, "info", "75 Hz at most on an unknown connection: the note");
var panel = MonitorRow("Laptop-Bildschirm", Dev, 60, 60, Conn.Internal, null, null)!;
Eq(panel.St, "ok", "a 60 Hz laptop panel is fine, no cable to blame");
Eq(panel.Value, "60 Hz, mehr bietet Windows hier nicht an", "the panel's row");
var panelHi = MonitorRow("Laptop-Bildschirm", Dev, 60, 165, Conn.Internal, null, null)!;
Eq(panelHi.St, "warn", "a 165 Hz laptop panel at 60 Hz (power saving on battery) is a warning too");
var miracast = MonitorRow("Monitor", Dev, 60, 60, Conn.Wireless, null, null)!;
Eq(miracast.St, "ok", "a wireless display gets no cable note");
var sw = MonitorRow("Monitor", Dev, 144, 144, Conn.DisplayPort, 60, 144)!;
Eq(sw.St, "ok", "switched by OrbitTweak: fine");
Eq(sw.Value, "144 Hz, von OrbitTweak umgestellt", "switched row says so");
Eq(sw.Fix, @"hw:hzback:\\.\DISPLAY1", "the way back names the display");
Eq(sw.Act, "Auf 60 Hz zurück", "the way back names the rate before");
var swGone = MonitorRow("Monitor", Dev, 120, 144, Conn.DisplayPort, 60, 144)!;
Eq(swGone.St, "warn", "set to something else by hand since: the normal check again");
Eq(swGone.Fix, @"hw:hz:\\.\DISPLAY1", "and the normal fix");
Eq(MonitorRow("Monitor", Dev, 0, 144, Conn.DisplayPort, null, null), null, "no current rate: no row");
Eq(MonitorRow("Monitor", Dev, 60, 0, Conn.DisplayPort, null, null), null, "no modes: no row");
Eq(MonitorRow("Monitor", Dev, 1, 60, Conn.DisplayPort, null, null), null, "\"hardware default\": no row");
var above = MonitorRow("Monitor", Dev, 75, 60, Conn.DisplayPort, null, null)!;
Eq(above.St, "info", "running above every listed mode: no claim, the top note");

foreach (var code in new[] { 1, -1, -2, -3, -4, -5, -6 })
{
    var t = ChangeError(code);
    Check(t.Length > 10 && !t.Contains("DISP_"), $"change error {code} is plain German");
}
Check(ChangeError(-4).Contains("-4"), "an unknown code is named");

// the fields of a switch: size, depth, rate and progressive flags always; position and rotation only as the driver
// reported them; nothing of DEVMODE's printer half (DM_ORIENTATION 0x1, DM_PAPERSIZE 0x2, DM_COPIES 0x100 …)
const int Base = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFLAGS | DM_DISPLAYFREQUENCY;
Eq(SwitchFields(0), Base, "nothing reported: the five a refresh rate switch needs");
Eq(SwitchFields(0x5C0000), Base, "the usual current-mode fields: the same five");
Eq(SwitchFields(0x5C0000 | DM_POSITION | DM_DISPLAYORIENTATION), Base | DM_POSITION | DM_DISPLAYORIENTATION, "a second or turned monitor keeps its place and rotation");
Eq(SwitchFields(DM_POSITION), Base | DM_POSITION, "position alone");
Eq(SwitchFields(0x1 | 0x2 | 0x100 | 0x20000000), Base, "printer fields and the scaling mode are not passed on");
Check((SwitchFields(-1) & DM_DISPLAYFREQUENCY) != 0 && (SwitchFields(-1) & 0x1) == 0, "every bit set: still the rate, never a printer field");
Eq(Base, 0x7C0000, "the five are the wingdi.h values");
Eq(DM_INTERLACED, 2, "DM_INTERLACED");

Eq(SwitchOutcome(0, 144, 60, 144), ((string?)null, false), "done");
var (oErr, oBack) = SwitchOutcome(-2, 144, 60, 60);
Check(oErr == ChangeError(-2) && !oBack, "refused and nothing moved: the reason, nothing to put back");
(oErr, oBack) = SwitchOutcome(-1, 144, 60, 144);
Check(oErr == ChangeError(-1) && oBack, "refused but the display moved anyway: put the rate before back");
(oErr, oBack) = SwitchOutcome(-3, 144, 60, 144);
Check(oErr == ChangeError(-3) && oBack, "switched but not saved: put back, there is no countdown for it");
(oErr, oBack) = SwitchOutcome(0, 144, 60, 60);
Check(oErr != null && oErr.Contains("weiter mit 60 Hz") && !oBack, "taken but still at the rate before: say so, nothing to put back");
(oErr, oBack) = SwitchOutcome(0, 144, 60, 120);
Check(oErr != null && oErr.Contains("120 statt 144") && oBack, "a third rate: say so and put the rate before back");
(oErr, oBack) = SwitchOutcome(0, 144, 60, 0);
Check(oErr != null && !oBack, "rate not readable afterwards: no claim, nothing blind put back");
(oErr, oBack) = SwitchOutcome(-1, 144, 60, 0);
Check(oErr == ChangeError(-1) && !oBack, "refused, rate not readable: nothing blind put back");
(oErr, oBack) = SwitchOutcome(1, 144, 60, 60);
Check(oErr == ChangeError(1) && !oBack, "restart wanted: nothing changed now");
(oErr, oBack) = SwitchOutcome(0, 60, 144, 60);
Check(oErr == null && !oBack, "the way back is a switch like any other");

// the trial is never stored: a reset, a power-off or a killed OrbitTweak during the countdown starts with the rate before
Check((TrialFlags & CDS_UPDATEREGISTRY) == 0, "the trial switch is dynamic only, Windows stores nothing");
Check((TrialFlags & CDS_TEST) == 0 && (SaveFlags & CDS_TEST) == 0, "neither the trial nor the save is only a test");
Check((SaveFlags & CDS_UPDATEREGISTRY) != 0, "„Behalten“ and the way back of a kept switch are stored");
Eq(CDS_UPDATEREGISTRY, 1u, "CDS_UPDATEREGISTRY");
Eq(CDS_TEST, 2u, "CDS_TEST");

Eq(WayBack(null, null, 60), 60, "first switch: back to the rate before it");
Eq(WayBack(60, 120, 120), 60, "on top of a kept 60 → 120: back to 60, the rate before OrbitTweak");
Eq(WayBack(60, 120, 100), 100, "the kept switch was changed by hand since: back to the rate before this one");

Check(SameSwitch("DELL S2721DGF", 2560, 1440, "DELL S2721DGF", 2560, 1440), "same monitor, same resolution");
Check(SameSwitch("DELL S2721DGF", 2560, 1440, "dell s2721dgf ", 2560, 1440), "names compared without case and spaces");
Check(SameSwitch("DELL S2721DGF", 2560, 1440, "DELL S2721DGF", 1440, 2560), "turned by 90° since");
Check(!SameSwitch("DELL S2721DGF", 2560, 1440, "DELL S2721DGF", 1920, 1080), "another resolution: the switch is no longer the one on screen");
Check(!SameSwitch("DELL S2721DGF", 2560, 1440, "LG ULTRAGEAR", 2560, 1440), "another monitor under the same GDI name: not its switch");
Check(SameSwitch("", 1920, 1080, "LG ULTRAGEAR", 1920, 1080), "name not known then: the resolution decides");
Check(SameSwitch("LG ULTRAGEAR", 1920, 1080, "", 1920, 1080), "name not known now (GDI fallback): the resolution decides");
Check(SameSwitch(null, 1920, 1080, null, 1920, 1080), "no names at all");

// =====================================================================
//  RAM: reading the PowerShell answer
// =====================================================================
Check(!RamQuery.Contains('"'), "the RAM query has no double quote (it goes inside -Command \"…\")");
Check(RamQuery.Contains("Win32_PhysicalMemory") && RamQuery.Contains("ConfiguredClockSpeed") && RamQuery.Contains("SMBIOSMemoryType") && RamQuery.Contains("count="), "the RAM query reads what the checks need");
string Line(params object[] f) => "mod=" + string.Join("\t", f);
var two = Line(17179869184, 3200, 2133, 26, 8, "DIMM_A2", "P0 CHANNEL A", "CMK32GX4M2B3200C16  ", "Corsair") + "\r\n"
        + Line(17179869184, 3200, 2133, 26, 8, "DIMM_B2", "P0 CHANNEL B", "CMK32GX4M2B3200C16", "Corsair") + "\r\ncount=2\r\n";
var parsed = ParseModules(two);
Check(parsed != null && parsed.Count == 2, "two modules read");
Eq(parsed![0].Capacity, 17179869184L, "capacity");
Eq(parsed[0].Speed, 3200, "speed");
Eq(parsed[0].Configured, 2133, "configured speed");
Eq(parsed[0].Type, 26, "SMBIOS type");
Eq(parsed[0].FormFactor, 8, "form factor");
Eq(parsed[0].Locator, "DIMM_A2", "slot");
Eq(parsed[1].Bank, "P0 CHANNEL B", "bank");
Eq(parsed[0].Part, "CMK32GX4M2B3200C16", "the part number is trimmed");
Eq(parsed[0].Maker, "Corsair", "maker");
Eq(ParseModules(two.Replace("count=2", "count=3")), null, "a module missing: no answer");
Eq(ParseModules(two.Replace("\r\ncount=2", "")), null, "no count line: no answer (cut off)");
Eq(ParseModules(Line(1, 2, 3) + "\ncount=1"), null, "too few fields: no answer");
Eq(ParseModules(Line(17179869184, 3200, 3200, 26, 8, "A", "B", "part\twith tab", "x") + "\ncount=1"), null, "a tab inside a field: no answer");
Eq(ParseModules("Timeout"), null, "a timeout");
Eq(ParseModules(""), null, "nothing");
Eq(ParseModules(null), null, "null");
Eq(ParseModules("Get-CimInstance : Zugriff verweigert\ncount=0"), null, "an error and no modules");
var blanks = ParseModules(Line(8589934592, "", "", "", "", "", "", "", "") + "\ncount=1");
Check(blanks != null && blanks[0].Speed == 0 && blanks[0].Configured == 0 && blanks[0].Type == 0 && blanks[0].Part == "", "empty fields read as 0 and \"\"");
Eq(ParseModules(Line(0, 0, 0, 0, 0, "", "", "", "") + "\ncount=1"), null, "only an empty slot: no modules");
var withEmpty = ParseModules(Line(0, 0, 0, 0, 0, "", "", "", "") + "\n" + Line(8589934592, 2666, 2666, 26, 8, "DIMM1", "", "", "") + "\ncount=2");
Check(withEmpty != null && withEmpty.Count == 1, "an entry with no capacity is dropped");

// =====================================================================
//  RAM: channels
// =====================================================================
Module M(string loc, string bank = "", int gb = 16, int type = 26, int ff = 8, int cfg = 3200, string part = "", int speed = 0)
    => new((long)gb * 1073741824, speed == 0 ? cfg : speed, cfg, type, ff, loc, bank, part, "");

string? K(string loc, string bank = "") => ChannelKey(loc, bank);
Eq(K("ChannelA-DIMM0", "BANK 0"), "A", "Intel: ChannelA-DIMM0");
Eq(K("ChannelB-DIMM1", "BANK 3"), "B", "Intel: ChannelB-DIMM1");
Eq(K("ChannelA-DIMM1", "BANK 1"), "A", "Intel: ChannelA-DIMM1");
Eq(K("Controller0-ChannelA-DIMM0", "BANK 0"), "C0-A", "Intel DDR5: memory controller 0");
Eq(K("Controller1-ChannelA-DIMM1", "BANK 0"), "C1-A", "Intel DDR5: memory controller 1");
Eq(K("Controller0-ChannelB-DIMM0", "BANK 0"), "C0-B", "controller and channel");
Eq(K("DIMM_A1", "P0 CHANNEL A"), "A", "ASUS: DIMM_A1");
Eq(K("DIMM_B2", "P0 CHANNEL B"), "B", "ASUS: DIMM_B2");
Eq(K("DIMM 0", "P0 CHANNEL A"), "A", "MSI/ASRock AMD: the bank label names the channel");
Eq(K("DIMM 1", "P0 CHANNEL B"), "B", "MSI/ASRock AMD: channel B");
Eq(K("DIMM 1", "CHANNEL A"), "A", "CHANNEL A in the bank label");
Eq(K("DDR4_A1", "BANK 0"), "A", "Gigabyte: DDR4_A1");
Eq(K("DDR4_B2", "BANK 3"), "B", "Gigabyte: DDR4_B2");
Eq(K("DDR5_A2", "BANK 1"), "A", "Gigabyte DDR5");
Eq(K("A1_DIMM0", "A1_BANK0"), "A", "A1_DIMM0");
Eq(K("B1_DIMM1", "B1_BANK1"), "B", "B1_DIMM1");
Eq(K("DIMMA1", "BANK 0"), "A", "DIMMA1");
Eq(K("DIMM A2", ""), "A", "DIMM A2");
Eq(K("P1-DIMMB1", "P0_Node0_Channel1_Dimm0"), "B", "Supermicro: P1-DIMMB1");
Eq(K("", "P0_Node0_Channel1_Dimm0"), "1", "a numbered channel in the bank label");
Eq(K("ChA-DIMM0", ""), "A", "ChA-DIMM0");
Eq(K("CHANNEL B DIMM 0", ""), "B", "CHANNEL B DIMM 0");
Eq(K("channela-dimm0", ""), "A", "lower case");
Eq(K("DIMM1", ""), null, "Dell/HP DIMM1: the slot, not the channel");
Eq(K("DIMM 0", "BANK 0"), null, "DIMM 0 with BANK 0: not guessed");
Eq(K("DIMM 1", "BANK 2"), null, "DIMM 1 with BANK 2: not guessed");
Eq(K("XMM1", "BANK 0"), null, "Dell XMM1: not guessed");
Eq(K("Bottom-Slot 1(left)", "BANK 0"), null, "HP laptop slot: not guessed");
Eq(K("DIMM A", ""), null, "DIMM A without a number: not guessed");
Eq(K("SODIMM1", ""), null, "SODIMM1: not guessed");
Eq(K("Chassis slot 2", ""), null, "a word starting with Ch is no channel");
Eq(K("", ""), null, "nothing");
Eq(K(null!, null!), null, "null");

var (cs, cn) = Channels(new[] { M("DIMM_A2", "P0 CHANNEL A") });
Check(cs == ChanState.Single && cn == 1, "one DIMM: single channel");
Check(Channels(new[] { M("ChannelA-DIMM0", "BANK 0", ff: 12) }).state == ChanState.Single, "one SO-DIMM: single channel");
Check(Channels(new[] { M("DIMM1", ff: 12) }).state == ChanState.Single, "one SO-DIMM without a channel label: still one module");
Check(Channels(new[] { M("DIMM 0", ff: 0) }).state == ChanState.Unknown, "one module of unknown form: maybe soldered, not guessed");
Check(Channels(new[] { M("Onboard Memory", ff: 12) }).state == ChanState.Unknown, "one soldered module: not guessed");
Check(Channels(new[] { M("ChannelA-DIMM0", "OnBoard", ff: 12) }).state == ChanState.Unknown, "on board in the bank label: not guessed");
Check(Channels(new[] { M("ChannelA-DIMM0", type: 35, ff: 12) }).state == ChanState.Unknown, "one LPDDR5 entry: soldered, not guessed");
Check(Channels(new[] { M("DIMM 0", type: 0) }).state == ChanState.Unknown, "unknown memory type (a virtual machine): not guessed");
Check(Channels(new[] { M("DIMM 0", type: 2) }).state == ChanState.Unknown, "SMBIOS \"unknown\": not guessed");
Check(Channels(Array.Empty<Module>()).state == ChanState.Unknown, "no modules");

Check(Channels(new[] { M("ChannelA-DIMM0", "BANK 0"), M("ChannelB-DIMM0", "BANK 2") }) == (ChanState.Multi, 2), "Intel: A and B is dual channel");
Check(Channels(new[] { M("ChannelA-DIMM0", "BANK 0"), M("ChannelA-DIMM1", "BANK 1") }).state == ChanState.Single, "Intel: both in channel A is single channel");
Check(Channels(new[] { M("ChannelA-DIMM1", "BANK 1"), M("ChannelB-DIMM1", "BANK 3") }).state == ChanState.Multi, "Intel: A2 and B2");
Check(Channels(new[] { M("DIMM_A2", "P0 CHANNEL A"), M("DIMM_B2", "P0 CHANNEL B") }).state == ChanState.Multi, "ASUS AMD: A2 and B2 is dual channel");
Check(Channels(new[] { M("DIMM_A1", "P0 CHANNEL A"), M("DIMM_A2", "P0 CHANNEL A") }).state == ChanState.Single, "ASUS AMD: A1 and A2 is single channel");
Check(Channels(new[] { M("DIMM 0", "P0 CHANNEL A"), M("DIMM 1", "P0 CHANNEL B") }).state == ChanState.Multi, "MSI AMD: channels from the bank labels");
Check(Channels(new[] { M("DIMM 0", "P0 CHANNEL A"), M("DIMM 1", "P0 CHANNEL A") }).state == ChanState.Single, "MSI AMD: both in channel A");
Check(Channels(new[] { M("Controller0-ChannelA-DIMM1", "BANK 0", type: 34), M("Controller1-ChannelA-DIMM1", "BANK 0", type: 34) }).state == ChanState.Multi, "Intel DDR5: two controllers is dual channel");
Check(Channels(new[] { M("Controller0-ChannelA-DIMM0", "BANK 0", type: 34), M("Controller0-ChannelA-DIMM1", "BANK 0", type: 34) }).state == ChanState.Single, "Intel DDR5: one controller twice is single channel");
Check(Channels(new[] { M("Controller0-ChannelA-DIMM0", type: 34), M("Controller0-ChannelA-DIMM1", type: 34), M("Controller1-ChannelA-DIMM0", type: 34), M("Controller1-ChannelA-DIMM1", type: 34) }) == (ChanState.Multi, 2), "Intel DDR5: four modules in two channels");
Check(Channels(new[] { M("ChannelA-DIMM0"), M("ChannelA-DIMM1"), M("ChannelB-DIMM0"), M("ChannelB-DIMM1") }) == (ChanState.Multi, 2), "four DIMMs, dual channel");
Check(Channels(new[] { M("DDR4_A2", "BANK 1"), M("DDR4_B2", "BANK 3") }).state == ChanState.Multi, "Gigabyte: A2 and B2");
Check(Channels(new[] { M("DIMM_A1"), M("DIMM_B1"), M("DIMM_C1"), M("DIMM_D1") }) == (ChanState.Multi, 4), "quad channel");
Check(Channels(new[] { M("DIMM1"), M("DIMM2") }).state == ChanState.Unknown, "Dell DIMM1/DIMM2: not guessed");
Check(Channels(new[] { M("DIMM 0", "BANK 0"), M("DIMM 1", "BANK 2") }).state == ChanState.Unknown, "only bank numbers: not guessed");
Check(Channels(new[] { M("ChannelA-DIMM0"), M("DIMM2") }).state == ChanState.Unknown, "one label known, one not: not guessed");
Check(Channels(new[] { M("ChannelA-DIMM0", ff: 12), M("ChannelB-DIMM0", ff: 12) }).state == ChanState.Multi, "laptop: soldered plus one SO-DIMM in two channels");
Check(Channels(Enumerable.Range(0, 8).Select(i => M($"Controller{i / 4}-Channel{(char)('A' + i % 4)}", gb: 2, type: 35, ff: 0)).ToList()).state == ChanState.Unknown, "LPDDR5 entries: not guessed");
Check(Channels(new[] { M("ChannelA-DIMM0", type: 24), M("ChannelB-DIMM0", type: 24) }).state == ChanState.Multi, "DDR3 dual channel");
Check(Channels(new[] { M("ChannelA-DIMM0", type: 26), M("ChannelB-DIMM0", type: 0) }).state == ChanState.Unknown, "one module of unknown type: not guessed");

// two CPU sockets each have their own channel A and their own channel count: no claim, neither single nor dual
Eq(SocketOf("P1-DIMMA1", "P0_Node0_Channel0_Dimm0"), "1", "Supermicro: the socket from the slot label first");
Eq(SocketOf("P2-DIMMA1", ""), "2", "Supermicro: the second CPU");
Eq(SocketOf("CPU1_DIMM_A1", "NODE 1"), "1", "ASUS workstation: CPU1_DIMM_A1");
Eq(SocketOf("DIMM 0", "P0 CHANNEL A"), "0", "AMD: the bank label's P0");
Eq(SocketOf("", "P1_Node1_Channel0_Dimm0"), "1", "a socket in the bank label");
Eq(SocketOf("ChannelA-DIMM0", "BANK 0"), null, "Intel desktop: no socket");
Eq(SocketOf("DIMM_A1", ""), null, "ASUS desktop: no socket");
Eq(SocketOf("P1", ""), null, "P1 alone is no socket label");
Eq(SocketOf("PROC 1 DIMM 1", ""), null, "PROC 1: not a known style, no socket");
Eq(SocketOf(null, null), null, "null");
Check(Channels(new[] { M("P1-DIMMA1"), M("P2-DIMMA1") }).state == ChanState.Unknown, "two sockets, channel A each: no single-channel claim");
Check(Channels(new[] { M("P1-DIMMA1"), M("P1-DIMMB1"), M("P2-DIMMA1"), M("P2-DIMMB1") }).state == ChanState.Unknown, "two sockets, A and B each: no dual-channel claim either");
Check(Channels(new[] { M("CPU0_DIMM_A1"), M("CPU1_DIMM_A1") }).state == ChanState.Unknown, "CPU0/CPU1: no claim");
Check(Channels(new[] { M("", "P0_Node0_Channel0_Dimm0"), M("", "P1_Node1_Channel0_Dimm0") }).state == ChanState.Unknown, "bank labels of two sockets: no claim");
Check(RamRows(new[] { M("P1-DIMMA1"), M("P2-DIMMA1") }, false).All(r => r.Name != "RAM-Kanäle"), "two sockets: no channel row");
Check(Channels(new[] { M("P1-DIMMA1"), M("P1-DIMMA2") }).state == ChanState.Single, "one socket, both in channel A: single channel");
Check(Channels(new[] { M("P1-DIMMA1"), M("P1-DIMMB1") }).state == ChanState.Multi, "one socket, A and B");
Check(Channels(new[] { M("DIMM 0", "P0 CHANNEL A"), M("DIMM 1", "P0 CHANNEL A") }).state == ChanState.Single, "AMD desktop P0 only: still single channel");
Check(Channels(new[] { M("DIMM_A2", "P0 CHANNEL A"), M("DIMM_B2", "P0 CHANNEL B") }) == (ChanState.Multi, 2), "ASUS AMD: still dual channel with the socket in the bank label");

// more board and laptop label styles seen in Win32_PhysicalMemory
Eq(K("ChannelB-DIMM0", "BANK 2"), "B", "Lenovo/Dell laptop: ChannelB-DIMM0");
Eq(K("Controller1-ChannelA", "BANK 0"), "C1-A", "Intel DDR5 without the DIMM part");
Eq(K("DIMM_A0", "BANK 0"), "A", "DIMM_A0");
Eq(K("CHANNEL A DIMM0", "BANK 0"), "A", "CHANNEL A DIMM0");
Eq(K("Top - on board", "BANK 0"), null, "a laptop's soldered row: not guessed");
Eq(K("Channel-A DIMM1", ""), "A", "Channel-A DIMM1");
Eq(K("DIMM1 ", "CHANNEL B "), "B", "spaces around the labels");
Eq(K("Node0_Dimm0", "BANK 0"), null, "Node0_Dimm0 without a channel: not guessed");
Check(Channels(new[] { M("ChannelA-DIMM0", ff: 12), M("Top - on board", ff: 0) }).state == ChanState.Unknown, "soldered row next to an SO-DIMM without a channel: not guessed");

// a laptop with one SO-DIMM: single channel is a real point there too (the guide says when a second slot exists)
var lapOne = RamRows(new[] { M("ChannelA-DIMM0", "BANK 0", gb: 8, ff: 12, cfg: 3200, part: "KF432S20IB/8") }, true);
Check(Find(lapOne, "RAM-Kanäle") is { St: "warn", Guide: true, Act: "Anleitung" }, "laptop single channel: a warning with the guide");
Check(Find(lapOne, "RAM-Takt") is { St: "ok" }, "laptop at its rated speed: fine");
var lapOff = RamRows(new[] { M("ChannelA-DIMM0", ff: 12, cfg: 2666, part: "KF432S20IBK2/32"), M("ChannelB-DIMM0", ff: 12, cfg: 2666, part: "KF432S20IBK2/32") }, false);
Check(Find(lapOff, "RAM-Takt") is { St: "info", Guide: false, Act: null }, "SO-DIMMs below their speed: a plain note, no guide");

// PowerShell's answer around the lines that count: an error or a warning before, other line endings, padded fields
var noisy = "WARNUNG: irgendwas\r\n" + Line(8589934592, 3200, 3200, 26, 8, " ChannelA-DIMM0 ", " BANK 0 ", "  F4-3200C16-8GVKB  ", " 04CD ") + "\n"
          + Line(8589934592, 3200, 3200, 26, 8, "ChannelB-DIMM0", "BANK 2", "F4-3200C16-8GVKB", "04CD") + "\ncount=2";
var np = ParseModules(noisy);
Check(np != null && np.Count == 2 && np[0].Locator == "ChannelA-DIMM0" && np[0].Bank == "BANK 0" && np[0].Part == "F4-3200C16-8GVKB" && np[0].Maker == "04CD", "other lines ignored, fields trimmed");
Eq(ParseModules(two.Replace("count=2", "count=x")), null, "a count that is no number: no answer");
Eq(ParseModules(two.Replace("count=2", "count=-2")), null, "a negative count: no answer");
Eq(ParseModules(Line("17179869184", "3200", "2133.5", 26, 8, "A", "B", "C", "D") + "\ncount=1")?[0].Configured, 0, "a speed that is no whole number reads as unknown");
Eq(ParseModules(Line("1.6E10", 3200, 3200, 26, 8, "A", "B", "C", "D") + "\ncount=1"), null, "a capacity that is no whole number: no module");

Eq(Sizes(new[] { M("a", gb: 16) }), "16 GB", "one module");
Eq(Sizes(new[] { M("a", gb: 16), M("b", gb: 16) }), "2 × 16 GB", "two the same");
Eq(Sizes(new[] { M("a", gb: 8), M("b", gb: 16) }), "16 GB + 8 GB", "two different, larger first");
Eq(Sizes(new[] { M("a", gb: 8), M("b", gb: 8), M("c", gb: 8), M("d", gb: 8) }), "4 × 8 GB", "four the same");
Eq(Sizes(new[] { new Module(25769803776, 5600, 5600, 34, 8, "a", "", "", ""), new Module(25769803776, 5600, 5600, 34, 8, "b", "", "", "") }), "2 × 24 GB", "24 GB DDR5 modules");

// =====================================================================
//  RAM: rated speed from the part number
// =====================================================================
void R(string part, int gen, int mts) => Eq(Rated(part), ((int, int)?)(gen, mts), "rated " + part);
void None(string? part) => Eq(Rated(part), ((int, int)?)null, "no rated speed for " + (part ?? "null"));

// Corsair
R("CMK16GX4M2B3200C16", 4, 3200); R("CMK32GX4M2D3600C18", 4, 3600); R("CMK16GX4M2A2400C14", 4, 2400); R("CMK8GX4M1A2400C16", 4, 2400);
R("CMW32GX4M2E3200C16", 4, 3200); R("CMW16GX4M2C3200C16W", 4, 3200); R("CMR16GX4M2C3000C15", 4, 3000); R("CMD32GX4M2C3200C16", 4, 3200);
R("CMU16GX4M2A2666C16", 4, 2666); R("CMN32GX4M2Z3600C18", 4, 3600); R("CMG32GX4M2E3200C16", 4, 3200); R("CMK32GX4M4B3200C16", 4, 3200);
R("CMK16GX4M2Z3200C16", 4, 3200); R("CMT32GX4M2C3200C16", 4, 3200); R("CMK64GX4M2E3200C16", 4, 3200);
R("CMK32GX5M2B5600C36", 5, 5600); R("CMK32GX5M2B6000C36", 5, 6000); R("CMK32GX5M2B6000Z30", 5, 6000); R("CMK32GX5M2B5200C40", 5, 5200);
R("CMH32GX5M2B6000C30", 5, 6000); R("CMT32GX5M2X6200C36", 5, 6200); R("CMT32GX5M2B5600C36", 5, 5600); R("CMK64GX5M2B5600C40", 5, 5600);
R("CMK32GX5M2B5200C40W", 5, 5200); R("CMH64GX5M2B6400C32", 5, 6400); R("CMK96GX5M2B6000C30", 5, 6000);
R("CMSX16GX4M2A3200C22", 4, 3200); R("CMSX32GX5M2A4800C40", 5, 4800); R("CMSX8GX4M1A2400C16", 4, 2400);
R("CMZ8GX3M2A1600C9", 3, 1600);
R("cmk16gx4m2b3200c16", 4, 3200);
// G.Skill
R("F4-3200C16D-16GVKB", 4, 3200); R("F4-3600C16D-32GTZNC", 4, 3600); R("F4-3200C14D-32GTZR", 4, 3200); R("F4-3600C18D-32GTZN", 4, 3600);
R("F4-4000C18D-32GTZR", 4, 4000); R("F4-3200C16-8GVKB", 4, 3200); R("F4-2400C15S-8GNT", 4, 2400); R("F4-3000C16D-16GISB", 4, 3000);
R("F4-3600C16-16GTZNC", 4, 3600); R("F4-3200C16D-32GIS", 4, 3200); R("F4-2133C15S-8GNT", 4, 2133);
R("F5-6000J3038F16GX2-TZ5RK", 5, 6000); R("F5-6000J3636F16GX2-FX5", 5, 6000); R("F5-6400J3239G16GX2-TZ5RK", 5, 6400);
R("F5-5600U3636C16GX2-RS5K", 5, 5600); R("F5-6000J3040G32GX2-TZ5NR", 5, 6000); R("F5-7200J3445G16GX2-TZ5RK", 5, 7200);
R("F5-6000J3038F16G", 5, 6000); R("F5-8000J3848H16GX2-TZ5RK", 5, 8000);
R("F3-1600C9D-8GXM", 3, 1600); R("F4-3200C22S-16GRS", 4, 3200);
// Kingston: kit numbers
R("KF436C17BBK2/16", 4, 3600); R("KF432C16BB/8", 4, 3200); R("KF432C16BBK2/32", 4, 3200); R("KF426C16BB/8", 4, 2666);
R("KF429C17BB/8", 4, 2933); R("KF437C19RB12AK2/32", 4, 3733); R("KF436C18BBAK2/32", 4, 3600);
R("KF560C36BBEK2-32", 5, 6000); R("KF552C40BBK2-32", 5, 5200); R("KF556C40BBK2-32", 5, 5600); R("KF564C32RSAK2-32", 5, 6400);
R("KF572C38RSAK2-32", 5, 7200); R("KF548S38IB-16", 5, 4800); R("KF432S20IBK2/32", 4, 3200); R("KF560C30BBEAK2-32", 5, 6000);
R("HX432C16FB3K2/16", 4, 3200); R("HX436C17PB4K2/16", 4, 3600); R("HX426C16FB3/8", 4, 2666); R("HX318C10F/8", 3, 1866);
R("HX426S15IB2/16", 4, 2666);
// Kingston: module numbers (what most modules report)
R("KHX3200C16D4/8GX", 4, 3200); R("KHX2666C16/8G", 4, 2666); R("KHX3600C17D4/8GX", 4, 3600); R("KF3200C16D4/8GX", 4, 3200);
R("KF3600C17D4/8GX", 4, 3600); R("KF3600C18D4/16GX", 4, 3600); R("KF5600C40-16", 5, 5600); R("KF6000C36-16", 5, 6000);
R("KF3200C20S4/16G", 4, 3200); R("KHX2933C17S4/16G", 4, 2933); R("KHX3000C15D4/8GX", 4, 3000);
R("KHX4000C19D4/8GX", 0, 4000);   // 4000 fits DDR4 and DDR5: the module's type decides
None("KVR32N22S8/8"); None("9905734-102.A00G"); None("KVR48U40BS8-16"); None("99U5701-036.A00G"); None("ACR26D4S9S8ME-8");
// Crucial
R("BL2K8G32C16U4B", 4, 3200); R("BL8G32C16U4B.M8FE1", 4, 3200); R("BL16G36C16U4BL.M16FE1", 4, 3600); R("BLM2K8G44C19U4B", 4, 4400);
R("BLM16G40C18U4B", 4, 4000); R("BL2K16G30C15U4W", 4, 3000); R("BL8G26C16U4B", 4, 2666); R("BL16G32C16S4B", 4, 3200);
R("BL2K16G36C16U4R", 4, 3600); R("BL8G30C15U4B.M8FE", 4, 3000);
R("BLS8G4D240FSB.16FBD", 4, 2400); R("BLS16G4D30AESB.M16FE", 4, 3000); R("BLS8G4D26BFSE.8FBD", 4, 2666); R("BLS2K8G4D32AESBK", 4, 3200);
R("BLT8G4D30AETA.K8FD", 4, 3000); R("BLE8G4D36BEEAK.K8FE", 4, 3600); R("BLS16G4D32AEST.M16FE", 4, 3200);
R("CP16G60C36U5B.M8D1", 5, 6000); R("CP2K16G60C36U5W", 5, 6000); R("CP2K16G64C38U5B", 5, 6400);
None("CP2K16G56C46U5"); None("CP16G56C46U5.M8D1"); None("CP16G4DFRA32A.M8FR"); None("CT16G4DFRA32A.M16FR"); None("CT16G56C46U5.M8A1");
None("CT8G4DFS8266.C8FE"); None("CT32G48C40U5.M16A1");
// TeamGroup
R("TEAMGROUP-UD4-3200", 4, 3200); R("TEAMGROUP-UD4-3600", 4, 3600); R("TEAMGROUP-UD5-6000", 5, 6000); R("TEAMGROUP-SD4-3200", 4, 3200);
R("TEAMGROUP-UD5-6400", 5, 6400); R("teamgroup-ud4-3000", 4, 3000);
R("TF3D416G3200HC16FDC01", 4, 3200); R("TF4D416G3600HC18JDC01", 4, 3600); R("TLZGD416G3200HC16CDC01", 4, 3200);
R("TDPGD416G3600HC18LDC01", 4, 3600); R("TLRD416G3200HC16FDC01", 4, 3200); R("FF3D532G6000HC38ADC01", 5, 6000);
R("FLBD532G6000HC38ADC01", 5, 6000); R("CTCED532G6000HC30DC01", 5, 6000); R("FF4D532G6400HC40BDC01", 5, 6400);
None("TED416G3200C2201"); None("TED532G5600C4601"); None("TED48G2666C1901");
// Patriot
R("PVS416G320C6K", 4, 3200); R("PVS416G360C8K", 4, 3600); R("PVS416G400C9K", 4, 4000); R("PVS416G360C8", 4, 3600);
R("PVB416G300C6K", 4, 3000); R("PVE2416G320C8K", 4, 3200); R("PVV532G600C30K", 5, 6000); R("PVX532G800C38K", 5, 8000);
R("PVS416G266C6K", 4, 2666); R("PVR416G320C6K", 4, 3200); R("PVS432G440C9K", 4, 4400); R("PVS416G373C7K", 4, 3733);
None("PSD416G32002"); None("PSD48G266681");
// ADATA XPG
R("AX4U320016G16A-DT50", 4, 3200); R("AX4U360016G18I-ST50", 4, 3600); R("AX4U41338G19J-DGM50X", 4, 4133);
R("AX5U6000C3016G-DCLARBK", 5, 6000); R("AX5U5200C3816G-CLABK", 5, 5200); R("AX4U32008G16A-SR10", 4, 3200);
R("AX5U6400C3216G-DCLARWH", 5, 6400);
None("AD4U320016G22-SGN"); None("AD5U48008G-S"); None("AD4U26668G19-SGN");
// OEM and junk
None("M378A1K43CB2-CTD"); None("M471A2K43DB1-CWE"); None("M323R2GA3BB0-CQKOD"); None("HMA81GU6CJR8N-XN"); None("HMCG78AGBUA081N");
None("MTA8ATF1G64AZ-2G6E1"); None("4ATF1G64HZ-3G2E2"); None("8ATF1G64HZ-2G6H1"); None(""); None(null); None("Unknown");
None("0x00000000"); None("Undefined"); None("CM"); None("F4-"); None("KF4"); None("BL"); None("TEAMGROUP-UD4");
None("F4-9999C16D-16GVKB");   // not a speed DDR4 runs at
None("CMK16GX4M2B9999C16");
None("F5-2400J3038F16GX2");   // too slow for DDR5

Check(IsJedecDefault(4, 2133) && IsJedecDefault(4, 2400) && IsJedecDefault(4, 2666) && IsJedecDefault(4, 2933) && IsJedecDefault(4, 3200), "DDR4 JEDEC defaults");
Check(IsJedecDefault(4, 2667) && IsJedecDefault(4, 2934) && IsJedecDefault(4, 2132), "Windows rounds them both ways");
Check(!IsJedecDefault(4, 3000) && !IsJedecDefault(4, 3600) && !IsJedecDefault(4, 3466) && !IsJedecDefault(4, 2800), "DDR4 overclock speeds are no JEDEC default");
Check(IsJedecDefault(5, 4800) && IsJedecDefault(5, 5200) && IsJedecDefault(5, 5600), "DDR5 JEDEC defaults");
Check(!IsJedecDefault(5, 6000) && !IsJedecDefault(5, 6400) && !IsJedecDefault(5, 4000), "6000 on DDR5 is what people set by hand (EXPO sweet spot): no default");
Check(!IsJedecDefault(3, 1600) && !IsJedecDefault(0, 2133), "only DDR4 and DDR5");

// =====================================================================
//  RAM: XMP/EXPO
// =====================================================================
(XmpState, int, int, int) X(params Module[] m) => Xmp(m);
Module D4(int cfg, string part, string loc = "ChannelA-DIMM0") => M(loc, cfg: cfg, part: part);
Module D5(int cfg, string part, string loc = "ChannelA-DIMM0") => M(loc, type: 34, cfg: cfg, part: part);
Eq(X(D4(2133, "CMK16GX4M2B3200C16"), D4(2133, "CMK16GX4M2B3200C16")), (XmpState.Off, 2133, 3200, 4), "Corsair 3200 kit at 2133: XMP probably off");
Eq(X(D4(3200, "CMK16GX4M2B3200C16"), D4(3200, "CMK16GX4M2B3200C16")), (XmpState.On, 3200, 3200, 4), "Corsair 3200 kit at 3200: on");
Eq(X(D4(3200, "F4-3600C16D-32GTZNC")), (XmpState.Off, 3200, 3600, 4), "3600 kit at the JEDEC 3200: probably off");
Eq(X(D4(2667, "KF436C17BBK2/16")), (XmpState.Off, 2667, 3600, 4), "Windows' 2667 is the 2666 default");
Eq(X(D4(3466, "F4-3600C16D-32GTZNC")), (XmpState.Lower, 3466, 3600, 4), "3466 is set by hand: lower, no claim about XMP");
Eq(X(D4(3600, "KHX3600C17D4/8GX")), (XmpState.On, 3600, 3600, 4), "Kingston module number at 3600");
Eq(X(D4(3733, "F4-3600C16D-32GTZNC")), (XmpState.On, 3733, 3600, 4), "above the rated speed is on");
Eq(X(D4(2133, "KHX4000C19D4/8GX")), (XmpState.Off, 2133, 4000, 4), "a part number without its generation, DDR4 by the module type");
Eq(X(D5(4800, "CMK32GX5M2B6000C36"), D5(4800, "CMK32GX5M2B6000C36")), (XmpState.Off, 4800, 6000, 5), "DDR5 6000 kit at 4800: EXPO/XMP probably off");
Eq(X(D5(6000, "CMK32GX5M2B6000C36")), (XmpState.On, 6000, 6000, 5), "DDR5 6000 at 6000: on");
Eq(X(D5(5600, "F5-6000J3038F16GX2-TZ5RK")), (XmpState.Off, 5600, 6000, 5), "DDR5 6000 at the JEDEC 5600: probably off");
Eq(X(D5(6000, "F5-6400J3239G16GX2-TZ5RK")), (XmpState.Lower, 6000, 6400, 5), "a 6400 kit at 6000 (the AM5 sweet spot): lower, no claim");
Eq(X(D5(5200, "KF560C36BBEK2-32")), (XmpState.Off, 5200, 6000, 5), "Kingston 6000 at 5200: probably off");
Eq(X(D5(4800, "TEAMGROUP-UD5-6000")), (XmpState.Off, 4800, 6000, 5), "TeamGroup module number");
Eq(X(D5(4800, "CP2K16G56C46U5")).Item1, XmpState.Unknown, "a JEDEC 5600 module at 4800: no claim");
Eq(X(D4(2666, "M378A1K43CB2-CTD"), D4(2666, "M378A1K43CB2-CTD")).Item1, XmpState.Unknown, "Samsung OEM: unknown");
Eq(X(D4(2666, "M378A1K43CB2-CTD")).Item2, 2666, "unknown still says the speed");
Eq(X(D4(2133, "CMK16GX4M2B3200C16"), D4(2133, "HMA81GU6CJR8N-XN")).Item1, XmpState.Unknown, "one module unknown: unknown");
Eq(X(D4(3200, "F4-3600C16D-32GTZNC"), D4(3200, "CMK16GX4M2B3200C16")), (XmpState.On, 3200, 3200, 4), "mixed kits: the slowest decides");
Eq(X(D4(2133, "F5-6000J3038F16GX2-TZ5RK")).Item1, XmpState.Unknown, "a DDR5 part number on a DDR4 module: no claim");
Eq(X(M("ChannelA-DIMM0", type: 24, cfg: 1333, part: "CMZ8GX3M2A1600C9")).Item1, XmpState.None, "DDR3: no row");
Eq(X(M("ChannelA-DIMM0", type: 35, cfg: 6400, part: "")).Item1, XmpState.None, "LPDDR5: no row");
Eq(X(D4(0, "CMK16GX4M2B3200C16")).Item1, XmpState.None, "no speed reported: no row");
Eq(X(D4(1600, "CMK16GX4M2B3200C16")).Item1, XmpState.None, "1600 on DDR4 looks halved: no row");
Eq(X(D5(2400, "CMK32GX5M2B6000C36")).Item1, XmpState.None, "2400 on DDR5 looks halved: no row");
Eq(X(D4(2133, "CMK16GX4M2B3200C16"), D5(4800, "CMK32GX5M2B6000C36")).Item1, XmpState.None, "DDR4 next to DDR5 cannot be: no row");
Eq(X().Item1, XmpState.None, "no modules");
Eq(X(D4(2400, "F4-2400C15S-8GNT")).Item1, XmpState.On, "a 2400 kit at 2400");
Eq(X(D4(2133, "F4-2400C15S-8GNT")).Item1, XmpState.Off, "a 2400 kit at 2133");
Eq(X(D4(2133, "F4-2133C15S-8GNT")).Item1, XmpState.On, "a 2133 kit at 2133 runs as sold");

Check(!Laptop(new[] { D4(3200, "") }, false), "a desktop");
Check(Laptop(new[] { D4(3200, "") }, true), "a battery is a laptop");
Check(Laptop(new[] { M("ChannelA-DIMM0", ff: 12) }, false), "an SO-DIMM is a laptop (or a mini PC)");

// =====================================================================
//  RAM: the Diagnose rows
// =====================================================================
Row? Find(List<Row> rows, string name) => rows.FirstOrDefault(r => r.Name == name);
var single = RamRows(new[] { M("DIMM_A2", "P0 CHANNEL A", gb: 16, cfg: 2133, part: "CMK16GX4M1E3200C16") }, false);
var ch = Find(single, "RAM-Kanäle");
Check(ch != null, "a channel row for one module");
Eq(ch!.St, "warn", "single channel is a warning");
Eq(ch.Value, "Single Channel: nur ein Riegel (16 GB)", "one module named");
Eq(ch.Fix, "hw:ram:chan", "the channel guide");
Eq(ch.Act, "Anleitung", "the button says what it does");
Check(ch.Guide, "a guide, not a one-click fix");
var xr = Find(single, "RAM-Takt");
Check(xr != null, "a speed row");
Eq(xr!.St, "warn", "XMP probably off on a desktop is a warning");
Eq(xr.Value, "XMP wahrscheinlich aus: 2133 statt 3200 MT/s", "says „wahrscheinlich“ and both speeds");
Eq(xr.Fix, "hw:ram:xmp", "the XMP guide");
Check(xr.Guide && xr.Act == "Anleitung", "a guide with its own label");
var same = RamRows(new[] { M("DIMM_A1", "P0 CHANNEL A", cfg: 3200, part: "CMK32GX4M2B3200C16"), M("DIMM_A2", "P0 CHANNEL A", cfg: 3200, part: "CMK32GX4M2B3200C16") }, false);
Eq(Find(same, "RAM-Kanäle")?.Value, "Single Channel: beide Riegel im selben Kanal", "both in one channel");
Eq(Find(same, "RAM-Takt")?.St, "ok", "XMP on");
Eq(Find(same, "RAM-Takt")?.Value, "3200 MT/s DDR4, wie auf den Riegeln angegeben", "on: the speed as sold");
Eq(Find(same, "RAM-Takt")?.Fix, null, "nothing to open when all is fine");
var four = RamRows(new[] { M("ChannelA-DIMM0"), M("ChannelA-DIMM1"), M("ChannelA-DIMM2"), M("ChannelA-DIMM3") }, false);
Eq(Find(four, "RAM-Kanäle")?.Value, "Single Channel: alle Riegel im selben Kanal", "four in one channel");
var dual = RamRows(new[] { M("ChannelA-DIMM1", cfg: 3600, part: "F4-3600C16D-32GTZNC"), M("ChannelB-DIMM1", cfg: 3600, part: "F4-3600C16D-32GTZNC") }, false);
Eq(Find(dual, "RAM-Kanäle")?.St, "ok", "dual channel is fine");
Eq(Find(dual, "RAM-Kanäle")?.Value, "Dual Channel · 2 × 16 GB", "dual channel with the sizes");
Eq(Find(dual, "RAM-Kanäle")?.Fix, null, "no button on a fine row");
var quad = RamRows(new[] { M("DIMM_A1", gb: 8), M("DIMM_B1", gb: 8), M("DIMM_C1", gb: 8), M("DIMM_D1", gb: 8) }, false);
Eq(Find(quad, "RAM-Kanäle")?.Value, "Quad Channel · 4 × 8 GB", "quad channel");
var lap = RamRows(new[] { M("ChannelA-DIMM0", cfg: 2666, part: "KF432S20IBK2/32", ff: 12), M("ChannelB-DIMM0", cfg: 2666, part: "KF432S20IBK2/32", ff: 12) }, true);
var lx = Find(lap, "RAM-Takt")!;
Eq(lx.St, "info", "a laptop below its modules' speed: a note, not a warning");
Check(lx.Value.Contains("2666") && lx.Value.Contains("3200") && lx.Value.Contains("Laptops"), "the laptop note names both speeds and why");
Check(!lx.Guide && lx.Fix == "hw:ram:xmp", "the laptop note opens the details");
Eq(Find(lap, "RAM-Kanäle")?.St, "ok", "laptop dual channel");
var sodimmDesk = RamRows(new[] { M("ChannelA-DIMM0", cfg: 2666, part: "CMSX16GX4M2A3200C22", ff: 12) }, false);
Eq(Find(sodimmDesk, "RAM-Takt")?.St, "info", "an SO-DIMM counts as a laptop");
var unknown = RamRows(new[] { M("DIMM1", cfg: 2666, part: "M378A1K43CB2-CTD"), M("DIMM2", cfg: 2666, part: "M378A1K43CB2-CTD") }, false);
Eq(Find(unknown, "RAM-Kanäle"), null, "unknown labels: no channel row");
var ux = Find(unknown, "RAM-Takt")!;
Eq(ux.St, "info", "unknown part numbers: a note");
Check(ux.Value.StartsWith("2666 MT/s DDR4") && ux.Value.Contains("Teilenummer"), "the note says the speed and why there is no claim");
Eq(ux.Fix, "hw:ram:info", "the note opens the module list");
var lower = RamRows(new[] { M("ChannelA-DIMM0", cfg: 3466, part: "F4-3600C16D-32GTZNC"), M("ChannelB-DIMM0", cfg: 3466, part: "F4-3600C16D-32GTZNC") }, false);
Eq(Find(lower, "RAM-Takt")?.St, "info", "set lower by hand: a note");
Check(Find(lower, "RAM-Takt")!.Value.Contains("3466") && Find(lower, "RAM-Takt")!.Value.Contains("3600"), "the note names both speeds");
var ddr5 = RamRows(new[] { D5(4800, "CMK32GX5M2B6000C36", "Controller0-ChannelA-DIMM1"), D5(4800, "CMK32GX5M2B6000C36", "Controller1-ChannelA-DIMM1") }, false);
Eq(Find(ddr5, "RAM-Kanäle")?.St, "ok", "Intel DDR5 dual channel");
Eq(Find(ddr5, "RAM-Takt")?.Value, "XMP/EXPO wahrscheinlich aus: 4800 statt 6000 MT/s", "DDR5: XMP or EXPO (AMD kits often have only EXPO)");
Eq(RamRows(null, false).Count, 0, "no modules read: no rows");
Eq(RamRows(Array.Empty<Module>(), false).Count, 0, "no modules: no rows");
Eq(RamRows(new[] { M("DIMM 0", type: 0, cfg: 0) }, false).Count, 0, "a virtual machine: no rows");
Eq(RamRows(Enumerable.Range(0, 4).Select(i => M($"Controller0-Channel{(char)('A' + i)}", gb: 4, type: 35, ff: 0, cfg: 6400)).ToList(), true).Count, 0, "soldered LPDDR5: no rows");
foreach (var rows in new[] { single, same, dual, quad, lap, unknown, lower, ddr5 })
    foreach (var r in rows)
    {
        Check(r.St is "ok" or "warn" or "info", $"{r.Name}: a known state");
        Check(r.St != "ok" || r.Fix == null, $"{r.Name}: a fine row has no button");
        Check(!r.Value.Contains("  ") && r.Value.Trim() == r.Value, $"{r.Name}: clean text");
        Check(r.St == "info" || r.Value.Length <= 50, $"{r.Name}: a one-line value stays short ({r.Value.Length})");
    }

// =====================================================================
//  makers and text
// =====================================================================
Eq(Maker("029E", "CMK16GX4M2B3200C16"), "Corsair", "Corsair by part number");
Eq(Maker("04CD", "F4-3600C16-8GTZNC"), "G.Skill", "G.Skill by part number");
Eq(Maker("Kingston", "KF3200C16D4/8GX"), "Kingston", "Kingston");
Eq(Maker("0198", "9905734-102.A00G"), "", "Kingston's JEDEC code and an internal number: nothing");
Eq(Maker("859B", "BL8G32C16U4B.M8FE1"), "Crucial", "Crucial by part number");
Eq(Maker("Samsung", "M378A1K43CB2-CTD"), "Samsung", "Samsung");
Eq(Maker("80CE000080CE", "M471A2K43DB1-CWE"), "Samsung", "Samsung SO-DIMM by part number");
Eq(Maker("80AD000080AD", "HMA81GU6CJR8N-XN"), "SK hynix", "SK hynix by part number");
Eq(Maker("802C0000802C", "MTA8ATF1G64AZ-2G6E1"), "Micron", "Micron by part number");
Eq(Maker("TEAMGROUP", "TEAMGROUP-UD4-3600"), "TeamGroup", "TeamGroup module number");
Eq(Maker("04EF", "TF3D416G3200HC16FDC01"), "TeamGroup", "TeamGroup kit number");
Eq(Maker("Patriot Memory", "PVS416G360C8"), "Patriot", "Patriot");
Eq(Maker("04CB", "AX4U320016G16A-DT50"), "ADATA", "ADATA XPG");
Eq(Maker("04CB", ""), "", "a JEDEC code alone: nothing");
Eq(Maker("Unknown", "Unknown"), "", "unknown");
Eq(Maker("Undefined", null), "", "undefined");
Eq(Maker("Ramaxel Technology", "RMSA3260MB78HAF-2666"), "Ramaxel Technology", "a real name from WMI");
Eq(Clean("  A\u0000B\tC  ", 10), "ABC", "control characters out");
Eq(Clean(null, 5), "", "null");
Eq(Clean("abcdefgh", 5), "abcde", "cut");

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
