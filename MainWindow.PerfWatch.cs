using System.IO;
using System.Text.Json;
using System.Windows;
using OrbitTweak.Core;

namespace OrbitTweak;

/// <summary>
/// Engpass-Analyse (2.31): while FiveM runs, a look every 2 s at what holds the frame rate (Core/PerfWatch.cs reads the
/// counters, Core/PerfWatchLogic.cs judges them), one summary per Spielzeit round in perf.json. Like the play time this
/// runs on the UI thread; the counters are read and the file is written on the thread pool. Without a game nothing runs.
/// </summary>
public partial class MainWindow
{
    private PerfRecorder? _pw;
    private PerfWatch? _pwWatch;               // the counter query, only while a game runs
    private bool _pwBusy, _pwHooked;
    private string? _pwErr;                    // why Windows gave no values on the last look (German), null when fine
    private DateTime _pwSaved;
    private readonly object _pwIo = new(); private long _pwSeq;

    private static string PerfFile => Path.Combine(AppDirs.Roaming, "perf.json");

    /// <summary>Read from the file on first use (at most 50 small entries).</summary>
    private PerfRecorder Pw => _pw ??= new PerfRecorder(PerfLog.Load(PerfFile));

    private Task<object?> PerfCmd(string cmd, JsonElement a)
    {
        object? res;
        switch (cmd)
        {
            case "perf.get": res = PerfGet(); break;
            case "perf.set":
            {
                if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("on", out var v) || v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidOperationException("Ungültige Einstellung für die Engpass-Analyse.");
                // the file could not be read at the start: writing the switch would cost the rounds in it
                if (Pw.Log.Unreadable) throw new InvalidOperationException("Deine Messwerte sind gerade gesperrt. Starte OrbitTweak neu und versuch es dann noch einmal.");
                bool on = v.GetBoolean();
                if (Pw.Log.Off == !on) { res = PerfGet(); break; }   // already so
                Pw.Log.Off = !on;
                if (!on) PwStop(false);   // what was measured of this round is kept
                PerfSave();
                Log(on ? "📈 Engpass-Analyse an: OrbitTweak misst mit, solange FiveM läuft" : "📈 Engpass-Analyse aus: es wird nichts mehr gemessen");
                res = PerfGet();
                break;
            }
            default: throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
        }
        return Task.FromResult<object?>(res);
    }

    /// <summary>The switch, the round being measured and the kept rounds with their verdicts (newest first).</summary>
    private object PerfGet()
    {
        var p = Pw;
        // the Spielzeit rounds name the server; a round removed there (or the whole history) goes here as well
        var play = Play;
        var names = new Dictionary<long, string?>();
        foreach (var r in play.Log.Rounds) names[r.Id] = r.Server ?? PlayStats.Addr(r.Key);
        if (!p.Log.Unreadable && !play.Log.Unreadable && p.Log.Prune(names.Keys, p.Open?.Id ?? p.RoundId) > 0) PerfSave();
        var rounds = p.Log.Rounds.OrderByDescending(r => r.Start).Select(r => PerfWatchLogic.Item(r, names.GetValueOrDefault(r.Id))).ToList();
        return new { on = !p.Log.Off, error = _pwWatch?.Error ?? _pwErr, unreadable = p.Log.Unreadable, live = p.Log.Off ? null : PerfWatchLogic.Live(p), rounds };
    }

    /// <summary>Called after every FiveM probe (every 2 s, on the UI thread). gamePid 0 = no game running.</summary>
    private void PerfTick(int gamePid)
    {
        try
        {
            if (!_pwHooked)
            {
                _pwHooked = true;
                // closing OrbitTweak or Windows: the round being measured is written along (the rest joins after the next start)
                Closing += (_, _) => PerfClose();
                if (Application.Current != null) Application.Current.SessionEnding += (_, _) => PerfClose();
            }
            if (gamePid == 0 || !_authed) { if (_pw != null) PwStop(true); return; }
            var p = Pw;
            if (p.Log.Off) { PwStop(true); return; }
            if (_pwBusy) return;   // the last look is still on its way: this one is left out
            long round = _play?.Open is { } o && o.Pid == gamePid ? o.Id : 0;
            bool front = GameFocus.Pid == (uint)gamePid;   // looked up every 400 ms (LiveTick)
            _ = PerfLook(gamePid, round, front);
        }
        catch { }   // measuring must never disturb the look at the game
    }

    /// <summary>One look off the UI thread; the result is judged back on it (one at a time, see _pwBusy).</summary>
    private async Task PerfLook(int pid, long round, bool front)
    {
        _pwBusy = true;
        try
        {
            var w = _pwWatch;
            if (w == null || w.Pid != pid)
            {
                if (w != null) { var old = w; _ = Task.Run(old.Dispose); }
                w = _pwWatch = new PerfWatch(pid);
            }
            var s = await Task.Run(() => w.Sample(front));
            if (_pwWatch != w || _pw == null) return;   // the game stopped meanwhile, or measuring was switched off
            _pwErr = w.Error;
            var p = _pw;
            var done = p.Look(round, DateTimeOffset.Now, s, front);
            // a finished round at once; the one being measured every two minutes, so a crash costs at most that much
            if (done != null || (p.Open != null && DateTime.Now - _pwSaved >= TimeSpan.FromMinutes(2))) PerfSave();
            Emit("perf", new { live = PerfWatchLogic.Live(p), done = done != null, error = w.Error });
        }
        catch { }
        finally { _pwBusy = false; }
    }

    /// <summary>The game is gone (or measuring is off): the round is finished, the query closed. emit: tell the page.</summary>
    private void PwStop(bool emit)
    {
        var w = _pwWatch; _pwWatch = null;
        if (w != null) _ = Task.Run(w.Dispose);   // waits for a look that is still on its way
        if (_pw == null || (_pw.RoundId == 0 && _pw.Open == null)) return;
        var done = _pw.Stop();
        if (done != null) PerfSave();
        if (emit) Emit("perf", new { live = (object?)null, done = true });
    }

    /// <summary>OrbitTweak or Windows is closing: written at once.</summary>
    private void PerfClose()
    {
        try
        {
            var w = _pwWatch; _pwWatch = null;
            w?.Dispose();
            if (_pw?.Open != null) PerfSave(true);
        }
        catch { }
    }

    /// <summary>
    /// Writes perf.json: turned into bytes here (UI thread, so nothing changes meanwhile), written on the thread pool one
    /// after the other; an older write that was overtaken is skipped.
    /// </summary>
    private void PerfSave(bool now = false)
    {
        if (_pw == null || _pw.Log.Unreadable) return;   // never over measurements that could not be read
        _pwSaved = DateTime.Now;
        byte[] data;
        try { data = _pw.Log.Serialize(_pw.Open); } catch { return; }
        long seq = Interlocked.Increment(ref _pwSeq);
        var file = _pw.Log.File ?? PerfFile;
        void Write()
        {
            lock (_pwIo)
            {
                if (seq != Interlocked.Read(ref _pwSeq)) return;
                try { PerfLog.Write(file, data); } catch { }
            }
        }
        if (now) Write(); else _ = Task.Run(Write);
    }
}
