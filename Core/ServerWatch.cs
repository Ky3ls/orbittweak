using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using static OrbitTweak.Core.ServerWatchLogic;

namespace OrbitTweak.Core;

/// <summary>Freunde online (2.31): the names the user looks for, kept with the settings.</summary>
public class ServerFriendsCfg
{
    public List<string> Names { get; set; } = new();
    /// <summary>"Melden, wenn ein Freund online kommt": looks in the background every 3 minutes while the app runs.</summary>
    public bool Notify { get; set; }
    /// <summary>"Auch Teilnamen finden": the friend's name inside a longer player name (exact is the default).</summary>
    public bool Partial { get; set; }
}

// =====================================================================
//  One look at a server through the public Cfx.re API: the same single endpoint as the favourites (Servers.CfxApi).
//  Shared by Platz-Alarm and Freunde online: answers are kept for a few seconds, at most two requests run at once,
//  and after an HTTP 429 nothing goes out until the time the server asked for.
// =====================================================================
internal sealed record CfxSnapshot(ProbeKind Kind, int Players, int Max, string? Name, string[] Names, string? Endpoint,
    string? Icon, int? RetryAfter, string? Error, DateTime At)
{
    public Probe Probe => new(Kind, Players, Max, RetryAfter);
}

internal static class CfxProbe
{
    private static readonly ConcurrentDictionary<string, CfxSnapshot> Recent = new();
    private static readonly SemaphoreSlim Gate = new(2, 2);
    private static long _coolUntil;   // UTC ticks: after a 429 no request before this
    private const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>The server's state, from an answer at most <paramref name="maxAge"/> old or a new request. Never throws
    /// (except when <paramref name="ct"/> is cancelled). Reading and parsing run on a pool thread (ConfigureAwait(false)),
    /// never on the UI thread of a host command: a big server's answer is up to a megabyte of JSON.</summary>
    public static async Task<CfxSnapshot> Get(string code, TimeSpan maxAge, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (!ValidCode(code)) return new(ProbeKind.Error, 0, 0, null, Array.Empty<string>(), null, null, null, "Kein gültiger Join-Code", now);
        if (Recent.TryGetValue(code, out var c) && now - c.At < maxAge && c.Kind is ProbeKind.Ok or ProbeKind.NotFound) return c;
        var cool = new DateTime(Interlocked.Read(ref _coolUntil), DateTimeKind.Utc);
        if (now < cool) return Limited((int)Math.Ceiling((cool - now).TotalSeconds), now);

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Servers.CfxApi}/api/servers/single/{code}");
            using var r = await Net.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            now = DateTime.UtcNow;
            if ((int)r.StatusCode == 429)
            {
                var ra = r.Headers.RetryAfter is { } h ? (h.Delta is { } d ? (int?)Math.Min(3600, (int)Math.Ceiling(d.TotalSeconds)) : h.Date is { } dt ? RetryAfter(dt.UtcDateTime.ToString("r"), now) : null) : null;
                int wait = Math.Max(ra ?? 120, 60);
                Interlocked.Exchange(ref _coolUntil, now.AddSeconds(wait).Ticks);
                return Limited(ra, now);
            }
            if (r.StatusCode == System.Net.HttpStatusCode.NotFound)
                return Recent[code] = new(ProbeKind.NotFound, 0, 0, null, Array.Empty<string>(), null, null, null, null, now);
            if (!r.IsSuccessStatusCode)
                return Fail($"Cfx.re antwortet mit Fehler {(int)r.StatusCode}", now);
            if (r.Content.Headers.ContentLength > MaxBytes) return Fail("Antwort von Cfx.re zu groß", now);
            using var body = await r.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var ms = new MemoryStream();
            var buf = new byte[81920]; int n;
            while ((n = await body.ReadAsync(buf, cts.Token).ConfigureAwait(false)) > 0)
            {
                if (ms.Length + n > MaxBytes) return Fail("Antwort von Cfx.re zu groß", now);
                ms.Write(buf, 0, n);
            }
            ms.Position = 0;
            using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: cts.Token).ConfigureAwait(false);
            return Recent[code] = Parse(doc.RootElement, code, now);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Fail("Cfx.re antwortet nicht (Zeitüberschreitung)", DateTime.UtcNow); }
        catch (HttpRequestException) { return Fail("Keine Verbindung zu Cfx.re", DateTime.UtcNow); }
        catch (JsonException) { return Fail("Unerwartete Antwort von Cfx.re", DateTime.UtcNow); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Fail("Cfx.re-Abfrage fehlgeschlagen: " + ex.Message, DateTime.UtcNow); }
        finally { Gate.Release(); }
    }

    private static CfxSnapshot Limited(int? ra, DateTime now) =>
        new(ProbeKind.RateLimited, 0, 0, null, Array.Empty<string>(), null, null, ra, "Cfx.re bremst gerade (zu viele Anfragen)", now);
    private static CfxSnapshot Fail(string error, DateTime now) =>
        new(ProbeKind.Error, 0, 0, null, Array.Empty<string>(), null, null, null, error, now);

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;
    private static int Int(JsonElement e, string name) => int.TryParse(Str(e, name), out var i) && i >= 0 ? i : 0;

    /// <summary>Reads what the features need from the single endpoint's answer (live check 2026-11-22: Data.clients,
    /// Data.sv_maxclients / svMaxclients, Data.players[] with name/ping/id/endpoint/identifiers, vars.sv_projectName,
    /// vars.icon_url on cfx-server-metadata.cfx-services.net; iconVersion is gone).</summary>
    private static CfxSnapshot Parse(JsonElement root, string code, DateTime now)
    {
        if (!root.TryGetProperty("Data", out var d) || d.ValueKind != JsonValueKind.Object)
            return Fail("Unerwartete Antwort von Cfx.re", now);
        d.TryGetProperty("vars", out var vars);
        int players = Int(d, "clients");
        int max = Int(d, "sv_maxclients");
        if (max == 0) max = Int(d, "svMaxclients");
        if (max == 0) max = Int(vars, "sv_maxClients");
        var names = new List<string>();
        if (d.TryGetProperty("players", out var pl) && pl.ValueKind == JsonValueKind.Array)
            foreach (var p in pl.EnumerateArray())
            {
                if (names.Count >= 4096) break;
                if (Str(p, "name") is { } nm) names.Add(nm.Length > 64 ? nm[..64] : nm);
            }
        string? ep = null;
        if (d.TryGetProperty("connectEndPoints", out var eps) && eps.ValueKind == JsonValueKind.Array && eps.GetArrayLength() > 0 && eps[0].ValueKind == JsonValueKind.String)
            ep = eps[0].GetString();
        // the icon: the old versioned address when Cfx.re still names a version, else the server's icon_url on Cfx.re's
        // metadata host (IconUrl refuses every other host)
        var icon = Str(d, "iconVersion") is { } iv && Regex.IsMatch(iv, @"^[0-9]{1,12}\z")
            ? IconUrl($"https://servers-live.fivem.net/servers/icon/{code}/{iv}.png") : IconUrl(Str(vars, "icon_url"));
        var name = Str(vars, "sv_projectName") ?? Str(d, "hostname");
        return new(ProbeKind.Ok, players, max, name, names.ToArray(), ep, icon, null, null, now);
    }
}

// =====================================================================
//  Platz-Alarm: up to three watches, each polling its server about every 20 s (jittered, backing off on errors and
//  429), ending after two hours, when FiveM starts or on "Beenden". In memory only: gone when the app closes.
// =====================================================================
internal sealed class SlotWatch
{
    public string Code { get; init; } = "";
    public string Name { get; set; } = "";
    public WatchKind Kind { get; set; }
    public bool Auto { get; set; }
    public DateTime Started { get; init; } = DateTime.UtcNow;
    /// <summary>watching | retry | fired | ended</summary>
    public string State { get; set; } = "watching";
    /// <summary>fired: free | online; ended: timeout | fivem | game | error</summary>
    public string? Reason { get; set; }
    public string? Error { get; set; }
    public int Players { get; set; }
    public int Max { get; set; }
    public bool Online { get; set; }
    public DateTime? Last { get; set; }
    public DateTime? Next { get; set; }
    public DateTime? Done { get; set; }
    public int Errors { get; set; }
    public int Limits { get; set; }
    public bool FmAtStart { get; set; }
    public bool Launched { get; set; }
    public CancellationTokenSource Cts { get; } = new();
}

internal sealed class ServerWatcher
{
    private readonly object _lock = new();
    private readonly List<SlotWatch> _list = new();

    /// <summary>A watch changed (raised on a pool thread).</summary>
    public event Action? Changed;
    /// <summary>A slot is free or the server is back (raised on a pool thread, once per watch).</summary>
    public event Action<SlotWatch>? Fired;

    private static long Ms(DateTime? t) => t is DateTime d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToUnixTimeMilliseconds() : 0;

    /// <summary>For the page: { code, name, kind, state, reason, error, players, max, online, auto, started, ends, last, next, done, launched } (times in ms since 1970).</summary>
    public object[] Views()
    {
        lock (_lock) return _list.Select(w => (object)new
        {
            code = "cfx:" + w.Code, name = w.Name, kind = w.Kind == WatchKind.Slot ? "slot" : "online", state = w.State,
            reason = w.Reason, error = w.Error, players = w.Players, max = w.Max, online = w.Online, auto = w.Auto,
            started = Ms(w.Started), ends = Ms(w.Started + WatchFor), last = Ms(w.Last), next = Ms(w.Next), done = Ms(w.Done), launched = w.Launched,
        }).ToArray();
    }

    public SlotWatch? Find(string code) { lock (_lock) return _list.FirstOrDefault(w => w.Code == code); }
    public int ActiveCount { get { lock (_lock) return _list.Count(w => Active(w.State)); } }

    /// <summary>Starts a watch whose first answer is <paramref name="first"/>. Returns a German error or null.</summary>
    public string? Start(SlotWatch w, CfxSnapshot first)
    {
        lock (_lock)
        {
            if (_list.Any(x => x.Code == w.Code && Active(x.State))) return null;
            if (_list.Count(x => Active(x.State)) >= MaxWatches) return $"Es laufen schon {MaxWatches} Platz-Alarme. Beende erst einen.";
            _list.RemoveAll(x => x.Code == w.Code);   // a finished one for the same server makes room
            // finished ones stay listed (the page shows how they ended) until dismissed; never more than five of them
            while (_list.Count(x => !Active(x.State)) >= 5) _list.Remove(_list.First(x => !Active(x.State)));
            _list.Add(w);
        }
        Apply(w, first);
        double delay = first.Kind switch
        {
            ProbeKind.RateLimited => NextDelay(++w.Limits, true, first.RetryAfter, Random.Shared.NextDouble()),
            ProbeKind.Error => NextDelay(++w.Errors, false, null, Random.Shared.NextDouble()),
            _ => NextDelay(0, false, null, Random.Shared.NextDouble()),
        };
        w.State = first.Kind is ProbeKind.Ok or ProbeKind.NotFound ? "watching" : "retry";
        _ = Task.Run(() => Run(w, delay));
        return null;
    }

    /// <summary>Ends a watch (or takes a finished one off the list).</summary>
    public bool Stop(string code)
    {
        SlotWatch? w;
        lock (_lock)
        {
            w = _list.FirstOrDefault(x => x.Code == code);
            if (w == null) return false;
            _list.Remove(w);
        }
        try { w.Cts.Cancel(); } catch { }
        Changed?.Invoke();
        return true;
    }

    public bool SetAuto(string code, bool on)
    {
        var w = Find(code);
        if (w == null) return false;
        w.Auto = on;
        Changed?.Invoke();
        return true;
    }

    private static void Apply(SlotWatch w, CfxSnapshot s)
    {
        w.Last = s.At;
        if (s.Kind == ProbeKind.Ok)
        {
            w.Online = true; w.Players = s.Players; w.Max = s.Max;
            if (!string.IsNullOrWhiteSpace(s.Name)) w.Name = ServerName(s.Name, w.Name);
        }
        else if (s.Kind == ProbeKind.NotFound) { w.Online = false; w.Players = 0; }
    }

    /// <summary>Whether FiveM runs at all and whether its game runs (process names only; nothing is opened).</summary>
    public static (bool fivem, bool game) FiveMState()
    {
        var ps = FiveM.Processes();
        try
        {
            bool game = ps.Any(p => { try { return p.ProcessName.Contains("GTAProcess", StringComparison.OrdinalIgnoreCase); } catch { return false; } });
            return (ps.Length > 0, game);
        }
        finally { foreach (var p in ps) p.Dispose(); }
    }

    private async Task Run(SlotWatch w, double delay)
    {
        var ct = w.Cts.Token;
        try
        {
            while (true)
            {
                // never past the two hours
                var left = (w.Started + WatchFor - DateTime.UtcNow).TotalSeconds;
                delay = Math.Max(1, Math.Min(delay, left + .5));
                w.Next = DateTime.UtcNow.AddSeconds(delay);
                Changed?.Invoke();
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);

                var now = DateTime.UtcNow;
                if (Expired(w.Started, now)) { End(w, "timeout"); return; }
                var (fm, game) = FiveMState();
                if (EndsForFiveM(w.FmAtStart, fm, game)) { End(w, game ? "game" : "fivem"); return; }
                if (!fm) w.FmAtStart = false;   // FiveM was closed: its next start ends the watch

                var s = await CfxProbe.Get(w.Code, TimeSpan.FromSeconds(8), ct);
                Apply(w, s);
                switch (Decide(w.Kind, s.Probe))
                {
                    case Step.Fire:
                        w.State = "fired"; w.Reason = w.Kind == WatchKind.Online ? "online" : "free";
                        w.Done = DateTime.UtcNow; w.Next = null; w.Error = null;
                        Changed?.Invoke();
                        Fired?.Invoke(w);
                        return;
                    case Step.Retry when s.Kind == ProbeKind.RateLimited:
                        w.State = "retry"; w.Error = s.Error; w.Limits++;
                        delay = NextDelay(w.Limits, true, s.RetryAfter, Random.Shared.NextDouble());
                        break;
                    case Step.Retry:
                        w.State = "retry"; w.Error = s.Error; w.Errors++;
                        delay = NextDelay(w.Errors, false, null, Random.Shared.NextDouble());
                        break;
                    default:
                        w.State = "watching"; w.Error = null; w.Errors = 0; w.Limits = 0;
                        delay = NextDelay(0, false, null, Random.Shared.NextDouble());
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { End(w, "error", "Platz-Alarm angehalten: " + ex.Message); }
    }

    private void End(SlotWatch w, string reason, string? error = null)
    {
        w.State = "ended"; w.Reason = reason; w.Error = error; w.Next = null; w.Done = DateTime.UtcNow;
        Changed?.Invoke();
    }
}

// =====================================================================
//  Freunde online: rounds over the favourites' public player lists (at most ten servers per round, one after another).
// =====================================================================
internal sealed class FriendScan
{
    public sealed record Srv(string Code, string Name, Listing? Listing, int Players, int Max, string[] Names, string? Endpoint, DateTime At, string? Error, bool Offline);

    private readonly object _lock = new();
    private readonly Dictionary<string, Srv> _srv = new();
    private readonly Dictionary<string, (DateTime at, int? ms)> _ping = new();
    private int _offset, _rounds;
    private DateTime? _last;
    private Task? _running;
    private string? _error;

    public DateTime? Last { get { lock (_lock) return _last; } }
    public bool Busy { get { lock (_lock) return _running != null; } }
    /// <summary>Finished rounds so far: who joins a running round sees the same number as who started it, so the
    /// "came online" notes are worked out once per round (MainWindow.SwFriendsRound).</summary>
    public int Rounds { get { lock (_lock) return _rounds; } }
    public FriendSeen Seen { get; } = new();

    /// <summary>Runs a round when one may run (never twice within 60 s; a running round is joined). True when a round ran.</summary>
    public async Task<bool> Round(IReadOnlyList<string> codes)
    {
        Task run;
        lock (_lock)
        {
            if (_running != null) run = _running;
            else if (codes.Count == 0 || !MayRound(_last, DateTime.UtcNow, FriendEvery)) return false;
            else _running = run = Task.Run(() => DoRound(codes.ToList()));
        }
        await run;
        return true;
    }

    private async Task DoRound(List<string> codes)
    {
        string? err = null; int failed = 0, asked = 0;
        try
        {
            int[] pick;
            lock (_lock)
            {
                (pick, _offset) = Rotate(codes.Count, ServersPerRound, _offset);
                foreach (var k in _srv.Keys.Where(k => !codes.Contains(k)).ToList()) _srv.Remove(k);
            }
            for (int i = 0; i < pick.Length; i++)
            {
                if (i > 0) await Task.Delay(350);   // one after another, never a burst
                var code = codes[pick[i]];
                var s = await CfxProbe.Get(code, TimeSpan.FromSeconds(30));
                asked++;
                Srv row = s.Kind switch
                {
                    ProbeKind.Ok => new(code, ServerName(s.Name, code), Classify(s.Players, s.Names), s.Players, s.Max, s.Names, s.Endpoint, s.At, null, false),
                    ProbeKind.NotFound => new(code, code, null, 0, 0, Array.Empty<string>(), null, s.At, null, true),
                    _ => new(code, code, null, 0, 0, Array.Empty<string>(), null, s.At, s.Error, false),
                };
                lock (_lock)
                {
                    // a failed look keeps the last good list (it still counts until it is 10 minutes old)
                    if (row.Error != null && _srv.TryGetValue(code, out var old) && old.Error == null) _srv[code] = old with { Error = row.Error };
                    else
                    {
                        if (row.Name == code && _srv.TryGetValue(code, out var o2)) row = row with { Name = o2.Name };
                        _srv[code] = row;
                    }
                }
                if (s.Kind == ProbeKind.Error) failed++;
                if (s.Kind == ProbeKind.RateLimited) { err = s.Error; break; }   // Cfx.re asks for a pause: the round stops here
            }
            if (err == null && asked > 0 && failed == asked) err = "Cfx.re ist gerade nicht erreichbar.";
        }
        catch (Exception ex) { err = "Suche fehlgeschlagen: " + ex.Message; }
        finally { lock (_lock) { _running = null; _last = DateTime.UtcNow; _error = err; _rounds++; } }
    }

    /// <summary>The user's own ping to a server with a friend on it (TCP handshake to its public address, like the
    /// favourites), kept for a minute. Null when Cfx.re shows no public IPv4 address for it (<see cref="PingTarget"/>).</summary>
    public async Task<int?> Ping(string? endpoint)
    {
        if (PingTarget(endpoint) is not { } t || endpoint == null) return null;
        lock (_lock) if (_ping.TryGetValue(endpoint, out var p) && DateTime.UtcNow - p.at < TimeSpan.FromMinutes(1)) return p.ms;
        var ms = await Net.TcpPing(t.host, t.port, 1500);
        lock (_lock) _ping[endpoint] = (DateTime.UtcNow, ms);
        return ms;
    }

    public (List<Srv> servers, string? error, DateTime? last) Snapshot()
    {
        lock (_lock) return (_srv.Values.ToList(), _error, _last);
    }
}
