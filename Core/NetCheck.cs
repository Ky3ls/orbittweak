using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>The server of a Netz-Check: how the user knows it (Addr), what was measured (Ip, Port) and why it could not
/// be measured (Skipped, Hidden).</summary>
internal sealed record NcServer(string Key, string Addr, string Name, IPAddress? Ip, int Port, bool Proxy, string ProxyHost, bool Hidden, string Skipped);

/// <summary>
/// Netz-Check (2.31): the Windows part. Finds the router of the active connection, looks up the server's address (the
/// Cfx.re single endpoint's connectEndPoints, or the saved IP:Port), and measures. The rules are in NetCheckLogic.
/// What it sends: ICMP echo (what the Windows command "ping" does) to the router, 1.1.1.1 and the server, and for a
/// server that ignores pings a plain TCP connection set-up to its port, once a second. Nothing touches FiveM, its
/// files or its process, and nothing leaves the PC except these packets and the one Cfx.re request.
/// </summary>
internal static class NetCheck
{
    /// <summary>The public Cfx.re API the FiveM server browser uses (servers-frontend.fivem.net answers /api/servers/
    /// with 404 since 2026-11).</summary>
    private const string CfxSingle = "https://frontend.cfx-services.net/api/servers/single/";
    private static readonly Dictionary<string, (DateTime at, NcServer s)> Cache = new();
    private static DateTime _cfxQuietUntil;   // after a 429 from Cfx.re: no request before this moment

    /// <summary>The router (default gateway) of the connection the PC uses, and whether that is WLAN. link "" = no
    /// connection at all.</summary>
    public static (IPAddress? gw, bool? wifi, string link) Route()
    {
        var nic = NetLink.DefaultRoute();
        if (nic == null) return (null, null, "");
        List<IPAddress> gws;
        try
        {
            gws = nic.GetIPProperties().GatewayAddresses.Select(g => g.Address)
                .Where(a => a != null && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)).ToList();
        }
        catch { gws = new(); }
        var gw = gws.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? gws.FirstOrDefault();
        return nic.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => (gw, true, "WLAN"),
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => (gw, false, "Kabel"),
            _ => (gw, null, "Netzwerk"),
        };
    }

    /// <summary>Looks up where a server is. Never throws: a server that cannot be measured comes back with Skipped
    /// (German, why) or Hidden, and the check then measures router and internet only.</summary>
    public static async Task<NcServer> Server(string q, string uiName, CancellationToken ct)
    {
        string key;
        try { key = q.StartsWith("cfx:", StringComparison.Ordinal) || q.StartsWith("ip:", StringComparison.Ordinal) ? q : Servers.Parse(q); }
        catch { return Skip(q, q, uiName, "Die Server-Adresse ist ungültig"); }
        lock (Cache)
            if (Cache.TryGetValue(key, out var c) && DateTime.UtcNow - c.at < TimeSpan.FromMinutes(5)) return c.s;

        string addr, name = NetCheckLogic.CleanName(uiName);
        NcEndpoint? ep;
        if (key.StartsWith("cfx:", StringComparison.Ordinal))
        {
            var code = key[4..];
            addr = "cfx.re/join/" + code;
            if (!Regex.IsMatch(code, "^[a-z0-9]{3,16}$")) return Skip(key, addr, name, "Der Join-Code ist ungültig");
            if (DateTime.UtcNow < _cfxQuietUntil) return Skip(key, addr, name, "Cfx.re bremst gerade zu viele Anfragen");
            string? epText = null;
            try
            {
                using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
                to.CancelAfter(TimeSpan.FromSeconds(6));
                using var res = await Net.Http.GetAsync(CfxSingle + code, to.Token);
                if ((int)res.StatusCode == 429)
                {
                    var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
                    _cfxQuietUntil = DateTime.UtcNow + (wait < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : wait);
                    return Skip(key, addr, name, "Cfx.re bremst gerade zu viele Anfragen");
                }
                if (res.StatusCode == HttpStatusCode.NotFound) return Skip(key, addr, name, "Cfx.re kennt diesen Server gerade nicht, er ist offline oder der Join-Code hat sich geändert");
                if (!res.IsSuccessStatusCode) return Skip(key, addr, name, "Cfx.re hat die Server-Adresse nicht genannt");
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(to.Token));
                if (doc.RootElement.TryGetProperty("Data", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    if (d.TryGetProperty("connectEndPoints", out var eps) && eps.ValueKind == JsonValueKind.Array && eps.GetArrayLength() > 0 && eps[0].ValueKind == JsonValueKind.String)
                        epText = eps[0].GetString();
                    string? n = d.TryGetProperty("vars", out var vars) && vars.ValueKind == JsonValueKind.Object && vars.TryGetProperty("sv_projectName", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString()
                        : d.TryGetProperty("hostname", out var hn) && hn.ValueKind == JsonValueKind.String ? hn.GetString() : null;
                    if (NetCheckLogic.CleanName(n) is { Length: > 0 } cn) name = cn;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Skip(key, addr, name, "Cfx.re hat nicht rechtzeitig geantwortet"); }
            catch (Exception) when (!ct.IsCancellationRequested) { return Skip(key, addr, name, "Cfx.re war nicht erreichbar"); }
            ep = NetCheckLogic.ParseEndpoint(epText);
            if (ep == null) return Skip(key, addr, name, "Cfx.re nennt für diesen Server keine Adresse");
            if (ep.Hidden) return Keep(key, new NcServer(key, addr, Or(name, addr), null, ep.Port, true, ep.Host, true, ""));
        }
        else
        {
            addr = key[3..];
            ep = NetCheckLogic.ParseEndpoint(addr);
            if (ep == null) return Skip(key, addr, name, "Die Server-Adresse ist ungültig");
        }

        IPAddress? ip = null;
        if (IPAddress.TryParse(ep.Host, out var lit)) ip = lit;
        else
        {
            try
            {
                using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
                to.CancelAfter(TimeSpan.FromSeconds(4));
                var all = await Dns.GetHostAddressesAsync(ep.Host, to.Token);
                ip = all.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork) ?? all.FirstOrDefault();
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            if (ip == null) return Skip(key, addr, name, $"Die Adresse {ep.Host} ließ sich nicht auflösen");
        }
        return Keep(key, new NcServer(key, addr, Or(name, addr), ip, ep.Port, ep.Proxy, ep.Host, false, ""));
    }

    private static string Or(string a, string b) => a.Length > 0 ? a : b;
    private static NcServer Skip(string key, string addr, string name, string why) => new(key, addr, Or(name, addr), null, 0, false, "", false, why);
    private static NcServer Keep(string key, NcServer s) { lock (Cache) Cache[key] = (DateTime.UtcNow, s); return s; }

    // ---------------------------------------------------------------- one measurement ----------------------------------------------------------------

    private static readonly byte[] Payload = new byte[32];

    /// <summary>One ICMP echo; the round trip as Windows reports it (whole ms, 0 = under 1 ms), null = no answer.</summary>
    public static async Task<double?> Icmp(IPAddress ip, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var r = await ping.SendPingAsync(ip, TimeSpan.FromMilliseconds(NetCheckLogic.TimeoutMs), Payload, null, ct);
            return r.Status == IPStatus.Success ? r.RoundtripTime : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>How long a TCP connection set-up to the port takes (one round trip), null = none within the timeout or
    /// refused. The connection is closed at once, nothing is sent over it.</summary>
    public static async Task<double?> Tcp(IPAddress ip, int port, CancellationToken ct)
    {
        using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
        to.CancelAfter(NetCheckLogic.TimeoutMs);
        using var s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        var sw = Stopwatch.StartNew();
        try
        {
            await s.ConnectAsync(new IPEndPoint(ip, port), to.Token);
            return Math.Round(sw.Elapsed.TotalMilliseconds, 1);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }   // the timeout
        catch (SocketException) { return null; }
        finally { try { if (s.Connected) s.Shutdown(SocketShutdown.Both); } catch { } }
    }
}

/// <summary>One minute of measuring: one loop per stage on a fixed schedule (two per second, a port once a second),
/// at most three measurements of a stage under way at a time, results to the page every half second, the verdict at the
/// end. Cancel() ends it at once; nothing is reported after that.</summary>
internal sealed class NetCheckRun
{
    private sealed class Probe
    {
        public string Id = "", Label = "", Name = "", Host = "", Mode = "icmp", Note = "";
        public IPAddress? Ip; public int Port; public int IntervalMs = NetCheckLogic.IntervalMs; public int Epoch;
        public bool Reset;
        public readonly List<NcSample> Samples = new(), Fresh = new();
        public bool Measured => Ip != null;
        public NcTarget Target() { lock (this) return new() { Id = Id, Label = Label, Name = Name, Host = Host, Mode = Mode, Port = Port, IntervalMs = IntervalMs, Measured = Measured, Samples = Samples.OrderBy(s => s.At).ToList() }; }
        public NcStats Stats() { lock (this) return NetCheckLogic.Stats(Samples, IntervalMs); }
        public int Answered() { lock (this) return Samples.Count(s => s.Ms != null); }
    }

    public int Id { get; }
    private readonly Probe _gw, _net; private readonly Probe? _srv;
    private readonly bool? _wifi; private readonly NcServer? _server; private readonly bool _noServer;
    private readonly Action<object> _emit; private readonly Action<NcResult> _done;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = new();
    private volatile bool _switched;

    public NetCheckRun(int id, IPAddress? gw, bool? wifi, NcServer? server, Action<object> emit, Action<NcResult> done)
    {
        Id = id; _wifi = wifi; _server = server; _noServer = server == null; _emit = emit; _done = done;
        _gw = new Probe { Id = "gw", Label = "Router", Name = "Router", Ip = gw, Host = gw?.ToString() ?? "", Note = gw == null ? "Kein Router gefunden" : "" };
        _net = new Probe { Id = "net", Label = "Internet", Name = "Internet", Ip = IPAddress.Parse(NetCheckLogic.Anchor), Host = NetCheckLogic.Anchor };
        if (server != null)
            _srv = new Probe
            {
                Id = "srv", Label = "Server", Name = server.Name, Ip = server.Ip, Host = server.Ip?.ToString() ?? "", Port = server.Port,
                Note = server.Hidden ? "Adresse geheim" : server.Skipped,
            };
    }

    /// <summary>What the page shows before the first result.</summary>
    public object[] Plan() => new[] { _gw, _net, _srv }.Where(p => p != null).Select(p => (object)new
    {
        id = p!.Id, label = p.Label, name = p.Name, host = p.Host, mode = p.Mode, port = p.Port, measured = p.Measured, note = p.Note,
    }).ToArray();

    public void Begin() => _ = Task.Run(RunAsync);
    public void Cancel() => _cts.Cancel();

    private async Task RunAsync()
    {
        var ct = _cts.Token;
        try
        {
            _clock.Start();
            var loops = Task.WhenAll(new[] { _gw, _net, _srv }.Where(p => p is { Measured: true }).Select(p => Loop(p!, ct)));
            while (!loops.IsCompleted)
            {
                await Task.WhenAny(loops, Task.Delay(NetCheckLogic.IntervalMs, ct));
                ct.ThrowIfCancellationRequested();
                Flush();
            }
            await loops;
            ct.ThrowIfCancellationRequested();
            var r = NetCheckLogic.Evaluate(new NcInput
            {
                Gw = _gw.Target(), Net = _net.Target(), Srv = _srv?.Target(), Wifi = _wifi, SrvSwitched = _switched,
                SrvProxy = _server?.Proxy == true, SrvProxyHost = _server?.ProxyHost ?? "", SrvHidden = _server?.Hidden == true,
                SrvSkipped = _server?.Skipped ?? "", NoServer = _noServer, ServerAddr = _server?.Addr ?? "",
                When = DateTime.Now, User = Environment.UserName,
            });
            if (ct.IsCancellationRequested) return;
            _emit(new { run = Id, phase = "done", result = r });
            _done(r);
        }
        catch (OperationCanceledException) { }   // stopped on the page: it already knows
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) _emit(new { run = Id, phase = "error", error = "Der Netz-Check wurde unterbrochen: " + ex.Message });
        }
        // (the token source is not disposed: measurements that are still under way may still look at their token)
    }

    /// <summary>One stage on its schedule. After a long pause (the PC slept) it does not catch up with a burst: the
    /// missed slots are skipped.</summary>
    private async Task Loop(Probe p, CancellationToken ct)
    {
        var busy = new List<Task>();
        double next = 0, end = NetCheckLogic.Seconds * 1000.0;
        while (next < end)
        {
            double wait = next - _clock.Elapsed.TotalMilliseconds;
            if (wait > 1) await Task.Delay(TimeSpan.FromMilliseconds(wait), ct);
            int iv; lock (p) iv = p.IntervalMs;
            if (wait < -2000) { next = Math.Ceiling(_clock.Elapsed.TotalMilliseconds / iv) * iv; continue; }
            ct.ThrowIfCancellationRequested();
            busy.RemoveAll(t => t.IsCompleted);
            // (a ping that times out ends a little after the next-but-one is due, so three can be under way; more means
            // Windows hangs on them, and that slot counts as lost instead of piling up requests)
            if (busy.Count < 3) busy.Add(Measure(p, next / 1000.0, ct));
            else lock (p) { var s = new NcSample(next / 1000.0, null); p.Samples.Add(s); p.Fresh.Add(s); }
            lock (p) iv = p.IntervalMs;
            next += iv;
        }
        await Task.WhenAll(busy);
    }

    private async Task Measure(Probe p, double at, CancellationToken ct)
    {
        int epoch; string mode;
        lock (p) { epoch = p.Epoch; mode = p.Mode; }
        double? ms;
        try { ms = mode == "tcp" ? await NetCheck.Tcp(p.Ip!, p.Port, ct) : await NetCheck.Icmp(p.Ip!, ct); }
        catch { return; }   // cancelled
        if (ct.IsCancellationRequested) return;
        lock (p)
        {
            if (p.Epoch != epoch) return;   // a ping from before the switch to the port
            var s = new NcSample(at, ms); p.Samples.Add(s); p.Fresh.Add(s);
        }
        if (p == _srv && mode == "icmp") MaybeSwitch(p);
    }

    /// <summary>A server that never answers a ping while the router or the internet do: from now on its port.</summary>
    private void MaybeSwitch(Probe p)
    {
        int sent, ans;
        lock (p) { if (p.Mode != "icmp") return; sent = p.Samples.Count; ans = p.Samples.Count(s => s.Ms != null); }
        if (!NetCheckLogic.SwitchToTcp(sent, ans, _gw.Answered() + _net.Answered(), p.Port)) return;
        lock (p)
        {
            if (p.Mode != "icmp") return;
            p.Mode = "tcp"; p.IntervalMs = NetCheckLogic.TcpIntervalMs; p.Epoch++; p.Samples.Clear(); p.Fresh.Clear(); p.Reset = true;
        }
        _switched = true;
    }

    /// <summary>The new results since the last time, and the numbers so far, to the page.</summary>
    private void Flush()
    {
        var points = new List<object>(); var reset = new List<string>();
        var live = new Dictionary<string, NcStats>();
        foreach (var p in new[] { _gw, _net, _srv })
        {
            if (p is not { Measured: true }) continue;
            lock (p)
            {
                if (p.Reset) { reset.Add(p.Id); p.Reset = false; }
                foreach (var s in p.Fresh) points.Add(new { t = p.Id, at = Math.Round(s.At, 2), ms = s.Ms is double v ? Math.Round(v, 1) : (double?)null });
                p.Fresh.Clear();
            }
            live[p.Id] = p.Stats();
        }
        double at = Math.Min(NetCheckLogic.Seconds, _clock.Elapsed.TotalSeconds);
        string srvMode; lock (_srv ?? _net) srvMode = _srv?.Mode ?? "";
        _emit(new { run = Id, phase = "live", at = Math.Round(at, 1), left = (int)Math.Ceiling(NetCheckLogic.Seconds - at), points, live, srvMode, reset });
    }
}
