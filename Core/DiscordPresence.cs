using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrbitTweak.Core;

/// <summary>What the Discord profile shows. Null or too short fields are left out.</summary>
internal sealed record Presence
{
    public string? Details { get; init; }        // first line, e.g. the server name
    public string? State { get; init; }          // second line, e.g. "Im Spiel · 12 Kills"
    public DateTime? Since { get; init; }        // Discord counts up from here ("00:42 vergangen")
    public string? LargeImage { get; init; }     // asset key from the Developer Portal, or an https:// image URL
    public string? LargeText { get; init; }
    public string? SmallImage { get; init; }
    public string? SmallText { get; init; }
    public (string Label, string Url)[] Buttons { get; init; } = Array.Empty<(string, string)>();
}

/// <summary>
/// Discord Rich Presence ("Spielt OrbitTweak") over Discord's local IPC pipe (\\.\pipe\discord-ipc-0 … 9), without any package.
/// <see cref="Set"/> never blocks and may be called as often as wanted: it only notes what should be shown.
/// One background loop connects when Discord is there, sends only when something changed and never more often than
/// every 15 s (the newest wish wins), sends again after Discord was restarted, and simply waits while Discord is
/// not running. With nothing to show (switched off) it closes the connection; closing the app takes the card off.
/// </summary>
internal sealed class DiscordPresence : IDisposable
{
    /// <summary>Application ID of the "OrbitTweak" app in the Discord Developer Portal (not a secret).
    /// Empty: nothing is shown until the user pastes an ID in Einstellungen › Verhalten.</summary>
    public const string DefaultAppId = "";

    private const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;
    private const long GapMs = 15_000;           // at most one SET_ACTIVITY per 15 s
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(20);      // Discord not there: look again after this
    private static readonly TimeSpan RefusedWait = TimeSpan.FromMinutes(5); // Discord closed the handshake (bad app ID …)

    // "·", "ü" and emoji stay readable in the frames (escaped would be valid JSON too, just longer)
    private static readonly JsonSerializerOptions Raw = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly int _pid = Environment.ProcessId;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly object _lock = new();

    private string _appId = "";                  // the app the wish is for
    private string? _want;                       // activity JSON that should be shown (null: nothing)
    private string? _sent;                       // what this connection told Discord last
    private bool _synced;                        // false right after (re)connecting: send in any case
    private long _lastSend = -GapMs;
    private volatile NamedPipeClientStream? _pipe;
    private string _pipeId = "";                 // the app the open connection belongs to
    private DateTime _notBefore;                 // no connection attempt before this
    private volatile bool _refused;

    public bool Connected => _pipe != null;
    /// <summary>Discord turned the app ID down (unknown application): waits 5 min or until another ID is set.</summary>
    public bool Refused => _refused;
    /// <summary>Discord name of the signed-in user (from READY), for "Verbunden als …" in the settings.</summary>
    public string? User { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>Raised on the thread pool (connected, lost, Discord error answers).</summary>
    public event Action<string>? Log;

    public DiscordPresence() => _ = Task.Run(() => Run(_cts.Token));

    /// <summary>What should be shown now with which Discord app (null or no ID: nothing). Cheap; unchanged values do nothing.</summary>
    public void Set(string appId, Presence? p)
    {
        appId = appId.Trim();
        var json = p == null || appId.Length == 0 ? null : Build(p).ToJsonString(Raw);
        lock (_lock)
        {
            if (json == _want && appId == _appId) return;
            if (appId != _appId) { _appId = appId; _notBefore = default; _refused = false; LastError = null; }
            _want = json;
        }
        Wake();
    }

    private void Wake() { try { if (_wake.CurrentCount == 0) _wake.Release(); } catch (SemaphoreFullException) { } }

    /// <summary>Waits for <see cref="Set"/>, a lost connection or the time; false once the app is closing.</summary>
    private async Task<bool> Sleep(TimeSpan t, CancellationToken ct)
    {
        try { await _wake.WaitAsync(t, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private async Task Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? want, sent; NamedPipeClientStream? pipe; bool synced; string id, pipeId; DateTime notBefore;
            lock (_lock) { want = _want; pipe = _pipe; synced = _synced; sent = _sent; id = _appId; pipeId = _pipeId; notBefore = _notBefore; }
            try
            {
                if (pipe == null)
                {
                    // nothing to show: no need to talk to Discord at all
                    if (want == null) { if (!await Sleep(Timeout.InfiniteTimeSpan, ct)) break; continue; }
                    var wait = notBefore - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero) { if (!await Sleep(wait, ct)) break; continue; }
                    if (!await Connect(id, ct))
                        lock (_lock) { if (_appId == id) _notBefore = DateTime.UtcNow + (_refused ? RefusedWait : Retry); }
                    continue;   // connected or not: go round once more with the fresh state
                }
                // switched off, or another app ID: this connection has nothing more to say
                if (pipeId != id || (want == null && (!synced || sent == null))) { Drop(pipe, null); continue; }
                if (synced && want == sent) { if (!await Sleep(Timeout.InfiniteTimeSpan, ct)) break; continue; }
                // a change: send it once the 15 s since the last one are over (taking the card off goes at once)
                long left = GapMs - (Environment.TickCount64 - _lastSend);
                if (want != null && left > 0) { if (!await Sleep(TimeSpan.FromMilliseconds(left), ct)) break; continue; }
                await SendActivity(pipe, want, ct);
                lock (_lock) { if (_pipe == pipe) { _sent = want; _synced = true; } }
                _lastSend = Environment.TickCount64;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                if (pipe != null) Drop(pipe, "Discord-Verbindung verloren: " + ex.Message);
                if (!await Sleep(Retry, ct)) break;
            }
        }
    }

    private async Task<bool> Connect(string id, CancellationToken ct)
    {
        for (int i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try { pipe.Connect(0); }            // 0: fails at once when this pipe does not exist (no waiting, no spinning)
            catch { pipe.Dispose(); continue; }
            try
            {
                await WriteFrame(pipe, OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = id }.ToJsonString(Raw), ct);
                using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
                to.CancelAfter(5000);
                var (op, json) = await ReadFrame(pipe, to.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (op == OpClose)
                {
                    // e.g. {"code":4000,"message":"Invalid Client ID"}: trying again every 20 s would not change that
                    LastError = Str(root, "message") ?? json;
                    lock (_lock) { if (_appId == id) _refused = true; }   // not when another ID was pasted meanwhile
                    Log?.Invoke("Discord lehnt die App-ID ab: " + LastError);
                    pipe.Dispose();
                    return false;
                }
                if (op != OpFrame || Str(root, "evt") != "READY") { pipe.Dispose(); continue; }
                string? user = null;
                if (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("user", out var u))
                    user = Str(u, "global_name") ?? Str(u, "username");
                lock (_lock) { _pipe = pipe; _pipeId = id; _synced = false; _sent = null; }
                User = user; LastError = null; _refused = false;
                _ = Task.Run(() => ReadLoop(pipe, ct));
                Log?.Invoke(user != null ? $"Discord verbunden ({user})" : "Discord verbunden");
                return true;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                LastError = ex.Message;
                pipe.Dispose();
            }
        }
        return false;
    }

    /// <summary>Answers pings, notices when Discord goes away and passes on error answers.</summary>
    private async Task ReadLoop(NamedPipeClientStream pipe, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (op, json) = await ReadFrame(pipe, ct);
                if (op == OpPing) { await WriteFrame(pipe, OpPong, json, ct); continue; }
                if (op == OpClose) { Drop(pipe, "Discord hat die Verbindung beendet: " + Msg(json)); return; }
                if (op != OpFrame) continue;
                using var doc = JsonDocument.Parse(json);
                if (Str(doc.RootElement, "evt") == "ERROR")
                {
                    // e.g. a text Discord does not accept: the card stays as it was, the next change is sent normally
                    LastError = doc.RootElement.TryGetProperty("data", out var d) ? Str(d, "message") : json;
                    Log?.Invoke("Discord: " + LastError);
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Drop(pipe, ex is EndOfStreamException ? "Discord wurde geschlossen" : "Discord-Verbindung verloren: " + ex.Message);
        }
        catch { }
    }

    /// <summary>Closes the connection (why == null: on purpose, nothing logged). The loop connects again when there is something to show.</summary>
    private void Drop(NamedPipeClientStream pipe, string? why)
    {
        lock (_lock)
        {
            if (_pipe != pipe) return;
            _pipe = null; _synced = false; _sent = null;
        }
        try { pipe.Dispose(); } catch { }
        User = null;
        if (why != null) Log?.Invoke(why);
        Wake();
    }

    private Task SendActivity(NamedPipeClientStream pipe, string? activity, CancellationToken ct)
    {
        var args = new JsonObject { ["pid"] = _pid };
        if (activity != null) args["activity"] = JsonNode.Parse(activity);   // without "activity" Discord takes the card off
        var cmd = new JsonObject { ["cmd"] = "SET_ACTIVITY", ["args"] = args, ["nonce"] = Guid.NewGuid().ToString() };
        return WriteFrame(pipe, OpFrame, cmd.ToJsonString(Raw), ct);
    }

    private async Task WriteFrame(Stream s, int op, string json, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(json);
        // header and body in ONE write: Discord's docs warn that writing them separately can break the pipe
        var buf = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buf, op);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4), body.Length);
        body.CopyTo(buf, 8);
        await _write.WaitAsync(ct);
        try { await s.WriteAsync(buf, ct); await s.FlushAsync(ct); }
        finally { _write.Release(); }
    }

    private static async Task<(int op, string json)> ReadFrame(Stream s, CancellationToken ct)
    {
        var head = new byte[8];
        await s.ReadExactlyAsync(head, ct);          // EndOfStreamException once Discord closes the pipe
        int op = BinaryPrimitives.ReadInt32LittleEndian(head);
        int len = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4));
        if (len is < 0 or > 1 << 20) throw new InvalidDataException("ungültiger Discord-Frame");
        var body = new byte[len];
        await s.ReadExactlyAsync(body, ct);
        return (op, Encoding.UTF8.GetString(body));
    }

    // ---------------- activity JSON ----------------

    internal static JsonObject Build(Presence p)
    {
        var a = new JsonObject { ["type"] = 0 };    // 0 = "Spielt <Name der Discord-Anwendung>"
        if (Fit(p.Details) is { } det) a["details"] = det;
        if (Fit(p.State) is { } st) a["state"] = st;
        // seconds, as in the example of Discord's RPC docs (the official discord-rpc library sends seconds as well)
        if (p.Since is DateTime since) a["timestamps"] = new JsonObject { ["start"] = new DateTimeOffset(since).ToUnixTimeSeconds() };
        var assets = new JsonObject();
        void Put(string k, string? v, int max) { if (Fit(v, max) is { } x) assets[k] = x; }
        Put("large_image", p.LargeImage, 256); Put("large_text", p.LargeText, 128);
        Put("small_image", p.SmallImage, 256); Put("small_text", p.SmallText, 128);
        if (assets.Count > 0) a["assets"] = assets;
        var buttons = new JsonArray();
        foreach (var (label, url) in p.Buttons.Take(2))
            if (Fit(label, 32) is { } l && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && url.Length <= 512)
                buttons.Add(new JsonObject { ["label"] = l, ["url"] = url });
        if (buttons.Count > 0) a["buttons"] = buttons;
        return a;
    }

    /// <summary>Discord refuses texts shorter than 2 characters (left out here) and longer than the limit (cut with "…").</summary>
    internal static string? Fit(string? s, int max = 128)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s) || s.Length < 2) return null;
        if (s.Length <= max) return s;
        int n = max - 1;
        if (char.IsHighSurrogate(s[n - 1])) n--;      // never cut an emoji in half
        return s[..n].TrimEnd() + "…";
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Msg(string json)
    {
        try { using var d = JsonDocument.Parse(json); return Str(d.RootElement, "message") ?? json; } catch { return json; }
    }

    /// <summary>Takes the card off at once (at most half a second) and closes the pipe. Safe to call on the UI thread.</summary>
    public void Dispose()
    {
        NamedPipeClientStream? pipe;
        lock (_lock) { pipe = _pipe; _pipe = null; _want = null; }
        if (pipe != null)
        {
            // on the thread pool so that waiting here can never dead-lock the UI thread; closing the pipe (and the
            // app ending) takes the card off as well, this only makes it immediate
            try { Task.Run(() => SendActivity(pipe, null, CancellationToken.None)).Wait(500); } catch { }
        }
        _cts.Cancel();
        try { pipe?.Dispose(); } catch { }
    }
}
