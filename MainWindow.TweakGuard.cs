using System.Text.Json;
using OrbitTweak.Core;
using TG = OrbitTweak.Core.TweakGuardLogic;

namespace OrbitTweak;

/// <summary>
/// Tweak-Wächter (2.31): Windows updates, a new driver, another tool or the user can switch back a tweak OrbitTweak set.
/// About every 15 minutes while no game runs (the first look 20 s after unlocking) the guard reads every tweak with a
/// mark and the FPS limit; one that is off in two looks in a row is shown on the Tweaks page with "Wieder anwenden"
/// (the normal switch path, tweak.set / fps.set) and "So lassen" (forgets the mark). Reading in Core/TweakGuard.cs, rules
/// in Core/TweakGuardLogic.cs, page in Web/tweakguard.js. Timer_Tick calls TwgTick once a second.
/// </summary>
public partial class MainWindow
{
    private readonly TweakGuardLogic.State _twg = new();
    /// <summary>One look or one "Wieder anwenden"/"So lassen" at a time.</summary>
    private readonly SemaphoreSlim _twgGate = new(1, 1);
    /// <summary>When the next look is due (default = not planned yet: the first one comes FirstDelay after unlocking).</summary>
    private DateTime _twgNext;
    private DateTime? _twgLast;
    private string? _twgErr;
    /// <summary>The last planned look found a game running.</summary>
    private bool _twgPaused;
    private int _twgWatched;
    /// <summary>A line per item for the banner (the FPS limit: what the driver profile has now).</summary>
    private readonly Dictionary<string, string> _twgDetail = new(StringComparer.Ordinal);
    /// <summary>Why "Wieder anwenden" did not work for an item, shown until it is applied or no longer pending.</summary>
    private readonly Dictionary<string, string> _twgFail = new(StringComparer.Ordinal);

    /// <summary>Once a second from Timer_Tick: starts a look when one is due. Nothing runs without a key.</summary>
    private void TwgTick()
    {
        if (!_authed) { _twgNext = default; return; }
        var now = DateTime.Now;
        if (_twgNext == default) { _twgNext = now + TG.FirstDelay; return; }
        if (now < _twgNext || _twgGate.CurrentCount == 0) return;
        _ = TwgLook(false);
    }

    private async Task<object?> TwgCmd(string cmd, JsonElement a)
    {
        switch (cmd)
        {
            // -> state (see TwgState). The pending ones are read again first: a switch flipped since, Boost or
            // "Alle zurücksetzen" may have settled them.
            case "twg.state":
                if (_twg.Pending.Count > 0) await TwgRecheck();
                return TwgState();

            // a look by hand ("Jetzt prüfen") -> state; while a game runs nothing is read (status "paused"). Status "busy"
            // when it did not get to look (the guard was held for 20 s): the event after that work brings the result
            case "twg.check":
                return await TwgLook(true) ? TwgState() : TwgState(busy: true);

            // { ids } -> { done: [{ id, name }], failed: [{ id, name, error }], restart, state }: exactly the asked ones
            // that are pending, each through the path of its own switch
            case "twg.apply":
            {
                var ids = TwgIds(a);
                if (!await _twgGate.WaitAsync(TimeSpan.FromSeconds(20))) return new { error = "Der Tweak-Wächter prüft gerade. Versuch es gleich noch einmal." };
                var done = new List<object>(); var failed = new List<object>(); bool restart = false;
                try
                {
                    foreach (var id in TG.Pick(_twg, ids, TwgOrder()))
                    {
                        string? err;
                        try { err = await TwgApply(id); }
                        catch (Exception ex) { err = "Ging nicht: " + ex.Message; }
                        var name = TwgName(id);
                        if (err == null)
                        {
                            _twg.Pending.Remove(id); _twgFail.Remove(id); _twgDetail.Remove(id);
                            done.Add(new { id, name });
                            if (_tweaks.FirstOrDefault(t => t.Id == id)?.NeedsRestart == true) restart = true;
                        }
                        else { _twgFail[id] = err; failed.Add(new { id, name, error = err }); }
                    }
                }
                finally { _twgGate.Release(); }
                var st = TwgState();
                Emit("twg", st);
                return new { done, failed, restart, state = st };
            }

            // { ids } -> { kept: [names], state }: the mark goes (the FPS limit: quiet for the limit kept now), so the
            // guard does not ask again until the tweak is switched on with OrbitTweak once more. Nothing is changed in Windows.
            case "twg.keep":
            {
                var ids = TwgIds(a);
                if (!await _twgGate.WaitAsync(TimeSpan.FromSeconds(20))) return new { error = "Der Tweak-Wächter prüft gerade. Versuch es gleich noch einmal." };
                var kept = new List<string>();
                try
                {
                    var pick = TG.Pick(_twg, ids, TwgOrder());
                    await Task.Run(() => { foreach (var id in pick) { if (id == TG.FpsId) TweakGuard.QuietFps(); else Marks.Clear(id); } });
                    foreach (var id in pick)
                    {
                        kept.Add(TwgName(id));
                        _twg.Pending.Remove(id); _twgFail.Remove(id); _twgDetail.Remove(id);
                    }
                    _twgWatched = Math.Max(0, _twgWatched - pick.Count);   // their marks are gone: no longer watched
                }
                catch (Exception ex) { return new { error = "Konnte das nicht speichern: " + ex.Message }; }
                finally { _twgGate.Release(); }
                if (kept.Count > 0) Log(TG.KeptLine(kept));
                var st = TwgState();
                Emit("twg", st);
                return new { kept, state = st };
            }
        }
        throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
    }

    /// <summary>One full look. byHand waits for a look that is running; a planned one skips when one runs. false = it did
    /// not get to look (another look, "Wieder anwenden" or "So lassen" held the guard).</summary>
    private async Task<bool> TwgLook(bool byHand)
    {
        if (!await _twgGate.WaitAsync(byHand ? TimeSpan.FromSeconds(20) : TimeSpan.Zero)) return false;
        TG.StepResult? res = null;
        try
        {
            var now = DateTime.Now;
            try
            {
                // first whether a game runs (process names only): then nothing is read, and the page is not told "busy"
                // every two minutes of a game
                if (await Task.Run(TweakGuard.GameRunning))
                {
                    _twgPaused = true;
                    _twgNext = DateTime.Now + TG.GameRetry;
                }
                else
                {
                    Emit("twg", TwgState(busy: true));
                    var r = await Task.Run(() =>
                    {
                        // a new FiveM build since FiveM last ran with OrbitTweak open: its exes get the per-app graphics fields
                        // that are switched on (what the next start of FiveM does anyway), so a new build never reads as "reset"
                        int added = 0;
                        if (Marks.Has("gpu") || Marks.Has("winopt")) try { added = Catalog.PrefUpkeep(); } catch { }
                        var looks = TweakGuard.Look(_tweaks);
                        string? fps = null;
                        if (TweakGuard.LookFps() is { } f) { looks.Add(f.look); fps = f.detail; }
                        return (looks, fps, added);
                    });
                    if (r.added > 0) Log($"🖥 Grafikeinstellung für {r.added} neue FiveM-Datei(en) übernommen");
                    _twgPaused = false; _twgErr = null; _twgLast = now;
                    res = TG.Step(_twg, r.looks, now);
                    _twgWatched = r.looks.Count(o => o.Marked && o.Applies);
                    if (r.fps != null) _twgDetail[TG.FpsId] = r.fps;
                    foreach (var id in _twgDetail.Keys.Where(k => !_twg.Pending.ContainsKey(k) && !_twg.Suspect.ContainsKey(k)).ToList()) _twgDetail.Remove(id);
                    foreach (var id in res.Cleared) _twgFail.Remove(id);
                    _twgNext = TG.Next(_twg, DateTime.Now);
                }
            }
            catch (Exception ex)
            {
                _twgErr = "Die letzte Prüfung ging nicht: " + ex.Message;
                _twgNext = DateTime.Now + TG.Interval;
            }
        }
        finally { _twgGate.Release(); }
        if (res is { Confirmed.Count: > 0 }) Log(TG.Announce(res.Confirmed.Select(TwgName).ToList()));
        Emit("twg", TwgState());
        return true;
    }

    /// <summary>The pending ones read again (quick: only those). Skipped while a look runs.</summary>
    private async Task TwgRecheck()
    {
        if (!await _twgGate.WaitAsync(TimeSpan.Zero)) return;
        try
        {
            var ids = _twg.Pending.Keys.ToHashSet(StringComparer.Ordinal);
            var looks = await Task.Run(() =>
            {
                var l = TweakGuard.Look(_tweaks, t => ids.Contains(t.Id));
                if (ids.Contains(TG.FpsId)) l.Add(TweakGuard.LookFps() is { } f ? f.look : new TG.Obs(TG.FpsId, false, false, TG.Read.Unknown));
                return l;
            });
            foreach (var id in TG.Recheck(_twg, looks)) { _twgFail.Remove(id); _twgDetail.Remove(id); }
        }
        catch { }
        finally { _twgGate.Release(); }
    }

    /// <summary>"Wieder anwenden" for one item, through the same command its switch uses. null = it is on again.</summary>
    private async Task<string?> TwgApply(string id)
    {
        if (id == TG.FpsId)
        {
            int kept = FpsLimit.Stored;
            if (kept <= 0) return "OrbitTweak hält gerade kein FPS-Limit";
            var f = JsonSerializer.SerializeToElement(await Handle("fps.set", JsonSerializer.SerializeToElement(new { limit = kept })), J);
            return f.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True ? null
                : f.TryGetProperty("error", out var fe) && fe.ValueKind == JsonValueKind.String ? fe.GetString() : "Das FPS-Limit ließ sich nicht setzen";
        }
        var r = JsonSerializer.SerializeToElement(await Handle("tweak.set", JsonSerializer.SerializeToElement(new { id, on = true })), J);
        if (r.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) return e.GetString();
        return r.TryGetProperty("applied", out var ap) && ap.ValueKind == JsonValueKind.True ? null : "Windows hat die Einstellung nicht übernommen";
    }

    /// <summary>
    /// For the page: status (wait = no look yet, idle, busy, paused = a game runs, error), when it looked last and looks
    /// next, whether a second look for a first "off" is planned (confirmAt), how many things it watches, and the pending
    /// items with name, icon, whether they need a restart, a detail line, since when and why re-applying failed.
    /// </summary>
    private object TwgState(bool busy = false) => new
    {
        status = busy ? "busy" : _twgErr != null ? "error" : _twgPaused ? "paused" : _twgLast == null ? "wait" : "idle",
        lastAt = _twgLast,
        nextAt = _authed && _twgNext != default ? _twgNext : (DateTime?)null,
        confirmAt = _twg.Suspect.Count > 0 ? TG.Next(_twg, DateTime.Now) : (DateTime?)null,
        error = _twgErr,
        watched = _twgWatched,
        items = TG.Pick(_twg, _twg.Pending.Keys, TwgOrder()).Select(id =>
        {
            var t = _tweaks.FirstOrDefault(x => x.Id == id);
            return new
            {
                id, name = TwgName(id), icon = t?.Icon ?? "gauge", cat = t?.Cat ?? "fivem",
                restart = t?.NeedsRestart ?? false,
                detail = _twgDetail.GetValueOrDefault(id),
                since = _twg.Pending[id],
                error = _twgFail.GetValueOrDefault(id),
            };
        }).ToList(),
    };

    private string TwgName(string id) => id == TG.FpsId ? "FPS-Limit für FiveM" : _tweaks.FirstOrDefault(t => t.Id == id)?.Name ?? id;
    private List<string> TwgOrder() => _tweaks.Select(t => t.Id).Append(TG.FpsId).ToList();

    /// <summary>The "ids" array of a command (strings only, at most 64).</summary>
    private static List<string> TwgIds(JsonElement a)
    {
        var l = new List<string>();
        if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("ids", out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 and <= 40 } s && l.Count < 64) l.Add(s);
        return l;
    }
}
