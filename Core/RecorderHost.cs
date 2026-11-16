using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OrbitTweak.Core;

// =====================================================================
//  The recorder runs in its own helper process (the same exe started with "--recorder").
//  Screen capture and video encoding are deep Windows / graphics driver machinery; should anything in
//  there ever crash, only the helper goes down and is started again, the app itself keeps running.
//  Both sides talk over the helper's standard input/output, one JSON object per line.
// =====================================================================

/// <summary>Helper process side.</summary>
internal static class RecorderHost
{
    public static int RunChild(string[] args)
    {
        // WinRT media objects must not live on a single-threaded apartment that never pumps messages
        int code = 0;
        var t = new Thread(() => code = Run(args)) { Name = "OrbitRecorderHost" };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start(); t.Join();
        return code;
    }

    private static int Run(string[] args)
    {
        int parent = args.Length > 1 && int.TryParse(args[1], out var pid) ? pid : 0;
        var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var gate = new object();
        void Send(object o) { try { lock (gate) output.WriteLine(JsonSerializer.Serialize(o)); } catch { } }

        var rec = new ReplayRecorder();
        rec.Log += text => Send(new { ev = "log", text });
        if (parent != 0)
            _ = Task.Run(() => { try { Process.GetProcessById(parent).WaitForExit(); } catch { } Environment.Exit(0); }); // never outlive the app
        using var status = new Timer(_ => Send(new { ev = "status", running = rec.Running, buffered = rec.Buffered, w = rec.Size.w, h = rec.Size.h, error = rec.Error }), null, 500, 1000);

        string? line;
        var saves = new ConcurrentDictionary<int, Task>();   // clips that are being written
        while ((line = input.ReadLine()) != null)
        {
            JsonElement m;
            try { m = JsonDocument.Parse(line).RootElement.Clone(); } catch { continue; }
            int id = m.TryGetProperty("id", out var idv) ? idv.GetInt32() : 0;
            var cmd = m.TryGetProperty("cmd", out var cv) ? cv.GetString() : null;
            var job = Task.Run(async () =>
            {
                try
                {
                    switch (cmd)
                    {
                        case "start":
                        {
                            var o = new ReplayRecorder.Options(m.GetProperty("fps").GetInt32(), m.GetProperty("maxHeight").GetInt32(), m.GetProperty("mbps").GetInt32(), m.GetProperty("audio").GetBoolean());
                            bool ok = await rec.Start(new IntPtr(m.GetProperty("hwnd").GetInt64()), o, m.GetProperty("keep").GetDouble());
                            Send(new { id, ok, error = rec.Error, w = rec.Size.w, h = rec.Size.h });
                            break;
                        }
                        case "save":
                        {
                            var file = await rec.Save(m.GetProperty("before").GetDouble(), m.GetProperty("after").GetDouble(), m.GetProperty("folder").GetString()!, m.GetProperty("name").GetString()!);
                            Send(new { id, ok = file != null, file, error = file == null ? rec.Error : null });
                            break;
                        }
                        case "stop":
                            await rec.Stop();
                            Send(new { id, ok = true });
                            break;
                        default: Send(new { id, ok = false, error = "unknown command" }); break;
                    }
                }
                catch (Exception ex) { Send(new { id, ok = false, error = ex.Message }); }
            });
            if (cmd == "save") { saves[id] = job; _ = job.ContinueWith(t => saves.TryRemove(id, out Task? gone)); }
        }
        // the app closed our input (recording is over): a clip that is still being written is finished first
        try { Task.WaitAll(saves.Values.ToArray(), 30000); } catch { }
        try { rec.Stop().Wait(5000); } catch { }
        return 0;
    }
}

/// <summary>App side: starts the helper, sends it commands and mirrors its state.</summary>
internal sealed class RecorderProxy
{
    public event Action<string>? Log;
    public bool Running { get; private set; }
    public string? Error { get; private set; }
    public double Buffered { get; private set; }
    public (int w, int h) Size { get; private set; }

    private Process? _p;
    private StreamWriter? _in;
    private int _id;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _wait = new();
    private readonly object _gate = new();

    private bool Alive => _p is { HasExited: false };

    private bool Ensure()
    {
        if (Alive) return true;
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad unbekannt");
            var psi = new ProcessStartInfo(exe, $"--recorder {Environment.ProcessId}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false)
            };
            var p = Process.Start(psi) ?? throw new InvalidOperationException("Hilfsprozess startet nicht");
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => Gone(p);
            _p = p; _in = p.StandardInput; _in.AutoFlush = true;
            _ = Task.Run(async () =>
            {
                try { string? line; while ((line = await p.StandardOutput.ReadLineAsync()) != null) Handle(line); }
                catch { }
            });
            return true;
        }
        catch (Exception ex) { Error = "Aufnahme-Hilfsprozess konnte nicht starten: " + ex.Message; return false; }
    }

    /// <summary>The helper ended (asked to, or crashed): everything that still waits gets an answer.</summary>
    private void Gone(Process p)
    {
        if (!ReferenceEquals(p, _p)) return;
        if (Running) Error = "Die Aufnahme wurde unerwartet beendet und startet neu";
        Running = false; Buffered = 0;
        foreach (var k in _wait.Keys) if (_wait.TryRemove(k, out var t)) t.TrySetException(new IOException("Aufnahme-Hilfsprozess wurde beendet"));
    }

    private void Handle(string line)
    {
        JsonElement m;
        try { m = JsonDocument.Parse(line).RootElement.Clone(); } catch { return; }
        if (m.TryGetProperty("ev", out var ev))
        {
            if (ev.GetString() == "log") { try { Log?.Invoke(m.GetProperty("text").GetString() ?? ""); } catch { } }
            else if (ev.GetString() == "status")
            {
                Running = m.GetProperty("running").GetBoolean();
                Buffered = m.GetProperty("buffered").GetDouble();
                Size = (m.GetProperty("w").GetInt32(), m.GetProperty("h").GetInt32());
                if (m.TryGetProperty("error", out var er) && er.ValueKind == JsonValueKind.String) Error = er.GetString();
            }
        }
        else if (m.TryGetProperty("id", out var id) && _wait.TryRemove(id.GetInt32(), out var t)) t.TrySetResult(m);
    }

    private async Task<JsonElement?> Call(Dictionary<string, object?> msg, int timeoutMs)
    {
        var id = Interlocked.Increment(ref _id);
        msg["id"] = id;
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _wait[id] = tcs;
        try
        {
            lock (_gate) _in!.WriteLine(JsonSerializer.Serialize(msg));
            if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)) != tcs.Task) { _wait.TryRemove(id, out _); return null; }
            return await tcs.Task;
        }
        catch { _wait.TryRemove(id, out _); return null; }
    }

    private static string? Err(JsonElement m) => m.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    public async Task<bool> Start(IntPtr hwnd, ReplayRecorder.Options o, double keepSeconds)
    {
        Error = null;
        if (!Ensure()) return false;
        var r = await Call(new() { ["cmd"] = "start", ["hwnd"] = hwnd.ToInt64(), ["fps"] = o.Fps, ["maxHeight"] = o.MaxHeight, ["mbps"] = o.Mbps, ["audio"] = o.Audio, ["keep"] = keepSeconds }, 20000);
        if (r is not { } m) { Error ??= "Die Aufnahme antwortet nicht"; await Stop(); return false; }
        bool ok = m.GetProperty("ok").GetBoolean();
        Running = ok; Error = ok ? null : Err(m) ?? "Aufnahme konnte nicht starten";
        if (ok) Size = (m.GetProperty("w").GetInt32(), m.GetProperty("h").GetInt32());
        else await Stop();
        return ok;
    }

    /// <summary>Stops recording and ends the helper process (it holds the video buffer, so nothing stays in memory).</summary>
    public async Task Stop()
    {
        var p = _p; _p = null;
        Running = false; Buffered = 0;
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                bool saving = !_wait.IsEmpty;                // a clip is being written: the helper finishes it before it ends
                try { lock (_gate) _in?.Close(); } catch { } // closing its input makes the helper stop recording and exit
                if (!await Task.Run(() => p.WaitForExit(saving ? 40000 : 6000))) p.Kill();
            }
        }
        catch { }
        finally
        {
            try { p.Dispose(); } catch { }
            _in = null;
            foreach (var k in _wait.Keys) if (_wait.TryRemove(k, out var t)) t.TrySetException(new IOException("Aufnahme wurde beendet"));
        }
    }

    public async Task<string?> Save(double before, double after, string folder, string name)
    {
        // also right after the recording ended (game closed or minimized): the helper still holds the last seconds
        if (!Alive) { Error ??= "Aufnahme läuft nicht"; return null; }
        var r = await Call(new() { ["cmd"] = "save", ["before"] = before, ["after"] = after, ["folder"] = folder, ["name"] = name }, (int)(Math.Max(0, after) * 1000) + 60000);   // (after < 0: a clip that ends in the past)
        if (r is not { } m) { Error = "Clip konnte nicht gespeichert werden (keine Antwort der Aufnahme)"; return null; }
        if (m.GetProperty("ok").GetBoolean()) return m.GetProperty("file").GetString();
        Error = Err(m) ?? "Clip konnte nicht gespeichert werden";
        return null;
    }
}
