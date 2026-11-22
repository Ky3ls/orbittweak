using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

// =====================================================================
//  ABSTURZ-ANALYSE (2.31): what FiveM writes when it crashes, read back and explained.
//
//  Researched in the citizenfx/fivem source (master, November 2026) and checked against real CitizenFX logs that players
//  posted on forum.cfx.re:
//
//  FiveM.app\crashes\   code/client/launcher/MiniDump.cpp: a Breakpad CrashGenerationServer ("DumpServer" process) writes
//                       one minidump per crash as <guid>.dmp; with [Game] EnableFullMemoryDump=1 in CitizenFX.ini also
//                       <guid>-full.dmp, and on game builds >= 2545 GTA's own crash log as <guid>.dmp.gamelog
//                       (gta-core-five/GameCrashLogHandler.cpp). PruneCrashDumps() keeps the newest 10 .dmp files and
//                       deletes the -full.dmp and .gamelog of older ones. There is no json next to the dumps: the crash
//                       data FiveM sends to crash-ingress.fivem.net is built in memory.
//  FiveM.app\logs\      code/client/launcher/Console.Logging.cpp: CitizenFX_log_<YYYY-MM-DD>T<HHMMSS>.log, named after the
//                       session start in UTC (GetSystemTime), one file per session, shared by all FiveM processes. Every
//                       line is "[%10lld] [%14s] %20s/ %s": milliseconds since the session started, the process name
//                       (the exe name after its first "_", cut to 14: "b3258_GTAProce" = FiveM_b3258_GTAProcess.exe,
//                       "    DumpServer", "    GTAProcess" on builds without a number in the name, "         FiveM"), the
//                       thread name or id (20 wide) and the text. Logs from 2019 and older have only "[%10lld] %s". The
//                       first line of a session is "--- BEGIN LOGGING AT %c ---" with fmt::gmtime, so also UTC. The
//                       ticks count from the moment in the file name (TickCountData.h: GetTickCount64 + GetSystemTime).
//                       InitLogging deletes every log whose name is more than 7 days old when FiveM starts, so the logs
//                       only reach back a week while the dumps (newest 10) can be older.
//  The crash itself     MiniDump.cpp, after the dump is written: trace("Process crash captured. Crash dialog content:\n%s\n
//                       %s\n", headline, content) from the DumpServer process. Each line of the dialog becomes its own
//                       log line with the same process, thread and (nearly) the same tick. The headline is
//                       - the crash signature: "GTA5_b3258.exe!sub_141322F74 (0x1a)" (symbolicated, the game exe is
//                         renamed to GTA5_b<build>.exe by UnblameCrash/GetGameName), "nvwgf2umx.dll+A3C0F1" (module+offset),
//                         "rtsshooks64.dll_unloaded+1F20" (a module that was unloaded), "GTA5.exe!…" / "GTA5+654BB2D" (old);
//                         the content then reads "An error at <signature> caused FiveM to stop working. A crash report is
//                         being uploaded to the FiveM developers.", "Legacy crash hash: word-word-word" (only when the
//                         faulting module is an .exe) and "Stack trace:" with up to 7 frames indented by two spaces;
//                       - "FiveM crashed... but we're on it!" when citizen/crash-data.json knows the legacy hash; its
//                         English explanation is then the first content line;
//                       - "Exception, unhandled!" with "An unhandled exception (of type X) caused …" and optionally
//                         "Exception details: …" (C++ exceptions, e.g. std::bad_alloc);
//                       - "Early-exit trap" / "Game integrity check failed" (FiveM's integrity check);
//                       - a blame from OverloadCrashData: "NVIDIA GPU drivers encountered an error" (nvwgf), "NVIDIA Game
//                         Filters (nvppex.dll) …", "Comodo Internet Security …" (guard64), "a third-party game plugin
//                         (x.asi) …", "AMD GPU drivers …" (atidxx/amdxx);
//                       - the first line of a FatalError's text, read from data\cache\error-pickup (deleted when FiveM
//                         exits, so only the log keeps it): "RAGE error: ERR_GFX_D3D_INIT" + "A game error (at …) caused
//                         the game to stop working." + the description from GTA's errorcodes/american.txt
//                         (gta-core-rdr3/ErrorHandler.cpp, also built into gta-core-five); "DirectX encountered an
//                         unrecoverable error: DXGI_ERROR_DEVICE_REMOVED - …" + "GetDeviceRemovedReason returned
//                         DXGI_ERROR_DEVICE_HUNG - …", with " (R)", " (E)", " (R+E)", " (M)" after "error" when ReShade,
//                         ENBSeries or another graphics mod (d3d11.dll/dxgi.dll/d3d10.dll) sits in the GTA V folder
//                         (rage-graphics-five/RenderHooks.cpp); "FiveM has stopped responding (script deadloop|DirectX
//                         query|NVIDIA drivers)" (citizen-devtools/ResourceTimeWarnings.cpp); "<Pool> Pool Full, Size == N"
//                         (gta-core-five/PoolManagement.cpp); Rockstar launcher errors such as "127. Could not locate the
//                         Rockstar Games Launcher Process! …" or "… (ExitCode = 506)". Links appear as <A HREF="…">…</A>;
//                       - "O\u0448\u0438\u0431\u043a\u0430 (Error)" for a disconnect (crashometry kill_network_msg), the
//                         content is then the disconnect message.
//                       The same process later logs "Crash report service returned si-<hex>" (the report ID Cfx.re
//                       support asks for) or "Error uploading crash: HTTP …" – but FiveM uploads only about one crash in
//                       20 (shouldUpload = dist(rd) < 5), so most crashes have no report ID. FatalErrors are also logged
//                       by the failing process as "Error: <text>".
//  Translated dialogs   MiniDump.cpp runs the dialog's own sentences through gettext (shared/CfxLocale.h, Boost.Locale with
//                       data/client/citizen/locales/<lang>/cfx.mo, language from HKCU\Software\CitizenFX "Global Locale"
//                       that FiveM's menu sets). With FiveM in German the log says "Ein Fehler bei <sig> hat verursacht,
//                       dass FiveM nicht mehr funktioniert. Ein Absturzbericht wird an die FiveM Entwickler
//                       hochgeladen.", "Alter crash hash: …", "FiveM ist abgestürzt... aber wir sind dran!" and
//                       "Fehlerinformationen: …"; "Stack trace:" stays English in German, Hungarian translates even that.
//                       Not translated: signatures and frames, "Exception, unhandled!", "An unhandled exception (of type
//                       X)", every FatalError text, the blame lines. So the rules below read English and German by
//                       their words and every other language by shape: the "caused" sentence is the line that names
//                       FiveM twice (all 20 translations keep both %s), a crash hash is three lowercase words with
//                       dashes after a colon, a stack is the run of frame-shaped lines after a line ending in ":".
//  Crashometry          data\cache\crashometry: records of [uint32 key length][uint32 value length][key][value], appended
//                       by AddCrashometry (shared/Utils.Win32.cpp) and deleted by the launcher at every start
//                       (launcher/Main.cpp), so it only belongs to the newest session. Keys used here: last_server_url,
//                       last_server (net/NetLibrary.cpp). RockstarId is also in there and is never read out. An empty
//                       value is written as a key with length 0 for the value (FiveM's own reader then loses its place;
//                       ours skips the record).
//  FiveM's own blame    the DLL names FiveM itself blames (nvwgf, nvppex, guard64, .asi, atidxx/amdxx) and the hooks it
//                       keeps out of the game (citicore/FileMapping.Win32.cpp blocks loading discordhook64, nvspcap –
//                       "high correlation with ERR_GFX_D3D_INIT" –, nvppex, Overwolf on RedM …) are the basis of the
//                       module table below; a crash in a blocked one is rare today but still clear.
//  Not fatal            a crash of a libcef.dll or Chrome-OOM subprocess, or nvwgf2 in Chrome's GPU process, does not end
//                       the game (MiniDump.cpp shouldTerminate = false) but is logged with the same dialog.
//
//  This file holds only the pure rules (no Windows calls); Core/CrashScan.cs reads the files, tests/CrashScanLogic.Tests
//  checks the rules with samples in exactly these formats.
// =====================================================================

/// <summary>One line of a CitizenFX log: tick (ms since the session started), process and thread tag (trimmed, empty in
/// the old format) and the text.</summary>
public sealed record CrashLogLine(long Tick, string Proc, string Thread, string Text);

/// <summary>The crash dialog as the DumpServer logged it: headline first, then the content lines.</summary>
public sealed class CrashBlock
{
    public long Tick { get; init; }
    public string Proc { get; init; } = "";
    public string Thread { get; init; } = "";
    public List<string> Lines { get; } = new();
    public string? ReportId { get; set; }
}

/// <summary>What one log file says about its session and the crashes in it.</summary>
public sealed class CrashLog
{
    public DateTime? StartUtc { get; set; }
    public long StartTick { get; set; }
    public string? Build { get; set; }
    public List<CrashBlock> Blocks { get; } = new();

    /// <summary>When a block was logged: session start + its tick, in UTC (null when the start is unknown).</summary>
    public DateTime? TimeOf(CrashBlock b) => StartUtc is { } s && b.Tick >= StartTick ? s.AddMilliseconds(b.Tick - StartTick) : StartUtc;
}

/// <summary>One crash, from the log, from a dump, or both.</summary>
public sealed class CrashReport
{
    public DateTime? TimeUtc { get; set; }
    public string? Build { get; set; }
    public string? Server { get; set; }
    /// <summary>The dialog's headline as FiveM showed it (null for a dump without log lines).</summary>
    public string? Title { get; set; }
    public string? Signature { get; set; }
    public string? Module { get; set; }
    /// <summary>An error code from the text: ERR_GFX_D3D_INIT, DXGI_ERROR_DEVICE_HUNG …</summary>
    public string? Code { get; set; }
    /// <summary>The error text in FiveM's words (English), links removed; null for a plain crash at a signature.</summary>
    public string? Text { get; set; }
    public string? LegacyHash { get; set; }
    public string? ReportId { get; set; }
    public string? ExceptionType { get; set; }
    /// <summary>FiveM's "FiveM crashed... but we're on it!" (in any language): citizen/crash-data.json knows this crash.</summary>
    public bool Known { get; set; }
    public List<string> Stack { get; } = new();
    /// <summary>File name of the .dmp in FiveM.app\crashes, when one belongs to it.</summary>
    public string? Dump { get; set; }
    /// <summary>File name of the log it came from.</summary>
    public string? Log { get; set; }
    public bool FromLog => Title != null;
}

/// <summary>A fix the page can offer: Kind "health" (health.fix with Arg), "tweakoff" (tweak.set off), "action" (Arg "fmcache":
/// fivem.cache), "page" (open a page, Arg "page" or "page#tab-or-selector"), "host" (crash.fix with Arg).</summary>
public sealed record CrashFix(string Id, string Label, string Kind, string Arg);

/// <summary>The likely cause in plain German. Sure: "klar" (FiveM or the module says it), "wahrscheinlich", "unklar".</summary>
public sealed record CrashCause(string Key, string Title, string Why, string[] Steps, string Sure, string Icon, CrashFix[] Fixes);

/// <summary>What the PC looks like right now, for which fixes still make sense (and the time now, for how old a crash is).</summary>
public sealed record CrashPc(string? GpuVendor = null, bool PagefileOk = true, bool HagsOn = false, bool TexRaised = false, bool HasPlugins = false, DateTime? NowUtc = null);

public static class CrashScanLogic
{
    public const string CaptureMarker = "Process crash captured. Crash dialog content:";
    public const int MaxCrashes = 10;
    /// <summary>How far a dump's file time may be from the logged dialog: symbolication waits up to 10 s, the report
    /// upload up to 20 s; a minute or two of slack covers slow disks.</summary>
    public static readonly TimeSpan DumpMatch = TimeSpan.FromMinutes(3);

    // ---------- log lines ----------

    private static readonly Regex NewLine = new(@"^\[\s*(\d{1,19})\] \[(.{14})\] (.{20})/ ?(.*)$", RegexOptions.Compiled);
    private static readonly Regex OldLine = new(@"^\[\s*(\d{1,19})\] ?(.*)$", RegexOptions.Compiled);

    public static CrashLogLine? ParseLine(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        raw = raw.TrimEnd('\r', '\n');
        var m = NewLine.Match(raw);
        if (m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var t))
            return new(t, m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim(), m.Groups[4].Value);
        m = OldLine.Match(raw);
        if (m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out t))
            return new(t, "", "", m.Groups[2].Value);
        return null;
    }

    private static readonly Regex LogName = new(@"^CitizenFX_log_(\d{4})-(\d{2})-(\d{2})T(\d{2})(\d{2})(\d{2})\.log$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The session start (UTC) from "CitizenFX_log_2026-11-21T193015.log".</summary>
    public static DateTime? StartFromName(string? fileName)
    {
        var m = LogName.Match(fileName ?? "");
        if (!m.Success) return null;
        int G(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        try { return new DateTime(G(1), G(2), G(3), G(4), G(5), G(6), DateTimeKind.Utc); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static readonly Regex Begin = new(@"^--- BEGIN LOGGING AT \w{3} (\w{3}) +(\d{1,2}) (\d{2}):(\d{2}):(\d{2}) (\d{4}) ---", RegexOptions.Compiled);
    private static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    /// <summary>"--- BEGIN LOGGING AT Tue Sep  1 05:12:11 2026 ---" (fmt %c of gmtime, so UTC).</summary>
    public static DateTime? ParseBegin(string? text)
    {
        var m = Begin.Match(text ?? "");
        if (!m.Success) return null;
        int mon = Array.IndexOf(Months, m.Groups[1].Value) + 1;
        if (mon == 0) return null;
        int G(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        try { return new DateTime(G(6), mon, G(2), G(3), G(4), G(5), DateTimeKind.Utc); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static readonly Regex BuildTag = new(@"(?:^|_)b(\d{4,5})_", RegexOptions.Compiled);
    private static readonly Regex BuildExe = new(@"GTA5_b(\d{4,5})\.exe", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReportLine = new(@"^Crash report service returned (\S{4,80})\s*$", RegexOptions.Compiled);

    /// <summary>Reads one log: start, build and every crash dialog with its report ID. Lines of other processes that land
    /// between the dialog's lines (all FiveM processes write into the same file) are skipped.</summary>
    public static CrashLog ScanLog(IEnumerable<string> lines, DateTime? startFromName = null)
    {
        var log = new CrashLog { StartUtc = startFromName };
        bool begun = false;
        CrashBlock? cur = null;       // the dialog being read
        CrashBlock? waiting = null;   // a finished dialog still waiting for its report ID
        foreach (var raw in lines)
        {
            var l = ParseLine(raw);
            if (l == null) continue;
            if (!begun && l.Text.StartsWith("--- BEGIN LOGGING AT", StringComparison.Ordinal))
            {
                begun = true;
                if (ParseBegin(l.Text) is { } b) { log.StartUtc = b; log.StartTick = l.Tick; }
            }
            if (log.Build == null && l.Proc.Length > 0 && BuildTag.Match(l.Proc) is { Success: true } bm) log.Build = "b" + bm.Groups[1].Value;

            if (l.Text.StartsWith(CaptureMarker, StringComparison.Ordinal))
            {
                cur = new CrashBlock { Tick = l.Tick, Proc = l.Proc, Thread = l.Thread };
                log.Blocks.Add(cur);
                waiting = null;
                continue;
            }
            if (cur != null)
            {
                bool same = l.Proc == cur.Proc && l.Thread == cur.Thread;
                if (same && l.Tick - cur.Tick <= 3000 && cur.Lines.Count < 80 && !ReportLine.IsMatch(l.Text) && !l.Text.StartsWith("Error uploading crash", StringComparison.Ordinal))
                {
                    cur.Lines.Add(l.Text);
                    continue;
                }
                if (same || l.Tick - cur.Tick > 3000) { waiting = cur; cur = null; }
                else continue;   // another process in between
            }
            if (waiting != null)
            {
                if (l.Tick - waiting.Tick > 90_000) waiting = null;
                else if (l.Proc == waiting.Proc && l.Thread == waiting.Thread && ReportLine.Match(l.Text) is { Success: true } rm)
                {
                    waiting.ReportId = rm.Groups[1].Value;
                    waiting = null;
                }
            }
        }
        // FiveM's content ends with a blank line
        foreach (var b in log.Blocks)
            while (b.Lines.Count > 0 && string.IsNullOrWhiteSpace(b.Lines[^1])) b.Lines.RemoveAt(b.Lines.Count - 1);
        log.Blocks.RemoveAll(b => b.Lines.Count == 0);
        return log;
    }

    // ---------- one dialog ----------

    private static readonly Regex Anchor = new(@"<a\s+href=""[^""]*"">(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SigShape = new(@"^(?:[\w.\- ]+\.(?:exe|dll|asi)(?:_unloaded)?[!+]|(?:GTA5|FiveM|RedM)\+[0-9A-Fa-f]+\b|0x[0-9a-fA-F]{4,}$)", RegexOptions.Compiled);
    private static readonly Regex ModuleShape = new(@"^([\w.\- ]+?\.(?:exe|dll|asi))(?:_unloaded)?[!+]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // English and German (FiveM's de locale: "Ein Fehler bei %s" + "%s hat verursacht, dass %s nicht mehr funktioniert …")
    private static readonly Regex ErrorAt = new(@"(?:An error at|Ein Fehler bei) (.+?) (?:caused |hat verursacht)", RegexOptions.Compiled);
    // "Legacy crash hash: a-b-c", German "Alter crash hash: …", French "Héritage du hash de crash : …" and so on
    private static readonly Regex Legacy = new(@"^[^:]{2,48}:\s?([a-z0-9]+(?:-[a-z0-9]+){2})\s*$", RegexOptions.Compiled);
    private static readonly Regex ExType = new(@"An unhandled exception \(of type (.+?)\)", RegexOptions.Compiled);
    private static readonly Regex Codes = new(@"\b(ERR_[A-Z0-9_]{3,}|DXGI_ERROR_[A-Z_]+|E_OUTOFMEMORY)\b", RegexOptions.Compiled);
    // a signature inside a sentence: "GTA5_b3258.exe!sub_1413A8C10 (0x4c)", "nvwgf2umx.dll+A3C0F1"
    private static readonly Regex SigToken = new(@"(?<![\w.\-])[\w.\-]+\.(?:exe|dll|asi)(?:_unloaded)?(?:![^\s()]+(?: \(0x[0-9a-fA-F]+\))?|\+[0-9A-Fa-f]+\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FiveMWord = new(@"\bFiveM\b", RegexOptions.Compiled);
    private static readonly Regex LineDetail = new(@" \([^()]*:\d+\)$", RegexOptions.Compiled);
    private static readonly string[] KnownTitles = { "FiveM crashed... but we're on it!", "FiveM ist abgestürzt... aber wir sind dran!" };

    /// <summary>The dialog's "&lt;cause&gt; caused FiveM to stop working. A crash report is being uploaded to the FiveM
    /// developers." in any language: every translation keeps both product names, and the cause is "An error[ at …]", an
    /// unhandled exception or a signature.</summary>
    private static bool IsCaused(string x) =>
        ErrorAt.IsMatch(x)
        || (FiveMWord.Matches(x).Count >= 2 && (x.StartsWith("An error", StringComparison.Ordinal) || x.StartsWith("An unhandled exception (of type", StringComparison.Ordinal) || SigToken.IsMatch(x)));

    /// <summary>Text of a dialog line as plain text: FiveM's &lt;A HREF&gt; links become their text.</summary>
    public static string Plain(string s) => Anchor.Replace(s, "$1").Trim();

    public static bool LooksLikeSignature(string? s) => !string.IsNullOrWhiteSpace(s) && SigShape.IsMatch(s.Trim());

    /// <summary>The module of a signature or stack frame: "nvwgf2umx.dll", "GTA5_b3258.exe", "GTA5" (old form).</summary>
    public static string? ModuleOf(string? sig)
    {
        if (string.IsNullOrWhiteSpace(sig)) return null;
        sig = sig.Trim();
        var m = ModuleShape.Match(sig);
        if (m.Success) return m.Groups[1].Value;
        var p = sig.IndexOf('+');
        return p > 0 && (sig.StartsWith("GTA5+", StringComparison.OrdinalIgnoreCase) || sig.StartsWith("FiveM+", StringComparison.OrdinalIgnoreCase)) ? sig[..p] : null;
    }

    /// <summary>Turns a logged dialog into a crash report.</summary>
    public static CrashReport FromBlock(CrashBlock b, CrashLog log, string? logName = null)
    {
        var r = new CrashReport { TimeUtc = log.TimeOf(b), Build = log.Build, ReportId = b.ReportId, Log = logName };
        var lines = b.Lines.Select(Plain).ToList();
        r.Title = lines.Count > 0 ? lines[0] : "";
        var body = lines.Skip(1).ToList();

        // "Stack trace:" (also in German); in other languages a line ending in ":" with a frame after it
        int st = body.FindIndex(x => x == "Stack trace:");
        if (st < 0)
            for (int i = 0; i + 1 < body.Count; i++)
                if (body[i].EndsWith(':') && LooksLikeSignature(body[i + 1]) && !Legacy.IsMatch(body[i])) { st = i; break; }
        if (st >= 0)
        {
            foreach (var f in body.Skip(st + 1))
            {
                if (f.Length == 0 || r.Stack.Count >= 7) break;
                r.Stack.Add(f.Trim());
            }
            body = body.Take(st).ToList();
        }

        int caused = body.FindIndex(IsCaused);
        if (caused >= 0)
        {
            // only the crash path of the dialog has a crash hash, after the caused sentence (a disconnect or error text could
            // hold anything); FiveM's English or German label first, any other label of the same shape after that
            int lh = body.FindIndex(caused + 1, x => x.StartsWith("Legacy crash hash:", StringComparison.Ordinal) || x.StartsWith("Alter crash hash:", StringComparison.Ordinal));
            if (lh < 0) lh = body.FindIndex(caused + 1, x => Legacy.IsMatch(x) && !x.StartsWith("Exception details", StringComparison.Ordinal) && !x.StartsWith("Fehlerinformationen", StringComparison.Ordinal));
            if (lh > caused && Legacy.Match(body[lh]) is { Success: true })
            {
                r.LegacyHash = Legacy.Match(body[lh]).Groups[1].Value;
                body.RemoveAt(lh);
            }
        }

        string all = string.Join("\n", lines);
        if (LooksLikeSignature(r.Title)) r.Signature = r.Title;
        else if (caused >= 0 && SigIn(body[caused], r.Stack) is { } sig) r.Signature = sig;
        else if (r.Title == "Exception, unhandled!" && r.Stack.Count > 0) r.Signature = Frame(r.Stack.FirstOrDefault(f => !IsSystem(ModuleOf(f))) ?? r.Stack[0]);
        // crash-data.json's explanation stands before the caused sentence, under a headline that is no signature
        r.Known = KnownTitles.Contains(r.Title)
            || (caused > 0 && !LooksLikeSignature(r.Title) && r.Title is not ("Exception, unhandled!" or "FiveM has stopped working")
                && body.Take(caused).Any(x => x.Length > 0) && !r.Title.EndsWith(" encountered an error", StringComparison.Ordinal));
        if (ExType.Match(all) is { Success: true } xm) r.ExceptionType = xm.Groups[1].Value;
        r.Module = ModuleOf(r.Signature);
        if (r.Signature != null && BuildExe.Match(r.Signature) is { Success: true } bx) r.Build = "b" + bx.Groups[1].Value;
        if (Codes.Match(all) is { Success: true } cm) r.Code = cm.Groups[1].Value;

        // the error text: everything FiveM said that is not boilerplate around a plain signature
        var said = new List<string>();
        if (!LooksLikeSignature(r.Title)) said.Add(r.Title);
        foreach (var raw in body)
        {
            var x = Unboiler(raw);
            if (x.Length == 0 || Boiler(x) || IsCaused(x)) continue;
            said.Add(x);
        }
        if (r.ExceptionType != null) said.Insert(0, "Unhandled exception: " + r.ExceptionType);
        said.RemoveAll(x => x.Length == 0 || x == "Exception, unhandled!" || x.StartsWith("An unhandled exception (of type", StringComparison.Ordinal));
        var text = said.Count == 0 ? "" : (said[0].Trim() + (said.Count > 1 ? " – " + string.Join(" ", said.Skip(1).Select(x => x.Trim())) : "")).Trim();
        r.Text = text.Length == 0 ? null : text.Length > 420 ? text[..417].TrimEnd() + "…" : text;
        return r;
    }

    /// <summary>The signature named in the caused sentence: by FiveM's English or German words, else the first frame of the
    /// stack (the signature is that frame), else the first signature-shaped token.</summary>
    private static string? SigIn(string line, List<string> stack)
    {
        if (ErrorAt.Match(line) is { Success: true } m) return m.Groups[1].Value;
        if (stack.Count > 0 && Frame(stack[0]) is { Length: > 0 } f0 && line.Contains(f0, StringComparison.Ordinal)) return f0;
        return SigToken.Match(line) is { Success: true } t ? t.Value : null;
    }

    /// <summary>A stack frame without its " (Main.cpp:1027)" source line: the form FiveM uses as signature.</summary>
    private static string Frame(string f) => LineDetail.Replace(f.Trim(), "");

    private static readonly string[] BoilerTails = { " When asking for help", " If you require immediate support" };

    /// <summary>A line without FiveM's help sentences at its end ("When asking for help, please click …").</summary>
    private static string Unboiler(string x)
    {
        foreach (var t in BoilerTails)
            if (x.IndexOf(t, StringComparison.Ordinal) is var i and > 0) x = x[..i];
        return x.Trim();
    }

    private static bool Boiler(string x) =>
        x.StartsWith("If you require immediate support", StringComparison.Ordinal)
        || x.StartsWith("You can press Ctrl-C", StringComparison.Ordinal)
        || x.StartsWith("This is not the fault of the FiveM developers", StringComparison.Ordinal)
        || x.StartsWith("When asking for help", StringComparison.Ordinal)
        || x == " ";

    // ---------- modules ----------

    private sealed record Mod(string[] Parts, string Kind, string Name, string? Vendor = null);

    // kind: gpu (graphics driver), overlay (a hook of another program), av (security software), fivem, script, nui,
    // game, system, gfxapi (Windows' DirectX itself)
    private static readonly Mod[] Mods =
    {
        new(new[] { "nvwgf2um", "nvgpucomp", "nvd3dum", "nvoglv" }, "gpu", "NVIDIA-Grafiktreiber", "nvidia"),
        new(new[] { "atidxx", "amdxx", "atiumd", "amdxc" }, "gpu", "AMD-Grafiktreiber", "amd"),
        new(new[] { "igd10iumd", "igd12umd", "igdumdim", "igc64" }, "gpu", "Intel-Grafiktreiber", "intel"),
        new(new[] { "rtsshooks" }, "overlay", "RivaTuner (MSI Afterburner)"),
        new(new[] { "gameoverlayrenderer" }, "overlay", "Steam-Overlay"),
        new(new[] { "discordhook", "discord_overlay" }, "overlay", "Discord-Overlay"),
        new(new[] { "owclient", "ow-graphics" }, "overlay", "Overwolf"),   // before OBS: "ow-graphics-hook64.dll"
        new(new[] { "graphics-hook" }, "overlay", "OBS-Spielaufnahme"),
        new(new[] { "nvspcap" }, "overlay", "NVIDIA-Overlay (ShadowPlay)"),
        new(new[] { "nvppex" }, "overlay", "NVIDIA-Spielfilter"),
        new(new[] { "reshade" }, "overlay", "ReShade"),
        new(new[] { "guard64" }, "av", "Comodo Internet Security"),
        new(new[] { "citizen-scripting-lua", "citizen-scripting-v8", "citizen-scripting-mono", "citizen-scripting-node", "mono-2.0-sgen", "v8_libbase", "v8.dll" }, "script", "Skript-Laufzeit"),
        new(new[] { "libcef", "chrome_elf" }, "nui", "NUI (Chromium)"),
        new(new[] { "adhesive", "citizen-", "corert", "legitimacy", "conhost", "gta-core", "gta-net", "gta-streaming", "gta-game", "rage-", "glue.dll", "net.dll", "nui-core", "voip-mumble", "scripting-gta", "devtools", "fivem.exe", "citilaunch" }, "fivem", "FiveM"),
        new(new[] { "gta5", "gtaprocess", "gameprocess" }, "game", "GTA V"),
        new(new[] { "d3d11.dll", "dxgi.dll", "d3d12.dll", "d3d10.dll", "d3dcompiler" }, "gfxapi", "DirectX"),
        new(new[] { "ntdll", "kernelbase", "kernel32", "ucrtbase", "msvcp", "vcruntime", "combase", "user32", "win32u", "ole32", "rpcrt4", "sechost", "advapi32", "ws2_32", "mswsock" }, "system", "Windows"),
    };

    private static Mod? Find(string? module)
    {
        if (string.IsNullOrWhiteSpace(module)) return null;
        var m = module.ToLowerInvariant();
        if (m.EndsWith(".asi", StringComparison.Ordinal)) return new(new[] { m }, "asi", module);
        if (m == "fivem") return Mods.First(x => x.Kind == "fivem");   // the old signature form "FiveM+1A2B"
        return Mods.FirstOrDefault(x => x.Parts.Any(p => m.Contains(p, StringComparison.Ordinal)));
    }

    public static string? ModuleKind(string? module) => Find(module)?.Kind;
    private static bool IsSystem(string? module) => Find(module)?.Kind is "system" or "gfxapi";

    // ---------- the cause ----------

    private static readonly Dictionary<string, string> DriverUrls = new()
    {
        ["nvidia"] = "https://www.nvidia.com/de-de/drivers/",
        ["amd"] = "https://www.amd.com/de/support/download/drivers.html",
        ["intel"] = "https://www.intel.de/content/www/de/de/support/detect.html",
    };

    /// <summary>The page the driver of this vendor comes from (null for an unknown vendor).</summary>
    public static string? DriverUrl(string? vendor) => vendor != null && DriverUrls.TryGetValue(vendor, out var u) ? u : null;

    /// <summary>"nvidia", "amd", "intel" from a GPU name (null when it is none of these).</summary>
    public static string? VendorOf(string? gpuName)
    {
        var g = (gpuName ?? "").ToLowerInvariant();
        if (g.Contains("nvidia") || g.Contains("geforce") || g.Contains("quadro")) return "nvidia";
        if (g.Contains("radeon") || g.Contains("amd")) return "amd";
        if (g.Contains("intel") || g.Contains("arc ")) return "intel";
        return null;
    }

    private static CrashFix? Driver(string? vendor) => DriverUrl(vendor) is null ? null
        : new("driver:" + vendor, vendor switch { "nvidia" => "NVIDIA-Treiber laden", "amd" => "AMD-Treiber laden", _ => "Intel-Treiber laden" }, "host", "driver:" + vendor);
    private static CrashFix? Pagefile(CrashPc pc) => pc.PagefileOk ? null : new("pagefile", "Auslagerungsdatei absichern", "health", "tweak:pagefile");
    private static CrashFix? Tex(CrashPc pc) => pc.TexRaised ? new("tex", "Texturbudget ansehen", "page", "gfx#texCard") : null;
    private static CrashFix? HagsOff(CrashPc pc) => pc.HagsOn ? new("hags", "GPU-Planung testweise aus", "tweakoff", "hags") : null;
    private static readonly CrashFix Mods_ = new("mods", "Deine Mods", "page", "mods#files");
    private static readonly CrashFix ReShadeFix = new("reshade", "ReShade verwalten", "page", "mods#rs");
    private static readonly CrashFix Autostart = new("autostart", "Autostart öffnen", "page", "autostart");
    private static readonly CrashFix Cache = new("cache", "FiveM-Cache leeren", "action", "fmcache");
    private static CrashFix? Plugins(CrashPc pc) => pc.HasPlugins ? new("plugins", "Plugins-Ordner öffnen", "host", "plugins") : null;

    private static CrashFix[] Fx(params CrashFix?[] f) => f.Where(x => x != null).Select(x => x!).ToArray();

    private const string OcNote = "Übertaktung (auch eine ab Werk) testweise auf Standard stellen – etwa in MSI Afterburner „Reset“.";

    /// <summary>The likely cause of one crash and the fixes that apply on this PC.</summary>
    public static CrashCause Analyze(CrashReport r, CrashPc pc)
    {
        string title = r.Title ?? "", text = r.Text ?? "", all = title + "\n" + text;
        string? code = r.Code;
        var vendor = pc.GpuVendor;

        if (!r.FromLog)
            return new("nolog", "Keine Details im Log", "FiveM hat einen Crash-Dump geschrieben, aber in seinen Logs steht nichts dazu. "
                + (r.TimeUtc is { } t && pc.NowUtc is { } now && now - t > TimeSpan.FromDays(7)
                    ? "Das Log ist schon weg: FiveM löscht Logs nach sieben Tagen, die letzten zehn Dumps behält es länger."
                    : "Entweder wurde das Log gelöscht (auch der Cleaner räumt FiveM-Logs auf), oder FiveM kam nicht mehr dazu, die Meldung zu schreiben.")
                + " Woran es lag, lässt sich so nicht sagen.",
                new[] { "Tritt es wieder auf, steht FiveMs Meldung im neuen Log – OrbitTweak liest sie dann mit.", "Für den Support: den Dump über „Dump zeigen“ finden und mitschicken." }, "unklar", "crs-unknown", Array.Empty<CrashFix>());

        // FiveM's own words first: they name the cause more precisely than any module could
        if (Regex.IsMatch(title, @"^O\S* \(Error\)$"))
            return new("disconnect", "Vom Server getrennt", "Das war kein Absturz deines PCs: FiveM hat die Verbindung beendet und die Meldung im Absturz-Fenster gezeigt. Der Text unten kommt vom Server oder von FiveM.",
                new[] { "Passiert es nur auf einem Server, melde den Text dem Server-Team.", "Kommt es auf allen Servern, prüf deine Verbindung im Verbindungstest oben." }, "klar", "wifi", Array.Empty<CrashFix>());

        if (title.StartsWith("FiveM has stopped responding", StringComparison.Ordinal))
        {
            if (title.Contains("(script deadloop)"))
                return new("freeze-script", "Eingefroren: Server-Skript", "Das Spiel hat zu lange nicht reagiert, weil ein Skript des Servers in einer Endlosschleife hing – das liegt nicht an deinem PC.",
                    new[] { "Melde es dem Server-Team mit dem Text unten.", "Passiert es auf mehreren Servern, entferne testweise eigene Mods." }, "klar", "terminal", Fx(Mods_));
            if (title.Contains("(DirectX query)"))
                return new("freeze-gpu", "Eingefroren: Grafikkarte hing", "Das Spiel hat auf die Grafikkarte gewartet, die nicht mehr antwortete. Laut FiveM können fehlerhafte Spiel-Inhalte (etwa vom Server) oder Grafik-Mods dahinterstecken, sonst Treiber oder Übertaktung.",
                    new[] { "Grafik-Mods wie ReShade testweise entfernen.", "Grafiktreiber aktualisieren.", OcNote }, "wahrscheinlich", "gpucard", Fx(Mods_, Driver(vendor), HagsOff(pc)));
            if (title.Contains("(NVIDIA drivers)"))
                return new("gpu-driver", "NVIDIA-Grafiktreiber", "FiveM hat beim Einfrieren eine NVIDIA-Treiberversion erkannt, die einen bekannten Fehler hat.",
                    new[] { "Grafiktreiber aktualisieren: Betroffen sind laut FiveM die Versionen 472 bis 497.10, FiveM selbst rät zu 471.68 oder älter.", "Overlays wie das Steam-Overlay testweise ausschalten." }, "klar", "gpucard", Fx(Driver("nvidia")));
            return new("freeze", "Spiel eingefroren", "Das Spiel hat zu lange nicht reagiert und wurde beendet. Woran es hing, sagt FiveM in diesem Fall nicht.",
                new[] { "Achte darauf, ob es immer an derselben Stelle oder auf demselben Server passiert.", "Für den Support: Text unten kopieren und den Dump mitschicken." }, "unklar", "timer", Array.Empty<CrashFix>());
        }

        if (title == "Game integrity check failed" || title == "Early-exit trap")
            return new("integrity", "FiveM-Integritätsprüfung", "FiveM hat bei seiner Prüfung eine Veränderung am laufenden Spiel bemerkt und es beendet. Laut FiveM kann das an kürzlichen Änderungen an deinem PC liegen; typisch sind Programme, die sich ins Spiel einklinken.",
                new[] { "Programme schließen, die sich ins Spiel einklinken (Overlays, Mod-Menüs, Injectoren).", "Cfx.re-Hilfe: aka.cfx.re/integrity-check-failed" }, "klar", "shield", Array.Empty<CrashFix>());

        if (title.EndsWith(" encountered an error", StringComparison.Ordinal) && !title.StartsWith("FiveM", StringComparison.Ordinal))
        {
            if (title.StartsWith("NVIDIA Game Filters", StringComparison.Ordinal)) return Overlay("NVIDIA-Spielfilter", pc, "klar");
            if (title.StartsWith("NVIDIA GPU drivers", StringComparison.Ordinal)) return Gpu("nvidia", "klar", pc);
            if (title.StartsWith("AMD GPU drivers", StringComparison.Ordinal)) return Gpu("amd", "klar", pc);
            if (title.StartsWith("Comodo", StringComparison.Ordinal))
                return new("av", "Comodo Internet Security", "FiveM selbst nennt Comodo als Auslöser: dessen Schutz klinkt sich ins Spiel ein und bringt es zum Absturz.",
                    new[] { "Laut FiveM hilft nur, Comodo zu deinstallieren oder das Problem im Comodo-Forum zu melden." }, "klar", "shield", Array.Empty<CrashFix>());
            if (title.Contains(".asi")) return Asi(pc, "klar");
        }

        if (r.Known)
            return new("known", "Bekannter FiveM-Fehler", "Cfx.re kennt diesen Absturz schon und hat eine Erklärung hinterlegt (unten auf Englisch). Meist hilft nur warten, bis FiveM ihn behebt.",
                new[] { "FiveM aktuell halten – Updates kommen beim nächsten Start von selbst.", "Steht in der Erklärung ein Auslöser (etwa ein Auto-Mod), meide ihn." }, "klar", "info", Array.Empty<CrashFix>());

        if (Regex.IsMatch(all, @"Pool Full, Size ==", RegexOptions.CultureInvariant))
            return new("pool", "Zu viele Objekte auf dem Server", "Eine Liste im Spiel war voll (zum Beispiel Fahrzeuge oder Objekte). Das kommt von Server-Inhalten oder Mods, die zu viel auf einmal erzeugen – nicht von deinem PC.",
                new[] { "Melde es dem Server-Team mit dem Text unten.", "Passiert es auf mehreren Servern, entferne testweise eigene Mods." }, "klar", "layers", Fx(Mods_));

        if (Regex.IsMatch(all, @"Rockstar Games Launcher|Social Club|\bROS\b|ExitCode = \d+", RegexOptions.CultureInvariant) && r.Signature == null)
            return new("launcher", "Rockstar Games Launcher", "FiveM braucht den Rockstar Games Launcher und konnte ihn nicht nutzen. Das ist ein Startproblem, kein Absturz im Spiel.",
                new[] { "Rockstar Games Launcher als normaler Nutzer starten, nicht als Administrator.", "Einmal im Launcher anmelden und GTA V dort starten können, dann FiveM neu starten.", "Hilft das nicht: den Launcher neu installieren." }, "klar", "rocket", Array.Empty<CrashFix>());

        if (code != null)
        {
            if (code.StartsWith("ERR_MEM", StringComparison.Ordinal) || code == "E_OUTOFMEMORY") return Memory(pc, "klar", code);
            if (code is "ERR_GFX_D3D_INIT")
                return new("gpu-init", "DirectX ließ sich nicht starten", "GTA V konnte die Grafikkarte nicht einrichten. Häufige Gründe sind Overlays, die sich in die Grafik einklinken, der Treiber, eine Übertaktung oder Grafik-Mods im GTA-V-Ordner.",
                    new[] { "Overlays testweise ausschalten (NVIDIA-Overlay, Steam, Discord, RivaTuner).", "Grafiktreiber aktualisieren und den PC neu starten.", OcNote }, "wahrscheinlich", "gpucard", Fx(Driver(vendor), ReShadeFix));
            if (Regex.IsMatch(code, "^ERR_GFX_.*(MEM|ALLOC)")) return Vram(pc);
            if (code == "ERR_GFX_DRAW_DATA")
                return new("assets", "Fehlerhafte Spiel-Inhalte", "GTA V stieß beim Zeichnen auf kaputte Modell-Daten. Das kommt meist von Server-Inhalten (Autos, Kleidung, MLOs) oder eigenen Mods.",
                    new[] { "Passiert es nur auf einem Server, melde es dort.", "Eigene Mods testweise entfernen." }, "wahrscheinlich", "puzzle", Fx(Mods_));
            if (code.StartsWith("ERR_GFX", StringComparison.Ordinal) || code.StartsWith("DXGI_ERROR", StringComparison.Ordinal))
                return Device(r, pc, all);
            if (code.StartsWith("ERR_STR", StringComparison.Ordinal))
                return new("streaming", "Spiel-Daten nicht ladbar", "GTA V konnte Daten nicht laden, die es beim Spielen nachlädt. Oft ist eine Datei im FiveM-Cache beschädigt, seltener ein Server-Inhalt.",
                    new[] { "FiveM-Cache leeren – der nächste Start lädt die Server-Daten neu.", "Passiert es nur auf einem Server, melde es dort." }, "wahrscheinlich", "download", Fx(Cache));
            if (code.StartsWith("ERR_FIL", StringComparison.Ordinal) || code == "ERR_GEN_INVALID")
                return GameFiles();
        }
        if (all.Contains("rage::fiPackfile", StringComparison.Ordinal)) return GameFiles();
        if (title.StartsWith("DirectX encountered an unrecoverable error", StringComparison.Ordinal)) return Device(r, pc, all);
        if (Regex.IsMatch(all, @"out of memory|bad_alloc|Failed to allocate|E_OUTOFMEMORY", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return Memory(pc, "wahrscheinlich", null);

        // then the module the crash happened in, and if that is Windows itself, the first telling one in the stack
        var top = Find(r.Module);
        var cause = FromModule(top, r.Module, pc, "klar", r);
        if (cause != null && top?.Kind is not ("system" or "gfxapi")) return cause;
        foreach (var f in r.Stack)
        {
            var m = Find(ModuleOf(f));
            if (m == null || m.Kind is "system" or "gfxapi" || (m.Kind is "game" or "fivem" && top?.Kind is "gfxapi")) continue;
            var c = FromModule(m, ModuleOf(f), pc, "wahrscheinlich", r);
            if (c != null) return c;
        }
        if (top?.Kind == "gfxapi")
            return new("gfxapi", "Grafik-Schnittstelle (DirectX)", $"Der Absturz passierte in {r.Module}, also in Windows' DirectX. Dahinter steckt meist der Grafiktreiber – oder ein Grafik-Mod wie ReShade oder ENB, der sich unter demselben Dateinamen in den GTA-V-Ordner legt.",
                new[] { "Grafiktreiber aktualisieren.", "ReShade/ENB im GTA-V-Ordner testweise entfernen.", OcNote }, "wahrscheinlich", "gpucard", Fx(Driver(vendor), ReShadeFix));
        if (r.ExceptionType != null)
            return new("exception", "Unbehandelte Ausnahme", $"In FiveM ist ein Fehler ({r.ExceptionType}) aufgetreten, den nichts abgefangen hat. Selbst beheben lässt sich das meist nicht.",
                new[] { "FiveM neu starten; tritt es öfter auf, im Cfx.re-Forum mit dem Text unten melden." }, "unklar", "crs-crack", Array.Empty<CrashFix>());
        return Unknown(r);
    }

    private static CrashCause? FromModule(Mod? m, string? module, CrashPc pc, string sure, CrashReport r)
    {
        if (m == null) return null;
        switch (m.Kind)
        {
            case "gpu": return Gpu(m.Vendor, sure, pc, module);
            case "overlay": return Overlay(m.Name, pc, sure, module);
            case "asi": return Asi(pc, sure, module);
            case "av":
                return new("av", m.Name, $"Der Absturz passierte in {module}, einem Teil von {m.Name}. FiveM nennt das selbst als Auslöser.",
                    new[] { "Laut FiveM hilft nur, das Programm zu deinstallieren oder das Problem beim Hersteller zu melden." }, sure, "shield", Array.Empty<CrashFix>());
            case "script":
                return new("script", "Server-Skript", $"Der Absturz passierte in der Skript-Laufzeit ({module}). Das ist meist ein Fehler in einem Skript des Servers, nicht an deinem PC.",
                    new[] { "Passiert es nur auf einem Server, melde es dem Server-Team mit dem Text unten.", "Kommt es überall vor, FiveM neu starten und den Cache leeren." }, sure, "terminal", Array.Empty<CrashFix>());
            case "nui":
                return new("nui", "Server-Oberfläche (NUI)", "Abgestürzt ist der eingebaute Browser, der Menüs und HUDs der Server zeigt. Das kann an vollem Arbeitsspeicher liegen, etwa durch eine Server-Oberfläche, die immer mehr Speicher belegt, oder an der Oberfläche selbst. Das Spiel läuft danach manchmal weiter.",
                    new[] { "Im Task-Manager beobachten, ob der Arbeitsspeicher vor dem Absturz voll läuft.", "Passiert es nur auf einem Server, melde es dort." }, sure, "globe", Fx(Pagefile(pc)));
            case "fivem":
                return new("fivem", "In FiveM selbst", $"Der Fehler liegt in einem Teil von FiveM ({module}). Selbst beheben lässt sich das kaum.",
                    new[] { "PC und FiveM neu starten – Updates kommen beim Start von selbst.", "Tritt es öfter auf, im Cfx.re-Forum mit dem Text unten melden." }, sure == "klar" ? "wahrscheinlich" : sure, "crs-crack", Array.Empty<CrashFix>());
            case "game":
                return new("game", "Im Spielcode von GTA V", "Das sagt nur, wo es krachte, nicht warum. Häufig sind es Inhalte des Servers (Autos, Kleidung, Karten) oder eigene Mods, manchmal ein Fehler im Spiel selbst.",
                    new[] { "Passiert es nur auf einem Server, melde es dort mit dem Text unten.", "Passiert es überall, entferne testweise eigene Mods.", "Für den Support zählt die Signatur – kopier sie mit." }, "unklar", "gamepad", Fx(Mods_));
        }
        return null;
    }

    private static CrashCause Gpu(string? vendor, string sure, CrashPc pc, string? module = null)
    {
        var name = vendor switch { "nvidia" => "NVIDIA-Grafiktreiber", "amd" => "AMD-Grafiktreiber", "intel" => "Intel-Grafiktreiber", _ => "Grafiktreiber" };
        var where = module != null ? $"Der Absturz passierte in {module}, dem {name}." : $"FiveM selbst nennt den {name} als Auslöser.";
        return new("gpu-driver", name, where + " Das liegt oft an der Treiberversion, manchmal an Übertaktung oder Overlays, die sich in die Grafik einklinken.",
            new[] { "Treiber aktualisieren – kam der Fehler nach einem Update, eine Version zurück.", OcNote, "PC neu starten, dann FiveM." }, sure, "gpucard", Fx(Driver(vendor), HagsOff(pc)));
    }

    private static CrashCause Vram(CrashPc pc) =>
        new("vram", "Grafikspeicher voll", "GTA V bekam von der Grafikkarte keinen Speicher mehr. Meist ist der Grafikspeicher (VRAM) voll – durch hohe Einstellungen, ein hohes Texturbudget oder viele Add-on-Inhalte des Servers.",
            new[] { "Texturqualität oder Auflösung im Spiel eine Stufe senken.", "Ist dein Texturbudget erhöht, senk es testweise.", "Grafiktreiber aktuell halten." }, "wahrscheinlich", "gpucard", Fx(Tex(pc), Driver(pc.GpuVendor)));

    private static CrashCause Device(CrashReport r, CrashPc pc, string all)
    {
        // RenderHooks.cpp: "GetDeviceRemovedReason returned E_OUTOFMEMORY - …" – the card ran out of video memory
        if (all.Contains("returned E_OUTOFMEMORY", StringComparison.Ordinal)) return Vram(pc);
        // RenderHooks.cpp marks graphics mods in the GTA folder: (R) ReShade, (E) ENBSeries, (R+E) both, (M) another one
        if (Regex.IsMatch(r.Title ?? "", @"unrecoverable error \((R|E|R\+E|M)\)") || all.Contains("graphics mods were found", StringComparison.Ordinal))
            return new("gfxmod", "Grafik-Mod im GTA-V-Ordner", "Die Grafikkarte ist ausgestiegen, und FiveM hat dabei einen Grafik-Mod im GTA-V-Ordner gefunden (ReShade, ENB oder ähnlich). Solche Mods sind laut FiveM ein häufiger Grund.",
                new[] { "Den Grafik-Mod testweise entfernen oder aktualisieren.", "Grafiktreiber aktualisieren." }, "wahrscheinlich", "sparkle", Fx(ReShadeFix, Driver(pc.GpuVendor)));
        return new("gpu-device", "Grafikkarte ausgestiegen", "Windows hat die Verbindung zur Grafikkarte zurückgesetzt („Device removed“). Häufige Gründe: eine instabile Übertaktung (auch ab Werk), der Treiber, zu wenig Strom vom Netzteil oder Hitze.",
            new[] { OcNote, "Grafiktreiber sauber neu installieren.", "Temperaturen prüfen; bei vielen solchen Abstürzen das Netzteil im Blick haben." }, "wahrscheinlich", "gpucard", Fx(Driver(pc.GpuVendor), HagsOff(pc)));
    }

    private static CrashCause Memory(CrashPc pc, string sure, string? code) =>
        new("memory", "Arbeitsspeicher voll", (code != null ? $"GTA V meldet einen Speicherfehler ({code}). " : "") + "Meist lief der Arbeitsspeicher voll – mit vielen offenen Programmen, einer zu kleinen Auslagerungsdatei oder sehr vielen Server-Inhalten.",
            new[] { pc.PagefileOk ? "Deine Auslagerungsdatei ist in Ordnung." : "Auslagerungsdatei wieder Windows überlassen.", "Browser und andere große Programme vor dem Spielen schließen.", "Ist dein Texturbudget erhöht, senk es testweise." }, sure, "ram", Fx(Pagefile(pc), Tex(pc)));

    private static CrashCause Overlay(string name, CrashPc pc, string sure, string? module = null)
    {
        string how = name switch
        {
            "RivaTuner (MSI Afterburner)" => "Das Overlay in RivaTuner für FiveM abschalten oder RivaTuner vor dem Spielen beenden.",
            "Steam-Overlay" => "Steam › Einstellungen › Im Spiel › Steam-Overlay ausschalten.",
            "Discord-Overlay" => "Discord › Einstellungen › Spiel-Overlay ausschalten.",
            "OBS-Spielaufnahme" => "In OBS mit „Fensteraufnahme“ oder „Bildschirmaufnahme“ statt „Spielaufnahme“ aufnehmen.",
            "NVIDIA-Overlay (ShadowPlay)" => "In der NVIDIA App das In-Game-Overlay testweise ausschalten.",
            "NVIDIA-Spielfilter" => "Spielfilter (Freestyle) in der NVIDIA App ausschalten – so rät es FiveM.",
            "Overwolf" => "Overwolf vor dem Spielen beenden.",
            "ReShade" => "ReShade testweise entfernen oder aktualisieren.",
            _ => "Das Programm vor dem Spielen beenden.",
        };
        var where = module != null ? $"Der Absturz passierte in {module}, einem Teil von {name} – nicht von FiveM." : $"FiveM selbst nennt {name} als Auslöser.";
        var fixes = name switch
        {
            "RivaTuner (MSI Afterburner)" or "Overwolf" => Fx(Autostart),
            "ReShade" => Fx(ReShadeFix),
            _ => Array.Empty<CrashFix>(),
        };
        return new("overlay", name, where + " Solche Programme klinken sich ins Spiel ein und können es zum Absturz bringen.", new[] { how, "Danach FiveM neu starten." }, sure, "layers", fixes);
    }

    private static CrashCause Asi(CrashPc pc, string sure, string? module = null) =>
        new("asi", "Spiel-Plugin (.asi)", (module != null ? $"Der Absturz passierte in {module}. " : "") + "Das ist ein Plugin aus dem Ordner FiveM.app\\plugins, kein Teil von FiveM.",
            new[] { "Die Datei aus dem Plugins-Ordner nehmen und FiveM neu starten." }, sure, "puzzle", Fx(Plugins(pc)));

    private static CrashCause GameFiles() =>
        new("gamefiles", "GTA-V-Dateien beschädigt", "GTA V konnte eigene Dateien nicht lesen. Sie sind beschädigt oder wurden verändert, etwa durch Mods direkt im GTA-V-Ordner.",
            new[] { "Spieldateien in Steam, Epic oder dem Rockstar Games Launcher überprüfen lassen.", "Mods direkt im GTA-V-Ordner entfernen – FiveM-Mods gehören nach FiveM.app\\mods." }, "klar", "file", Array.Empty<CrashFix>());

    private static CrashCause Unknown(CrashReport r) =>
        new("unknown", "Ursache unbekannt", "Aus dem Log lässt sich keine Ursache ablesen. Unten steht, was FiveM gemeldet hat – damit kann dir der Support oder das Cfx.re-Forum weiterhelfen.",
            new[] { "Text kopieren und im Support deines Servers oder im Cfx.re-Forum posten." }, "unklar", "crs-unknown", Array.Empty<CrashFix>());

    // ---------- crashes from logs and dumps ----------

    /// <summary>Crashes from the logs and the dumps, newest first, at most <see cref="MaxCrashes"/>. A dump within
    /// <see cref="DumpMatch"/> of a logged dialog belongs to it; a dump without one stays a crash of its own.</summary>
    public static List<CrashReport> Merge(IEnumerable<CrashReport> logged, IEnumerable<(string name, DateTime utc)> dumps)
    {
        var list = logged.OrderByDescending(r => r.TimeUtc ?? DateTime.MinValue).ToList();
        var free = dumps.Where(d => !d.name.EndsWith("-full.dmp", StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => d.utc).ToList();
        foreach (var r in list)
        {
            if (r.TimeUtc is not { } t || free.Count == 0) continue;
            int best = -1; var gap = DumpMatch;
            for (int i = 0; i < free.Count; i++)
            {
                var g = (free[i].utc - t).Duration();
                if (g <= gap) { gap = g; best = i; }
            }
            if (best < 0) continue;
            r.Dump = free[best].name;
            free.RemoveAt(best);
        }
        list.AddRange(free.Select(d => new CrashReport { TimeUtc = d.utc, Dump = d.name }));
        return list.OrderByDescending(r => r.TimeUtc ?? DateTime.MinValue).Take(MaxCrashes).ToList();
    }

    /// <summary>A stable id for the page (time and signature, no paths).</summary>
    public static string IdOf(CrashReport r) =>
        (r.TimeUtc?.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) ?? "x") + "-" + (r.Dump ?? Fnv(r.Title + r.Signature + r.Log).ToString("x8", CultureInfo.InvariantCulture));

    private static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (var c in s) { h ^= c; h *= 16777619; }
        return h;
    }

    // ---------- crashometry ----------

    /// <summary>data\cache\crashometry: [uint32 key length][uint32 value length][key][value] … The Rockstar ID in there is
    /// personal and dropped right here; a cut-off or broken file gives what was readable.</summary>
    public static Dictionary<string, string> ParseCrashometry(byte[]? data)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data == null) return d;
        int p = 0;
        while (p + 8 <= data.Length)
        {
            uint kl = BitConverter.ToUInt32(data, p), vl = BitConverter.ToUInt32(data, p + 4);
            p += 8;
            if (kl > 256 || vl > 4096 || p + kl + vl > data.Length) break;
            // AddCrashometry writes an empty value as a value length of 0: nothing to keep, the next record follows
            if (kl == 0 || vl == 0) { p += (int)(kl + vl); continue; }
            var k = Encoding.UTF8.GetString(data, p, (int)kl);
            var v = Encoding.UTF8.GetString(data, p + (int)kl, (int)vl);
            p += (int)(kl + vl);
            if (k == "RockstarId") continue;
            if (k == "did_render_mrt" && d.ContainsKey(k)) continue;
            d[k] = v;
        }
        return d;
    }

    private static readonly Regex ServerShape = new(@"^(?:https?://)?((?:cfx\.re/join/[a-z0-9]{3,12})|(?:[a-z0-9-]+\.)*[a-z0-9-]+(?::\d{2,5})?|\[[0-9a-f:]+\](?::\d{2,5})?)/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The server of the last session from crashometry: the join link if FiveM kept one, else IP:Port.</summary>
    public static string? ServerOf(IReadOnlyDictionary<string, string> crashometry)
    {
        foreach (var k in new[] { "last_server_url", "last_server" })
            if (crashometry.TryGetValue(k, out var v) && ServerShape.Match(v.Trim()) is { Success: true } m) return m.Groups[1].Value;
        return null;
    }

    // ---------- privacy and the text for support ----------

    /// <summary>The Windows user name out of a text that is meant for others: in profile paths and on its own.</summary>
    public static string Redact(string? text, string? userName, string? profilePath = null)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = text;
        if (!string.IsNullOrWhiteSpace(profilePath) && profilePath.Length > 3)
            s = s.Replace(profilePath.TrimEnd('\\'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        s = Regex.Replace(s, @"(?<=[\\/](?:Users|Benutzer)[\\/])[^\\/:*?""<>|\r\n]+", "<Nutzer>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!string.IsNullOrWhiteSpace(userName) && userName.Trim().Length >= 3)
            s = Regex.Replace(s, @"(?<![\p{L}\p{N}])" + Regex.Escape(userName.Trim()) + @"(?![\p{L}\p{N}])", "<Nutzer>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return s;
    }

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>A short text for Discord: time, build, signature, error text, cause; the Windows user name taken out.</summary>
    public static string SupportText(CrashReport r, CrashCause c, DateTime? localTime, string? userName, string? profilePath = null)
    {
        static string Code(string s) => s.Replace('`', '\'');
        var sb = new StringBuilder();
        sb.Append("**FiveM-Absturz**");
        if (localTime is { } t) sb.Append(" · ").Append(t.ToString("dd.MM.yyyy, HH:mm", De)).Append(" Uhr");
        sb.Append('\n');
        sb.Append("Build: ").Append(r.Build ?? "unbekannt").Append('\n');
        if (r.Signature != null) sb.Append("Signatur: `").Append(Code(r.Signature)).Append("`\n");
        if (r.Code != null && (r.Text == null || !r.Text.Contains(r.Code, StringComparison.Ordinal))) sb.Append("Fehlercode: ").Append(r.Code).Append('\n');
        if (r.Text != null) sb.Append("Fehler: ").Append(r.Text.Length > 300 ? r.Text[..297].TrimEnd() + "…" : r.Text).Append('\n');
        if (r.Signature == null && r.Text == null) sb.Append("Fehler: keine Details im Log").Append('\n');
        if (r.LegacyHash != null) sb.Append("Crash-Hash: ").Append(r.LegacyHash).Append('\n');
        if (r.ReportId != null) sb.Append("Report-ID: ").Append(r.ReportId).Append('\n');
        sb.Append("Vermutete Ursache: ").Append(c.Title);
        if (c.Sure == "unklar") sb.Append(" (unsicher)");
        sb.Append(" – laut OrbitTweak");
        return Redact(sb.ToString(), userName, profilePath);
    }

    /// <summary>When at least two of the recent crashes (last 14 days) share a known cause, that is worth one line.</summary>
    public static (string key, string title, int count, int of)? Pattern(IReadOnlyList<(DateTime? utc, CrashCause cause)> crashes, DateTime nowUtc)
    {
        var recent = crashes.Where(c => c.utc is { } t && nowUtc - t <= TimeSpan.FromDays(14)).ToList();
        if (recent.Count < 2) return null;
        var top = recent.Where(c => c.cause.Sure != "unklar").GroupBy(c => c.cause.Key)
            .Select(g => (g.Key, title: g.First().cause.Title, n: g.Count())).OrderByDescending(g => g.n).FirstOrDefault();
        return top.n >= 2 ? (top.Key, top.title, top.n, recent.Count) : null;
    }
}
