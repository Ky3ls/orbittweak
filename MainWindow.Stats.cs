using System.IO;
using System.Text.Json;
using System.Windows;
using OrbitTweak.Core;

namespace OrbitTweak;

/// <summary>
/// Play time statistics ("Spielzeit", 2.27): one round per game process and server, kept in playtime.json (Core/PlayStats.cs).
/// Everything here runs on the UI thread; only the server look-up and the file writes go to the thread pool.
/// </summary>
public partial class MainWindow
{
    private PlayRecorder? _play;
    private DateTime _playSaved, _playSrvNext;   // last write of the open round / next server look-up
    private bool _playSrvBusy, _playHooked;
    private readonly object _playIo = new(); private long _playSeq;

    private static string PlayFile => Path.Combine(AppDirs.Roaming, "playtime.json");

    /// <summary>Read from the file on first use (a few ms even for a long history).</summary>
    private PlayRecorder Play => _play ??= MakePlay();

    private PlayRecorder MakePlay()
    {
        var p = new PlayRecorder(PlayLog.Load(PlayFile));
        p.Ended = r =>
        {
            if (r.Secs >= PlayStats.NoteSecs)
                Emit("round", new { secs = Math.Round(r.Secs), server = r.Server ?? PlayStats.Addr(r.Key), kills = r.Kills, clips = r.Clips });
        };
        return p;
    }

    private Task<object?> StatsCmd(string cmd, JsonElement a)
    {
        object? res;
        switch (cmd)
        {
            case "stats.get":
            {
                var now = DateTimeOffset.Now;
                if (Play.Open is { } o && now > o.End) o.End = now;   // the game was seen at most 2 s ago
                res = PlayStats.Summarize(Play.Log.Rounds, Play.Open, now);
                break;
            }
            case "stats.clear":
            {
                int n = Play.Clear();
                PlaySave();
                if (n > 0) Log($"📊 Spielzeit-Verlauf gelöscht ({n} {(n == 1 ? "Runde" : "Runden")})");
                res = new { ok = true, removed = n };
                break;
            }
            case "stats.remove":
            {
                long id = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : 0;
                bool ok = id != 0 && Play.Remove(id);
                if (ok) PlaySave();
                res = new { ok };
                break;
            }
            default: throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
        }
        return Task.FromResult<object?>(res);
    }

    /// <summary>Called after every FiveM probe (every 2 s, on the UI thread). gamePid 0 = no game running.</summary>
    private void StatsTick(int gamePid, DateTime? gameStart)
    {
        try
        {
            if (!_playHooked)
            {
                _playHooked = true;
                // closing OrbitTweak or Windows: the open round is written with this moment as its last-seen time
                Closing += (_, _) => PlayClose();
                if (Application.Current != null) Application.Current.SessionEnding += (_, _) => PlayClose();
            }
            var p = Play;
            bool had = p.Open != null;
            var now = DateTimeOffset.Now;
            bool save = p.Tick(gamePid, gameStart is DateTime gs ? new DateTimeOffset(gs) : null, had ? null : PlayBuild(), now, _roundPid, _roundKills, _playPid);
            if (p.Open == null) { if (save) PlaySave(); return; }
            if (!had) _playSrvNext = DateTime.Now.AddSeconds(8);   // a new round: look for its server soon (FiveM notes it on connect)
            if (!_playSrvBusy && DateTime.Now >= _playSrvNext) _ = PlayServer(p.Open);
            // while a round runs it is written about once a minute, so a crash or power cut costs at most that much
            if (save || DateTime.Now - _playSaved >= TimeSpan.FromSeconds(60)) PlaySave();
        }
        catch { }   // play time must never disturb the look at the game
    }

    /// <summary>A clip was saved: it counts for the round being played.</summary>
    private void StatsClip() { try { _play?.Clip(); } catch { } }

    /// <summary>The FiveM build of the running game, from the last probe ("b3095"), if known.</summary>
    private string? PlayBuild()
    {
        try { return JsonSerializer.SerializeToElement(_fm, J).TryGetProperty("build", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null; }
        catch { return null; }
    }

    /// <summary>
    /// The server of the round: FiveM's last server (crashometry), only if it was written after the game started, named at
    /// Cfx.re (same rule as "server.current"). First a few seconds after the start, then every few minutes; a different
    /// server splits the round. Never two at once, the file and the network off the UI thread.
    /// </summary>
    private async Task PlayServer(PlayRound round)
    {
        _playSrvBusy = true;
        try
        {
            int pid = round.Pid;
            var gs = round.GameStart ?? round.Start;   // the game process's start (unknown: the round's)
            var (url, at) = await Task.Run(Servers.LastServer);
            string? key = null;
            if (!string.IsNullOrWhiteSpace(url) && at >= gs.LocalDateTime.AddSeconds(-5)) try { key = Servers.Parse(url); } catch { }
            if (key == null) return;   // not on a server yet
            string? name = null;
            if (key != round.Key || round.Server == null)
            {
                var el = JsonSerializer.SerializeToElement(await Servers.Fetch(key));
                if (el.TryGetProperty("online", out var on) && on.ValueKind == JsonValueKind.True
                    && el.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = CleanServerName(n.GetString() ?? "");
            }
            if (_play?.Server(pid, key, name, new DateTimeOffset(at), DateTimeOffset.Now) == true) PlaySave();
        }
        catch { }
        finally
        {
            _playSrvBusy = false;
            // the server known: a look every three minutes (did it change?); not yet: again soon
            _playSrvNext = DateTime.Now.AddSeconds(_play?.Open?.Key != null ? 180 : 20);
        }
    }

    /// <summary>OrbitTweak or Windows is closing: written at once, the round stays open for the next start.</summary>
    private void PlayClose()
    {
        try
        {
            if (_play?.Open == null) return;
            _play.Close(DateTimeOffset.Now);
            PlaySave(true);
        }
        catch { }
    }

    /// <summary>
    /// Writes playtime.json. The rounds are turned into bytes here (UI thread, so nothing changes meanwhile) and written on
    /// the thread pool; writes happen one after the other and an older one that was overtaken is skipped.
    /// </summary>
    private void PlaySave(bool now = false)
    {
        if (_play == null) return;
        _playSaved = DateTime.Now;
        byte[] data;
        try { data = _play.Log.Serialize(); } catch { return; }
        long seq = Interlocked.Increment(ref _playSeq);
        var file = _play.Log.File ?? PlayFile;
        void Write()
        {
            lock (_playIo)
            {
                if (seq != Interlocked.Read(ref _playSeq)) return;
                try { PlayLog.Write(file, data); } catch { }
            }
        }
        if (now) Write(); else _ = Task.Run(Write);
    }
}
