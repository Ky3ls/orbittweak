using System.Text.Json;
using OrbitTweak.Core;

namespace OrbitTweak;

/// <summary>
/// Netz-Check (2.31): a minute of pings to the router, 1.1.1.1 and the FiveM server shows where a lag starts (Web/netcheck.js).
/// The measuring is in Core/NetCheck.cs, the rules in Core/NetCheckLogic.cs.
///   netcheck.start { server?, name? } → { ok, run, seconds, wifi, link, targets: [{ id, label, name, host, mode, port, measured, note }] }
///                                       or { ok: false, error }   (server = "cfx:code", "ip:host:port" or what the user typed;
///                                       none = router and internet only)
///   then the event "netcheck": { run, phase: "live", at, left, points: [{ t, at, ms }], live: { gw, net, srv: stats }, srvMode, reset: [id] }
///                              every half second, at the end { run, phase: "done", result } or { run, phase: "error", error }
///   netcheck.stop { run } → { ok }   (nothing more is sent for that run)
/// </summary>
public partial class MainWindow
{
    private NetCheckRun? _nc;
    private int _ncGen;   // a newer start or a stop makes a start that is still looking up its server give up

    private async Task<object?> NetCheckCmd(string cmd, JsonElement a)
    {
        switch (cmd)
        {
            case "netcheck.start":
            {
                _nc?.Cancel(); _nc = null;
                int gen = ++_ncGen;
                var q = Str(a, "server").Trim();
                if (q.Length > 300) q = "";
                try
                {
                    var (gw, wifi, link, tunnel) = await Task.Run(NetCheck.Route);
                    if (link.Length == 0) return new { ok = false, error = "Keine Netzwerkverbindung gefunden. Prüf, ob dein PC per WLAN oder Kabel verbunden ist." };
                    var srv = q.Length > 0 ? await NetCheck.Server(q, Str(a, "name"), CancellationToken.None) : null;
                    if (gen != _ncGen) return new { ok = false, error = "Abgebrochen" };
                    var run = new NetCheckRun(gen, gw, wifi, tunnel, srv, d => Emit("netcheck", d), r => Log($"📶 Netz-Check: {r.Title}"));
                    _nc = run;
                    run.Begin();
                    return new { ok = true, run = run.Id, seconds = NetCheckLogic.Seconds, wifi, link, targets = run.Plan() };
                }
                catch (Exception ex) { return new { ok = false, error = "Der Netz-Check ließ sich nicht starten: " + ex.Message }; }
            }

            case "netcheck.stop":
            {
                int id = Int(a, "run");
                if (id == 0 || id == _ncGen) { _ncGen++; _nc?.Cancel(); _nc = null; }
                return new { ok = true };
            }
        }
        throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
    }
}
