using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OrbitTweak.Core;

// =====================================================================
//  GTA V settings.xml – graphics presets
// =====================================================================
internal static class Gfx
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rockstar Games", "GTA V", "settings.xml");
    private static string Backup => FilePath + ".orbit-backup";

    private static readonly HashSet<string> Floats = new(StringComparer.OrdinalIgnoreCase)
    { "LodScale", "CityDensity", "MotionBlurStrength", "PedLodBias", "VehicleLodBias", "Shadow_Distance", "PedVarietyMultiplier", "VehicleVarietyMultiplier", "MaxLodScale" };

    // live in <video> instead of <graphics>; Windowed: 0 fullscreen, 1 window, 2 borderless
    private static readonly HashSet<string> VideoKeys = new(StringComparer.OrdinalIgnoreCase) { "Windowed" };

    public static object Get()
    {
        if (!File.Exists(FilePath)) return new { found = false, path = FilePath, values = new Dictionary<string, string>(), hasBackup = false };
        var doc = XDocument.Load(FilePath);
        var g = doc.Root?.Element("graphics");
        var values = g?.Elements().Where(e => e.Attribute("value") != null)
                      .ToDictionary(e => e.Name.LocalName, e => e.Attribute("value")!.Value) ?? new();
        if (doc.Root?.Element("video")?.Element("Windowed")?.Attribute("value")?.Value is { } win) values["Windowed"] = win;
        return new { found = true, path = FilePath, values, hasBackup = File.Exists(Backup) };
    }

    public static void Save(JsonElement values)
    {
        if (!File.Exists(FilePath)) throw new InvalidOperationException("settings.xml nicht gefunden – starte GTA V / FiveM einmal.");
        if (FiveM.IsRunning) throw new InvalidOperationException("Bitte FiveM zuerst schließen, sonst überschreibt das Spiel die Datei.");
        if (!File.Exists(Backup)) File.Copy(FilePath, Backup);
        var attr = File.GetAttributes(FilePath);
        if (attr.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(FilePath, attr & ~FileAttributes.ReadOnly);

        var doc = XDocument.Load(FilePath);
        var g = doc.Root!.Element("graphics") ?? new XElement("graphics");
        if (g.Parent == null) doc.Root.Add(g);
        foreach (var p in values.EnumerateObject())
        {
            string v = p.Value.ValueKind switch
            {
                JsonValueKind.Number when Floats.Contains(p.Name) => p.Value.GetDouble().ToString("0.000000", CultureInfo.InvariantCulture),
                JsonValueKind.Number => ((int)Math.Round(p.Value.GetDouble())).ToString(CultureInfo.InvariantCulture),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => p.Value.GetString() ?? ""
            };
            if (Floats.Contains(p.Name) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                v = d.ToString("0.000000", CultureInfo.InvariantCulture);
            var sec = g;
            if (VideoKeys.Contains(p.Name))
            {
                sec = doc.Root.Element("video") ?? new XElement("video");
                if (sec.Parent == null) doc.Root.Add(sec);
            }
            var el = sec.Element(p.Name);
            if (el == null) sec.Add(new XElement(p.Name, new XAttribute("value", v)));
            else el.SetAttributeValue("value", v);
        }
        doc.Save(FilePath);
    }

    public static void Restore()
    {
        if (!File.Exists(Backup)) throw new InvalidOperationException("Es existiert noch kein Backup.");
        if (FiveM.IsRunning) throw new InvalidOperationException("Bitte FiveM zuerst schließen.");
        File.Copy(Backup, FilePath, true);
    }
}

// =====================================================================
//  CitizenFX.ini
// =====================================================================
internal static class CitizenIni
{
    public static string? Read(string key)
    {
        if (FiveM.CitizenIni is not { } f || !File.Exists(f)) return null;
        foreach (var l in File.ReadAllLines(f))
            if (l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return l[(key.Length + 1)..].Trim();
        return null;
    }

    public static void Write(string key, string value)
    {
        if (FiveM.CitizenIni is not { } f) throw new InvalidOperationException("FiveM nicht gefunden");
        var lines = File.Exists(f) ? File.ReadAllLines(f).ToList() : new List<string> { "[Game]" };
        var i = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
        if (i >= 0) lines[i] = $"{key}={value}";
        else
        {
            var g = lines.FindIndex(l => l.Trim().Equals("[Game]", StringComparison.OrdinalIgnoreCase));
            if (g < 0) { lines.Insert(0, "[Game]"); g = 0; }
            lines.Insert(g + 1, $"{key}={value}");
        }
        File.WriteAllLines(f, lines);
    }
}

// =====================================================================
//  Network helpers
// =====================================================================
internal static class Net
{
    public static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitTweak/2.0");
        return h;
    }

    /// <summary>TCP connect time in ms (works even when ICMP is blocked).</summary>
    public static async Task<int?> TcpPing(string host, int port, int timeout = 2000)
    {
        try
        {
            using var c = new TcpClient();
            var sw = Stopwatch.StartNew();
            var t = c.ConnectAsync(host, port);
            if (await Task.WhenAny(t, Task.Delay(timeout)) != t || !c.Connected) return null;
            return (int)sw.ElapsedMilliseconds;
        }
        catch { return null; }
    }

    public static async Task<object> CfxPing()
    {
        var list = new List<int>();
        for (int i = 0; i < 3; i++) if (await TcpPing("servers-frontend.fivem.net", 443) is int ms) list.Add(ms);
        if (list.Count == 0) return new { ms = (int?)null, jitter = 0 };
        return new { ms = (int?)(int)list.Average(), jitter = list.Max() - list.Min() };
    }

    public static async Task<object> CfxStatus()
    {
        var targets = new (string name, string url)[]
        {
            ("Server-Liste", "https://servers-frontend.fivem.net/api/servers/"),
            ("Cfx.re Web", "https://cfx.re/"),
            ("Content-CDN", "https://runtime.fivem.net/"),
            ("Cfx.re Forum", "https://forum.cfx.re/"),
        };
        var res = await Task.WhenAll(targets.Select(async t =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var r = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, t.url), HttpCompletionOption.ResponseHeadersRead);
                return new { name = t.name, ok = (int)r.StatusCode < 500, ms = (int)sw.ElapsedMilliseconds };
            }
            catch { return new { name = t.name, ok = false, ms = 0 }; }
        }));
        return res;
    }
}

// =====================================================================
//  Server favorites (Cfx.re API + direct IP)
// =====================================================================
internal static class Servers
{
    private static readonly Dictionary<string, (DateTime at, object data)> Cache = new();

    /// <summary>Normalizes user input into "cfx:code" or "ip:host:port".</summary>
    public static string Parse(string q)
    {
        q = q.Trim();
        var m = Regex.Match(q, @"cfx\.re/join/([a-z0-9]+)", RegexOptions.IgnoreCase);
        if (m.Success) return "cfx:" + m.Groups[1].Value.ToLowerInvariant();
        if (Regex.IsMatch(q, @"^[a-z0-9]{4,8}$", RegexOptions.IgnoreCase)) return "cfx:" + q.ToLowerInvariant();
        q = Regex.Replace(q, @"^(fivem://connect/|https?://)", "", RegexOptions.IgnoreCase).TrimEnd('/');
        if (Regex.IsMatch(q, @"^[\w\.\-]+(:\d+)?$")) return "ip:" + (q.Contains(':') ? q : q + ":30120");
        throw new InvalidOperationException("Ungültiges Format. Nutze cfx.re/join/abc123 oder IP:Port");
    }

    public static async Task<object> Fetch(string key, bool force = false)
    {
        if (!force && Cache.TryGetValue(key, out var c) && DateTime.Now - c.at < TimeSpan.FromSeconds(60)) return c.data;
        var data = key.StartsWith("cfx:") ? await FetchCfx(key[4..]) : await FetchIp(key[3..]);
        Cache[key] = (DateTime.Now, data);
        return data;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;
    private static int Int(JsonElement e, string name) =>
        int.TryParse(Str(e, name), out var i) ? i : 0;

    private static async Task<object> FetchCfx(string code)
    {
        try
        {
            var json = await Net.Http.GetStringAsync($"https://servers-frontend.fivem.net/api/servers/single/{code}");
            using var doc = JsonDocument.Parse(json);
            var d = doc.RootElement.GetProperty("Data");
            d.TryGetProperty("vars", out var vars);
            var name = Str(vars, "sv_projectName") ?? Str(d, "hostname") ?? code;
            var desc = Str(vars, "sv_projectDesc") ?? "";
            var tags = (Str(vars, "tags") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4).ToArray();
            var locale = Str(vars, "locale") ?? "";
            if (locale.Contains('-')) locale = locale.Split('-')[1];
            var iconVer = Str(d, "iconVersion");
            int? ping = null;
            if (d.TryGetProperty("connectEndPoints", out var eps) && eps.ValueKind == JsonValueKind.Array && eps.GetArrayLength() > 0)
            {
                var ep = eps[0].GetString() ?? "";
                var m = Regex.Match(ep, @"^([\d\.]+):(\d+)$");
                if (m.Success) ping = await Net.TcpPing(m.Groups[1].Value, int.Parse(m.Groups[2].Value));
            }
            var max = Int(d, "sv_maxclients");
            if (max == 0) max = Int(d, "svMaxclients");
            return new
            {
                code = "cfx:" + code, connect = "cfx.re/join/" + code, name, desc, tags, locale,
                players = Int(d, "clients"), max, online = true, ping,
                icon = iconVer != null ? $"https://servers-live.fivem.net/servers/icon/{code}/{iconVer}.png" : null,
                banner = Str(vars, "banner_detail") ?? Str(vars, "banner_connecting"),
                addr = "cfx.re/join/" + code
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Offline("cfx:" + code, "cfx.re/join/" + code, "Server offline / nicht gefunden");
        }
        catch { return Offline("cfx:" + code, "cfx.re/join/" + code, "Keine Verbindung zur Cfx.re API"); }
    }

    private static async Task<object> FetchIp(string addr)
    {
        var host = addr[..addr.LastIndexOf(':')];
        var port = int.Parse(addr[(addr.LastIndexOf(':') + 1)..]);
        try
        {
            var infoJson = await Net.Http.GetStringAsync($"http://{addr}/info.json");
            using var info = JsonDocument.Parse(infoJson);
            info.RootElement.TryGetProperty("vars", out var vars);
            int players = 0;
            try { using var pl = JsonDocument.Parse(await Net.Http.GetStringAsync($"http://{addr}/players.json")); players = pl.RootElement.GetArrayLength(); } catch { }
            var max = Int(vars, "sv_maxClients");
            var tags = (Str(vars, "tags") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4).ToArray();
            return new
            {
                code = "ip:" + addr, connect = addr, name = Str(vars, "sv_projectName") ?? Str(vars, "sv_hostname") ?? addr,
                desc = Str(vars, "sv_projectDesc") ?? addr, tags, locale = (Str(vars, "locale") ?? "").Split('-').Last(),
                players, max, online = true, ping = await Net.TcpPing(host, port), icon = (string?)null,
                banner = Str(vars, "banner_detail"), addr
            };
        }
        catch
        {
            var ping = await Net.TcpPing(host, port);
            return new
            {
                code = "ip:" + addr, connect = addr, name = addr, desc = ping != null ? "Info gesperrt – Server erreichbar" : "Nicht erreichbar",
                tags = Array.Empty<string>(), locale = "", players = 0, max = 0, online = ping != null, ping, icon = (string?)null, banner = (string?)null, addr
            };
        }
    }

    private static object Offline(string code, string connect, string desc) => new
    {
        code, connect, name = connect, desc, tags = Array.Empty<string>(), locale = "", players = 0, max = 0,
        online = false, ping = (int?)null, icon = (string?)null, banner = (string?)null, addr = connect
    };
}

// =====================================================================
//  Health check & logs
// =====================================================================
internal static class Health
{
    private record Item(string name, string value, string st, string? fix = null);

    public static object Run(List<Tweak> tweaks)
    {
        var items = new List<Item>();
        bool T(string id) => tweaks.FirstOrDefault(t => t.Id == id)?.SafeCheck() ?? false;

        items.Add(FiveM.Root is { } r ? new("FiveM installiert", r, "ok") : new("FiveM installiert", "Nicht gefunden – Pfad im Launcher wählen", "bad"));

        var gb = Math.Round(Native.Memory().total / 1073741824.0);
        items.Add(new("Arbeitsspeicher", $"{gb} GB" + (gb >= 16 ? " – perfekt für FiveM" : gb >= 8 ? " – knapp, Auto-RAM-Clean empfohlen" : " – zu wenig für FiveM"),
            gb >= 16 ? "ok" : gb >= 8 ? "warn" : "bad", gb >= 16 ? null : "tweak:autoram"));

        try
        {
            var drv = new DriveInfo(Path.GetPathRoot(FiveM.Root ?? Environment.SystemDirectory)!);
            var free = drv.AvailableFreeSpace / 1073741824.0;
            items.Add(new($"Freier Speicher ({drv.Name.TrimEnd('\\')})", $"{free:0} GB frei", free >= 30 ? "ok" : free >= 10 ? "warn" : "bad", free < 30 ? "clean" : null));
        }
        catch { }

        var build = Environment.OSVersion.Version.Build;
        items.Add(new("Windows-Version", $"Build {build}" + (build >= 19045 ? "" : " – Update empfohlen"), build >= 19045 ? "ok" : "warn"));

        items.Add(T("ult") ? new("Energieplan", "Höchstleistung aktiv", "ok") : new("Energieplan", "Ausbalanciert – CPU taktet herunter", "warn", "tweak:ult"));
        items.Add(T("dvr") ? new("Xbox Game DVR", "Deaktiviert", "ok") : new("Xbox Game DVR", "Aktiv – kostet FPS", "bad", "tweak:dvr"));
        items.Add(T("gpu") ? new("FiveM GPU-Zuweisung", "Dedizierte GPU erzwungen", "ok") : new("FiveM GPU-Zuweisung", "Windows entscheidet – evtl. iGPU", "warn", "tweak:gpu"));
        items.Add(T("nagle") ? new("Netzwerk-Latenz", "Nagle deaktiviert", "ok") : new("Netzwerk-Latenz", "Nagle aktiv – Pakete werden verzögert", "warn", "tweak:nagle"));

        var (gpuName, drvVer, drvDate) = GpuDriver();
        if (drvDate is { } dd)
        {
            var age = (DateTime.Now - dd).TotalDays;
            items.Add(new("GPU-Treiber", $"{gpuName} · {drvVer} · {(int)(age / 30)} Monate alt", age < 180 ? "ok" : "warn"));
        }

        var cache = FiveM.Root != null ? Cleaner.Measure(Cleaner.Targets().First(t => t.Id == "fm")) : 0;
        items.Add(new("FiveM-Cache", SysInfo.Bytes(cache) + (cache > 4L * 1073741824 ? " – leeren empfohlen" : ""), cache > 4L * 1073741824 ? "warn" : "ok", cache > 4L * 1073741824 ? "cache" : null));

        var crashes = Crashes().Count(c => c.time > DateTime.Now.AddDays(-7));
        items.Add(new("Crashes (7 Tage)", crashes == 0 ? "Keine Abstürze" : $"{crashes} Abstürze – Logs prüfen", crashes == 0 ? "ok" : crashes < 3 ? "warn" : "bad"));

        var bg = FiveM.BackgroundApps.Sum(n => { var p = Process.GetProcessesByName(n); foreach (var x in p) x.Dispose(); return p.Length; });
        items.Add(new("Hintergrund-Apps", bg == 0 ? "Keine Störenfriede aktiv" : $"{bg} Prozesse (OneDrive, Launcher …)", bg == 0 ? "ok" : "warn", bg == 0 ? null : "killbg"));

        var iv = CitizenIni.Read("IVPath");
        if (FiveM.Root != null)
            items.Add(iv != null && Directory.Exists(iv) ? new("GTA V Installation", iv, "ok") : new("GTA V Installation", "Pfad in CitizenFX.ini fehlt oder ungültig", "warn"));

        var score = (int)Math.Round(items.Sum(i => i.st == "ok" ? 100 : i.st == "warn" ? 50 : 0) / (double)items.Count);
        return new { score, items };
    }

    private static (string, string, DateTime?) GpuDriver()
    {
        const string cls = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        foreach (var sk in Reg.SubKeys(cls))
        {
            if (!int.TryParse(sk, out _)) continue;
            var p = $@"{cls}\{sk}";
            var name = Reg.Get(p, "DriverDesc") as string;
            if (name == null || name.Contains("Basic") || name.Contains("Virtual")) continue;
            var date = Reg.Get(p, "DriverDate") as string;
            DateTime? d = DateTime.TryParseExact(date, new[] { "M-d-yyyy", "MM-dd-yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
            if (name.Contains("NVIDIA") || name.Contains("Radeon") || name.Contains("Arc") || d != null)
                return (name, Reg.Get(p, "DriverVersion") as string ?? "?", d);
        }
        return ("", "", null);
    }

    public static IEnumerable<(string name, DateTime time, long size)> Crashes()
    {
        if (FiveM.AppDir is not { } a) yield break;
        var dir = Path.Combine(a, "crashes");
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles().OrderByDescending(f => f.LastWriteTime))
            yield return (f.Name, f.LastWriteTime, f.Length);
    }

    public static object Logs()
    {
        var lines = new List<string>();
        string? file = null;
        if (FiveM.AppDir is { } a && Directory.Exists(Path.Combine(a, "logs")))
        {
            var f = new DirectoryInfo(Path.Combine(a, "logs")).EnumerateFiles("*.log").Concat(new DirectoryInfo(Path.Combine(a, "logs")).EnumerateFiles("*.txt"))
                    .OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
            if (f != null)
            {
                file = f.FullName;
                using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length > 400_000) fs.Seek(-400_000, SeekOrigin.End);
                using var sr = new StreamReader(fs);
                var all = sr.ReadToEnd().Split('\n');
                lines = all.Skip(Math.Max(0, all.Length - 300)).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            }
        }
        var crashes = Crashes().Take(8).Select(c => new { c.name, date = c.time.ToString("dd.MM. HH:mm"), c.size });
        return new { file, lines, crashes };
    }
}

// =====================================================================
//  Start FiveM as the normal desktop user (FiveM refuses to run elevated)
// =====================================================================
internal static class Unelevated
{
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr tok, uint access, IntPtr attrs, int impLevel, int tokType, out IntPtr newTok);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr tok, uint logonFlags, string? app, string? cmd, uint flags, IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    public static bool Start(string exe, string args, string dir)
    {
        IntPtr hProc = IntPtr.Zero, hTok = IntPtr.Zero, hDup = IntPtr.Zero;
        try
        {
            var shell = GetShellWindow();
            if (shell == IntPtr.Zero) return false;
            GetWindowThreadProcessId(shell, out var pid);
            hProc = OpenProcess(0x0400, false, pid); // PROCESS_QUERY_INFORMATION
            if (hProc == IntPtr.Zero || !OpenProcessToken(hProc, 0x0002, out hTok)) return false; // TOKEN_DUPLICATE
            if (!DuplicateTokenEx(hTok, 0x018B, IntPtr.Zero, 2, 1, out hDup)) return false; // primary token
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            var ok = CreateProcessWithTokenW(hDup, 0, exe, $"\"{exe}\" {args}".Trim(), 0, IntPtr.Zero, dir, ref si, out var pi);
            if (ok) { CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
            return ok;
        }
        catch { return false; }
        finally
        {
            if (hDup != IntPtr.Zero) CloseHandle(hDup);
            if (hTok != IntPtr.Zero) CloseHandle(hTok);
            if (hProc != IntPtr.Zero) CloseHandle(hProc);
        }
    }
}
