using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OrbitTweak.Core;

/// <summary>
/// Kill detection by a vision model: after a firefight a handful of pictures of it (the strip around the crosshair
/// and a few whole pictures) are sent to OpenRouter, and the model says whether the player killed someone or died.
/// Works on any server, whatever its kill message looks like. Only pictures are sent, nothing in the game is read.
/// This file has no dependencies on the rest of the app, so the same code can be tried on recorded clips.
/// </summary>
internal static class KillJudge
{
    /// <summary>Free models that take pictures, tried in this order (OpenRouter moves on when one is busy).</summary>
    public const string DefaultModels = "nvidia/nemotron-3.5-super-vl:free, google/gemma-4-31b-it:free, google/gemma-4-26b-a4b-it:free";
    private const string Url = "https://openrouter.ai/api/v1/chat/completions";

    /// <summary>One picture: seconds since the first one, whole picture or crosshair strip, JPEG bytes.</summary>
    public sealed record Frame(double T, bool Full, byte[] Jpeg);

    public sealed record Verdict(int Kills, bool Died, double? FirstKillT, string Text, string Word, string Reason, string Model, double Seconds);

    /// <summary>The model could not be asked. Quota = the daily or per-minute limit of the account is used up.</summary>
    public sealed class JudgeException : Exception
    {
        public bool Quota { get; }
        public JudgeException(string message, bool quota = false) : base(message) { Quota = quota; }
    }

    private const string Prompt = """
You check screenshots of a GTA V / FiveM multiplayer firefight for a clip recorder. The screenshots are from the screen of one player who has just been shooting. Decide whether THIS PLAYER killed someone, and whether this player died.

The images are in time order. Each one is announced by a line of text with its number, its kind and its time:
- "crosshair crop": the area around the crosshair at full resolution. Servers show the kill confirmation here, for example the text "KILLED <name>", "ELIMINATED <name>", "Eliminiert", "+1 Kill", "+100", "2 Streak", a skull, or a hit marker that turns into a red X.
- "full frame": the whole screen, scaled down. Use it to see whether the player died: a kill cam showing another player in close-up (often with a small player card), a "you died" / "wasted" / respawn screen, a spectator view, a grey screen, or the player's own character lying dead.

Rules:
- A kill counts only when the screen shows a confirmation that the player killed someone. Aiming or shooting at somebody, damage numbers, or an enemy falling over without any confirmation are not enough.
- The same confirmation visible in several images in a row is ONE kill. A different name, a higher streak number, or a confirmation that reappears after it was gone is a further kill.
- Kill feed lines about other players do not count. "Killed by <name>" or "you were killed" means the player died.
- If you are not sure, report no kill.
- Keep your thinking short: go through the images once, then answer.

Reply with one JSON object and nothing else:
{"kills": <number of kills by the player>, "died": <true or false>, "kill_images": [<numbers of the images in which a kill confirmation is visible>], "text": "<the kill confirmation exactly as written on screen, or empty>", "word": "<the one word of that text that means killed, in upper case, or empty>", "reason": "<one short sentence>"}
""";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Asks the model about one firefight. Throws <see cref="JudgeException"/> when there is no usable answer.</summary>
    public static async Task<Verdict> Ask(string key, string? models, IReadOnlyList<Frame> frames, int timeoutSeconds = 40, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new JudgeException("kein OpenRouter-Key eingetragen");
        if (frames.Count == 0) throw new JudgeException("keine Bilder vom Gefecht");
        var list = (string.IsNullOrWhiteSpace(models) ? DefaultModels : models)
            .Split(new[] { ',', ';', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Contains('/')).Distinct().Take(3).ToList();
        if (list.Count == 0) list = DefaultModels.Split(',').Select(x => x.Trim()).ToList();

        var inv = CultureInfo.InvariantCulture;
        var content = new List<object>();
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            content.Add(new { type = "text", text = $"Image {i + 1}: {(f.Full ? "full frame" : "crosshair crop")}, {f.T.ToString("0.0", inv)} s" });
            content.Add(new { type = "image_url", image_url = new { url = "data:image/jpeg;base64," + Convert.ToBase64String(f.Jpeg) } });
        }
        content.Add(new { type = "text", text = $"That was all {frames.Count} images. Reply with the JSON object only." });
        var body = new
        {
            model = list[0],
            models = list,                       // the next one is used when the first is busy or fails
            max_tokens = 6000,                   // the models think before they answer; too little leaves no room for the answer
            temperature = 0,
            reasoning = new { effort = "low" },
            messages = new object[] { new { role = "system", content = Prompt }, new { role = "user", content } },
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        string raw; int status;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            using var res = await Http.SendAsync(req, cts.Token);
            status = (int)res.StatusCode;
            raw = await res.Content.ReadAsStringAsync(cts.Token);
        }
        catch (OperationCanceledException) { throw new JudgeException($"keine Antwort nach {timeoutSeconds} s"); }
        catch (HttpRequestException ex) { throw new JudgeException("keine Verbindung zu OpenRouter (" + ex.Message + ")"); }

        JsonElement root;
        try { root = JsonDocument.Parse(raw).RootElement; }
        catch { throw new JudgeException($"unlesbare Antwort (HTTP {status})"); }
        if (root.TryGetProperty("error", out var err))
        {
            int code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : status;
            string msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            string detail = err.TryGetProperty("metadata", out var md) && md.TryGetProperty("raw", out var rw) ? rw.ToString() : "";
            // 429 from OpenRouter itself = the account's limit; 429 from the provider behind it = that model is busy right now
            bool upstream = detail.Contains("upstream", StringComparison.OrdinalIgnoreCase);
            throw code switch
            {
                401 => new JudgeException("OpenRouter-Key ungültig"),
                402 => new JudgeException("OpenRouter: kein Guthaben für dieses Modell"),
                429 when !upstream => new JudgeException("Limit des OpenRouter-Kontos erreicht (kostenlose Modelle: 50 Anfragen pro Tag, 20 pro Minute)", true),
                429 => new JudgeException("die kostenlosen Modelle sind gerade überlastet"),
                _ => new JudgeException($"OpenRouter-Fehler {code}: {(msg.Length > 120 ? msg[..120] : msg)}"),
            };
        }
        string model = root.TryGetProperty("model", out var mo) ? mo.GetString() ?? list[0] : list[0];
        string text = "";
        try { text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? ""; } catch { }
        var v = Parse(text, frames, model, sw.Elapsed.TotalSeconds);
        return v ?? throw new JudgeException(text.Length == 0 ? $"{model} hat nicht geantwortet (nur nachgedacht)" : $"{model}: Antwort nicht verstanden");
    }

    /// <summary>Reads the model's answer: the JSON object, wherever it stands in the text.</summary>
    public static Verdict? Parse(string text, IReadOnlyList<Frame> frames, string model, double seconds)
    {
        int a = text.IndexOf('{'), b = text.LastIndexOf('}');
        if (a < 0 || b <= a) return null;
        try
        {
            var o = JsonDocument.Parse(text[a..(b + 1)]).RootElement;
            int kills = o.TryGetProperty("kills", out var k) ? k.ValueKind switch
            {
                JsonValueKind.Number => (int)k.GetDouble(),
                JsonValueKind.True => 1,
                JsonValueKind.String when int.TryParse(k.GetString(), out var n) => n,
                _ => 0
            } : 0;
            bool died = o.TryGetProperty("died", out var d) && (d.ValueKind == JsonValueKind.True || d.ValueKind == JsonValueKind.String && d.GetString()?.ToLowerInvariant() == "true");
            double? first = null;
            if (o.TryGetProperty("kill_images", out var ki) && ki.ValueKind == JsonValueKind.Array)
                foreach (var e in ki.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Number && e.GetInt32() is var n && n >= 1 && n <= frames.Count && (first == null || frames[n - 1].T < first)) first = frames[n - 1].T;
            string S(string name) => o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? (p.GetString() ?? "").Trim() : "";
            return new Verdict(Math.Clamp(kills, 0, 20), died, kills > 0 ? first : null, S("text"), S("word"), S("reason"), model, seconds);
        }
        catch { return null; }
    }
}
