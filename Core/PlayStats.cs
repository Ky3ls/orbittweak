using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitTweak.Core;

// =====================================================================
//  Play time ("Spielzeit"): one round per game process and server, kept in playtime.json.
//  Plain logic without Windows calls, so it can be checked anywhere (tests/ClipLogic.Tests).
// =====================================================================

/// <summary>
/// One round: one GTA game process on one server (a server change splits it). Times carry their UTC offset, so a round
/// over a clock change keeps its real length, and days are cut at the local midnight of the time it was played.
/// </summary>
internal sealed class PlayRound
{
    public long Id { get; set; }
    public DateTimeOffset Start { get; set; }
    /// <summary>When it ended; while it is open, the last moment the game was seen running.</summary>
    public DateTimeOffset End { get; set; }
    /// <summary>"cfx:abc123" or "ip:host:port", null while the server is not known.</summary>
    public string? Key { get; set; }
    /// <summary>The server's name without colour codes.</summary>
    public string? Server { get; set; }
    public int Kills { get; set; }
    public int Clips { get; set; }
    public string? Build { get; set; }
    /// <summary>Still being played when it was saved. Found that way on the next start, it ended at <see cref="End"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Open { get; set; }
    /// <summary>While open: the game process and when it started, so a restart of OrbitTweak alone can take the round up again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Pid { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? GameStart { get; set; }

    [JsonIgnore] public double Secs => Math.Max(0, (End - Start).TotalSeconds);
}

/// <summary>The rounds and their file: written whole to a temp file and then put in place, so a crash never leaves half a file.</summary>
internal sealed class PlayLog
{
    public const int Keep = 5000;
    private static readonly JsonSerializerOptions Opts = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    private sealed class Shape { public int V { get; set; } = 1; public List<PlayRound>? Rounds { get; set; } }

    /// <summary>The file, null for a log that only lives in memory.</summary>
    public string? File { get; }
    public List<PlayRound> Rounds { get; private set; } = new();

    public PlayLog(string? file = null) { File = file; }

    /// <summary>Reads the file. An unreadable one is kept aside as .bad and the log starts empty.</summary>
    public static PlayLog Load(string file)
    {
        var log = new PlayLog(file);
        // a first save that was cut off between writing and putting it in place leaves only the temp file
        var src = System.IO.File.Exists(file) ? file : System.IO.File.Exists(file + ".tmp") ? file + ".tmp" : null;
        if (src == null) return log;
        try
        {
            var s = JsonSerializer.Deserialize<Shape>(System.IO.File.ReadAllBytes(src), Opts);
            log.Rounds = (s?.Rounds ?? new()).Where(r => r != null && r.End >= r.Start).OrderBy(r => r.Start).ToList();
            log.Trim();
        }
        catch
        {
            try { System.IO.File.Copy(src, file + ".bad", true); } catch { }
        }
        return log;
    }

    /// <summary>Adds a round with an id of its own (its start in ms, made unique).</summary>
    public void Add(PlayRound r)
    {
        long id = r.Start.ToUnixTimeMilliseconds();
        while (Rounds.Any(x => x.Id == id)) id++;
        r.Id = id;
        Rounds.Add(r);
        Trim();
    }

    /// <summary>Only the last <see cref="Keep"/> rounds stay (by start time).</summary>
    public void Trim()
    {
        if (Rounds.Count <= Keep) return;
        Rounds = Rounds.OrderBy(r => r.Start).ToList();
        Rounds.RemoveRange(0, Rounds.Count - Keep);
    }

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(new Shape { Rounds = Rounds }, Opts);

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
/// Follows the game: a round starts with a GTA game process and ends with it, a server change splits it, kills and clips
/// are added to the round being played. The host calls <see cref="Tick"/> after every look at the game (every 2 s).
/// </summary>
internal sealed class PlayRecorder
{
    public PlayLog Log { get; }
    /// <summary>The round being played now (it is also in the log, marked open).</summary>
    public PlayRound? Open { get; private set; }
    /// <summary>A round was finished and kept (60 s or more).</summary>
    public Action<PlayRound>? Ended { get; set; }
    private bool _resolved;
    // the clipper's kill count as last seen: (game process it counts for, kills); only what is added to it is new
    private uint _seenPid; private int _seenKills;

    public PlayRecorder(PlayLog log) { Log = log; }

    /// <summary>
    /// One look at the game. pid 0 = no game. killPid/kills: the clipper's count of the round (it starts again at 1 for a
    /// new game process); playPid: the game process last seen in front. Returns true when the log should be saved now.
    /// </summary>
    public bool Tick(int pid, DateTimeOffset? gameStart, string? build, DateTimeOffset now, uint killPid, int kills, uint playPid)
    {
        bool changed = false;
        if (!_resolved) { _resolved = true; changed |= Resume(pid, gameStart); _seenPid = killPid; _seenKills = kills; }
        // the game was closed or another one runs: the round ended when it was last seen
        if (Open != null && (pid == 0 || pid != Open.Pid)) { Finish(Open.End); changed = true; }
        if (pid == 0) return changed;
        if (Open == null)
        {
            var r = new PlayRound { Start = gameStart ?? now, End = now, Open = true, Pid = pid, GameStart = gameStart, Build = build };
            if (r.Start > now) r.Start = now;
            Log.Add(r); Open = r;
            _seenPid = killPid; _seenKills = kills;   // kills from before the round are not its own
        }
        var o = Open!;
        if (now > o.End) o.End = now;
        if (o.Build == null && build != null) o.Build = build;
        bool mine = killPid != 0 && (killPid == (uint)pid || killPid == playPid);
        if (killPid == _seenPid) { if (mine && kills > _seenKills) o.Kills += kills - _seenKills; }
        else if (mine) o.Kills += Math.Max(0, kills);   // the clipper started counting for this game: all of it is new
        _seenPid = killPid; _seenKills = kills;
        return changed;
    }

    /// <summary>
    /// The server the game is on, looked up for game process pid (connect noted at "at"). The first one found is the server
    /// of the whole round; another one later splits the round where the new connect was noted. Returns true if it split.
    /// </summary>
    public bool Server(int pid, string key, string? name, DateTimeOffset at, DateTimeOffset now)
    {
        var r = Open;
        if (r == null || r.Pid != pid) return false;
        if (r.Key == null || r.Key == key)
        {
            r.Key = key;
            if (name != null) r.Server = name;
            return false;
        }
        var cut = at > r.Start && at <= now ? at : now;
        var (p, gs, build) = (r.Pid, r.GameStart, r.Build);
        Finish(cut);
        var n = new PlayRound { Start = cut, End = now, Open = true, Pid = p, GameStart = gs, Build = build, Key = key, Server = name };
        Log.Add(n); Open = n;
        return true;
    }

    /// <summary>A clip was saved: it belongs to the round being played.</summary>
    public void Clip() { if (Open != null) Open.Clips++; }

    /// <summary>OrbitTweak is closing: the open round is saved with this last-seen time and stays open for the next start.</summary>
    public void Close(DateTimeOffset now) { if (Open != null && now > Open.End) Open.End = now; }

    /// <summary>Deletes the history. The round being played goes on.</summary>
    public int Clear() => Log.Rounds.RemoveAll(r => r != Open);

    public bool Remove(long id) => Log.Rounds.RemoveAll(r => r.Id == id && r != Open) > 0;

    private void Finish(DateTimeOffset end)
    {
        var r = Open!; Open = null;
        if (end > r.Start) r.End = end;
        r.Open = false; r.Pid = 0; r.GameStart = null;
        if (r.Secs < PlayStats.MinSecs) Log.Rounds.Remove(r);   // the game crashed while loading
        else Ended?.Invoke(r);
    }

    /// <summary>
    /// First look after the start: rounds still open in the file ended when they were last seen (crash, PC turned off),
    /// unless it is the same game process that still runs, then that round simply goes on.
    /// </summary>
    private bool Resume(int pid, DateTimeOffset? gameStart)
    {
        bool changed = false;
        foreach (var r in Log.Rounds.Where(x => x.Open).ToList())
        {
            if (Open == null && pid != 0 && r.Pid == pid && r.GameStart is { } a && gameStart is { } b && Math.Abs((a - b).TotalSeconds) < 2)
            {
                Open = r;
                continue;
            }
            r.Open = false; r.Pid = 0; r.GameStart = null; changed = true;
            if (r.Secs < PlayStats.MinSecs) Log.Rounds.Remove(r);
        }
        return changed;
    }
}

// ---------- what the page gets ("stats.get"); times in ms since 1970, durations in seconds ----------
internal sealed record PlayDay(string Day, double Secs, int Rounds);
internal sealed record PlayServer(string? Name, string? Addr, double Secs, double Share, int Rounds);
internal sealed record PlayItem(long Id, long Start, long End, double Secs, string? Server, string? Addr, int Kills, int Clips, string? Build);
internal sealed record PlayRecord(double Secs, int Kills, long At, string? Server);
internal sealed record PlayRecords(PlayRecord? Longest, PlayRecord? MostKills, int Streak, int BestStreak, double Avg);
internal sealed record PlaySummary(long Now, PlayItem? Current, double Today, int TodayRounds, double Week, double LastWeek, double Total,
    int Rounds, long? Since, List<PlayDay> Days, double[] Hours, PlayRecords Records, List<PlayServer> Servers, List<PlayItem> Recent);

internal static class PlayStats
{
    /// <summary>Shorter rounds are dropped: the game crashed while loading.</summary>
    public const double MinSecs = 60;
    /// <summary>From this length a finished round is mentioned on the page ("Runde vorbei").</summary>
    public const double NoteSecs = 300;
    /// <summary>A day counts for "Tage am Stück" with at least this much play on it.</summary>
    public const double DaySecs = 60;
    /// <summary>A round longer than this is cut there for the day and hour pieces (clock jumps, never a real round).</summary>
    private static readonly TimeSpan MaxSpan = TimeSpan.FromDays(7);

    /// <summary>"cfx:abc123" → "cfx.re/join/abc123", "ip:1.2.3.4:30120" → "1.2.3.4:30120".</summary>
    public static string? Addr(string? key) =>
        key == null ? null : key.StartsWith("cfx:") ? "cfx.re/join/" + key[4..] : key.StartsWith("ip:") ? key[3..] : key;

    /// <summary>The pieces of [start, end) per calendar day, on the clock the round was played on.</summary>
    public static List<(DateTime day, double secs)> SplitDays(DateTimeOffset start, DateTimeOffset end)
    {
        var list = new List<(DateTime, double)>();
        if (end - start > MaxSpan) end = start + MaxSpan;
        for (var t = start; t < end;)
        {
            var next = new DateTimeOffset(t.DateTime.Date.AddDays(1), t.Offset);
            var stop = next < end ? next : end;
            list.Add((t.DateTime.Date, (stop - t).TotalSeconds));
            t = stop;
        }
        return list;
    }

    /// <summary>Seconds per hour of the day (0–23) played on or after the day "from".</summary>
    public static double[] Hours(IEnumerable<PlayRound> rounds, DateTime from)
    {
        var h = new double[24];
        foreach (var r in rounds)
        {
            var end = r.End - r.Start > MaxSpan ? r.Start + MaxSpan : r.End;
            for (var t = r.Start; t < end;)
            {
                var c = t.DateTime;
                var next = new DateTimeOffset(c.Date.AddHours(c.Hour + 1), t.Offset);
                var stop = next < end ? next : end;
                if (c.Date >= from) h[c.Hour] += (stop - t).TotalSeconds;
                t = stop;
            }
        }
        return h;
    }

    /// <summary>Seconds and rounds per calendar day.</summary>
    public static Dictionary<DateTime, (double secs, int rounds)> PerDay(IEnumerable<PlayRound> rounds)
    {
        var d = new Dictionary<DateTime, (double secs, int rounds)>();
        foreach (var r in rounds)
            foreach (var (day, secs) in SplitDays(r.Start, r.End))
            {
                var v = d.TryGetValue(day, out var x) ? x : (0, 0);
                d[day] = (v.secs + secs, v.rounds + 1);
            }
        return d;
    }

    /// <summary>The n days up to today, oldest first, days without play included.</summary>
    public static List<PlayDay> Days(Dictionary<DateTime, (double secs, int rounds)> perDay, DateTime today, int n)
    {
        var list = new List<PlayDay>(n);
        for (int i = n - 1; i >= 0; i--)
        {
            var d = today.Date.AddDays(-i);
            var v = perDay.TryGetValue(d, out var x) ? x : (0, 0);
            list.Add(new PlayDay(d.ToString("yyyy-MM-dd"), Math.Round(v.secs), v.rounds));
        }
        return list;
    }

    /// <summary>Monday of the week (weeks start on Monday here).</summary>
    public static DateTime WeekStart(DateTime day) => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static double Span(Dictionary<DateTime, (double secs, int rounds)> perDay, DateTime from, DateTime to)
    {
        double s = 0;
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1)) if (perDay.TryGetValue(d, out var x)) s += x.secs;
        return s;
    }

    /// <summary>
    /// Days in a row with play: the current run (up to today, or up to yesterday while today has none yet, since the run
    /// is not broken before the day is over) and the longest run ever.
    /// </summary>
    public static (int current, int best) Streak(Dictionary<DateTime, (double secs, int rounds)> perDay, DateTime today)
    {
        bool Played(DateTime d) => perDay.TryGetValue(d, out var x) && x.secs >= DaySecs;
        int cur = 0;
        var day = today.Date;
        if (!Played(day)) day = day.AddDays(-1);
        while (Played(day)) { cur++; day = day.AddDays(-1); }
        int best = 0, run = 0; DateTime? prev = null;
        foreach (var d in perDay.Where(kv => kv.Value.secs >= DaySecs).Select(kv => kv.Key).OrderBy(x => x))
        {
            run = prev is DateTime p && d == p.AddDays(1) ? run + 1 : 1;
            best = Math.Max(best, run); prev = d;
        }
        return (cur, Math.Max(cur, best));
    }

    /// <summary>The servers with the most time, with their share of all play time. Rounds without a known server form one entry (no name).</summary>
    public static List<PlayServer> Servers(IReadOnlyCollection<PlayRound> rounds, int top)
    {
        double total = rounds.Sum(r => r.Secs);
        return rounds
            .GroupBy(r => r.Key ?? (r.Server != null ? "name:" + r.Server : ""))
            .Select(g =>
            {
                double secs = g.Sum(r => r.Secs);
                var named = g.Where(r => r.Server != null).OrderBy(r => r.Start).LastOrDefault();
                return new PlayServer(named?.Server, Addr(g.First().Key), Math.Round(secs), total > 0 ? Math.Round(secs / total, 4) : 0, g.Count());
            })
            .OrderByDescending(s => s.Secs).ThenBy(s => s.Name == null)
            .Take(top).ToList();
    }

    private static PlayItem Item(PlayRound r) => new(r.Id, r.Start.ToUnixTimeMilliseconds(), r.End.ToUnixTimeMilliseconds(), Math.Round(r.Secs),
        r.Server, Addr(r.Key), r.Kills, r.Clips, r.Build);

    /// <summary>Everything the page shows. open = the round being played (its end is now), it is not in the list of recent rounds.</summary>
    public static PlaySummary Summarize(IEnumerable<PlayRound> all, PlayRound? open, DateTimeOffset now, int days = 30, int recent = 50, int top = 5)
    {
        var today = now.DateTime.Date;
        var rounds = all.OrderBy(r => r.Start).ToList();
        var perDay = PerDay(rounds);
        var week = WeekStart(today);
        double total = rounds.Sum(r => r.Secs);
        var (streak, best) = Streak(perDay, today);
        var longest = rounds.Where(r => r.Secs > 0).MaxBy(r => r.Secs);
        var kills = rounds.Where(r => r.Kills > 0).MaxBy(r => r.Kills);
        PlayRecord? Rec(PlayRound? r) => r == null ? null : new(Math.Round(r.Secs), r.Kills, r.Start.ToUnixTimeMilliseconds(), r.Server ?? Addr(r.Key));
        return new PlaySummary(
            now.ToUnixTimeMilliseconds(),
            open != null ? Item(open) : null,
            Math.Round(Span(perDay, today, today)),
            perDay.TryGetValue(today, out var td) ? td.rounds : 0,
            Math.Round(Span(perDay, week, today)),
            Math.Round(Span(perDay, week.AddDays(-7), week.AddDays(-1))),
            Math.Round(total),
            rounds.Count,
            rounds.Count > 0 ? rounds[0].Start.ToUnixTimeMilliseconds() : null,
            Days(perDay, today, days),
            Hours(rounds, today.AddDays(-(days - 1))).Select(x => Math.Round(x)).ToArray(),
            new PlayRecords(Rec(longest), Rec(kills), streak, best, rounds.Count > 0 ? Math.Round(total / rounds.Count) : 0),
            Servers(rounds, top),
            rounds.Where(r => r != open).OrderByDescending(r => r.Start).Take(recent).Select(Item).ToList());
    }
}
