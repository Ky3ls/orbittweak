using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>One measurement: when it was sent (seconds since the start) and the round trip in ms (null = no answer
/// within the timeout).</summary>
public readonly record struct NcSample(double At, double? Ms);

/// <summary>The numbers of one stage of the route. Loss in percent; Median, P95, Jitter and Max in ms (null without
/// enough answers); Spikes = how many times the ping jumped far above its median, High = how many answers were that
/// far above it (a full line keeps the ping up for seconds: one spike, many high answers); Seconds = how long it was measured.</summary>
public sealed record NcStats(int Sent, int Answered, double Loss, double? Median, double? P95, double? Jitter, int Spikes, int High, double? Max, double Seconds);

/// <summary>A server address as FiveM gives it (connectEndPoints) or as it was saved: host and port, whether it is a web
/// address in front of the server (a proxy or protection service: the route is measured up to there) and whether
/// Cfx.re keeps the address secret ("private-placeholder").</summary>
public sealed record NcEndpoint(string Host, int Port, bool Proxy, bool Hidden);

/// <summary>What the check knows about one stage before it is judged.</summary>
public sealed class NcTarget
{
    public string Id { get; init; } = "";        // gw | net | srv
    public string Label { get; init; } = "";     // Router | Internet | Server
    public string Name { get; init; } = "";      // what is shown (the server's name)
    public string Host { get; init; } = "";      // the address that was measured
    public string Mode { get; init; } = "icmp";  // icmp = ping, tcp = connection set-up to Port
    public int Port { get; init; }
    public int IntervalMs { get; init; } = NetCheckLogic.IntervalMs;
    public bool Measured { get; init; } = true;  // false: no router address found, no server chosen or none to reach
    public IReadOnlyList<NcSample> Samples { get; init; } = Array.Empty<NcSample>();
}

/// <summary>Everything the verdict needs: the three stages and what happened around them.</summary>
public sealed class NcInput
{
    public NcTarget Gw { get; init; } = new() { Id = "gw", Label = "Router", Measured = false };
    public NcTarget Net { get; init; } = new() { Id = "net", Label = "Internet", Measured = false };
    public NcTarget? Srv { get; init; }
    public bool? Wifi { get; init; }
    public bool SrvSwitched { get; init; }       // the server never answered a ping, its port was measured instead
    public bool SrvProxy { get; init; }
    public string SrvProxyHost { get; init; } = "";
    public bool SrvHidden { get; init; }
    public string SrvSkipped { get; init; } = ""; // why a chosen server could not be measured (German, ends without a dot)
    public bool NoServer { get; init; }          // measured without a server on purpose
    public string ServerAddr { get; init; } = ""; // how the user knows it: cfx.re/join/… or IP:Port
    public DateTime When { get; init; }
    public string User { get; init; } = "";      // the Windows user name: never in the copied text
}

public sealed record NcTargetResult(string Id, string Label, string Name, string Host, string Mode, int Port, bool Measured,
    string Level, string Why, bool Start, NcStats Stats, List<double?[]> Trace);

public sealed record NcResult(string Kind, bool Mild, string Start, string Title, string Text, List<string> Advice, List<string> Notes,
    bool? Wifi, double Seconds, List<NcTargetResult> Targets, string Report);

/// <summary>
/// Netz-Check (2.31): the pure rules. A minute of pings to the router, a public anchor and the FiveM server, then the
/// numbers per stage and where a lag problem starts: in the home network/WLAN, at the provider, or at the server and the
/// route there. A problem only counts where it starts AND stays visible on every stage after it (traffic to the server
/// passes the router, so a router that answers pings unevenly while the server is calm is no problem: routers answer
/// pings on the side). No Windows calls here, so tests/NetCheckLogic.Tests checks it anywhere.
/// </summary>
public static class NetCheckLogic
{
    public const int Seconds = 60;
    public const int IntervalMs = 500;       // two pings per second per stage
    public const int TcpIntervalMs = 1000;   // connection set-ups to a server are gentler: one per second
    public const int TimeoutMs = 1000;
    public const int SilentAfter = 6;        // this many tries without any answer = the stage does not answer
    public const double MinSeconds = 15;     // below that there is no verdict
    public const string Anchor = "1.1.1.1";
    public const int DefaultPort = 30120;

    // ---------------------------------------------------------------- numbers ----------------------------------------------------------------

    public static NcStats Stats(IReadOnlyList<NcSample> samples, int intervalMs = IntervalMs)
    {
        int sent = samples.Count;
        var rtt = new List<double>(sent);
        foreach (var s in samples) if (s.Ms is double v && v >= 0 && !double.IsNaN(v)) rtt.Add(v);
        int ans = rtt.Count;
        double loss = sent == 0 ? 0 : Math.Round((sent - ans) * 100.0 / sent, 1);
        double seconds = sent * Math.Max(1, intervalMs) / 1000.0;
        if (ans == 0) return new NcStats(sent, 0, loss, null, null, null, 0, 0, null, seconds);
        var sorted = rtt.OrderBy(x => x).ToList();
        double med = Median(sorted);
        double? jit = ans >= 2 ? Math.Round(Jitter(rtt), 1) : null;
        double lim = SpikeLimit(med);
        return new NcStats(sent, ans, loss, Math.Round(med, 1), Math.Round(Percentile(sorted, 95), 1), jit, Spikes(rtt, med), rtt.Count(v => v > lim), sorted[^1], seconds);
    }

    /// <summary>The middle value of a sorted list (the mean of the two middle ones for an even count).</summary>
    public static double Median(IReadOnlyList<double> sorted)
    {
        if (sorted.Count == 0) return 0;
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    /// <summary>Nearest rank: the smallest value that p percent of the values do not exceed.</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    /// <summary>Jitter = the mean absolute difference of consecutive round trips (a lost ping is skipped).</summary>
    public static double Jitter(IReadOnlyList<double> rtt)
    {
        if (rtt.Count < 2) return 0;
        double sum = 0;
        for (int i = 1; i < rtt.Count; i++) sum += Math.Abs(rtt[i] - rtt[i - 1]);
        return sum / (rtt.Count - 1);
    }

    /// <summary>A spike: a round trip above the median plus the median again, and at least 30 ms above it (so 4 ms on
    /// a router becomes a spike from 34 ms, 40 ms to a server from 80 ms).</summary>
    public static double SpikeLimit(double median) => median + Math.Max(30, median);

    /// <summary>How many times the ping jumped above the spike limit; several high pings in a row count once.</summary>
    public static int Spikes(IReadOnlyList<double> rtt, double median)
    {
        double lim = SpikeLimit(median);
        int n = 0; bool inSpike = false;
        foreach (var v in rtt)
        {
            bool hi = v > lim;
            if (hi && !inSpike) n++;
            inSpike = hi;
        }
        return n;
    }

    // ---------------------------------------------------------------- one stage ----------------------------------------------------------------

    /// <summary>ok / warn / bad for a stage that answered, silent when it never answered (after SilentAfter tries), none
    /// when it was not measured. Why = the worst numbers in words, made to follow "gab es …".</summary>
    public static (string level, string why) Level(string id, NcStats s, bool measured = true)
    {
        if (!measured || s.Sent == 0) return ("none", "");
        if (s.Answered == 0) return s.Sent >= SilentAfter ? ("silent", "keine Antwort") : ("none", "");
        bool gw = id == "gw";
        double perMin = s.Spikes * 60.0 / Math.Max(s.Seconds, MinSeconds);
        double high = s.High * 100.0 / s.Answered, highSec = s.High * s.Seconds / Math.Max(1, s.Sent);
        double jit = s.Jitter ?? 0, med = s.Median ?? 0;
        var parts = new List<(int lv, string why)>
        {
            (s.Loss < 1 ? 0 : s.Loss < 3 ? 1 : 2, Pct(s.Loss) + " Verlust"),
            (perMin < 2 ? 0 : perMin < 5 ? 1 : 2, s.Spikes == 1 ? $"eine Ping-Spitze bis {Ms(s.Max ?? 0)}" : $"{s.Spikes} Ping-Spitzen bis {Ms(s.Max ?? 0)}"),
            // (a long stretch of high ping is one spike, but felt the whole time)
            (high < 3 ? 0 : high < 10 ? 1 : 2, $"{Sec(highSec)} lang stark erhöhten Ping bis {Ms(s.Max ?? 0)}"),
            (jit < (gw ? 4 : 6) ? 0 : jit < (gw ? 10 : 15) ? 1 : 2, $"einen Jitter von {Ms(jit)}"),
        };
        // a busy WLAN or an overloaded router already shows in the plain ping to it (to the internet that is distance)
        if (gw) parts.Add((med < 10 ? 0 : med < 30 ? 1 : 2, $"im Mittel {Ms(med)} Ping"));
        int worst = parts.Max(p => p.lv);
        if (worst == 0) return ("ok", "");
        // spikes and a long high stretch say the same twice: keep the one that tells more (long stretches: the stretch)
        if (parts[1].lv == worst && parts[2].lv == worst) parts.RemoveAt(s.High > s.Spikes * 3 ? 1 : 2);
        var why = parts.Where(p => p.lv == worst).Take(2).Select(p => p.why).ToList();
        return (worst == 2 ? "bad" : "warn", string.Join(" und ", why));
    }

    /// <summary>The level as a number for the verdict: ok 0, warn 1, bad 2. A router that does not answer pings is
    /// unknown (null: many routers do that, it says nothing); an internet anchor or a server that does not answer is bad.</summary>
    public static int? Rank(string id, string level) => level switch
    {
        "ok" => 0, "warn" => 1, "bad" => 2,
        "silent" => id == "gw" ? null : 2,
        _ => null,
    };

    /// <summary>The server never answered a ping while the router or the internet did: its port is measured instead.</summary>
    public static bool SwitchToTcp(int srvSent, int srvAnswered, int othersAnswered, int port)
        => srvSent >= SilentAfter && srvAnswered == 0 && othersAnswered > 0 && port is > 0 and <= 65535;

    // ---------------------------------------------------------------- the verdict ----------------------------------------------------------------

    public static NcResult Evaluate(NcInput x)
    {
        var gw = Judge(x.Gw); var net = Judge(x.Net);
        NcTarget? srvT = x.Srv is { Measured: true } st ? st : null;
        var srv = srvT != null ? Judge(srvT) : default;
        bool hasSrv = srvT != null;
        // (a server measured by its port started a few seconds later: its own minute is shorter, the run is not)
        double seconds = Math.Round(Math.Min(Seconds, Math.Max(gw.s.Seconds, Math.Max(net.s.Seconds, hasSrv ? srv.s.Seconds : 0))), 1);

        int? gL = Rank("gw", gw.level), nL = Rank("net", net.level), sL = hasSrv ? Rank("srv", srv.level) : null;
        // a problem counts where it starts and on every stage after it
        int? home = gL is int g ? Min(g, nL, sL) : null;
        int? prov = nL is int n ? Min(n, sL) : null;
        int? serv = sL;
        int worst = Math.Max(home ?? 0, Math.Max(prov ?? 0, serv ?? 0));

        bool nothing = x.Gw.Samples.All(s => s.Ms == null) && x.Net.Samples.All(s => s.Ms == null) && (!hasSrv || srvT!.Samples.All(s => s.Ms == null));
        string kind; string start = "";
        if (seconds < MinSeconds) kind = "short";
        else if (nothing) kind = "none";
        else if (worst == 0) kind = "ok";
        else if (home == worst) { kind = "home"; start = "gw"; }
        else if (prov == worst) { kind = "provider"; start = "net"; }
        else { kind = "server"; start = "srv"; }
        bool mild = worst == 1 && kind is "home" or "provider" or "server";

        bool wifi = x.Wifi == true;
        string dest = hasSrv ? "zum Server" : "ins Internet";
        bool gwKnown = gL != null;
        bool netSilent = net.level == "silent", srvSilent = hasSrv && srv.level == "silent";
        string title, text; var advice = new List<string>();
        switch (kind)
        {
            case "short":
                title = "Zu kurz für ein Urteil";
                text = $"Für ein verlässliches Ergebnis braucht der Test mindestens {MinSeconds:0} Sekunden.";
                advice.Add($"Lass ihn am besten die vollen {Seconds} Sekunden laufen.");
                break;
            case "none":
                title = "Keine Verbindung";
                text = "Weder dein Router noch das Internet haben geantwortet.";
                advice.Add("Prüf, ob dein PC mit dem Netzwerk verbunden ist (das Netzwerksymbol unten rechts in der Taskleiste).");
                advice.Add("Startet dein Router gerade neu, warte eine Minute und teste dann noch einmal.");
                break;
            case "home":
                if (wifi)
                {
                    title = mild ? "Dein WLAN schwankt leicht" : "Das Problem fängt in deinem WLAN an";
                    text = $"Schon zwischen deinem PC und deinem Router gab es {gw.why}" + (mild ? $". Das ist noch kein großes Problem, zieht sich aber bis {dest} durch." : $", und das zieht sich bis {dest} durch.");
                    advice.Add("Ein LAN-Kabel ist der größte Hebel: damit fällt dieser Teil der Strecke praktisch weg.");
                    advice.Add("Geht kein Kabel: verbinde dich mit dem 5-GHz-Netz deines Routers, falls er eins hat, und rück näher an den Router. Jede Wand dazwischen kostet.");
                    advice.Add("Laden andere Geräte im WLAN gerade viel (Streams, Updates), teilen sie sich die Funkzeit mit dir.");
                }
                else
                {
                    title = mild ? "Leichte Schwankungen schon bis zum Router" : "Das Problem fängt zwischen PC und Router an";
                    text = $"Schon zwischen deinem PC und deinem Router gab es {gw.why}" + (mild ? $". Das ist noch kein großes Problem, zieht sich aber bis {dest} durch." : $", und das zieht sich bis {dest} durch.")
                        + (x.Wifi == false ? " Über Kabel ist das ungewöhnlich." : "");
                    advice.Add("Probier ein anderes LAN-Kabel und einen anderen Anschluss am Router.");
                    advice.Add("Hängt ein Switch, ein Powerline-Adapter oder ein Repeater dazwischen, teste einmal ohne.");
                    advice.Add("Ein Neustart des Routers kann helfen, wenn er sich verschluckt hat.");
                }
                break;
            case "provider":
                if (netSilent && (!hasSrv || srvSilent))
                {
                    title = "Hinter deinem Router kommt nichts an";
                    text = gwKnown ? "Dein Router antwortet, aber das Internet dahinter nicht." : "Weder das Internet noch dein Server haben geantwortet.";
                    advice.Add("Starte deinen Router neu und warte, bis er wieder online ist.");
                    advice.Add("Hilft das nicht, liegt wahrscheinlich eine Störung bei deinem Anbieter vor: schau auf seiner Störungsseite nach oder ruf an.");
                    break;
                }
                title = mild ? "Leichte Schwankungen hinter deinem Router" : gwKnown ? "Das Problem fängt hinter deinem Router an" : "Das Problem liegt bei deinem Anschluss";
                var nw = net.level == "silent" ? srv.why : net.why;
                text = gwKnown
                    ? $"Bis zu deinem Router ist alles ruhig, erst dahinter gab es {nw}: das ist die Strecke deines Internetanbieters." + (mild ? " Noch kein großes Problem." : "")
                    : $"Dein Router antwortet nicht auf Ping, deshalb lässt sich nicht trennen, ob es schon in deinem Heimnetz anfängt. Ab dem Internet gab es {nw}.";
                if (!gwKnown && wifi) advice.Add("Du bist per WLAN verbunden: teste einmal mit LAN-Kabel. Bleibt es dann gleich, liegt es nicht am WLAN.");
                advice.Add("Läuft gerade ein großer Download, Upload oder Stream, auch auf einem anderen Gerät bei dir? Der füllt die Leitung, und der Ping steigt.");
                advice.Add("Teste zu einer anderen Uhrzeit noch einmal: abends ist die Leitung bei vielen Anbietern voller.");
                advice.Add("Bleibt es so, schick deinem Anbieter dieses Ergebnis (Ergebnis kopieren).");
                break;
            case "server":
                if (srvSilent)
                {
                    title = "Dein Server antwortet nicht";
                    text = $"Router und Internet antworten, dein Server aber weder auf Ping noch auf einen Verbindungsaufbau zu Port {srvT!.Port}. Er ist offline, überlastet oder blockt solche Anfragen.";
                    advice.Add("Schau im Discord des Servers, ob er gerade neu startet oder gewartet wird.");
                    advice.Add("Auf deinem PC kannst du daran nichts ändern.");
                    break;
                }
                title = mild ? "Leichte Schwankungen auf dem Weg zum Server" : "Das Problem liegt beim Server oder auf dem Weg dorthin";
                text = $"Bis ins Internet ist alles ruhig, erst auf dem Weg zum Server gab es {srv.why}." + (mild ? " Das ist noch kein großes Problem." : "");
                advice.Add("Auf deinem PC kannst du daran nichts ändern, das kann auch kein Tool.");
                advice.Add("Sag den Server-Admins Bescheid und schick ihnen dieses Ergebnis (Ergebnis kopieren).");
                advice.Add("Laggen andere Spieler auch, liegt es am Server. Hast nur du den Lag, liegt es eher an der Strecke deines Anbieters zum Server.");
                break;
            default:
                title = "Deine Verbindung war ruhig";
                text = $"In diesen {seconds:0} Sekunden gab es bis {(hasSrv ? "zum Server" : "ins Internet")} keinen nennenswerten Verlust und kaum Schwankungen.";
                advice.Add(hasSrv ? "Ruckelt FiveM trotzdem, liegt es eher am Server selbst (zum Beispiel an seiner Leistung) oder an FPS-Einbrüchen auf deinem PC. Das zeigt dieser Test nicht."
                    : "Ruckelt FiveM trotzdem, kann es am Weg zum Server, am Server selbst oder an FPS-Einbrüchen auf deinem PC liegen. Das zeigt dieser Test ohne Server nicht.");
                advice.Add("Am meisten sagt der Test, wenn du ihn genau dann startest, wenn der Lag auftritt.");
                break;
        }

        // ---- notes: how it was measured first, then what the numbers do not say on their own
        var notes = new List<string>();
        if (hasSrv && x.SrvSwitched) notes.Add($"Dein Server antwortet nicht auf Ping. Gemessen wurde deshalb der Verbindungsaufbau zu Port {srvT!.Port}, einmal pro Sekunde.");
        if (hasSrv && x.SrvProxy && !x.SrvHidden) notes.Add($"Der Server ist über {x.SrvProxyHost} erreichbar (ein Proxy oder Schutzdienst davor): gemessen wurde der Weg bis dorthin.");
        if (x.SrvHidden) notes.Add("Der Server hält seine Adresse geheim, deshalb wurden nur Router und Internet gemessen.");
        else if (!hasSrv && x.SrvSkipped.Length > 0) notes.Add(x.SrvSkipped + ", deshalb wurden nur Router und Internet gemessen.");
        else if (!hasSrv && x.NoServer) notes.Add("Ohne Server gemessen: der Test zeigt nur Router und Internet.");
        if (!x.Gw.Measured) notes.Add("Kein Router gefunden: deine Verbindung hat kein Standard-Gateway.");
        else if (gw.level == "silent" && kind != "none") notes.Add("Dein Router antwortet nicht auf Ping. Das machen manche Router so, es ist kein Fehler.");
        if (kind is not ("short" or "none"))
        {
            if (gL >= 1 && home < gL)
                notes.Add($"Dein Router beantwortet Pings unruhig ({gw.why}), die Strecke dahinter ist aber ruhiger. Router beantworten Pings nur nebenbei, das allein ist kein Problem.");
            if (hasSrv && nL >= 1 && prov < nL)
                notes.Add(netSilent ? $"{Anchor} antwortet in deinem Netz nicht auf Ping, dein Server schon: für FiveM zählt der Weg zum Server."
                    : $"Nur bis {Anchor} gab es {net.why}, zum Server nicht: für FiveM zählt der Weg zum Server.");
            if (kind != "home" && home == 1 && worst == 2) notes.Add($"Leicht schwankt es schon bis zum Router ({gw.why}).");
            if (hasSrv && !srvSilent && srv.s.Median is double sm && sm >= 100 && kind != "server")
                notes.Add($"Zum Server sind es im Mittel {Ms(sm)}: das ist vor allem Entfernung, kein Fehler.");
            if (kind == "ok" && wifi) notes.Add("Du bist per WLAN verbunden, im Test war es ruhig. Schwankt es zu anderen Zeiten, bleibt ein LAN-Kabel die stabilste Lösung.");
        }

        var targets = new List<NcTargetResult> { Result(x.Gw, gw, start == "gw"), Result(x.Net, net, start == "net") };
        if (x.Srv != null) targets.Add(Result(x.Srv, hasSrv ? srv : (Stats(Array.Empty<NcSample>()), "none", ""), start == "srv"));
        var res = new NcResult(kind, mild, start, title, text, advice, notes, x.Wifi, seconds, targets, "");
        return res with { Report = Report(res, x) };
    }

    private static (NcStats s, string level, string why) Judge(NcTarget t)
    {
        var s = Stats(t.Samples, t.IntervalMs);
        var (lv, why) = Level(t.Id, s, t.Measured);
        return (s, lv, why);
    }

    private static int Min(int a, params int?[] more)
    {
        foreach (var m in more) if (m is int v && v < a) a = v;
        return a;
    }

    private static NcTargetResult Result(NcTarget t, (NcStats s, string level, string why) j, bool start)
        => new(t.Id, t.Label, t.Name, t.Host, t.Mode, t.Port, t.Measured, j.level, j.why, start, j.s,
            t.Samples.Select(s => new double?[] { Math.Round(s.At, 2), s.Ms is double v ? Math.Round(v, 1) : null }).ToList());

    // ---------------------------------------------------------------- the copied text ----------------------------------------------------------------

    /// <summary>The result as plain text for the provider, the server admins or a support channel. Holds no user name
    /// (it is replaced, also inside a server name) and no path.</summary>
    public static string Report(NcResult r, NcInput x)
    {
        var b = new StringBuilder();
        string link = r.Wifi == true ? "WLAN" : r.Wifi == false ? "Kabel" : "unbekannt";
        b.Append("OrbitTweak Netz-Check · ").Append(x.When.ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture)).Append(" Uhr · ")
         .Append(r.Seconds.ToString("0", CultureInfo.InvariantCulture)).Append(" Sekunden · Verbindung: ").Append(link).Append('\n');
        b.Append("Ergebnis: ").Append(r.Title).Append('\n').Append(r.Text).Append("\n\n");
        foreach (var t in r.Targets)
        {
            string head = t.Id switch
            {
                "srv" => "Server" + (t.Name.Length > 0 && t.Name != "Server" ? " " + t.Name : "") + (x.ServerAddr.Length > 0 ? $" ({x.ServerAddr}" + (t.Host.Length > 0 && t.Host != x.ServerAddr ? $" · {t.Host}" : "") + ")" : t.Host.Length > 0 ? $" ({t.Host})" : ""),
                _ => t.Label + (t.Host.Length > 0 ? $" ({t.Host})" : ""),
            };
            b.Append(head).Append(": ");
            if (!t.Measured || t.Level == "none") { b.Append("nicht gemessen\n"); continue; }
            if (t.Level == "silent") { b.Append("keine Antwort auf ").Append(t.Stats.Sent).Append(t.Mode == "tcp" ? $" Verbindungsaufbauten zu Port {t.Port}\n" : " Pings\n"); continue; }
            var s = t.Stats;
            b.Append("Ping ").Append(Ms(s.Median ?? 0)).Append(" (Median) · 95 % bis ").Append(Ms(s.P95 ?? 0))
             .Append(" · Jitter ").Append(Ms(s.Jitter ?? 0)).Append(" · Verlust ").Append(Pct(s.Loss)).Append($" ({s.Sent - s.Answered} von {s.Sent})")
             .Append(" · ").Append(s.Spikes == 1 ? "1 Spitze" : $"{s.Spikes} Spitzen").Append(s.Max is double mx ? $" (höchster Wert {Ms(mx)})" : "")
             .Append(s.High > 0 ? $" · {Sec(s.High * s.Seconds / Math.Max(1, s.Sent))} stark erhöht" : "")
             .Append(t.Mode == "tcp" ? $" · gemessen über Port {t.Port}" : "").Append(t.Start ? " ← hier fängt es an" : "").Append('\n');
        }
        if (r.Notes.Count > 0) { b.Append('\n'); foreach (var n in r.Notes) b.Append("Hinweis: ").Append(n).Append('\n'); }
        b.Append("\nGemessen: zwei Pings pro Sekunde je Strecke (Server ohne Ping-Antwort: ein Verbindungsaufbau pro Sekunde), Zeitlimit ")
         .Append(TimeoutMs).Append(" ms. Jitter = mittlerer Unterschied aufeinanderfolgender Pings, Spitze = mehr als doppelter Median und mindestens 30 ms darüber.");
        return Redact(b.ToString(), x.User);
    }

    /// <summary>The Windows user name out of a text that is shared (as a whole word, any case).</summary>
    public static string Redact(string text, string? user)
    {
        user = (user ?? "").Trim();
        if (user.Length < 2) return text;
        return Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(user)}(?![\p{{L}}\p{{N}}])", "<Nutzer>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // ---------------------------------------------------------------- addresses ----------------------------------------------------------------

    private static readonly Regex HostRx = new(@"^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)*$", RegexOptions.CultureInvariant);
    private static readonly Regex V6Rx = new(@"^[0-9A-Fa-f:\.]{2,45}$", RegexOptions.CultureInvariant);

    /// <summary>An entry of connectEndPoints ("1.2.3.4:30120", "https://play.example.com/", "[2001:db8::1]:30120") or a
    /// saved address ("host:port", "host"). null when it is no address.</summary>
    public static NcEndpoint? ParseEndpoint(string? ep)
    {
        ep = (ep ?? "").Trim();
        if (ep.Length == 0 || ep.Length > 300) return null;
        if (ep.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || ep.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(ep, UriKind.Absolute, out var u) || u.Host.Length == 0) return null;
            var h = u.Host.Trim('[', ']');
            if (!HostRx.IsMatch(h) && !(h.Contains(':') && V6Rx.IsMatch(h))) return null;
            bool hidden = h.Equals("private-placeholder.cfx.re", StringComparison.OrdinalIgnoreCase);
            return new NcEndpoint(h, u.Port > 0 ? u.Port : u.Scheme == "https" ? 443 : 80, true, hidden);
        }
        string host; int port = DefaultPort;
        if (ep.StartsWith('['))
        {
            int close = ep.IndexOf(']');
            if (close < 2) return null;
            host = ep[1..close];
            var rest = ep[(close + 1)..];
            if (rest.Length > 0 && (!rest.StartsWith(':') || !TryPort(rest[1..], out port))) return null;
            return V6Rx.IsMatch(host) && host.Contains(':') ? new NcEndpoint(host, port, false, false) : null;
        }
        int colon = ep.LastIndexOf(':');
        if (colon >= 0 && ep.IndexOf(':') != colon) return V6Rx.IsMatch(ep) ? new NcEndpoint(ep, DefaultPort, false, false) : null;   // a bare IPv6 address
        host = colon >= 0 ? ep[..colon] : ep;
        if (colon >= 0 && !TryPort(ep[(colon + 1)..], out port)) return null;
        return HostRx.IsMatch(host) ? new NcEndpoint(host, port, false, false) : null;
    }

    private static bool TryPort(string s, out int port)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535;

    /// <summary>A server name without FiveM's colour codes (^0…^9), squeezed spaces, at most 60 characters.</summary>
    public static string CleanName(string? name)
    {
        var s = Regex.Replace(name ?? "", @"\^\d", "");
        s = Regex.Replace(s, @"[\u0000-\u001F\u007F]", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > 60 ? s[..59].TrimEnd() + "…" : s;
    }

    // ---------------------------------------------------------------- German numbers ----------------------------------------------------------------

    /// <summary>"<1 ms" below 1, one decimal below 10 ("4,1 ms"), whole numbers above ("42 ms").</summary>
    public static string Ms(double v)
    {
        if (v < 1) return "<1 ms";
        return (v < 10 ? Dec(v) : Math.Round(v).ToString("0", CultureInfo.InvariantCulture)) + " ms";
    }

    /// <summary>"0 %", one decimal below 10 ("2,5 %"), whole numbers above.</summary>
    public static string Pct(double v)
    {
        if (v <= 0) return "0 %";
        return (v < 10 ? Dec(v) : Math.Round(v).ToString("0", CultureInfo.InvariantCulture)) + " %";
    }

    /// <summary>"eine Sekunde", "16 Sekunden" (whole seconds, at least one).</summary>
    public static string Sec(double v)
    {
        int n = Math.Max(1, (int)Math.Round(v, MidpointRounding.AwayFromZero));
        return n == 1 ? "eine Sekunde" : n + " Sekunden";
    }

    private static string Dec(double v)
    {
        var s = Math.Round(v, 1).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
        return s.EndsWith(",0") ? s[..^2] : s;
    }
}
