using System.Globalization;
using System.IO;

namespace OrbitTweak.Core;

// =====================================================================
//  Grafik & Farben › Spiel-Grafik, the two cards that are no GTA settings: the NVIDIA driver's frame rate limit for
//  FiveM's game processes and FiveM's texture budget. Plain logic without Windows calls, so it can be checked anywhere
//  (tests/ClipLogic.Tests); the Windows side is Core/FpsTex.cs.
// =====================================================================

/// <summary>
/// FPS-Limit für FiveM. FiveM has no frame limiter of its own; NVIDIA's per-application "Max Frame Rate" in a driver
/// profile caps the game's processes, written through NVAPI's driver settings (DRS) like the NVIDIA Control Panel does.
/// </summary>
internal static class FpsLimitLogic
{
    public const int Min = 30, Max = 500;

    /// <summary>The driver profile OrbitTweak makes for game exes that belong to no profile yet ("Aus" deletes it again).</summary>
    public const string OwnProfile = "OrbitTweak FiveM";

    /// <summary>
    /// FRL_FPS_ID from NVIDIA/nvapi NvApiDriverSettings.h ("Frame Rate Limiter", 0 = off, up to 0x3FF FPS). It is the
    /// setting behind the Control Panel's "Max Frame Rate": NVIDIA Profile Inspector lists 0x10835002 as "Frame Rate
    /// Limiter V3" with 441.87 as the first driver (the driver that added "Max Frame Rate"), and its 0x1083500A "Frame
    /// Rate Limiter For Nvcpl" keeps "the previous slider value when the FRL_FPS is set to Disabled". The older ids are
    /// 0x10834FEE (PS_FRAMERATE_LIMITER: bit 31 on, low byte FPS) and 0x10834FFF ("Frame Rate Limiter 2 Control",
    /// deprecated, up to driver 511.78).
    /// </summary>
    public const uint FrlFpsId = 0x10835002;

    // ---------- NVAPI DRS (NVIDIA/nvapi: nvapi.h, nvapi_interface.h, nvapi_lite_common.h) ----------
    public const uint IdInitialize = 0x0150E828, IdCreateSession = 0x0694D52E, IdDestroySession = 0xDAD9CFF8,
        IdLoadSettings = 0x375DBD6B, IdSaveSettings = 0xFCBC7E14, IdCreateProfile = 0xCC176068, IdDeleteProfile = 0x17093206,
        IdGetProfileInfo = 0x61CD6FD6, IdFindProfileByName = 0x7E4A9A0B, IdCreateApplication = 0x4347A9DE,
        IdFindApplicationByName = 0xEEE566B2, IdSetSetting = 0x577DD202, IdGetSetting = 0x73BF8338, IdDeleteProfileSetting = 0xE4A26362,
        IdEnumApplications = 0x7FA2173A;

    public const int OK = 0, EndEnumeration = -7, SettingNotFound = -160, ProfileNotFound = -163, ExecutableNotFound = -166,
        ExecutableAlreadyInUse = -167, InvalidUserPrivilege = -137, PathAmbiguous = -182;

    /// <summary>NvAPI_UnicodeString: NvU16[2048].</summary>
    public const int StrBytes = 2048 * 2;
    /// <summary>NVDRS_BINARY_SETTING: NvU32 valueLength + NvU8[4096]; the largest member of the value unions.</summary>
    private const int BinBytes = 4 + 4096;

    /// <summary>NVDRS_SETTING_V1 (pack 4): version, settingName, settingId, settingType, settingLocation,
    /// isCurrentPredefined, isPredefinedValid, then the predefined and the current value union.</summary>
    public const int SetName = 4, SetId = SetName + StrBytes, SetType = SetId + 4, SetLocation = SetType + 4,
        SetIsCurPredefined = SetLocation + 4, SetPredefValid = SetIsCurPredefined + 4, SetPredefined = SetPredefValid + 4,
        SetCurrent = SetPredefined + BinBytes, SettingSize = SetCurrent + BinBytes;

    /// <summary>NVDRS_APPLICATION_V4: version, isPredefined, appName, userFriendlyName, launcher, fileInFolder,
    /// the isMetro/isCommandLine bits, commandLine.</summary>
    public const int AppName = 8, AppFriendly = AppName + StrBytes, AppSize = 8 + 4 * StrBytes + 4 + StrBytes;

    /// <summary>NVDRS_PROFILE_V1: version, profileName, gpuSupport (NVDRS_GPU_SUPPORT, 32 one-bit fields in one NvU32),
    /// isPredefined, numOfApps ("Total number of applications that belong to this profile. Read-only"), numOfSettings.</summary>
    public const int ProfName = 4, ProfNumOfApps = ProfName + StrBytes + 2 * 4, ProfileSize = 4 + StrBytes + 4 * 4;

    /// <summary>MAKE_NVAPI_VERSION(struct, ver): the struct's size with the version in the high word.</summary>
    public static uint Ver(int size, int ver) => (uint)size | ((uint)ver << 16);
    public static readonly uint SettingVer = Ver(SettingSize, 1), AppVer = Ver(AppSize, 4), ProfileVer = Ver(ProfileSize, 1);

    // ---------- values ----------
    /// <summary>0 = off, else 30 to 500 FPS.</summary>
    public static bool Valid(int limit) => limit == 0 || limit is >= Min and <= Max;

    /// <summary>Suggestion for G-Sync/FreeSync: 3 below the refresh rate keeps the game inside the VRR range (Blur Busters'
    /// rule of thumb). 0 = none (refresh rate unknown or too low).</summary>
    public static int Suggestion(int hz) => hz - 3 >= Min ? Math.Min(hz - 3, Max) : 0;

    // ---------- which exes ----------
    /// <summary>True for FiveM's game process exes (FiveM_b3095_GTAProcess.exe, FiveM_GTAProcess.exe); FiveM.exe and the other
    /// subprocesses (browser, Steam child) do not draw the game.</summary>
    public static bool IsGameExe(string name) =>
        name.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase) && name.EndsWith("_GTAProcess.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The game exe names: the files FiveM keeps in cache\subprocess (one per game build, see FiveM.GameExecutables)
    /// and the game process running right now (its process name, without ".exe"). Each name once, sorted.
    /// </summary>
    public static List<string> GameExes(IEnumerable<string> paths, string? runningProcess)
    {
        var names = paths.Select(p => Path.GetFileName(p)).Where(IsGameExe);
        if (!string.IsNullOrEmpty(runningProcess))
        {
            var r = runningProcess.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? runningProcess : runningProcess + ".exe";
            if (IsGameExe(r)) names = names.Append(r);
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A name the driver profiles were not checked for yet (a new game build), or not checked at all since the
    /// app started (<paramref name="known"/> null). Nothing to check without any exe.</summary>
    public static bool NeedsSync(IReadOnlyCollection<string> now, IReadOnlyCollection<string>? known) =>
        now.Count > 0 && (known == null || now.Any(n => !known.Contains(n, StringComparer.OrdinalIgnoreCase)));

    /// <summary>What the limit is written to.</summary>
    /// <param name="AddToOwn">Exes in no profile: they go into <see cref="OwnProfile"/>.</param>
    /// <param name="Foreign">Profiles of someone else (NVIDIA's own, the user's, another tool's) that an exe already belongs
    /// to: only their limit changes, with a backup.</param>
    /// <param name="Own">The own profile gets the limit (an exe is in it or goes into it).</param>
    public record Plan(List<string> AddToOwn, List<string> Foreign, bool Own);

    /// <summary>Decides per exe from the profile the driver applies to it (null = none).</summary>
    public static Plan Decide(IEnumerable<(string exe, string? profile)> found)
    {
        var add = new List<string>(); var foreign = new List<string>(); bool own = false;
        foreach (var (exe, profile) in found)
        {
            if (profile == null) { if (!add.Contains(exe, StringComparer.OrdinalIgnoreCase)) add.Add(exe); own = true; }
            else if (profile.Equals(OwnProfile, StringComparison.OrdinalIgnoreCase)) own = true;
            else if (!foreign.Contains(profile, StringComparer.OrdinalIgnoreCase)) foreign.Add(profile);
        }
        return new Plan(add, foreign, own);
    }

    /// <summary>
    /// What the FiveM probe does on its own (Upkeep, on the first look after the app started and for each new game build):
    /// it only adds what is new and never writes a profile again that OrbitTweak already set, so a value changed in the
    /// NVIDIA Control Panel stays. Exes in no profile go into the own profile; the own profile gets the limit only when
    /// it is made right now (<see cref="Plan.Own"/>), else the new exe takes the value the profile has. Of someone else's
    /// profiles only those without a backup entry yet (<paramref name="withEntry"/>) get the limit.
    /// </summary>
    public static Plan UpkeepPlan(IEnumerable<(string exe, string? profile)> found, IEnumerable<string> withEntry, bool ownExists)
    {
        var p = Decide(found);
        var set = new HashSet<string>(withEntry, StringComparer.OrdinalIgnoreCase);
        return new Plan(p.AddToOwn, p.Foreign.Where(f => !set.Contains(f)).ToList(), p.AddToOwn.Count > 0 && !ownExists);
    }

    /// <summary>"FiveM b3095": the name the NVIDIA Control Panel shows for an exe in the own profile.</summary>
    public static string FriendlyName(string exe)
    {
        var build = Path.GetFileNameWithoutExtension(exe).Split('_').FirstOrDefault(x => x.Length > 1 && x[0] == 'b' && char.IsDigit(x[1]));
        return build == null ? "FiveM" : "FiveM " + build;
    }

    // ---------- what else is in the profile ----------
    /// <summary>FiveM's own exes (FiveM.exe and its FiveM_… subprocesses): a limit on them is still a limit only for FiveM.</summary>
    public static bool IsFiveMApp(string name) =>
        name.Equals("FiveM.exe", StringComparison.OrdinalIgnoreCase) || name.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The applications of a driver profile that are not FiveM's (NvAPI_DRS_EnumApplications' appName, which can be a
    /// full path): each file name once, in the driver's order. These get the limit too, e.g. GTA5.exe in NVIDIA's GTA V profile.
    /// </summary>
    public static List<string> OtherApps(IEnumerable<string> appNames)
    {
        var r = new List<string>();
        foreach (var a in appNames)
        {
            var n = a[(a.LastIndexOfAny(new[] { '\\', '/' }) + 1)..].Trim();
            if (n.Length > 0 && !IsFiveMApp(n) && !r.Contains(n, StringComparer.OrdinalIgnoreCase)) r.Add(n);
        }
        return r;
    }

    /// <summary>Without the list of applications (an old driver without EnumApplications): NVDRS_PROFILE's numOfApps tells
    /// only that the FiveM exe is alone (0 others); with more apps it is unknown which they are (null).</summary>
    public static int? OthersFromCount(uint numOfApps) => numOfApps <= 1 ? 0 : null;

    // ---------- backup of a profile of someone else ----------
    // One registry value per such profile: "<backup>|<written>", the value before OrbitTweak's first change and the value
    // OrbitTweak wrote there last. An entry without '|' (2.29.0 before release) means written = the stored limit.
    /// <summary>
    /// What a foreign profile had before OrbitTweak set its limit: its own value, or "-" when it had none (the setting
    /// was not found, or only inherited from the global or base profile) or NVIDIA's predefined value (deleting the
    /// setting brings that back, NvAPI_DRS_DeleteProfileSetting "sets it back to predefined value").
    /// </summary>
    public static string Backup(bool found, uint location, bool currentPredefined, uint value) =>
        !found || location != 0 || currentPredefined ? "-" : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The value to set back; null = delete the profile's own setting.</summary>
    public static uint? RestoreValue(string? backup) =>
        uint.TryParse(backup, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>The limit a profile has itself (0 = none of its own; settingLocation 0 = NVDRS_CURRENT_PROFILE_LOCATION).</summary>
    public static int ProfileLimit(bool found, uint location, uint value) => found && location == 0 ? (int)Math.Min(value, (uint)int.MaxValue) : 0;

    /// <summary>"Aus" puts a foreign profile back only while it still has the limit OrbitTweak set: a value the user
    /// changed since stays. When OrbitTweak's own record of its limit is gone (<paramref name="set"/> 0) the backup alone
    /// counts.</summary>
    public static bool ShouldRestore(int live, int set) => set == 0 || live == set;

    /// <summary>The registry value for a profile of someone else: its backup and what OrbitTweak wrote there.</summary>
    public static string Entry(string backup, int written) => backup + "|" + written.ToString(CultureInfo.InvariantCulture);

    /// <summary>Backup and written value of an entry; an entry without '|' was written with the stored limit
    /// (<paramref name="stored"/>), one without a readable written value too.</summary>
    public static (string backup, int written) ParseEntry(string? entry, int stored)
    {
        if (entry == null) return ("-", stored);
        int i = entry.LastIndexOf('|');
        if (i < 0) return (entry, stored);
        return (entry[..i], int.TryParse(entry[(i + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var w) ? w : stored);
    }

    /// <summary>A new limit set on the card also goes to a profile OrbitTweak set before whose exe is no longer in FiveM's
    /// cache (cleaned, another build), as long as that profile still has OrbitTweak's value: so "Aus" later finds it as
    /// OrbitTweak left it. A profile with a value changed since is not OrbitTweak's to change.</summary>
    public static bool Follows(int live, int written) => written > 0 && live == written;

    public enum OffStep { Restore, Drop, Keep }

    /// <summary>
    /// What "Aus" does with a profile of someone else that has an entry: <see cref="OffStep.Restore"/> its value from
    /// before while it still has what OrbitTweak wrote there (then the entry goes), <see cref="OffStep.Drop"/> only the
    /// entry when the profile is gone or already has its value from before, <see cref="OffStep.Keep"/> it as it is, entry
    /// included, when its limit was changed outside OrbitTweak since (the card and the toast say which and why).
    /// </summary>
    public static OffStep OffDecision(bool exists, int live, string? entry, int stored)
    {
        if (!exists) return OffStep.Drop;
        var (backup, written) = ParseEntry(entry, stored);
        if (ShouldRestore(live, written)) return OffStep.Restore;
        return live == (int)Math.Min(RestoreValue(backup) ?? 0, (uint)int.MaxValue) ? OffStep.Drop : OffStep.Keep;
    }
}

/// <summary>
/// Texturbudget: FiveM's archived convar vid_budgetScale (citizenfx/fivem
/// code/components/gta-streaming-five/src/PatchExtendedBudgeting.cpp), the same value as the slider "Extended Texture
/// Budget" in FiveM's graphics settings. FiveM keeps it in fxd:/fivem.cfg (%AppData%\CitizenFX\fivem.cfg).
/// </summary>
internal static class TexBudgetLogic
{
    public const string Var = "vid_budgetScale";

    /// <summary>The slider's end. It shows percent in steps of 5: "a 40% budget slider (vid_budgetScale being 8)" (nta,
    /// Cfx.re staff, forum.cfx.re/t/4923060), so 100 % is 20.</summary>
    public const int Max = 20;

    /// <summary>GetBudgetMultiplier(): value / 12 + 1. 0 is FiveM's default, 12 doubles the texture budget.</summary>
    public static double Multiplier(int value) => value / 12.0 + 1;
    public static int Percent(int value) => value * 5;

    /// <summary>
    /// The texture cache cap for "very high" textures: nta gives about 6575 MB at 8 (×1,67), so about 3945 MB × multiplier.
    /// An estimate from staff numbers, not read from the game.
    /// </summary>
    public const int BaseCapMb = 3945;
    public static int CapMb(int value) => BaseCapMb * (12 + value) / 12;

    /// <summary>
    /// The suggestion for a graphics card: the highest step whose texture cap stays within half of the dedicated VRAM.
    /// The other half is for everything else on the card: render targets and models of the game, FiveM's browser UI,
    /// Windows, Discord and overlays (nta: NVIDIA Broadcast alone takes 1 to 2 GB). Going past the real VRAM makes the
    /// game stutter. That gives 0 up to 8 GB (staff: never above 0 % on 4 GB, "worst case 20 %" unless 12 or 24 GB),
    /// 15 % on 10 GB, 30 % on 12 GB, 60 % (double) on 16 GB and 100 % from 24 GB on. 0 when the VRAM is unknown.
    /// </summary>
    public static int Suggestion(long vramMb)
    {
        for (int v = Max; v > 0; v--)
            if ((long)BaseCapMb * (12 + v) <= 6L * vramMb) return v;   // CapMb(v) <= vramMb / 2
        return 0;
    }

    public static bool Valid(int value) => value is >= 0 and <= Max;

    // ---------- fivem.cfg ----------
    // FiveM writes the file itself (code/client/citicore/console/Console.cpp, Context::SaveConfiguration): a first line
    // "// generated by CitizenFX", the key bindings, then one line per archived convar as  seta "name" "value"  with
    // \r\n line ends (Console.Variables.cpp). It rewrites the whole file from what it has in memory on the first frame
    // of the game and whenever an archived convar changes (SaveConfigurationIfNeeded), so it is only written while
    // FiveM is closed. On start the game runs the file like console input: the last line that sets a convar wins.

    /// <summary>The words of a console line like FiveM reads them: "quoted" words without the quotes, // starts a comment.</summary>
    public static List<string> Words(string line)
    {
        var words = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length || (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')) break;
            if (line[i] == '"')
            {
                int end = line.IndexOf('"', i + 1);
                if (end < 0) end = line.Length;
                words.Add(line[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                int s = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '"') i++;
                words.Add(line[s..i]);
            }
        }
        return words;
    }

    /// <summary>A line that sets the convar (seta or set), null otherwise; else its value word ("" when it has none).</summary>
    public static string? SetsVar(string line)
    {
        var w = Words(line);
        if (w.Count < 2 || !(w[0].Equals("seta", StringComparison.OrdinalIgnoreCase) || w[0].Equals("set", StringComparison.OrdinalIgnoreCase))
            || !w[1].Equals(Var, StringComparison.OrdinalIgnoreCase)) return null;
        return w.Count > 2 ? w[2] : "";
    }

    /// <summary>The line FiveM itself writes for the value.</summary>
    public static string Line(string raw) => $"seta \"{Var}\" \"{raw}\"";

    /// <summary>The number in a value word; 0 (FiveM's default) when it is none.</summary>
    public static int Parse(string? raw) => int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>Whether the file sets the convar, its value word and the number FiveM uses (0 when there is no such line).</summary>
    public static (bool present, string? raw, int value) Read(string text)
    {
        var lines = Split(text);
        int i = Last(lines);
        if (i < 0) return (false, null, 0);
        var raw = SetsVar(lines[i].body);
        return (true, raw, Parse(raw));
    }

    /// <summary>
    /// The file with the convar set to <paramref name="raw"/>: the last line that sets it gets the new value (earlier ones
    /// have no effect and stay), a file without one gets the line at the end. raw null takes that line out again (the
    /// value was not in the file before). Every other line and every line end stays exactly as it was.
    /// </summary>
    public static string Write(string text, string? raw)
    {
        var lines = Split(text);
        int i = Last(lines);
        if (i >= 0)
        {
            if (raw == null) lines.RemoveAt(i);
            else
            {
                var body = lines[i].body;
                lines[i] = (body[..(body.Length - body.TrimStart().Length)] + Line(raw), lines[i].end);
            }
            return string.Concat(lines.Select(l => l.body + l.end));
        }
        if (raw == null) return text;
        // FiveM's own line end; a last line without one gets it first
        var nl = lines.Select(l => l.end).FirstOrDefault(e => e.Length > 0) ?? "\r\n";
        var sb = new System.Text.StringBuilder(text);
        if (lines.Count > 0 && lines[^1].end.Length == 0) sb.Append(nl);
        return sb.Append(Line(raw)).Append(nl).ToString();
    }

    private static int Last(List<(string body, string end)> lines)
    {
        for (int i = lines.Count - 1; i >= 0; i--) if (SetsVar(lines[i].body) != null) return i;
        return -1;
    }

    /// <summary>The lines with their own line end ("\r\n", "\n", "\r" or "" for the last one).</summary>
    private static List<(string body, string end)> Split(string text)
    {
        var lines = new List<(string, string)>();
        int i = 0;
        while (i < text.Length)
        {
            int j = text.IndexOfAny(new[] { '\r', '\n' }, i);
            if (j < 0) { lines.Add((text[i..], "")); break; }
            var end = text[j] == '\r' && j + 1 < text.Length && text[j + 1] == '\n' ? "\r\n" : text[j].ToString();
            lines.Add((text[i..j], end));
            i = j + end.Length;
        }
        return lines;
    }
}
