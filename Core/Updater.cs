using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OrbitTweak.Core;

/// <summary>
/// In-app updates via GitHub Releases.
/// Release flow: bump &lt;Version&gt; in OrbitTweak.csproj, build, then create a GitHub release
/// tagged "v&lt;version&gt;" with OrbitTweak.exe attached. Each bullet line ("- ...") in the
/// release text is shown as a changelog entry in the app.
/// </summary>
internal static class Updater
{
    /// <summary>GitHub repository that hosts the releases, e.g. "maxf/orbittweak".</summary>
    public const string Repo = "Ky3ls/orbittweak";

    private static string? _url, _sha;
    private static long _size;
    private static Version? _latest;
    private static string TempFile => Path.Combine(Path.GetTempPath(), "OrbitTweak-update.exe");

    public static Version Current
    {
        get { var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0); return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
    }

    public static bool Configured => !Repo.StartsWith("OWNER");

    /// <summary>Removes the previous exe left over from the last update.</summary>
    public static void CleanupOld()
    {
        try { if (Environment.ProcessPath is { } p && File.Exists(p + ".old")) File.Delete(p + ".old"); } catch { }
        try { if (File.Exists(TempFile)) File.Delete(TempFile); } catch { }
    }

    public static async Task<object> Check()
    {
        var current = Current.ToString(3);
        if (!Configured) return new { configured = false, current, available = false };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await Net.Http.SendAsync(req);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new { configured = true, current, available = false, noRelease = true };
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            var tag = (r.GetProperty("tag_name").GetString() ?? "0.0.0").TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var latest)) latest = new Version(0, 0, 0);
            _latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));

            _url = null; _sha = null; _size = 0;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                _url = a.GetProperty("browser_download_url").GetString();
                _size = a.GetProperty("size").GetInt64();
                if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && d.GetString()!.StartsWith("sha256:"))
                    _sha = d.GetString()![7..];
                break;
            }

            var body = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var notes = body.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("- ") || l.StartsWith("* "))
                            .Select(l => l[2..].Trim()).Take(12).ToArray();
            var date = r.TryGetProperty("published_at", out var p) && p.TryGetDateTime(out var dt) ? dt.ToLocalTime().ToString("dd.MM.yyyy") : "";

            return new { configured = true, current, latest = _latest.ToString(3), available = _latest > Current && _url != null, notes, date, size = _size };
        }
        catch (Exception ex) { return new { configured = true, current, available = false, error = ex.Message }; }
    }

    public static async Task Download(Action<int, long, long> progress)
    {
        if (_url == null) throw new InvalidOperationException("Kein Update gefunden – bitte erneut prüfen.");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitTweak/" + Current.ToString(3));
        using var res = await http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? _size;
        await using (var src = await res.Content.ReadAsStreamAsync())
        await using (var dst = File.Create(TempFile))
        {
            var buf = new byte[81920]; long done = 0; int n, lastPct = -1;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                var pct = total > 0 ? (int)(done * 100 / total) : 0;
                if (pct != lastPct) { lastPct = pct; progress(pct, done, total); }
            }
        }
        if (_sha != null)
        {
            await using var fs = File.OpenRead(TempFile);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs));
            if (!hash.Equals(_sha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(TempFile);
                throw new InvalidOperationException("Prüfsumme stimmt nicht – Download beschädigt.");
            }
        }
        if (new FileInfo(TempFile).Length < 1_000_000) throw new InvalidOperationException("Download unvollständig.");
    }

    /// <summary>Swaps the running exe for the downloaded one and restarts.
    /// Windows allows renaming a running executable, so no helper script is needed.</summary>
    public static void Install()
    {
        var cur = Environment.ProcessPath ?? throw new InvalidOperationException("Pfad unbekannt");
        if (!File.Exists(TempFile)) throw new InvalidOperationException("Update wurde nicht heruntergeladen.");
        var old = cur + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(cur, old);
        try { File.Copy(TempFile, cur); }
        catch { File.Move(old, cur); throw; }
        try { File.Delete(TempFile); } catch { }
        Process.Start(new ProcessStartInfo(cur, "--updated") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(cur)! });
        System.Windows.Application.Current.Shutdown();
    }
}
