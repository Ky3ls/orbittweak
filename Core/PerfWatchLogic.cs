using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

// =====================================================================
//  Engpass-Analyse (2.31): what holds the frame rate while FiveM runs, the graphics card, the processor, a cap or the
//  memory. Plain rules without Windows calls, so they can be checked anywhere (tests/PerfWatchLogic.Tests); the
//  counters are read in Core/PerfWatch.cs, the host part is MainWindow.PerfWatch.cs.
//  OrbitTweak measures no FPS: it looks at who runs at the limit. That is said on the page as well.
// =====================================================================

/// <summary>
/// One look at the counters (every 2 s), each value the average since the look before. Gpu: the busiest 3D engine of the
/// game's own process in % (<see cref="Own"/>), else of the whole card; null when Windows gives no GPU values. Core: the
/// busiest logical processor, Cpu: all of them. VRAM: dedicated memory of the card the game runs on, null when unknown.
/// </summary>
internal readonly record struct PerfSample(double? Gpu, bool Own, double Core, double Cpu, double? VramMb, double? VramUsedMb, double RamMb, double RamUsedMb);

internal enum PerfKind { Gpu, Cpu, Cap, Mixed, Unknown }

/// <summary>
/// The totals of one measured round: counts per kind and sums, so two parts of the same round (OrbitTweak restarted
/// while the game ran on) add up exactly. Only looks while the game was in front count. Linked to the Spielzeit round
/// by <see cref="Id"/> (PlayRound.Id).
/// </summary>
internal sealed class PerfRound
{
    public long Id { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    /// <summary>Time measured with the game in front, in seconds.</summary>
    public double Secs { get; set; }
    public int N { get; set; }
    public int Gpu { get; set; }
    public int Cpu { get; set; }
    public int Cap { get; set; }
    public int Mixed { get; set; }
    public int Unknown { get; set; }
    /// <summary>Looks with a GPU value, and how many of them were the game's own.</summary>
    public int GpuN { get; set; }
    public int OwnN { get; set; }
    /// <summary>Looks with a known VRAM size, and how many of them found it full.</summary>
    public int VramN { get; set; }
    public int VramFull { get; set; }
    public int RamTight { get; set; }
    public double SumGpu { get; set; }
    public double SumCore { get; set; }
    public double SumCpu { get; set; }
    public double SumVram { get; set; }
    public double SumRam { get; set; }
    public double PeakGpu { get; set; }
    public double PeakCore { get; set; }
    public double PeakVram { get; set; }
    public double PeakRam { get; set; }
    public double PeakCpu { get; set; }
    public double VramMb { get; set; }
    public double PeakVramMb { get; set; }
    public double RamMb { get; set; }
    public double PeakRamMb { get; set; }

    /// <summary>Adds one look (already cleaned) that stands for dt seconds of play.</summary>
    public void Add(PerfSample s, double dt)
    {
        N++; Secs += dt;
        switch (PerfWatchLogic.Classify(s))
        {
            case PerfKind.Gpu: Gpu++; break;
            case PerfKind.Cpu: Cpu++; break;
            case PerfKind.Cap: Cap++; break;
            case PerfKind.Mixed: Mixed++; break;
            default: Unknown++; break;
        }
        if (s.Gpu is double g) { GpuN++; SumGpu += g; PeakGpu = Math.Max(PeakGpu, g); if (s.Own) OwnN++; }
        SumCore += s.Core; PeakCore = Math.Max(PeakCore, s.Core);
        SumCpu += s.Cpu; PeakCpu = Math.Max(PeakCpu, s.Cpu);
        if (PerfWatchLogic.VramPct(s) is double v)
        {
            VramN++; SumVram += v; PeakVram = Math.Max(PeakVram, v);
            if (v >= PerfWatchLogic.VramFullPct) VramFull++;
            VramMb = s.VramMb!.Value; PeakVramMb = Math.Max(PeakVramMb, s.VramUsedMb!.Value);
        }
        double r = PerfWatchLogic.RamPct(s);
        SumRam += r; PeakRam = Math.Max(PeakRam, r);
        if (r >= PerfWatchLogic.RamTightPct) RamTight++;
        if (s.RamMb > 0) RamMb = s.RamMb;
        PeakRamMb = Math.Max(PeakRamMb, s.RamUsedMb);
    }

    /// <summary>Another part of the same round joins this one.</summary>
    public void Merge(PerfRound o)
    {
        if (o.Start < Start) Start = o.Start;
        if (o.End > End) End = o.End;
        Secs += o.Secs; N += o.N;
        Gpu += o.Gpu; Cpu += o.Cpu; Cap += o.Cap; Mixed += o.Mixed; Unknown += o.Unknown;
        GpuN += o.GpuN; OwnN += o.OwnN; VramN += o.VramN; VramFull += o.VramFull; RamTight += o.RamTight;
        SumGpu += o.SumGpu; SumCore += o.SumCore; SumCpu += o.SumCpu; SumVram += o.SumVram; SumRam += o.SumRam;
        PeakGpu = Math.Max(PeakGpu, o.PeakGpu); PeakCore = Math.Max(PeakCore, o.PeakCore);
        PeakVram = Math.Max(PeakVram, o.PeakVram); PeakRam = Math.Max(PeakRam, o.PeakRam); PeakCpu = Math.Max(PeakCpu, o.PeakCpu);
        if (o.VramMb > 0 && o.End >= End) VramMb = o.VramMb; else if (VramMb <= 0) VramMb = o.VramMb;
        if (o.RamMb > 0 && o.End >= End) RamMb = o.RamMb; else if (RamMb <= 0) RamMb = o.RamMb;
        PeakVramMb = Math.Max(PeakVramMb, o.PeakVramMb); PeakRamMb = Math.Max(PeakRamMb, o.PeakRamMb);
    }

    public PerfRound Clone() => (PerfRound)MemberwiseClone();

    /// <summary>Sums to one decimal, so the file stays small and readable.</summary>
    public void Tidy()
    {
        static double T(double x) => Math.Round(x, 1);
        Secs = T(Secs); SumGpu = T(SumGpu); SumCore = T(SumCore); SumCpu = T(SumCpu); SumVram = T(SumVram); SumRam = T(SumRam);
        PeakGpu = T(PeakGpu); PeakCore = T(PeakCore); PeakCpu = T(PeakCpu); PeakVram = T(PeakVram); PeakRam = T(PeakRam);
        VramMb = Math.Round(VramMb); PeakVramMb = Math.Round(PeakVramMb); RamMb = Math.Round(RamMb); PeakRamMb = Math.Round(PeakRamMb);
    }
}

/// <summary>perf.json: the last <see cref="Keep"/> measured rounds, written whole to a temp file and then put in place.</summary>
internal sealed class PerfLog
{
    public const int Keep = 50;
    private static readonly JsonSerializerOptions Opts = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    private sealed class Shape { public int V { get; set; } = 1; public bool Off { get; set; } public List<PerfRound>? Rounds { get; set; } }

    public string? File { get; }
    public List<PerfRound> Rounds { get; private set; } = new();
    /// <summary>The player switched the measuring off.</summary>
    public bool Off { get; set; }
    /// <summary>The file is there but could not be opened: the log starts empty and must not be written.</summary>
    public bool Unreadable { get; private set; }

    public PerfLog(string? file = null) { File = file; }

    /// <summary>Reads the file. A broken one is kept aside as .bad and the log starts empty.</summary>
    public static PerfLog Load(string file)
    {
        var log = new PerfLog(file);
        var src = System.IO.File.Exists(file) ? file : System.IO.File.Exists(file + ".tmp") ? file + ".tmp" : null;
        if (src == null) return log;
        byte[] bytes;
        for (int i = 0; ; i++)
        {
            try { bytes = System.IO.File.ReadAllBytes(src); break; }
            catch (IOException) when (i < 2) { Thread.Sleep(150); }   // a virus scanner or backup tool has it open for a moment
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log.Unreadable = true; return log; }
        }
        try
        {
            var s = JsonSerializer.Deserialize<Shape>(bytes, Opts);
            log.Off = s?.Off ?? false;
            foreach (var r in (s?.Rounds ?? new()).Where(r => r != null && r.N > 0 && r.End >= r.Start).OrderBy(r => r.Start)) log.Put(r);
        }
        catch
        {
            try { System.IO.File.Copy(src, file + ".bad", true); } catch { }
        }
        return log;
    }

    /// <summary>A finished round: joins the one with the same id (an earlier part of it), else it is added.</summary>
    public void Put(PerfRound r)
    {
        var same = r.Id != 0 ? Rounds.FirstOrDefault(x => x.Id == r.Id) : null;
        if (same != null) same.Merge(r); else Rounds.Add(r);
        Trim();
    }

    /// <summary>Only the last <see cref="Keep"/> rounds stay (by start time).</summary>
    public void Trim()
    {
        if (Rounds.Count <= Keep) return;
        Rounds = Rounds.OrderBy(r => r.Start).ToList();
        Rounds.RemoveRange(0, Rounds.Count - Keep);
    }

    /// <summary>Rounds whose Spielzeit round is gone (removed or the history cleared) go as well. keep: the round being measured.</summary>
    public int Prune(ICollection<long> known, long keep) => Rounds.RemoveAll(r => r.Id != keep && !known.Contains(r.Id));

    public int Clear() { int n = Rounds.Count; Rounds.Clear(); return n; }

    /// <summary>
    /// The file's bytes. open: the round being measured, written along when it is long enough, so a crash or power cut
    /// costs at most the time since the last save (read back, it is a finished round; the rest of it joins later).
    /// </summary>
    public byte[] Serialize(PerfRound? open = null)
    {
        var list = Rounds.Select(r => r.Clone()).ToList();
        if (open != null && open.N >= PerfRecorder.MinLooks)
        {
            var o = open.Clone(); o.Tidy();
            var same = list.FirstOrDefault(x => x.Id == o.Id && o.Id != 0);
            if (same != null) same.Merge(o); else list.Add(o);
        }
        foreach (var r in list) r.Tidy();
        return JsonSerializer.SerializeToUtf8Bytes(new Shape { Off = Off, Rounds = list.OrderBy(r => r.Start).TakeLast(Keep).ToList() }, Opts);
    }

    /// <summary>Writes the whole file to "file.tmp", flushed to the disk, and then moves it over the old one.</summary>
    public static void Write(string file, byte[] data)
    {
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = file + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(data);
            fs.Flush(true);
        }
        System.IO.File.Move(tmp, file, true);
    }
}

/// <summary>
/// Follows the measuring: the host calls <see cref="Look"/> after every look at the game (every 2 s) with the Spielzeit
/// round being played. A new round id ends the measured round (game closed, server changed, the PC slept).
/// </summary>
internal sealed class PerfRecorder
{
    /// <summary>Seconds between two looks.</summary>
    public const double Interval = 2;
    /// <summary>A longer gap (the app hung, the PC slept) counts as one look, not as the whole gap.</summary>
    public const double MaxStep = 10;
    /// <summary>A round is kept with at least this many looks in front (one minute).</summary>
    public const int MinLooks = 30;
    /// <summary>The live "right now" is the most common kind of the last looks (10 s), so it does not flicker.</summary>
    public const int NowLooks = 5;

    public PerfLog Log { get; }
    public PerfRound? Open { get; private set; }
    /// <summary>The last look with values (while the game was in front).</summary>
    public PerfSample? Last { get; private set; }
    public bool Front { get; private set; }
    /// <summary>The Spielzeit round of the last look, 0 when no game runs.</summary>
    public long RoundId { get; private set; }
    private DateTimeOffset? _at;
    private readonly List<PerfKind> _recent = new();

    public PerfRecorder(PerfLog log) { Log = log; }

    /// <summary>
    /// One look. s: null when this look brought no values (the first one after opening or after a pause); front: the
    /// game was in front. Returns the round that was finished and kept by this look, if any.
    /// </summary>
    public PerfRound? Look(long roundId, DateTimeOffset now, PerfSample? s, bool front)
    {
        PerfRound? done = null;
        if (Open != null && Open.Id != roundId) done = Finish();
        RoundId = roundId;
        double dt = _at is { } a && now > a && (now - a).TotalSeconds <= MaxStep ? (now - a).TotalSeconds : Interval;
        _at = now;
        Front = front;
        if (!front) { _recent.Clear(); return done; }
        if (s is not PerfSample raw || roundId == 0) return done;
        var x = PerfWatchLogic.Clean(raw);
        Last = x;
        Open ??= new PerfRound { Id = roundId, Start = now - TimeSpan.FromSeconds(dt), End = now };
        Open.Add(x, dt);
        if (now > Open.End) Open.End = now;
        _recent.Add(PerfWatchLogic.Classify(x));
        if (_recent.Count > NowLooks) _recent.RemoveAt(0);
        return done;
    }

    /// <summary>What limits right now: the most common kind of the last looks, on a tie the latest. Null before the first values.</summary>
    public PerfKind? Now
    {
        get
        {
            if (_recent.Count == 0) return null;
            int best = -1; PerfKind kind = _recent[^1];
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                int n = _recent.Count(k => k == _recent[i]);
                if (n > best) { best = n; kind = _recent[i]; }
            }
            return kind;
        }
    }

    /// <summary>The measured round is over: kept when long enough. Returns the kept round (as it is in the log now).</summary>
    public PerfRound? Finish()
    {
        var r = Open; Open = null; _recent.Clear();
        if (r == null || r.N < MinLooks) return null;
        r.Tidy();
        Log.Put(r);
        return Log.Rounds.FirstOrDefault(x => x.Id == r.Id) ?? r;
    }

    /// <summary>The game is gone (or measuring was switched off): the round is finished and nothing is left running.</summary>
    public PerfRound? Stop()
    {
        var r = Finish();
        Last = null; _at = null; Front = false; RoundId = 0;
        return r;
    }
}

// ---------- what the page gets ("perf.get" and the "perf" event); times in ms since 1970 ----------
internal sealed record PerfShares(double Gpu, double Cpu, double Cap, double Mixed, double Unknown);
internal sealed record PerfLoad(double? Gpu, double Core, double Cpu, double? Vram, double Ram);
/// <summary>One thing to try. Go: where the page leads (presets, tuning, fps, tex, bgbrake, cleaner, autostart, diag), null for none.</summary>
internal sealed record PerfTip(string Text, string? Go, string? Label);
internal sealed record PerfVerdict(string Key, string Short, string Title, string Text, List<PerfTip> Help, string? NoHelp, List<string> Notes);
internal sealed record PerfItem(long Id, long Start, long End, double Secs, string? Server, PerfShares Shares, double VramFull, double RamTight,
    PerfLoad Avg, PerfLoad Peak, double? VramGb, double? PeakVramGb, double RamGb, double PeakRamGb, bool Own, PerfVerdict Verdict);
internal sealed record PerfNow(double? Gpu, bool Own, double Core, double Cpu, double? Vram, double? VramGb, double? VramUsedGb,
    double Ram, double RamGb, double RamUsedGb, bool VramFull, bool RamTight);
/// <summary>State: warmup (no values yet), live, paused (the game is not in front).</summary>
internal sealed record PerfLive(long Round, string State, string? Kind, string Title, string Text, PerfNow? Now, long? Since, double Secs, PerfShares? Shares);

internal static class PerfWatchLogic
{
    public const double GpuFullPct = 95, GpuBusyPct = 85, CoreFullPct = 90, CpuFullPct = 90, CapCpuPct = 80, VramFullPct = 95, RamTightPct = 90;
    /// <summary>A share of the time from which one limit is "the" limit of a round.</summary>
    public const double ClearShare = .5;
    /// <summary>VRAM full or RAM tight for this share of the time is what the round is about.</summary>
    public const double MemShare = .2;
    /// <summary>Less memory trouble than <see cref="MemShare"/>, but from this share on it is mentioned.</summary>
    public const double NoteShare = .05;
    /// <summary>Rounds shorter than this (measured) get a note that the verdict is rough.</summary>
    public const double ShortSecs = 300;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string Nb = " ";   // a number never parts from its unit

    // ---------- one look ----------
    private static double Pct(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 100) : 0;
    private static double? Pct(double? v) => v is double x && double.IsFinite(x) ? Math.Clamp(x, 0, 100) : null;
    private static double Pos(double v) => double.IsFinite(v) && v > 0 ? v : 0;

    /// <summary>Values out of range (counters can briefly report more than 100 %, or nonsense) are put back in range.</summary>
    public static PerfSample Clean(PerfSample s)
    {
        double? total = s.VramMb is double t && double.IsFinite(t) && t > 0 ? t : null;
        double? used = total != null && s.VramUsedMb is double u && double.IsFinite(u) && u >= 0 ? u : null;
        double ram = Pos(s.RamMb), ramUsed = Pos(s.RamUsedMb);
        if (ram > 0) ramUsed = Math.Min(ramUsed, ram);
        var gpu = Pct(s.Gpu);
        return new PerfSample(gpu, s.Own && gpu != null, Pct(s.Core), Pct(s.Cpu), used != null ? total : null, used, ram, ramUsed);
    }

    public static double? VramPct(PerfSample s) => s.VramMb is double t && t > 0 && s.VramUsedMb is double u ? Math.Clamp(u / t * 100, 0, 100) : null;
    public static double RamPct(PerfSample s) => s.RamMb > 0 ? Math.Clamp(s.RamUsedMb / s.RamMb * 100, 0, 100) : 0;

    /// <summary>
    /// What held the frame rate during one look. GPU at 95 % or more: the card. Below 85 % with the busiest core or the
    /// whole processor at 90 % or more: the processor. Both with room (card below 85 %, processor below 80 %): something
    /// caps the frame rate (an FPS limit, V-Sync, a menu). Everything in between is no clear limit.
    /// </summary>
    public static PerfKind Classify(PerfSample s)
    {
        if (Pct(s.Gpu) is not double g) return PerfKind.Unknown;
        double core = Pct(s.Core), cpu = Pct(s.Cpu);
        if (g >= GpuFullPct) return PerfKind.Gpu;
        if (g < GpuBusyPct && (core >= CoreFullPct || cpu >= CpuFullPct)) return PerfKind.Cpu;
        if (g < GpuBusyPct && core < CapCpuPct && cpu < CapCpuPct) return PerfKind.Cap;
        return PerfKind.Mixed;
    }

    public static bool VramFull(PerfSample s) => VramPct(s) >= VramFullPct;
    public static bool RamTight(PerfSample s) => RamPct(s) >= RamTightPct;

    public static string Key(PerfKind k) => k switch { PerfKind.Gpu => "gpu", PerfKind.Cpu => "cpu", PerfKind.Cap => "cap", PerfKind.Mixed => "mixed", _ => "unknown" };

    // ---------- counter instances (PDH) ----------
    private static readonly Regex EngineRx = new(@"^pid_(\d+)_luid_(0x[0-9a-f]+_0x[0-9a-f]+)_phys_(\d+)_eng_(\d+)_engtype_(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AdapterRx = new(@"^luid_(0x[0-9a-f]+_0x[0-9a-f]+)_phys_(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"pid_1234_luid_0x00000000_0x0000D1B6_phys_0_eng_0_engtype_3D" → its parts (luid in lower case), null for anything else.</summary>
    public static (int pid, string luid, int phys, int eng, string type)? ParseEngine(string? name)
    {
        var m = name == null ? null : EngineRx.Match(name);
        if (m == null || !m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, Inv, out var pid)
            || !int.TryParse(m.Groups[3].Value, NumberStyles.None, Inv, out var phys) || !int.TryParse(m.Groups[4].Value, NumberStyles.None, Inv, out var eng)) return null;
        return (pid, m.Groups[2].Value.ToLowerInvariant(), phys, eng, m.Groups[5].Value);
    }

    /// <summary>A DXGI adapter LUID as the counters name it: "0x{high:x8}_0x{low:x8}".</summary>
    public static string Luid(uint low, int high) => $"0x{(uint)high:x8}_0x{low:x8}";

    /// <summary>
    /// The 3D load from "\GPU Engine(*)\Utilization Percentage": per engine (adapter, physical GPU, engine) the processes
    /// add up, as in the Task Manager. own: the busiest 3D engine of process pid (null when it has none), total: the
    /// busiest 3D engine over all processes, luid: the adapter of the game's busiest engine (else of the busiest one).
    /// </summary>
    public static (double? own, double? total, string? luid) GpuLoad(IEnumerable<(string name, double value)> items, int pid)
    {
        var own = new Dictionary<(string luid, int phys, int eng), double>(); var all = new Dictionary<(string luid, int phys, int eng), double>();
        foreach (var (name, value) in items)
        {
            if (ParseEngine(name) is not { } e || !e.type.Equals("3D", StringComparison.OrdinalIgnoreCase) || !double.IsFinite(value)) continue;
            var key = (e.luid, e.phys, e.eng);
            double v = Math.Max(0, value);
            all[key] = all.GetValueOrDefault(key) + v;
            if (e.pid == pid) own[key] = own.GetValueOrDefault(key) + v;
        }
        static KeyValuePair<(string luid, int phys, int eng), double>? Top(Dictionary<(string luid, int phys, int eng), double> d) =>
            d.Count == 0 ? null : d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.luid, StringComparer.Ordinal).ThenBy(kv => kv.Key.phys).ThenBy(kv => kv.Key.eng).First();
        var o = Top(own); var t = Top(all);
        return (o is { } a ? Math.Min(100, a.Value) : null, t is { } b ? Math.Min(100, b.Value) : null, (o ?? t)?.Key.luid);
    }

    /// <summary>
    /// Dedicated VRAM in use from "\GPU Adapter Memory(*)\Dedicated Usage" (bytes) of the adapter luid (all its physical
    /// GPUs together); without a luid, or when it is not listed, the adapter that uses the most. Null when nothing fits.
    /// </summary>
    public static (double bytes, string luid)? AdapterUse(IEnumerable<(string name, double value)> items, string? luid)
    {
        var per = new Dictionary<string, double>();
        foreach (var (name, value) in items)
        {
            var m = AdapterRx.Match(name ?? "");
            if (!m.Success || !double.IsFinite(value)) continue;
            var l = m.Groups[1].Value.ToLowerInvariant();
            per[l] = per.GetValueOrDefault(l) + Math.Max(0, value);
        }
        if (per.Count == 0) return null;
        if (luid != null && per.TryGetValue(luid.ToLowerInvariant(), out var b)) return (b, luid.ToLowerInvariant());
        var top = per.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
        return (top.Value, top.Key);
    }

    /// <summary>
    /// "\Processor Information(*)\% Processor Time" (instances "0,0", "0,1", "0,_Total", "_Total") or "\Processor(*)"
    /// ("0", "1", "_Total"): the whole processor and its busiest logical processor. Null without any value.
    /// </summary>
    public static (double total, double core)? CpuLoad(IEnumerable<(string name, double value)> items)
    {
        double? total = null; double core = -1, sum = 0; int n = 0;
        foreach (var (name, value) in items)
        {
            if (!double.IsFinite(value) || string.IsNullOrEmpty(name)) continue;
            double v = Math.Clamp(value, 0, 100);
            if (name == "_Total") { total = v; continue; }
            if (name.Contains("_Total", StringComparison.Ordinal)) continue;   // one processor group
            core = Math.Max(core, v); sum += v; n++;
        }
        if (n == 0 && total == null) return null;
        return (total ?? sum / n, n > 0 ? core : total!.Value);
    }

    // ---------- a round for the page ----------
    private static double Share(int part, int of) => of > 0 ? Math.Round((double)part / of, 3) : 0;
    public static PerfShares Shares(PerfRound r) => new(Share(r.Gpu, r.N), Share(r.Cpu, r.N), Share(r.Cap, r.N), Share(r.Mixed, r.N), Share(r.Unknown, r.N));
    public static double VramShare(PerfRound r) => Share(r.VramFull, r.VramN);
    public static double RamShare(PerfRound r) => Share(r.RamTight, r.N);
    private static double? Avg(double sum, int n) => n > 0 ? Math.Round(sum / n, 1) : null;
    private static double? Gb(double mb) => mb > 0 ? Math.Round(mb / 1024, 1) : null;

    public static PerfItem Item(PerfRound r, string? server) => new(
        r.Id, r.Start.ToUnixTimeMilliseconds(), r.End.ToUnixTimeMilliseconds(), Math.Round(r.Secs), server,
        Shares(r), VramShare(r), RamShare(r),
        new PerfLoad(Avg(r.SumGpu, r.GpuN), Avg(r.SumCore, r.N) ?? 0, Avg(r.SumCpu, r.N) ?? 0, Avg(r.SumVram, r.VramN), Avg(r.SumRam, r.N) ?? 0),
        new PerfLoad(r.GpuN > 0 ? Math.Round(r.PeakGpu, 1) : null, Math.Round(r.PeakCore, 1), Math.Round(r.PeakCpu, 1), r.VramN > 0 ? Math.Round(r.PeakVram, 1) : null, Math.Round(r.PeakRam, 1)),
        r.VramN > 0 ? Gb(r.VramMb) : null, r.VramN > 0 ? Gb(r.PeakVramMb) : null, Gb(r.RamMb) ?? 0, Gb(r.PeakRamMb) ?? 0,
        r.GpuN > 0 && r.OwnN * 2 >= r.GpuN, Verdict(r));

    // ---------- German formats ----------
    /// <summary>A share as "62 %", a tiny one as "< 1 %".</summary>
    public static string PctText(double share) => share > 0 && share < .005 ? "<" + Nb + "1" + Nb + "%" : Math.Round(share * 100).ToString("0", Inv) + Nb + "%";
    /// <summary>A load in % as "87 %".</summary>
    public static string LoadText(double? pct) => pct is double p ? Math.Round(p).ToString("0", Inv) + Nb + "%" : "–";
    /// <summary>MB as "7,1 GB".</summary>
    public static string GbText(double mb) => (mb / 1024).ToString("0.0", Inv).Replace('.', ',') + Nb + "GB";

    /// <summary>"Grafikkarte am Limit 62 %, Prozessor 21 %, gedeckelt 9 %": the kinds that took at least 1 % of the time.</summary>
    public static string Mix(PerfShares s)
    {
        var parts = new List<string>();
        void P(string what, double share) { if (share >= .01) parts.Add(what + " " + PctText(share)); }
        P("Grafikkarte am Limit", s.Gpu); P("Prozessor", s.Cpu); P("gedeckelt", s.Cap); P("gemischt", s.Mixed);
        return string.Join(", ", parts);
    }

    /// <summary>
    /// The verdict of a round: what it was about, what helps (with the page that does it) and what does not. Memory that
    /// ran full comes first (it stutters whatever else holds the frame rate), then the limit that held most of the time.
    /// </summary>
    public static PerfVerdict Verdict(PerfRound r)
    {
        var s = Shares(r);
        double vf = VramShare(r), rt = RamShare(r);
        int known = r.N - r.Unknown;
        double? avgGpu = Avg(r.SumGpu, r.GpuN), avgCore = Avg(r.SumCore, r.N), avgRam = Avg(r.SumRam, r.N);
        string key;
        if (vf >= MemShare) key = "vram";
        else if (rt >= MemShare) key = "ram";
        else if (known * 2 < r.N || known == 0) key = "nogpu";
        else
        {
            var tops = new[] { ("gpu", r.Gpu), ("cpu", r.Cpu), ("cap", r.Cap), ("mixed", r.Mixed) }.OrderByDescending(x => x.Item2).ToArray();
            key = (double)tops[0].Item2 / known >= ClearShare ? tops[0].Item1 : "mixed";
        }

        var help = new List<PerfTip>(); var notes = new List<string>();
        string shortT, title, text; string? no;
        switch (key)
        {
            case "gpu":
                shortT = "Grafikkarte"; title = "Deine Grafikkarte war das Limit";
                text = $"In {PctText(s.Gpu)} der Zeit lief sie am Anschlag, im Schnitt zu {LoadText(avgGpu)}. Das ist bei hohen Einstellungen ganz normal: Sie gibt alles, und mehr FPS gibt es nur, wenn sie weniger zu rechnen hat.";
                help.Add(new("Ein Preset tiefer, etwa „Ausgewogen“ statt „Kino“, oder einzeln runter: MSAA, Gras, Schatten und Post-FX kosten in GTA am meisten.", "presets", "Zu den Presets"));
                help.Add(new("Mit NVIDIA-Karte: Ein FPS-Limit knapp unter deinen üblichen FPS nimmt ihr die Spitzen. Das bringt gleichmäßigere Bilder und weniger Hitze, aber keine FPS.", "fps", "Zum FPS-Limit"));
                no = "RAM freigeben, der Cleaner und Windows-Tweaks ändern daran nichts: Die Karte rechnet schon so schnell sie kann.";
                break;
            case "cpu":
                shortT = "Prozessor"; title = "Dein Prozessor hat gebremst";
                text = $"In {PctText(s.Cpu)} der Zeit hat die Grafikkarte auf ihn gewartet (Grafikkarte im Schnitt {LoadText(avgGpu)}, stärkster Kern {LoadText(avgCore)}). In FiveM ist das oft ein einzelner Kern, den Skripte, viele Spieler und Autos in der Nähe auslasten.";
                help.Add(new("„Hintergrund-Apps ausbremsen“ stellt Browser und Launcher hinter das Spiel. Das hilft genau dann, wenn der Prozessor voll ist.", "bgbrake", "Zum Tweak"));
                help.Add(new("Bevölkerungsdichte und Distanz-Skalierung im Feintuning runter entlasten den Prozessor etwas. Auf vielen Servern bestimmt der Server die Bevölkerung aber selbst.", "tuning", "Zum Feintuning"));
                help.Add(new("Schließ, was nebenbei rechnet: Videos im Browser, Streams, Aufnahmen.", null, null));
                no = "Niedrigere Auflösung, weniger Kantenglättung oder kleinere Texturen bringen hier kaum FPS, sie entlasten nur die Grafikkarte, die ohnehin wartet. Viele Skripte und Spieler auf vollen Servern kosten Prozessorzeit, daran kann kein Tool etwas ändern.";
                break;
            case "cap":
                shortT = "Gedeckelt"; title = "Deine FPS waren gedeckelt";
                text = $"In {PctText(s.Cap)} der Zeit hatten Grafikkarte (im Schnitt {LoadText(avgGpu)}) und Prozessor Luft. Meist hält dann ein FPS-Limit oder V-Sync die Bildrate fest, und das ist gut so: gleichmäßige Bilder, weniger Hitze und Lüfterlärm.";
                help.Add(new("Willst du mehr FPS, setz das Limit höher oder schalte V-Sync in den GTA-Einstellungen aus. Ohne V-Sync kann das Bild reißen.", "fps", "Zum FPS-Limit"));
                no = "Tweaks, der Cleaner und ein kleineres Preset bringen hier keine FPS, solange das Limit greift.";
                notes.Add("Hast du weder ein Limit noch V-Sync an, bremst eher ein einzelner Prozessorkern, den Windows zwischen den Kernen hin- und herschiebt. Auch Menüs und Ladebildschirme sehen so aus.");
                break;
            case "vram":
                shortT = "VRAM voll"; title = "Dein Grafikspeicher war voll";
                text = $"In {PctText(vf)} der Zeit war er zu 95{Nb}% oder mehr belegt (Spitze {GbText(r.PeakVramMb)} von {GbText(r.VramMb)}). Dann muss Windows Texturen in den Arbeitsspeicher auslagern, das gibt oft Nachladeruckler und matschige Texturen.";
                help.Add(new("Texturbudget eine Stufe runter: FiveM hält dann weniger Texturen im Grafikspeicher.", "tex", "Zum Texturbudget"));
                help.Add(new("Texturen im Feintuning von „Sehr hoch“ auf „Hoch“.", "tuning", "Zum Feintuning"));
                help.Add(new("Schließ nebenbei Programme, die Grafikspeicher belegen: Browser mit Videos, NVIDIA Broadcast, Aufnahme-Tools.", null, null));
                no = "„RAM freigeben“ im Cleaner leert den Grafikspeicher nicht.";
                break;
            case "ram":
                shortT = "RAM knapp"; title = "Dein Arbeitsspeicher war knapp";
                text = $"In {PctText(rt)} der Zeit waren 90{Nb}% oder mehr belegt (Spitze {GbText(r.PeakRamMb)} von {GbText(r.RamMb)}). Dann lagert Windows auf die Festplatte aus, das gibt Nachladeruckler.";
                help.Add(new("Schließ, was nebenbei viel Speicher braucht: Browser mit vielen Tabs, Launcher, Aufnahme-Tools. Im Autostart siehst du, was ungefragt mitläuft.", "autostart", "Zum Autostart"));
                help.Add(new("„RAM freigeben“ im Cleaner schafft kurz Luft, hält aber nicht lange: Die Programme holen sich den Speicher zurück.", "cleaner", "Zum Cleaner"));
                if (r.RamMb > 0 && r.RamMb < 14 * 1024)
                    help.Add(new($"Mit {GbText(r.RamMb)} ist dein PC für FiveM knapp bemessen. Auf Dauer hilft nur mehr Arbeitsspeicher, mit 16{Nb}GB ist meist Ruhe.", null, null));
                no = "Windows-Tweaks und ein kleineres Preset ändern daran wenig.";
                break;
            case "nogpu":
                shortT = "Ohne GPU-Werte"; title = "Ohne Werte der Grafikkarte kein Urteil";
                text = $"Windows hat für deine Grafikkarte keine Auslastung geliefert, dafür braucht es einen aktuellen Treiber. Gemessen wurde trotzdem: stärkster Prozessorkern im Schnitt {LoadText(avgCore)}, Arbeitsspeicher {LoadText(avgRam)}.";
                help.Add(new("Grafiktreiber aktualisieren, dann misst OrbitTweak ab der nächsten Runde auch die Grafikkarte.", "diag", "Zur Diagnose"));
                no = null;
                break;
            default:
            {
                shortT = "Gemischt"; title = "Kein klarer Engpass";
                bool busy = known > 0 && (double)r.Mixed / known >= ClearShare;   // mostly busy, never at the limit
                text = busy
                    ? "Grafikkarte und Prozessor haben beide viel gearbeitet, aber keiner lag lange am Anschlag. Für deine Einstellungen ist dein PC gut im Gleichgewicht."
                    : $"Mal war es die Grafikkarte, mal der Prozessor, keiner von beiden lange genug für ein klares Urteil ({Mix(s)}).";
                help.Add(new("Hier gibt es keinen einzelnen Hebel. Ruckelt es trotzdem, findet die Diagnose oft die Ursache, etwa einen alten Treiber oder einen vollen Cache.", "diag", "Zur Diagnose"));
                no = "Einzelne Tweaks bringen hier selten spürbar etwas.";
                break;
            }
        }
        if (key is "vram" or "ram" && known > 0 && Mix(s) is { Length: > 0 } mix) notes.Add($"Daneben: {mix}.");
        if (key != "vram" && vf >= NoteShare) notes.Add($"Der Grafikspeicher war zeitweise voll ({PctText(vf)} der Zeit).");
        if (key != "ram" && rt >= NoteShare) notes.Add($"Der Arbeitsspeicher war zeitweise knapp ({PctText(rt)} der Zeit).");
        if (key != "nogpu" && r.GpuN > 0 && r.OwnN * 2 < r.GpuN) notes.Add("Die Werte der Grafikkarte gelten für die ganze Karte, nicht nur für FiveM.");
        if (r.Secs < ShortSecs) notes.Add("Kurze Runde, nimm das Urteil als groben Hinweis.");
        return new PerfVerdict(key, shortT, title, text, help, no, notes);
    }

    // ---------- right now ----------
    /// <summary>The live headline and its one line.</summary>
    public static (string title, string text) NowText(string state, PerfKind? kind) => state switch
    {
        "warmup" => ("Messung läuft an", "Die ersten Werte kommen in ein paar Sekunden."),
        "paused" => ("Pausiert", "FiveM ist gerade nicht vorne. Gemessen wird nur, solange du spielst."),
        _ => kind switch
        {
            PerfKind.Gpu => ("Grafikkarte am Limit", "Sie läuft am Anschlag und bestimmt gerade deine FPS."),
            PerfKind.Cpu => ("Prozessor bremst", "Die Grafikkarte wartet auf den Prozessor, meist auf einen einzelnen Kern."),
            PerfKind.Cap => ("Gedeckelt", "Beide haben Luft: Ein FPS-Limit oder V-Sync hält die Bildrate fest."),
            PerfKind.Mixed => ("Kein klarer Engpass", "Grafikkarte und Prozessor arbeiten, keiner liegt am Anschlag."),
            _ => ("Ohne Werte der Grafikkarte", "Windows liefert für deine Grafikkarte gerade keine Auslastung."),
        },
    };

    /// <summary>What the page shows while a round is measured; null when no game runs.</summary>
    public static PerfLive? Live(PerfRecorder p)
    {
        if (p.RoundId == 0) return null;
        string state = p.Last == null ? "warmup" : !p.Front ? "paused" : "live";
        var kind = state == "live" ? p.Now : null;
        var (title, text) = NowText(state, kind);
        PerfNow? now = null;
        if (p.Last is PerfSample s)
        {
            var v = VramPct(s);
            now = new PerfNow(s.Gpu is double g ? Math.Round(g) : null, s.Own, Math.Round(s.Core), Math.Round(s.Cpu), v is double x ? Math.Round(x) : null,
                v != null ? Gb(s.VramMb!.Value) : null, v != null ? Math.Round(s.VramUsedMb!.Value / 1024, 1) : null,
                Math.Round(RamPct(s)), Gb(s.RamMb) ?? 0, Math.Round(s.RamUsedMb / 1024, 1), VramFull(s), RamTight(s));
        }
        var o = p.Open;
        return new PerfLive(p.RoundId, state, kind is PerfKind k ? Key(k) : null, title, text, now, o?.Start.ToUnixTimeMilliseconds(), o != null ? Math.Round(o.Secs) : 0, o != null ? Shares(o) : null);
    }
}
