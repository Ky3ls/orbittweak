using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using OrbitTweak.Core;
using static OrbitTweak.Core.ServerWatchLogic;

namespace OrbitTweak;

/// <summary>
/// 2.31, Server page: Platz-Alarm (watch a full or offline favourite, note on the desktop when a slot is free),
/// Freunde online (find friends in the favourites' public player lists) and Server auf den Desktop (a shortcut that
/// starts OrbitTweak with "--connect &lt;code&gt;" and so connects with the pre-launch boost). The work is in
/// Core/ServerWatch.cs, Core/ServerShortcut.cs and Core/Notify.cs, the rules in Core/ServerWatchLogic.cs.
/// Nothing here touches FiveM or GTA: Cfx.re's public API, process names, the desktop folder.
/// </summary>
public partial class MainWindow
{
    private readonly ServerWatcher _sw = new();
    private readonly FriendScan _swScan = new();
    private bool _swInit, _swConnecting, _swBgBusy;
    private int _swNoted;   // the friends round whose "came online" notes are done
    private string? _swPending; private string _swPendingSrc = ""; private DateTime _swPendingAt;
    private DateTime _swBgAt;
    private DispatcherTimer? _swTimer;

    /// <summary>Once, from SourceInitialized: the hand-over from a second start, the watch events, a "--connect" this
    /// start came with, and a quiet 2 s timer (a waiting connect, friends in the background).</summary>
    private void SwInit(IntPtr hwnd)
    {
        if (_swInit) return;
        _swInit = true;
        try
        {
            HwndSource.FromHwnd(hwnd)?.AddHook(SwWndProc);
            _sw.Changed += () => Emit("swWatch", new { watches = _sw.Views() });
            _sw.Fired += w => Dispatcher.InvokeAsync(() => SwFired(w));
            if (ConnectArg(Environment.GetCommandLineArgs()) is { } code) SwQueue(code, "desktop");
            _swTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _swTimer.Tick += (_, _) => SwTick();
            _swTimer.Start();
        }
        catch { }   // runs in SourceInitialized before the hotkeys are registered: nothing here may stop that
    }

    /// <summary>A second start (desktop shortcut) handed its join code over. Only a valid code is taken.</summary>
    private IntPtr SwWndProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg != ServerShortcut.WM_COPYDATA) return IntPtr.Zero;
        if (ServerShortcut.ReadCopy(lp) is not { } code) return IntPtr.Zero;
        handled = true;
        Dispatcher.InvokeAsync(() => SwQueue(code, "desktop"));
        return (IntPtr)1;
    }

    private void SwQueue(string code, string source)
    {
        if (!ValidCode(code)) return;
        _swPending = code; _swPendingSrc = source; _swPendingAt = DateTime.Now;
        SwTick();
    }

    private void SwTick()
    {
        try
        {
            // a connect from the desktop shortcut waits for the key check of a fresh start (3 minutes at most)
            if (_swPending is { } code && !_swConnecting)
            {
                if (DateTime.Now - _swPendingAt > TimeSpan.FromMinutes(3)) { _swPending = null; Log("🖥 Desktop-Verknüpfung: nicht verbunden, OrbitTweak war noch nicht freigeschaltet"); }
                else if (_authed) { _swPending = null; _ = SwConnect(code, _swPendingSrc, null); }
            }
            // "Melden, wenn ein Freund online kommt": every 3 minutes while the app runs (the page's own rounds count too)
            var f = S.SrvFriends;
            if (f.Notify && f.Names.Count > 0 && _authed && !_swBgBusy && DateTime.UtcNow - _swBgAt >= TimeSpan.FromSeconds(30)
                && DateTime.UtcNow - (_swScan.Last ?? DateTime.MinValue) >= FriendBackgroundEvery)
                _ = SwFriendsBackground();
        }
        catch { }
    }

    private async Task<object?> SwCmd(string cmd, JsonElement a)
    {
        try
        {
            switch (cmd)
            {
                // -> { watches, friends: { names, notify, partial }, max, maxFriends }
                case "sw.state": return SwState();

                // { code: "cfx:…", online, players, max, auto } -> { ok, watches, free?, error? }
                case "sw.watch": return await SwWatch(a);

                // { code } -> { ok, watches }: ends a watch or takes a finished one off the list
                case "sw.unwatch":
                {
                    var code = CodeOfKey(Str(a, "code"));
                    bool ok = code != null && _sw.Stop(code);
                    return new { ok, watches = _sw.Views() };
                }

                // { code, on } -> { ok, watches }
                case "sw.auto":
                {
                    var code = CodeOfKey(Str(a, "code"));
                    bool ok = code != null && _sw.SetAuto(code, Bool(a, "on"));
                    return new { ok, watches = _sw.Views() };
                }

                // { } -> the friends view (a new round only when one may run: page shown, 60 s apart)
                case "sw.friends": return await SwFriendsView(true);

                // { name } -> { ok, names, error? }
                case "sw.friendAdd":
                {
                    var err = AddFriend(S.SrvFriends.Names, Str(a, "name"));
                    if (err != null) return new { ok = false, error = err, names = S.SrvFriends.Names };
                    S.Save();
                    return new { ok = true, names = S.SrvFriends.Names };
                }
                case "sw.friendRemove":
                {
                    bool ok = RemoveFriend(S.SrvFriends.Names, Str(a, "name"));
                    if (ok) S.Save();
                    return new { ok, names = S.SrvFriends.Names };
                }

                // { notify?, partial? } -> { ok, notify, partial }
                case "sw.friendOpts":
                {
                    var f = S.SrvFriends;
                    if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("notify", out var n) && n.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        bool on = n.GetBoolean();
                        if (on && !f.Notify) _swScan.Seen.Reset();   // friends who are on already do not bring a note
                        f.Notify = on;
                    }
                    if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("partial", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        f.Partial = p.GetBoolean();
                    S.Save();
                    return new { ok = true, notify = f.Notify, partial = f.Partial };
                }

                // { code, name } -> { ok, path, file, serverIcon, replaced, error? }
                case "sw.shortcut": return await SwShortcut(a);
            }
        }
        catch (Exception ex) { return new { ok = false, error = "Das hat nicht geklappt: " + ex.Message }; }
        throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
    }

    private object SwState() => new
    {
        watches = _sw.Views(),
        friends = new { names = S.SrvFriends.Names, notify = S.SrvFriends.Notify, partial = S.SrvFriends.Partial },
        max = MaxWatches, maxFriends = MaxFriends,
    };

    // =====================================================================
    //  Platz-Alarm
    // =====================================================================
    private async Task<object> SwWatch(JsonElement a)
    {
        var code = CodeOfKey(Str(a, "code"));
        if (code == null) return new { ok = false, error = "Platz-Alarm gibt es nur für Server mit Join-Code (cfx.re/join/…).", watches = _sw.Views() };
        if (_sw.Find(code) is { } had && Active(had.State)) return new { ok = true, watches = _sw.Views() };
        if (_sw.ActiveCount >= MaxWatches) return new { ok = false, error = $"Es laufen schon {MaxWatches} Platz-Alarme. Beende erst einen.", watches = _sw.Views() };
        var (fm, game) = await Task.Run(ServerWatcher.FiveMState);
        if (game) return new { ok = false, error = "Du bist gerade im Spiel. Ein Platz-Alarm endet, sobald FiveM spielt.", watches = _sw.Views() };

        var s = await CfxProbe.Get(code, TimeSpan.FromSeconds(10));
        WatchKind kind;
        if (s.Kind == ProbeKind.Ok)
        {
            if (s.Max <= 0) return new { ok = false, error = "Der Server meldet keine Platzzahl. Ob ein Platz frei wird, lässt sich hier nicht erkennen.", watches = _sw.Views() };
            if (!IsFull(s.Players, s.Max)) return new { ok = false, free = true, error = $"Gerade ist ein Platz frei ({s.Players} von {s.Max}). Verbinde dich einfach.", watches = _sw.Views() };
            kind = WatchKind.Slot;
        }
        else if (s.Kind == ProbeKind.NotFound) kind = WatchKind.Online;
        else kind = Bool(a, "online") ? WatchKind.Slot : WatchKind.Online;   // Cfx.re did not answer: what the page saw

        var name = ServerName(s.Name ?? Str(a, "name"), "cfx.re/join/" + code);
        var w = new SlotWatch
        {
            Code = code, Name = name, Kind = kind, Auto = Bool(a, "auto"), FmAtStart = fm,
            Online = s.Kind == ProbeKind.Ok || (s.Kind != ProbeKind.NotFound && Bool(a, "online")),
            Players = s.Kind == ProbeKind.Ok ? s.Players : Int(a, "players"), Max = s.Kind == ProbeKind.Ok ? s.Max : Int(a, "max"),
        };
        if (_sw.Start(w, s) is { } err) return new { ok = false, error = err, watches = _sw.Views() };
        Log(kind == WatchKind.Slot ? $"🔔 Platz-Alarm für „{name}“: meldet sich, sobald ein Platz frei ist" : $"🔔 Platz-Alarm für „{name}“: meldet sich, sobald der Server wieder online ist");
        return new { ok = true, watches = _sw.Views() };
    }

    /// <summary>A watched server has a free slot or is back: page event, note on the desktop (never over the game),
    /// blinking taskbar button, and with "Automatisch verbinden" FiveM starts (only when it is not running).</summary>
    private async void SwFired(SlotWatch w)
    {
        try
        {
            var (fm, _) = await Task.Run(ServerWatcher.FiveMState);
            bool auto = MayAutoConnect(w.Auto, fm, _authed);
            bool slot = w.Kind == WatchKind.Slot;
            string nums = w.Max > 0 ? $"{w.Players} von {w.Max} Spielern" : "";
            Emit("swFire", new { code = "cfx:" + w.Code, name = w.Name, kind = slot ? "slot" : "online", players = w.Players, max = w.Max, auto });
            Log(slot ? $"🔔 Platz frei auf „{w.Name}“ ({nums})" : $"🔔 „{w.Name}“ ist wieder online{(nums.Length > 0 ? $" ({nums})" : "")}");

            if (!GameFocus.IsGame())
            {
                string title = slot ? $"Platz frei auf {w.Name}" : $"{w.Name} ist wieder online";
                string line;
                if (auto) line = "FiveM startet mit deinem Boost und verbindet. Ist der Platz bis dahin weg, meldet FiveM „voll“ oder du landest in der Warteschlange des Servers.";
                else if (slot) line = $"{nums}. Schnell sein: Ein freier Platz kann nach Sekunden wieder weg sein.";
                else line = w.Max > 0 && IsFull(w.Players, w.Max) ? $"{nums}, also gerade voll." : nums.Length > 0 ? nums + "." : "Der Server ist wieder in der Liste.";
                if (auto) Notify.Show("Platz-Alarm", title, line, null, null, _accentHex);
                else Notify.Show("Platz-Alarm", title, line, "Jetzt verbinden", () => _ = SwConnect(w.Code, "alarm", w.Name), _accentHex);
            }
            if (!IsActive) Notify.Flash(new WindowInteropHelper(this).Handle);
            if (auto)
            {
                w.Launched = await SwConnect(w.Code, "auto", w.Name);
                Emit("swWatch", new { watches = _sw.Views() });
            }
        }
        catch { }
    }

    /// <summary>Starts FiveM with the launcher's boost settings and connects (the same Launch as the page's buttons).
    /// A running FiveM is never touched.</summary>
    private async Task<bool> SwConnect(string code, string source, string? name)
    {
        if (!ValidCode(code) || _swConnecting) return false;
        _swConnecting = true;
        try
        {
            var shown = name ?? "cfx.re/join/" + code;
            if (!_authed) { Emit("swConnect", new { code = "cfx:" + code, name = shown, source, ok = false, error = "OrbitTweak ist noch nicht freigeschaltet." }); return false; }
            var (fm, _) = await Task.Run(ServerWatcher.FiveMState);
            if (fm)
            {
                Emit("swConnect", new { code = "cfx:" + code, name = shown, source, ok = false, error = "FiveM läuft schon. OrbitTweak fasst ein laufendes FiveM nicht an: beende es und verbinde dann." });
                return false;
            }
            var args = JsonSerializer.SerializeToElement(new { build = S.Launch.Build, pure = S.Launch.Pure, connect = ConnectAddress(code), pre = S.Launch.Pre });
            object r;
            try { r = await Launch(args); }
            catch (Exception ex) { r = new { error = "FiveM konnte nicht starten: " + ex.Message }; }
            var el = JsonSerializer.SerializeToElement(r, J);
            string? err = el.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            string? summary = el.TryGetProperty("summary", out var sm) && sm.ValueKind == JsonValueKind.String ? sm.GetString() : null;
            long freed = el.TryGetProperty("freed", out var fr) && fr.ValueKind == JsonValueKind.Number ? fr.GetInt64() : 0;
            Emit("swConnect", new { code = "cfx:" + code, name = shown, source, ok = err == null, error = err, summary, freed });
            return err == null;
        }
        finally { _swConnecting = false; }
    }

    // =====================================================================
    //  Freunde online
    // =====================================================================
    private (List<string> cfx, int ip) SwFavs()
    {
        var cfx = new List<string>(); int ip = 0;
        foreach (var f in S.Favorites.ToList())
        {
            if (CodeOfKey(f) is { } c) { if (!cfx.Contains(c)) cfx.Add(c); }
            else ip++;
        }
        return (cfx, ip);
    }

    /// <summary>
    /// The friends card: { at, next, busy, error, total, ip, friends, found: [{ friend, player, code, server, ping,
    /// players, max }], servers: [{ code, name, listing, players, max, at, error }] } (times in ms since 1970).
    /// listing: names | anon (only stand-ins like "Player") | hidden (players on, empty list) | empty | offline | error | wait.
    /// </summary>
    private async Task<object> SwFriendsView(bool round)
    {
        var f = S.SrvFriends;
        var (codes, ip) = SwFavs();
        if (round) await SwFriendsRound(codes);
        var (servers, error, last) = _swScan.Snapshot();
        var now = DateTime.UtcNow;
        var found = new List<object>();
        var rows = new List<object>();
        foreach (var code in codes)
        {
            var s = servers.FirstOrDefault(x => x.Code == code);
            if (s == null) { rows.Add(new { code = "cfx:" + code, name = code, listing = "wait", players = 0, max = 0, at = 0L, error = (string?)null }); continue; }
            string listing = s.Offline ? "offline" : s.Listing switch
            {
                Listing.Names => "names", Listing.Anonymous => "anon", Listing.Hidden => "hidden", Listing.Empty => "empty",
                _ => "error",
            };
            rows.Add(new { code = "cfx:" + code, name = s.Name, listing, players = s.Players, max = s.Max, at = Ms(s.At), error = s.Error });
            if (s.Listing != Listing.Names || now - s.At > FriendStale) continue;
            foreach (var (friend, player) in Find(f.Names, s.Names, f.Partial))
                found.Add(new { friend, player, code = "cfx:" + code, server = s.Name, ping = await _swScan.Ping(s.Endpoint), players = s.Players, max = s.Max });
        }
        long at = Ms(last);
        return new
        {
            at, next = last is DateTime l ? Ms(l + FriendEvery) : 0L, busy = _swScan.Busy, error,
            total = codes.Count, ip, friends = f.Names.Count, found, servers = rows,
        };
    }

    private static long Ms(DateTime? t) => t is DateTime d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToUnixTimeMilliseconds() : 0;

    /// <summary>A round over the favourites when one may run (FriendScan keeps it to one per 60 s), then the "came
    /// online" notes, once per round: the page and the background look can join the same round, and a second pass
    /// would count a friend who is missing once as gone twice (and bring a second note on their return).</summary>
    private async Task SwFriendsRound(List<string> codes)
    {
        var f = S.SrvFriends;
        if (f.Names.Count == 0 || codes.Count == 0 || !await _swScan.Round(codes)) return;
        int round = _swScan.Rounds;
        if (!f.Notify || round == _swNoted) return;
        _swNoted = round;
        SwNoteFresh(codes);
    }

    /// <summary>Who came online since the last rounds: page event, and (while OrbitTweak is not the window in use and
    /// nobody plays) a note on the desktop. More than two at once make one note.</summary>
    private void SwNoteFresh(List<string> codes)
    {
        var f = S.SrvFriends;
        var (servers, _, _) = _swScan.Snapshot();
        var now = DateTime.UtcNow;
        var hits = new List<(string friend, string server, string code)>();
        foreach (var s in servers.Where(s => codes.Contains(s.Code) && s.Listing == Listing.Names && now - s.At <= FriendStale))
            foreach (var (friend, _) in Find(f.Names, s.Names, f.Partial)) hits.Add((friend, s.Name, s.Code));
        var fresh = _swScan.Seen.Round(hits.Select(h => Key(h.friend)));
        if (fresh.Count == 0) return;
        var news = hits.Where(h => fresh.Contains(Key(h.friend))).GroupBy(h => Key(h.friend)).Select(g => g.First()).ToList();
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var h in news) Emit("swFriend", new { friend = h.friend, server = h.server, code = "cfx:" + h.code });
            Log(news.Count == 1 ? $"👥 {news[0].friend} ist online auf „{news[0].server}“" : $"👥 {news.Count} Freunde sind online gekommen");
            if (IsActive || GameFocus.IsGame()) return;
            if (news.Count <= 2)
                foreach (var h in news) Notify.Show("Freunde online", $"{h.friend} ist online", "auf " + h.server, "Beitreten", () => _ = SwConnect(h.code, "friend", h.server), _accentHex);
            else
                Notify.Show("Freunde online", $"{news.Count} Freunde sind online", string.Join(", ", news.Take(4).Select(h => h.friend)) + (news.Count > 4 ? " …" : ""), null, null, _accentHex);
            Notify.Flash(new WindowInteropHelper(this).Handle);
        });
    }

    private async Task SwFriendsBackground()
    {
        _swBgBusy = true;
        try
        {
            var (_, game) = await Task.Run(ServerWatcher.FiveMState);
            if (game) return;   // nobody is disturbed while playing; the next look after the game
            await SwFriendsRound(SwFavs().cfx);   // only the lists: no ping to anyone's server for a note
        }
        catch { }
        finally { _swBgBusy = false; _swBgAt = DateTime.UtcNow; }
    }

    // =====================================================================
    //  Server auf den Desktop
    // =====================================================================
    private async Task<object> SwShortcut(JsonElement a)
    {
        var key = Str(a, "code");
        var code = CodeOfKey(key);
        if (code == null || !S.Favorites.Contains(key)) return new { ok = false, error = "Verknüpfungen gibt es für Favoriten mit Join-Code (cfx.re/join/…)." };
        var s = await CfxProbe.Get(code, TimeSpan.FromMinutes(1));
        var name = ServerName(s.Name ?? Str(a, "name"), "FiveM-Server " + code);
        var r = await ServerShortcut.Create(code, name, s.Icon);
        if (!r.Ok) return new { ok = false, error = r.Error };
        Log($"🖥 Desktop-Verknüpfung „{r.File}“ {(r.Replaced ? "erneuert" : "erstellt")}");
        return new { ok = true, path = r.Path, file = r.File, serverIcon = r.ServerIcon, replaced = r.Replaced };
    }
}
