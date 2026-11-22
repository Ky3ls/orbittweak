using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using OrbitTweak.Core;

namespace OrbitTweak;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions J = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions JIn = new() { PropertyNameCaseInsensitive = true };

    private readonly Settings S = Settings.Load();
    private readonly List<Tweak> _tweaks;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private object _fm = new { running = false, game = false };
    private bool _probing;
    private int _tick;
    // live tweaks of the game process (see ProbeFiveM)
    private int _qosPid;                       // the game process last tagged "full speed" (tweak nothrottle)
    private bool _prioLooked;                  // the one look for a game left at High by the old "Auto-Priorität Hoch" is done
    private DateTime _prefUpkeep;              // last look for new FiveM exes that need the per-app graphics fields
    private Task<Catalog.MigrationResult>? _migrated;
    private double _zoom = 1;
    private readonly CrosshairOverlay _xhair = new();
    private readonly CrosshairOverlay _minimap = new() { Anchor = Anchor.BottomLeft };
    private readonly SnapTap _snap = new();
    private readonly AutoClip _autoClip = new();
    private readonly KillFeed _killFeed = new();
    private DateTime _kfSaveAt, _kfFirst; private bool _kfPending, _kfDied; private int _kfKills, _kfDeaths;
    private readonly KillMark _killMark = new();
    private readonly RecorderProxy _rec = new();
    private bool _recBusy; private DateTime _recRetry; private string? _recError; private string _recKey = "";
    private int _saving; private DateTime _recHoldAt;   // clips being written right now / since when the end of the recording is put off for them
    private bool _hlBusy;                               // a highlight video is being put together
    private bool _shareBusy;                            // a clip is being made smaller for sharing
    private readonly PadOverlay _pad = new();
    private CoreWebView2Environment? _env;
    private bool _padWatch;
    private DateTime _padEmit;
    private const int HK_PAD = 0xB03;
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _dispActive, _preview, _lastGame;
    private bool? _gammaOk, _vibOk;
    private const int HK_XHAIR = 0xB00, HK_DISPLAY = 0xB01, HK_CLIP = 0xB02, HK_LAG = 0xB04;
    private readonly CrosshairOverlay _flash = new() { Anchor = Anchor.TopCenter, Animate = true };
    // small counter in a corner of the game: recording dot and kills of the round
    private readonly CrosshairOverlay _hud = new() { Anchor = Anchor.TopLeft, Animate = true };
    private string _hudKey = "";
    private FileSystemWatcher? _dlWatch;
    private DateTime? _playSince; private uint _playPid;
    // Discord status ("Spielt OrbitTweak"), see DiscordTick. The _dcGame/_dcFm values come from ProbeFiveM (set on the UI thread).
    private readonly DiscordPresence _dc = new();
    private Presence? _dcLast;
    private bool _dcFm, _dcProbed; private int _dcGamePid; private DateTime? _dcGameStart;
    private (string? name, string? icon, int pid, DateTime at) _dcSrv; private bool _dcSrvBusy;
    private DateTime _flashUntil;
    // short note in the game when a kill was noticed, with the kill count of the round
    private readonly CrosshairOverlay _banner = new() { Anchor = Anchor.TopCenter, Animate = true };
    private DateTime _bannerUntil; private int _roundKills; private uint _roundPid; private string _accentHex = "#FF7A3D";
    // the kill clips saved since the game was started (for the automatic highlight video when it is closed)
    private readonly List<string> _roundClips = new();
    // short note in the game when the key switches WASD "Laggen" on or off
    private readonly CrosshairOverlay _lagFlash = new() { Anchor = Anchor.TopCenter, Animate = true };
    private DateTime _lagFlashUntil; private byte[]? _lagPngOn, _lagPngOff; private bool _lagKeyOk = true;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public MainWindow()
    {
        FiveM.Settings = S;
        if (Mods.ImportLegacy(S.ModCats, S.CatalogMods)) S.Save();   // mod categories of versions before 2.24 move into the mod list
        _tweaks = Catalog.All(S);
        InitializeComponent();

        var wa = SystemParameters.WorkArea;
        _zoom = Math.Min(1, Math.Min((wa.Width - 24) / 1280, (wa.Height - 24) / 800));
        if (wa.Width >= 2500 && wa.Height >= 1500) _zoom = 1.25;
        Width = 1280 * _zoom;
        Height = 800 * _zoom;

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND (Windows 11)
            DwmSetWindowAttribute(h, 33, ref round, sizeof(int));
            int dark = 1;
            DwmSetWindowAttribute(h, 20, ref dark, sizeof(int));
            HwndSource.FromHwnd(h)?.AddHook(WndProc);
            SwInit(h);   // 2.31: Platz-Alarm, Freunde online, --connect from a desktop shortcut (MainWindow.ServerWatch.cs)
            RegisterKeys();
        };
        StateChanged += (_, _) =>
        {
            Root.Margin = WindowState == WindowState.Maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
            // minimized: the page is not drawn at all (WebView2 stops rendering, timers slow down) and gives memory back
            Web.Visibility = WindowState == WindowState.Minimized ? Visibility.Hidden : Visibility.Visible;
            WebRest();
        };
        // the page rests (no blur, no running effects) whenever the window is not the one being used
        Activated += (_, _) => Emit("winfocus", new { on = true });
        Deactivated += (_, _) => Emit("winfocus", new { on = false });
        Loaded += async (_, _) => await InitWeb();
        Closing += (_, _) => { S.Save(); GameTimer.Release(); StopLive(); };
        Closing += (_, _) => _dc.Dispose();   // takes the card off the Discord profile at once
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { ScreenColor.Restore(); NvVibrance.Restore(); };
        Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
        _live.Tick += (_, _) => LiveTick();
        _autoClip.Fight += (shots, secs, span) => Dispatcher.InvokeAsync(async () =>
        {
            if (!_authed || (!S.Clip.Auto && !NetOn && !AiOn)) return;
            // the fight ended about 3 s ago. If a kill was noticed in it, that clip is already being saved.
            var started = DateTime.Now.AddSeconds(-(span + 3.5));
            var n = shots == 1 ? "1 Schuss" : $"{shots} Schüsse";
            if (_lastKillAt > started.AddSeconds(-1)) { ClipLog.Add($"Gefecht ({n}, {span:0.0} s): schon als Kill gespeichert"); return; }
            // AI check: where kills are not noticed at the crosshair (yet), a vision model looks at pictures of the fight.
            // Where they are, a fight without a kill needs no look (and the free daily requests are kept for other servers).
            // the player's own death was read in this fight: taken now, the AI check can take half a minute and a death
            // in the next fight must not count for this one
            bool died = _lastDeathAt > started.AddSeconds(-1);
            if (!S.Clip.Auto && AiOn && !NetQuiet && await AiJudge(n, span, started, died)) return;
            if (!S.Clip.Auto && !NetOn) return;
            // a fight lost, not worth a safety-net clip
            if (!S.Clip.Auto && died) { ClipLog.Add($"Gefecht ({n}, {span:0.0} s) ohne Kill, dein Tod erkannt: nicht gespeichert"); return; }
            // the safety net is only for places where kills are not noticed. Where they are (a kill was noticed a short
            // while ago), a fight without a kill is just that, mostly the player's own death, and is not saved.
            if (!S.Clip.Auto && NetQuiet)
            {
                ClipLog.Add($"Gefecht ({n}, {span:0.0} s) ohne Kill: nicht gespeichert, die Kill-Erkennung funktioniert hier (letzter erkannter Kill vor {(DateTime.Now - S.Clip.ProvenAt!.Value).TotalMinutes:0} min)");
                return;
            }
            // fights in quick succession: the previous clip is still fresh, wait a little before the next one
            if ((DateTime.Now - _lastFightClip).TotalSeconds < 12) { ClipLog.Add($"Gefecht ({n}): direkt nach dem letzten Gefecht-Clip, übersprungen"); return; }
            _lastFightClip = DateTime.Now;
            _statFights++;
            ClipLog.Add($"Gefecht ({n}, {span:0.0} s) ohne erkannten Kill: wird gespeichert");
            // reach back to a few seconds before the first shot
            await SaveClip(S.Clip.Auto ? $"Auto-Clip nach Gefecht ({n})" : $"Auto-Clip: Gefecht ohne erkannten Kill ({n})", "Gefecht", Math.Min(8, S.Clip.Before) + span + 3.5, 0);
        });
        // when the kill happened is taken where it was noticed (the main thread may be busy for a moment)
        _killFeed.Hit += kill => { var at = DateTime.Now.AddSeconds(-KillLogic.LagFeed); Dispatcher.InvokeAsync(() => OnKill(kill, null, at)); };
        _killMark.Kill += (how, at) => Dispatcher.InvokeAsync(() => OnKill(true, how, at));
        _killMark.Death += text => Dispatcher.InvokeAsync(() => OnDeath(text));
        _killMark.Note += ClipLog.Add;
#if DEBUG
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORBIT_TEST_GAME"))) _killMark.NeedShot = false; // the stand-in game is not shot at
#endif
        _rec.Log += Log;
        _rec.Log += ClipLog.Add;
        _dc.Log += m => Log("💬 " + m);
        // a pack saved from Discord (or any browser) into the Downloads folder is noticed at once and offered for installing
        try
        {
            var dlw = new FileSystemWatcher(ModDownloads.Dir) { NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
            void Seen(string path)
            {
                if (ModDownloads.Ext.Contains(Path.GetExtension(path).ToLowerInvariant())) Emit("dlNew", new { name = Path.GetFileName(path), cat = ModDownloads.Guess(Path.GetFileName(path)) });
            }
            dlw.Created += (_, e) => Seen(e.FullPath);
            dlw.Renamed += (_, e) => Seen(e.FullPath);   // browsers write a temporary name first
            _dlWatch = dlw;
        }
        catch { }
        // Snap Tap, timer resolution etc. only start after a valid key (see OnAuthed)
        _pad.Configure(S.Pad, S.Pad.Layout == "auto" ? "xbox" : S.Pad.Layout);
        _live.Start();


        Task.Run(Catalog.UndoKeyboardTurbo);
        // what older versions switched on and that is gone now is undone once (see Catalog.Migrate)
        _migrated = Task.Run(() =>
        {
            var r = new Catalog.MigrationResult();
            try { r = Catalog.Migrate(S); } catch { }
            foreach (var l in r.Log) Log(l);
            if (S.TweaksMigrated) S.Save();
            return r;
        });
        Task.Run(() => { int back = BgThrottle.Recover(); if (back > 0) Log($"⚡ {back} Programme liefen nach einem unsauberen Ende noch gebremst und sind wieder normal"); });
        Updater.CleanupOld();
        Background = S.Mode == "light" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 237, 231)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 17));
    }

    // =====================================================================
    //  WEBVIEW
    // =====================================================================
    private async Task InitWeb()
    {
        try
        {
            var data = AppDirs.Local;
            var env = _env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(data, "WebView2"));
            Web.DefaultBackgroundColor = S.Mode == "light" ? System.Drawing.Color.FromArgb(255, 239, 237, 231) : System.Drawing.Color.FromArgb(255, 18, 18, 17);
            await Web.EnsureCoreWebView2Async(env);
            var c = Web.CoreWebView2;
            c.Settings.AreDefaultContextMenusEnabled = false;
            c.Settings.IsZoomControlEnabled = false;
            c.Settings.IsStatusBarEnabled = false;
            c.Settings.AreBrowserAcceleratorKeysEnabled = false;
            c.Settings.IsPinchZoomEnabled = false;
#if !DEBUG
            c.Settings.AreDevToolsEnabled = false;
#endif
            c.NewWindowRequested += (_, e) => { e.Handled = true; Proc.Open(e.Uri); };
            var webDir = ExtractWeb(Path.Combine(data, "web"));
            c.SetVirtualHostNameToFolderMapping("orbit.local", webDir, CoreWebView2HostResourceAccessKind.Allow);
            try { Directory.CreateDirectory(Backgrounds.Dir); c.SetVirtualHostNameToFolderMapping("bg.orbit", Backgrounds.Dir, CoreWebView2HostResourceAccessKind.Allow); } catch { }
            try { if (Directory.Exists(Clipper.VideosDir)) c.SetVirtualHostNameToFolderMapping("clips.orbit", Clipper.VideosDir, CoreWebView2HostResourceAccessKind.Allow); } catch { }
            c.WebMessageReceived += OnMessage;
            Web.ZoomFactor = _zoom;
            c.Navigate("https://orbit.local/index.html");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("Für OrbitTweak wird die Microsoft Edge WebView2 Runtime benötigt.\nDer Download wird jetzt geöffnet.", "OrbitTweak", MessageBoxButton.OK, MessageBoxImage.Information);
            Proc.Open("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Start fehlgeschlagen: " + ex.Message, "OrbitTweak", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private static string ExtractWeb(string dir)
    {
        if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { }
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
        {
            var n = name.Replace('\\', '/');
            if (!n.StartsWith("web/")) continue;
            var target = Path.Combine(dir, n[4..].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var src = asm.GetManifestResourceStream(name)!;
            using var dst = File.Create(target);
            src.CopyTo(dst);
        }
        return dir;
    }

    private void Emit(string ev, object data)
    {
        var json = JsonSerializer.Serialize(new { @event = ev, data }, J);
        Dispatcher.InvokeAsync(() => Web.CoreWebView2?.PostWebMessageAsJson(json));
    }

    private void Log(string text) => Emit("log", new { text });

    /// <summary>While the game is played or the window is minimized, WebView2 is asked to keep as little memory as it
    /// can (it trims caches); back to normal as soon as the window is shown again. A plain switch to another program
    /// only pauses the page's effects (event "winfocus"), so coming back stays instant.</summary>
    private void WebRest()
    {
        var c = Web.CoreWebView2;
        if (c == null) return;
        bool low = _lastGame || WindowState == WindowState.Minimized;
        try
        {
            var want = low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
            if (c.MemoryUsageTargetLevel != want) c.MemoryUsageTargetLevel = want;
        }
        catch { }   // older WebView2 runtimes do not have it
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        int id = 0;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetInt32();
            var cmd = root.GetProperty("cmd").GetString() ?? "";
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;
            var data = await Handle(cmd, args);
            Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = true, data }, J));
        }
        catch (Exception ex)
        {
            Web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = false, error = ex.Message }, J));
        }
    }

    private static string Str(JsonElement a, string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static string? StrOrNull(JsonElement a, string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Bool(JsonElement a, string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
    private static int Int(JsonElement a, string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : 0;

    // =====================================================================
    //  COMMANDS
    // =====================================================================
    // Without a valid key only these commands work (window, login, updates).
    private static readonly HashSet<string> Unlocked = new()
    {
        "win.drag", "win.min", "win.max", "win.close", "init", "auth.info", "auth.check", "auth.redeem", "auth.logout",
        "update.check", "update.download", "update.install", "open"
    };
    private bool _authed;

    /// <summary>Takes the key's runtime from a server answer ("days", "exp" in Unix seconds; missing = lifetime).</summary>
    private void TakePlan(JsonElement j)
    {
        S.AuthDays = j.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : null;
        S.AuthExp = j.TryGetProperty("exp", out var e) && e.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(e.GetInt64()).UtcDateTime : null;
    }
    private bool KeyExpired => S.AuthExp is DateTime ex && DateTime.UtcNow > ex;
    private object PlanInfo => new { days = S.AuthDays, exp = S.AuthExp is DateTime ex ? new DateTimeOffset(ex, TimeSpan.Zero).ToUnixTimeSeconds() : (long?)null };

    private void OnAuthed(string? role)
    {
        bool first = !_authed;
        // offline grace: three days, but never past the end of the key
        var grace = DateTime.Now.AddDays(3);
        if (S.AuthExp is DateTime ex && ex.ToLocalTime() < grace) grace = ex.ToLocalTime();
        _authed = true; S.AuthRole = role ?? S.AuthRole; S.AuthOkUntil = grace; S.Save();
        if (!first) return;
        SnapApply();
        KfApply();
    }

    private void Lock()
    {
        _authed = false; S.AuthToken = null; S.AuthRole = null; S.AuthOkUntil = null; S.AuthExp = null; S.AuthDays = null; S.Save();
        _snap.Apply(false, false, 0, 0); _autoClip.Enable(false); _killFeed.Enable(false); _killMark.Enable(false); GameTimer.Release(); WlanGame.Restore(); BgThrottle.Restore(); LiveTick();
    }

    // =====================================================================
    //  BUILT-IN RECORDER (kill clipper)
    // =====================================================================
    private bool _recActive;
    private IntPtr _recWnd; private DateTime _recWndAt, _recSizeAt, _recStartedAt; private (int, int) _recSize; private int _recFails;
    /// <summary>Seconds the game window has to exist before the recording may start.</summary>
#if DEBUG
    private static readonly double RecDelay = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORBIT_TEST_GAME")) ? 30 : 3;
#else
    private const double RecDelay = 30;
#endif
    /// <summary>The built-in recorder only runs when something can ask for a clip: kill clipper, fight clips or the clip key.</summary>
    private bool RecArmed => S.Clip.Recorder == "orbit" && (S.Clip.Kills || S.Clip.Auto || S.Clip.Key > 0);

    /// <summary>Keeps the built-in recorder running while the game window is there (only if it is the chosen recorder).</summary>
    private async void RecTick()
    {
        if (_recBusy) return;
        _recBusy = true;
        try
        {
            var hwnd = GameFocus.Window;
            var now = DateTime.Now;
            // FiveM must never be touched while it is still starting up: the recording only begins once the real game
            // window (not the small start window) has been there for half a minute and has kept its size for a few seconds
            bool ready = false;
            if (hwnd != IntPtr.Zero && !GameFocus.Minimized)
            {
                var (l, t, r, b) = GameFocus.Client();
                var size = (r - l, b - t);
                if (hwnd != _recWnd) { _recWnd = hwnd; _recWndAt = now; _recSize = size; _recSizeAt = now; }
                else if (size != _recSize) { _recSize = size; _recSizeAt = now; }
                ready = size.Item1 >= 800 && size.Item2 >= 450 && (now - _recWndAt).TotalSeconds >= RecDelay && (now - _recSizeAt).TotalSeconds >= 5;
            }
            else _recWnd = IntPtr.Zero;
            bool want = _authed && RecArmed && ready;
            // the AI check answers some seconds after a fight: the recording holds that much more
            int hold = S.Clip.Before + S.Clip.After + 30 + (AiOn ? 40 : 0);
            var key = $"{hwnd}|{S.Clip.Quality}|{S.Clip.Sound}|{hold}";
            bool end = _recActive && (!want || key != _recKey || !_rec.Running);
            // A clip that is being written, or a kill that was just noticed and is saved in a moment, still needs what the
            // recording holds: closing the game or tabbing out right after a kill must not cost the clip.
            if (end && (_saving > 0 || _kfPending))
            {
                if (_recHoldAt == default) _recHoldAt = now;
                if ((now - _recHoldAt).TotalSeconds < 30) return;
            }
            _recHoldAt = default;
            if (end)
            {
                // stopped by itself (window resized or closed, encoder trouble): never start again right away,
                // and wait longer each time it happens in a row
                if (want && !_rec.Running)
                {
                    _recFails++;
                    _recError = _rec.Error == "resize" ? null : _rec.Error;
                    _recRetry = now.AddSeconds(Math.Min(120, 8 * _recFails));
                    ClipLog.Add($"Aufnahme hat sich beendet ({_rec.Error ?? "unbekannt"}), neuer Versuch in {Math.Min(120, 8 * _recFails)} s");
                }
                await _rec.Stop();
                _recActive = false;
                if (!want) Log("🎬 Aufnahme beendet");
                if (GameFocus.Window == IntPtr.Zero) RoundEnded();   // the game was closed
            }
            if (want && !_recActive && now >= _recRetry)
            {
                _recKey = key;
                bool ok = await _rec.Start(hwnd, Clipper.Quality(S.Clip.Quality, S.Clip.Sound), hold);
                _recActive = ok;
                _recError = ok ? null : _rec.Error;
                if (ok) _recStartedAt = now;
                else
                {
                    _recFails++;
                    _recRetry = now.AddSeconds(Math.Min(180, 20 * _recFails));
                    Log("🎬 Aufnahme konnte nicht starten: " + _rec.Error);
                    ClipLog.Add($"Aufnahme konnte nicht starten: {_rec.Error}");
                }
            }
            // a recording that has been running fine for a while clears the error count
            if (_recActive && _rec.Running && _recFails > 0 && (now - _recStartedAt).TotalSeconds > 90) _recFails = 0;
        }
        catch (Exception ex) { _recError = ex.Message; _recRetry = DateTime.Now.AddSeconds(20); }
        finally { _recBusy = false; }
    }

    private async Task<object?> Handle(string cmd, JsonElement a)
    {
        if (!_authed && !Unlocked.Contains(cmd)) throw new InvalidOperationException("Gesperrt: Bitte zuerst deinen Key einlösen");
        switch (cmd)
        {
            // ---------- window ----------
            case "win.drag":
                if (WindowState == WindowState.Maximized) return null;
                ReleaseCapture();
                SendMessage(new WindowInteropHelper(this).Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
                return null;
            case "win.min": WindowState = WindowState.Minimized; return null;
            case "win.max": WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; return null;
            case "win.close": Close(); return null;
            case "open": Proc.Open(Str(a, "target")); return null;
            case "clip":
                try { Clipboard.SetText(Str(a, "text")); return new { ok = true }; }
                catch (Exception ex) { return new { ok = false, error = ex.Message }; }

            // ---------- init / settings ----------
            case "init":
                var sys = await Task.Run(() => new { cpu = SysInfo.CpuName, gpu = SysInfo.GpuName, os = SysInfo.OsName, ramGb = Math.Round(Native.Memory().total / 1073741824.0) });
                Native.CpuUsage();
                if (!_timer.IsEnabled) { _timer.Tick += Timer_Tick; _timer.Start(); }
                WebRest();   // the window may have been minimized or the game in front before the page was ready
                // focus and game state as they are now: changes before this moment were sent before the page listened
                return new { user = Environment.UserName, admin = Proc.IsAdmin(), version = Updater.Current.ToString(3), sys, fivem = FiveMInfo(), settings = S, focused = IsActive, game = _lastGame };
            case "settings.set":
                var key = Str(a, "key");
                var val = a.GetProperty("value");
                switch (key)
                {
                    case "accent": S.Accent = val.GetString() ?? "signal"; break;
                    case "mode": S.Mode = val.GetString() ?? "dark"; Web.DefaultBackgroundColor = S.Mode == "light" ? System.Drawing.Color.FromArgb(255, 239, 237, 231) : System.Drawing.Color.FromArgb(255, 18, 18, 17); break;
                    case "autoUpdate": S.AutoUpdate = val.GetBoolean(); break;
                    case "autoInstall": S.AutoInstall = val.GetBoolean(); break;
                    case "restoreBeforeBoost": S.RestoreBeforeBoost = val.GetBoolean(); break;
                    case "purgeStandby": S.PurgeStandby = val.GetBoolean(); break;
                    case "firstRun": S.FirstRun = val.GetBoolean(); break;
                    case "xhairAck": S.XhairAck = val.GetBoolean(); break;
                    case "autoBorderless": S.AutoBorderless = val.GetBoolean(); break;
                    case "f8Auto": S.F8Auto = val.GetBoolean(); break;
                    case "xhairShrink": S.XhairShrink = val.GetBoolean(); break;
                    case "modsAck": S.ModsAck = val.GetBoolean(); break;
                    case "modSoundGta": S.ModSoundGta = val.GetBoolean(); break;
                    case "bgPreset": S.BgPreset = val.GetString(); break;
                    case "fx": S.Fx = val.GetBoolean(); break;
                    case "snapTap": S.SnapTap = val.GetBoolean(); SnapApply(); Log(S.SnapTap ? "⌨ Snap Tap aktiv (nur im Spiel)" : "⌨ Snap Tap aus"); break;
                    case "launch": S.Launch = val.Deserialize<LaunchCfg>(JIn) ?? new LaunchCfg(); break;
                    case "discordOn": S.DiscordOn = val.GetBoolean(); DiscordTick(); break;
                    case "discordServer": S.DiscordServer = val.GetBoolean(); DiscordTick(); break;
                    case "discordKills": S.DiscordKills = val.GetBoolean(); DiscordTick(); break;
                    case "discordAppId":
                    {
                        // the Application ID is a Discord snowflake: 17 to 20 digits (a pasted line around it is fine)
                        var raw = val.GetString()?.Trim() ?? "";
                        var m = System.Text.RegularExpressions.Regex.Match(raw, @"(?<!\d)\d{17,20}(?!\d)");
                        if (raw.Length > 0 && !m.Success) throw new InvalidOperationException("Das ist keine Application ID (17 bis 20 Ziffern)");
                        if (m.Success && S.DiscordAppId.Length == 0) S.DiscordOn = true;   // on by default once there is an ID
                        S.DiscordAppId = m.Success ? m.Value : "";
                        DiscordTick();
                        break;
                    }
                }
                S.Save();
                return null;
            case "discord.state": return DiscordState();

            // ---------- tweaks ----------
            case "tweaks.list": return await TweakList();
            case "tweak.set":
            {
                var t = _tweaks.FirstOrDefault(x => x.Id == Str(a, "id"));
                if (t == null) return new { applied = false, error = "Diesen Tweak gibt es nicht mehr" };
                var on = Bool(a, "on");
                string? err = null;
                await Task.Run(() =>
                {
                    TweakRun.Explicit = true;
                    try { if (on) t.Apply(); else { t.Revert(); Marks.Clear(t.Id); } }
                    catch (Exception ex) { err = ex.Message; }
                    finally { TweakRun.Explicit = false; }
                });
                var applied = await Task.Run(t.SafeCheck);
                if (on && err == null && applied) Marks.Set(t.Id);
                if (t.Id == "winkey") SnapApply();
                Log(err != null ? $"✖ {t.Name}: {err}" : $"{(on ? "✔" : "↺")} {t.Name}");
                return new { applied, error = err };
            }
            case "tweaks.revertAll":
            {
                var left = await Task.Run(() =>
                {
                    // only what OrbitTweak changed: a state that is Windows' default or the user's own choice stays
                    TweakRun.All = true;
                    try
                    {
                        foreach (var t in _tweaks)
                            if (t.SafeCheck() && (t.IsOwned || Marks.Has(t.Id))) try { t.Revert(); Marks.Clear(t.Id); } catch { }
                        // removed tweaks match by value only and may be the user's or another tool's: just the ones under our own flag
                        foreach (var l in Catalog.Old) if (l.Own && l.SafeCheck()) try { l.Revert(); } catch { }
                        // a tweak that is off now was not switched on by OrbitTweak any more (e.g. USB power saving that went
                        // with the power plan): its mark must not make a later revert act on it
                        foreach (var t in _tweaks) if (!t.SafeCheck()) Marks.Clear(t.Id);
                    }
                    finally { TweakRun.All = false; }
                    // removed tweaks that are still active (not ours, or "Speicher-Komprimierung" while SysMain is off): the page says so
                    return Catalog.Old.Where(l => l.SafeCheck()).Select(l => l.Name).ToList();
                });
                SnapApply();
                Log("↺ Alle Tweaks zurückgesetzt");
                return new { left };
            }
            case "tweaks.legacy":
            {
                // removed tweaks that are still active, and (once after the update) what was undone by itself
                var mig = _migrated != null ? await _migrated : null;
                var items = await Task.Run(() => Catalog.Old.Where(l => l.SafeCheck()).Select(l => new { id = l.Id, name = l.Name, sum = l.Sum, desc = l.Desc, hint = l.Hint }).ToList());
                object? note = null;
                if (S.TweaksMigrated && mig != null && (mig.Undone.Count > 0 || items.Count > 0))
                    note = new { undone = mig.Undone, reboot = mig.Reboot, active = items.Count };
                S.TweaksMigrated = false;
                return new { items, note };
            }
            case "tweak.legacyRevert":
            {
                var l = Catalog.Old.FirstOrDefault(x => x.Id == Str(a, "id"));
                if (l == null) return new { ok = false, error = "Unbekannt" };
                string? err = null;
                await Task.Run(() => { try { l.Revert(); } catch (Exception ex) { err = ex.Message; } });
                bool ok = err == null && !await Task.Run(l.SafeCheck);
                Log(ok ? $"↺ {l.Name}: zurückgesetzt" : $"✖ {l.Name}: {err ?? "nicht zurückgesetzt"}");
                return new { ok, error = ok ? null : err ?? "Nicht zurückgesetzt" };
            }
            case "boost": return await Boost();

            // ---------- WASD tweak: "Laggen" (fast key repeat) and the size of the Windows keyboard buffer ----------
            case "wasd.get": return WasdState();
            case "wasd.chop":
            {
                // "Laggen": held movement keys are let go for a moment again and again while the game is in front
                S.Lag.On = Bool(a, "on");
                if (a.TryGetProperty("gap", out _) && Int(a, "gap") is >= 10 and <= 250 and var gap) S.Lag.Gap = gap;
                if (a.TryGetProperty("every", out _) && Int(a, "every") is >= 40 and <= 500 and var every) S.Lag.Every = Math.Max(every, S.Lag.Gap + 15);
                if (a.TryGetProperty("key", out var kv) && kv.ValueKind == JsonValueKind.Number && kv.GetInt32() is var lagKey and (0 or (>= 0x21 and <= 0x7B))) S.Lag.Key = lagKey;
                S.Save(); SnapApply(); RegisterKeys();
                Log(S.Lag.On ? $"⌨ WASD-Laggen an ({S.Lag.Gap} ms Aussetzer alle {S.Lag.Every} ms, nur im Spiel)" : "⌨ WASD-Laggen aus");
                return WasdState();
            }
            case "wasd.flash":
            {
                // the two small pictures shown in the game when the key switches "Laggen" on or off (drawn by the page)
                try { _lagPngOn = Convert.FromBase64String(Str(a, "on")); _lagPngOff = Convert.FromBase64String(Str(a, "off")); } catch { }
                return null;
            }
            case "wasd.lag":
            {
                // fast key repeat of the Windows keyboard filter (part of some WASD packs)
                bool on = Bool(a, "on");
                bool ok = await Task.Run(() => { try { return WasdTweak.SetLag(on); } catch { return false; } });
                if (ok) Log(on ? $"⌨ Schnelle Tastenwiederholung an (alle {WasdTweak.LagRepeat} ms)" : "⌨ Schnelle Tastenwiederholung aus");
                return WasdState(ok ? null : "Windows hat die Einstellung nicht übernommen");
            }
            case "wasd.set":
            {
                int size = Int(a, "size");
                if (size != 0 && !WasdTweak.Levels.Contains(size)) return WasdState("Unbekannte Stufe");
                string? err = null;
                await Task.Run(() => { try { WasdTweak.SetBuffer(size); } catch (Exception ex) { err = ex.Message; } });
                if (err == null)
                {
                    S.WasdSetAt = DateTime.Now; S.Save();
                    Log(size == 0 ? "⌨ Tastatur-Puffer: Windows-Standard (wirkt nach Neustart)" : $"⌨ Tastatur-Puffer {size} (wirkt nach Neustart)");
                }
                return WasdState(err);
            }

            // ---------- fivem ----------
            case "fivem.cacheSize":
                return new { found = FiveM.Root != null, size = FiveM.Root != null ? await Task.Run(() => Cleaner.Measure(Target("fm"))) : 0 };
            case "fivem.cache":
                if (FiveM.Root == null) return new { error = "FiveM nicht gefunden" };
                if (FiveM.IsRunning) return new { error = "Bitte FiveM zuerst schließen" };
                var cf = await Task.Run(() => Cleaner.Clean(Target("fm")));
                Log($"🧹 FiveM-Cache geleert: {SysInfo.Bytes(cf)}");
                return new { freed = cf };
            case "fivem.kill":
            {
                int n = 0;
                foreach (var p in FiveM.Processes()) { try { p.Kill(true); n++; } catch { } finally { p.Dispose(); } }
                if (n > 0) Log($"✖ FiveM beendet ({n} Prozesse)");
                return new { count = n };
            }
            case "fivem.folder": if ((FiveM.AppDir ?? FiveM.Root) is { } fd && Directory.Exists(fd)) Proc.Open(fd); return null;
            case "fivem.crashes": if (FiveM.AppDir is { } ad) { var cd = Path.Combine(ad, "crashes"); Proc.Open(Directory.Exists(cd) ? cd : ad); } return null;
            case "fivem.pick":
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "FiveM.exe auswählen", Filter = "FiveM|FiveM.exe", FileName = "FiveM.exe" };
                if (dlg.ShowDialog(this) == true) { S.FiveMPath = Path.GetDirectoryName(dlg.FileName); S.Save(); }
                return FiveMInfo();
            }
            case "fivem.launch": return await Launch(a);
            case "ini.get": return new { channel = CitizenIni.Read("UpdateChannel") ?? "production" };
            case "ini.set":
                CitizenIni.Write("UpdateChannel", Str(a, "channel"));
                Log($"Update-Kanal → {Str(a, "channel")}");
                return null;

            // ---------- servers ----------
            case "servers.list":
            {
                var force = Bool(a, "force");
                return await Task.WhenAll(S.Favorites.Select(f => Servers.Fetch(f, force)));
            }
            case "servers.add":
            {
                var k = Servers.Parse(Str(a, "q"));
                var data = await Servers.Fetch(k, true);
                if (!S.Favorites.Contains(k)) { S.Favorites.Add(k); S.Save(); }
                return data;
            }
            case "servers.remove": S.Favorites.Remove(Str(a, "code")); S.Save(); return null;
            case "server.current":
            {
                var (url, at) = Servers.LastServer();
                if (string.IsNullOrWhiteSpace(url)) return new { found = false };
                string skey;
                try { skey = Servers.Parse(url); } catch { return new { found = false }; }
                bool connected = false;
                try
                {
                    using var gp = FiveM.GameProcess();
                    connected = gp != null && at >= gp.StartTime.AddSeconds(-5);
                }
                catch { }
                var data = await Servers.Fetch(skey, Bool(a, "force"));
                return new { found = true, connected, key = skey, url, at, fav = S.Favorites.Contains(skey), server = data };
            }
            case "cfx.status": return await Net.CfxStatus();
            case "ping": return await Net.CfxPing();

            // ---------- graphics ----------
            case "gfx.get": return await Task.Run(Gfx.Get);
            case "gfx.save":
                try { var (kind, path) = Gfx.Save(a.GetProperty("values")); Log($"🎨 Grafik-Einstellungen gespeichert ({Path.GetFileName(path)})"); return new { ok = true, kind, path }; }
                catch (Exception ex) { return new { error = ex.Message }; }
            case "gfx.restore":
                try
                {
                    var (kind, path, old) = Gfx.Restore();
                    Log($"↺ Grafik-Backup wiederhergestellt ({Path.GetFileName(path)}){(old && kind != "gtaOld" ? ", dazu die alten GTA-V-Einstellungen" : "")}");
                    return new { ok = true, kind, path, old };
                }
                catch (Exception ex) { return new { error = ex.Message }; }
            // FPS limit in the NVIDIA driver profile of FiveM's game processes (Core/FpsTex.cs)
            case "fps.get": return await Task.Run(FpsLimit.Get);
            case "fps.set":
            {
                if (!(a.ValueKind == JsonValueKind.Object && a.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number)) return new { error = "Kein Wert angegeben" };
                int limit = Int(a, "limit");
                var (ok, err, stayed) = await Task.Run(() => FpsLimit.SetLimit(limit));
                if (!ok) return new { error = err };
                Log(limit != 0 ? $"🎯 FPS-Limit für FiveM: {limit} FPS ab dem nächsten Start"
                    : stayed.Count == 0 ? "↺ FPS-Limit für FiveM aus, Treiberprofil wie vorher"
                    : $"↺ FPS-Limit für FiveM aus; außerhalb von OrbitTweak geändert und so gelassen: {string.Join(", ", stayed.Select(x => $"„{x.profile}“ ({(x.limit > 0 ? x.limit + " FPS" : "kein Limit")})"))}");
                return new { ok = true, limit, stayed = stayed.Select(x => new { x.profile, x.limit }).ToArray() };
            }
            // texture budget: vid_budgetScale in FiveM's fivem.cfg
            case "tex.get": return await Task.Run(TexBudget.Get);
            case "tex.set":
            {
                bool restore = Bool(a, "restore");
                if (!restore && !(a.ValueKind == JsonValueKind.Object && a.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)) return new { error = "Kein Wert angegeben" };
                var (value, err) = await Task.Run(() => TexBudget.Set(Int(a, "value"), restore));
                if (err != null) return new { error = err };
                Log(restore ? $"↺ Texturbudget wie vorher ({TexBudgetLogic.Percent(value)} %)" : $"🧱 Texturbudget {TexBudgetLogic.Percent(value)} % (vid_budgetScale {value}) ab dem nächsten Start");
                return new { ok = true, value };
            }

            // ---------- crosshair ----------
            case "xhair.set":
            {
                var x = S.Xhair;
                var wasOn = x.On;
                x.On = Bool(a, "on"); x.OnlyGame = Bool(a, "onlyGame"); x.Key = Int(a, "key");
                x.OffsetX = Math.Clamp(Int(a, "offsetX"), -400, 400); x.OffsetY = Math.Clamp(Int(a, "offsetY"), -400, 400);
                var json = Str(a, "json"); if (json.Length > 0) x.Json = json;
                var png = Str(a, "png");
                if (png.Length > 0) _xhair.SetImage(Convert.FromBase64String(png), x.OffsetX, x.OffsetY);
                else _xhair.SetOffset(x.OffsetX, x.OffsetY);
                S.Save();
                var keys = RegisterKeys();
                LiveTick();
                if (!wasOn && x.On) ShrinkReticle();
                return new { keyOk = keys.xhair };
            }

            // ---------- minimap frame ----------
            case "minimap.preview": _mmPreview = Bool(a, "on"); LiveTick(); return null;
            case "minimap.set":
            {
                var m = S.Minimap;
                m.On = Bool(a, "on"); m.X = Int(a, "x"); m.Y = Int(a, "y");
                var json = Str(a, "json"); if (json.Length > 0) m.Json = json;
                var png = Str(a, "png");
                if (png.Length > 0) _minimap.SetImage(Convert.FromBase64String(png), m.X, m.Y); else _minimap.SetOffset(m.X, m.Y);
                S.Save(); LiveTick();
                return new { ok = true, client = GameFocus.Client() is var c ? new { w = c.r - c.l, h = c.b - c.t } : null };
            }

            // ---------- clipper ----------
            case "clip.cfg":
            {
                S.Clip.Key = Int(a, "key"); S.Clip.Flash = Bool(a, "flash");
                S.Clip.Auto = Bool(a, "auto"); var sens = Int(a, "sens"); S.Clip.Sens = sens is >= 1 and <= 3 ? sens : 2;
                if (a.TryGetProperty("net", out var nv) && nv.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.Net = nv.GetBoolean();
                S.Clip.Kills = Bool(a, "kills"); S.Clip.Deaths = Bool(a, "deaths");
                S.Clip.Name = Str(a, "name").Trim(); if (S.Clip.Name.Length > 40) S.Clip.Name = S.Clip.Name[..40];
                var area = Str(a, "area"); if (area == "auto" || KillFeed.Areas.ContainsKey(area)) S.Clip.Area = area;
                if (Str(a, "detect") is "mark" or "feed") S.Clip.Detect = Str(a, "detect");
                if (a.TryGetProperty("ai", out var aiv) && aiv.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.Ai = aiv.GetBoolean();
                if (a.TryGetProperty("hud", out var hv) && hv.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.Hud = hv.GetBoolean();
                if (a.TryGetProperty("hudClock", out var hc) && hc.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.HudClock = hc.GetBoolean();
                if (a.TryGetProperty("hudTime", out var ht) && ht.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.HudTime = ht.GetBoolean();
                if (a.TryGetProperty("banner", out var bn) && bn.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.Banner = bn.GetBoolean();
                if (a.TryGetProperty("autoHighlight", out var ah) && ah.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.AutoHighlight = ah.GetBoolean();
                if (a.TryGetProperty("highlightTail", out _) && Int(a, "highlightTail") is 0 or 8) S.Clip.HighlightTail = Int(a, "highlightTail");
                if (a.TryGetProperty("shareMb", out _) && Int(a, "shareMb") is 10 or 50 or 500) S.Clip.ShareMb = Int(a, "shareMb");
                MontageOpts(a);
                // the accent colour of the page, for what is drawn here (kill note)
                if (Str(a, "accent") is { Length: 7 } hex && hex[0] == '#') _accentHex = hex;
                if (a.TryGetProperty("words", out var wv) && wv.ValueKind == JsonValueKind.String)
                {
                    var words = string.Join(", ", (wv.GetString() ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length is >= 3 and <= 24).Take(12));
                    S.Clip.Words = words.Length > 0 ? words : KillText.DefaultWords;
                }
                KfApply();
                var rec = Str(a, "recorder"); if (Clipper.Recorders.ContainsKey(rec)) S.Clip.Recorder = rec;
                // built-in recorder
                if (a.TryGetProperty("before", out _) && Int(a, "before") is >= 5 and <= 60 and var bf) S.Clip.Before = bf;
                if (a.TryGetProperty("after", out _) && Int(a, "after") is >= 0 and <= 15 and var af) S.Clip.After = af;
                if (Str(a, "quality") is "low" or "std" or "high") S.Clip.Quality = Str(a, "quality");
                if (a.TryGetProperty("sound", out var snd) && snd.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.Sound = snd.GetBoolean();
                var png = Str(a, "png"); if (png.Length > 0) _flash.SetImage(Convert.FromBase64String(png), 0, 40);
                S.Save();
                var keys = RegisterKeys();
                RecTick();
                return new { keyOk = keys.clip, combo = Clipper.Recorders[S.Clip.Recorder].label, words = S.Clip.Words };
            }
            case "clip.save": { var err = await SaveClip(); return new { ok = err == null, error = err }; }
            case "clip.status":
                return new
                {
                    recorder = S.Clip.Recorder, running = _rec.Running, buffered = (int)_rec.Buffered, w = _rec.Size.w, h = _rec.Size.h,
                    error = _recError, game = GameFocus.Window != IntPtr.Zero, supported = ReplayRecorder.Supported, found = _killFeed.Found, armed = RecArmed,
                    detect = S.Clip.Detect, markAt = _killMark.LastAt == default ? (DateTime?)null : _killMark.LastAt, markImg = _killMark.LastImage, markWord = _killMark.LastWord,
                    kills = _statKills, fights = _statFights, net = NetOn, netQuiet = NetOn && NetQuiet, words = S.Clip.Words,
                    ai = new { on = S.Clip.Ai, key = AiKey.Get() != null, active = AiOn, asked = _aiAsked, kills = _aiKills, last = _aiLast, error = _aiError, paused = DateTime.Now < _aiQuotaUntil }
                };
            case "clip.aikey":
            {
                // the OpenRouter key for the AI check: empty removes it. It is never sent back to the page.
                var k = Str(a, "key").Trim();
                if (k.Length > 0 && (!k.StartsWith("sk-or-") || k.Length < 30 || k.Length > 200)) return new { ok = false, error = "Das sieht nicht nach einem OpenRouter-Key aus (beginnt mit sk-or-)" };
                AiKey.Set(k); _aiError = null; _aiQuotaUntil = default;
                KfApply(); RecTick();
                return new { ok = true, key = AiKey.Get() != null };
            }
            case "clip.log": Proc.Open(ClipLog.File); return null;
            case "clip.kftest":
            {
                // reads the kill-feed area once and returns what the text recognition sees
                try
                {
                    var (area, lines, img) = await _killFeed.Test(Str(a, "name").Trim(), Str(a, "area"));
                    var res = new { ok = true, img, area, game = GameFocus.Pid != 0, lines = (lines ?? new()).Select(l => new { l.text, hit = KillFeed.Classify(l, _killFeed.Name) is bool k ? (k ? "kill" : "death") : null }).ToList() };
                    KfApply();
                    return res;
                }
                catch (Exception ex) { KfApply(); return new { ok = false, error = ex.Message }; }
            }

            case "clip.marktest":
            {
                // reads the strip around the crosshair once, all three ways. A running game gets a few seconds to be put in front.
                try
                {
                    bool running = GameFocus.Window != IntPtr.Zero;
                    if (!running)
                        foreach (var p in Process.GetProcesses())
                        {
                            try { running |= p.ProcessName.Contains("GTAProcess", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("GTA5", StringComparison.OrdinalIgnoreCase); } catch { }
                            p.Dispose();
                        }
                    var until = DateTime.Now.AddSeconds(Math.Clamp(Int(a, "wait"), 0, 10));
                    bool front = GameFocus.IsGame();
                    while (running && !front && DateTime.Now < until) { await Task.Delay(100); front = GameFocus.IsGame(); }
                    if (front) await Task.Delay(400);   // the picture settles after switching
                    var r = await Task.Run(() => _killMark.Test());
                    static string? Hit(KillText.Kind k) => k == KillText.Kind.Kill ? "kill" : k == KillText.Kind.Death ? "death" : null;
                    return new { ok = true, game = running, front, img = r.image, kind = Hit(r.kind), text = r.text, word = r.word, how = r.how, lines = r.lines.Select(l => new { l.text, hit = Hit(l.hit) }).ToList() };
                }
                catch (Exception ex) { return new { ok = false, error = ex.Message }; }
            }

            // ---------- custom minimap from a picture ----------
            case "minimap.pick":
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Bild für die Minimap auswählen", Filter = "Bilder (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
                if (dlg.ShowDialog(this) != true) return new { cancelled = true };
                var file = dlg.FileName;
                return await Task.Run(() => MinimapBuilder.Info(file));
            }
            case "minimap.info": { var file = Str(a, "path"); return await Task.Run(() => MinimapBuilder.Info(file)); }
            case "minimap.status": return new { status = await Task.Run(MinimapBuilder.Status), cfg = S.MinimapMod };
            case "minimap.build":
            {
                var cfg = a.GetProperty("cfg").Deserialize<MinimapModCfg>(JIn) ?? new MinimapModCfg();
                cfg.Repeat = Math.Clamp(cfg.Repeat, 1, 120); cfg.Dim = Math.Clamp(cfg.Dim, 0, 80);
                var (size, err) = await Task.Run(() => MinimapBuilder.Install(cfg, (pct, text) => Emit("mmbuild", new { pct, text })));
                if (err != null) return new { error = err };
                S.MinimapMod = cfg; S.Save();
                Log($"🗺 Eigene Minimap installiert ({SysInfo.Bytes(size)})");
                return new { size };
            }
            case "minimap.remove":
            {
                var err = Mods.Delete(MinimapBuilder.FileName);
                if (err == null) Log("🗺 Eigene Minimap entfernt");
                return new { error = err };
            }
            case "clip.list": return await Task.Run(Clipper.List);
            case "clip.recycle":
            {
                // moves clips of the built-in recorder to the recycle bin (only files in Videos\OrbitTweak, nothing else)
                var rels = a.TryGetProperty("rels", out var rv) && rv.ValueKind == JsonValueKind.Array ? rv.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
                var (gone, failed) = await Task.Run(() => Clipper.Recycle(rels));
                if (gone > 0) { ClipLog.Add($"{gone} Clip(s) in den Papierkorb verschoben"); Log($"🗑 {gone} Clip(s) in den Papierkorb verschoben"); }
                return new { deleted = gone, failed };
            }
            // ---------- highlight video: the kill clips joined into one ----------
            case "clip.kills": return await Task.Run(() => new { today = Highlight.KillClips(true).Count, all = Highlight.KillClips(false).Count, busy = _hlBusy });
            case "clip.montageMusic":
            {
                // picks (or removes) the music that is laid under montages
                if (Bool(a, "clear")) { S.Clip.MontageMusic = null; S.Save(); return new { music = (string?)null }; }
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Musik für die Montage auswählen", Filter = "Musik (*.mp3;*.m4a;*.wav;*.aac;*.wma)|*.mp3;*.m4a;*.wav;*.aac;*.wma" };
                if (dlg.ShowDialog(this) == true) { S.Clip.MontageMusic = dlg.FileName; S.Save(); }
                return new { music = S.Clip.MontageMusic is { } mf && File.Exists(mf) ? Path.GetFileName(mf) : null };
            }
            case "clip.ffmpeg":
            {
                // loads the cutting program for slow motion once
                if (FfMontage.Find() != null) return new { ok = true };
                var e2 = await FfMontage.Download((pct, text) => Emit("highlight", new { pct, text }));
                if (e2 == null) Log("🎬 Schnittprogramm für Zeitlupe geladen");
                return new { ok = e2 == null, error = e2 };
            }            case "clip.montage":
            {
                if (_hlBusy) return new { error = "Es wird gerade schon ein Video erstellt" };
                _hlBusy = true;
                try
                {
                    var music = S.Clip.MontageMusic is { } mf && File.Exists(mf) ? mf : null;
                    // the cut is done by ffmpeg (slow motion needs it, so it is loaded for that); without slow motion and
                    // without ffmpeg, Windows' own video machinery makes a plainer montage
                    bool slow = !a.TryGetProperty("slowmo", out var sm) || sm.ValueKind != JsonValueKind.False;
                    string? file, err; int n; double bpm = 0;
                    var ff = FfMontage.Find();
                    if (slow && ff == null) return new { needFfmpeg = true };
                    if (ff != null)
                    {
                        var all = Highlight.KillClips(false);
                        if (all.Count < 2) return new { error = "Dafür braucht es mindestens zwei Kill-Clips" };
                        var pick = all.Skip(Math.Max(0, all.Count - Math.Clamp(Int(a, "count"), 2, 30))).ToList();
                        // optional, kept for next time: the big line of the intro, intro/outro on or off, the kill counter on or off
                        MontageOpts(a, "title", "intro", "counter");
                        S.Save();
                        var title = S.Clip.MontageTitle.Length > 0 ? S.Clip.MontageTitle : S.Clip.Name;
                        (file, err, n, bpm) = await FfMontage.Make(ff, pick, S.Clip.After, music, _accentHex, (pct, text) => Emit("highlight", new { pct, text }), title, S.Clip.MontageIntro, S.Clip.MontageCounter, slow);
                    }
                    else (file, err, n) = await Highlight.MakeMontage(Int(a, "count"), music, _accentHex, (pct, text) => Emit("highlight", new { pct, text }), S.Clip.After);
                    if (file == null) { ClipLog.Add("Montage nicht erstellt: " + err); return new { error = err }; }
                    ClipLog.Add($"Montage aus {n} Kill-Clips: {Path.GetFileName(file)}" + (bpm > 0 ? $" (Kills im Takt, {bpm:0} BPM)" : music != null && ff != null ? " (kein klarer Takt in der Musik)" : ""));
                    Log($"🎬 Montage aus {n} Clips → {Path.GetFileName(file)}");
                    return new { ok = true, file = Path.GetFileName(file), clips = n, music = music != null, slowmo = slow, bpm = Math.Round(bpm) };
                }
                finally { _hlBusy = false; }
            }            case "clip.highlight":
            {
                if (_hlBusy) return new { error = "Es wird gerade schon ein Highlight-Video erstellt" };
                _hlBusy = true;
                try
                {
                    bool today = Str(a, "scope") != "all";
                    var (file, err, n) = await Highlight.Make(today, S.Clip.HighlightTail, (pct, text) => Emit("highlight", new { pct, text }), S.Clip.After);
                    if (file != null)
                    {
                        ClipLog.Add($"Highlight-Video aus {n} Kill-Clips: {Path.GetFileName(file)}");
                        Log($"🎬 Highlight-Video aus {n} Clips → {Path.GetFileName(file)}");
                    }
                    else ClipLog.Add($"Highlight-Video nicht erstellt: {err}");
                    return new { file = file != null ? Path.GetFileName(file) : null, error = err, clips = n };
                }
                finally { _hlBusy = false; }
            }
            case "clip.share":
            {
                // "share for Discord": the clip (made smaller first if it is over the limit) is put on the clipboard as a file
                var src = Clipper.Resolve(Str(a, "rel"));
                if (src == null) return new { error = "Clip nicht gefunden" };
                if (_shareBusy) return new { error = "Es wird gerade schon ein Clip vorbereitet" };
                _shareBusy = true;
                try
                {
                    long max = S.Clip.ShareMb * 1024L * 1024L;
                    string target = src; bool shrunk = false;
                    if (new FileInfo(src).Length > max)
                    {
                        var (small, err) = await Highlight.Shrink(src, max, (pct, text) => Emit("share", new { pct, text }));
                        if (small == null) return new { error = err };
                        target = small; shrunk = true;
                    }
                    bool onClipboard = false;
                    if (!a.TryGetProperty("clipboard", out var cbv) || cbv.ValueKind != JsonValueKind.False)
                    {
                        var list = new System.Collections.Specialized.StringCollection { target };
                        for (int i = 0; i < 6 && !onClipboard; i++)
                        {
                            try { Clipboard.SetFileDropList(list); onClipboard = true; }
                            catch { await Task.Delay(90); } // another program has the clipboard open for a moment
                        }
                    }
                    Log($"📋 Clip zum Teilen bereit: {Path.GetFileName(target)} ({SysInfo.Bytes(new FileInfo(target).Length)})");
                    return new { ok = true, name = Path.GetFileName(target), size = new FileInfo(target).Length, shrunk, clipboard = onClipboard, path = target };
                }
                finally { _shareBusy = false; }
            }
            case "clip.cut":
            {
                // cuts a part out of a clip: saved as a new clip, or as a small copy on the clipboard for sharing
                var src = Clipper.Resolve(Str(a, "rel"));
                if (src == null || !src.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return new { error = "Clip nicht gefunden" };
                if (_shareBusy) return new { error = "Es wird gerade ein anderer Clip bearbeitet" };
                double from = a.TryGetProperty("from", out var fv) ? fv.GetDouble() : 0, to = a.TryGetProperty("to", out var tv) ? tv.GetDouble() : 0;
                if (to - from < 0.5) return new { error = "Der Ausschnitt ist zu kurz" };
                bool share = Bool(a, "share");
                _shareBusy = true;
                try
                {
                    var (file, err) = await Highlight.Cut(src, from, to, share ? S.Clip.ShareMb * 1024L * 1024L : 0, (pct, text) => Emit("share", new { pct, text }));
                    if (file == null) return new { error = err };
                    bool onClipboard = false;
                    if (share)
                    {
                        var list = new System.Collections.Specialized.StringCollection { file };
                        for (int i = 0; i < 6 && !onClipboard; i++)
                        {
                            try { Clipboard.SetFileDropList(list); onClipboard = true; }
                            catch { await Task.Delay(90); }
                        }
                    }
                    Log($"✂ Clip geschnitten: {Path.GetFileName(file)} ({to - from:0.0} s, {SysInfo.Bytes(new FileInfo(file).Length)})");
                    return new { ok = true, name = Path.GetFileName(file), size = new FileInfo(file).Length, clipboard = onClipboard, share };
                }
                finally { _shareBusy = false; }
            }            case "clip.open":
            {
                var f = Clipper.Resolve(Str(a, "rel"));
                if (f != null) Process.Start("explorer.exe", $"/select,\"{f}\"");
                else Proc.Open(Clipper.VideosDir);
                return null;
            }
            case "clip.delete":
            {
                var f = Clipper.Resolve(Str(a, "rel"));
                if (f == null) return new { ok = false };
                // a clip that was just playing in the page may be held open for a moment after the player let go of it
                for (int tries = 0; ; tries++)
                {
                    try { File.Delete(f); Log("🗑 Clip gelöscht: " + Path.GetFileName(f)); return new { ok = true }; }
                    catch (IOException) when (tries < 5) { await Task.Delay(300); }
                    catch (UnauthorizedAccessException) when (tries < 5) { await Task.Delay(300); }
                    catch (Exception ex) { return new { ok = false, error = ex.Message }; }
                }
            }
            case "clip.gamebar":
                Clipper.EnableGameBarCapture();
                Log("🎬 Game-Bar-Aufnahme im Hintergrund aktiviert");
                return new { ok = true };

            // ---------- mods ----------
            case "mods.list": return await Task.Run(Mods.List);
            case "mods.install":
            {
                // "files": the same files again, after the user chose one of several versions in an archive
                var files = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("files", out var fv) && fv.ValueKind == JsonValueKind.Array
                    ? fv.EnumerateArray().Select(x => x.GetString() ?? "").Where(File.Exists).ToArray() : Array.Empty<string>();
                if (files.Length == 0)
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Mod auswählen", Filter = "FiveM-Mods (*.rpf;*.zip;*.rar;*.7z;*.oiv)|*.rpf;*.zip;*.rar;*.7z;*.oiv", Multiselect = true };
                    if (dlg.ShowDialog(this) != true) return new { count = 0, cancelled = true };
                    files = dlg.FileNames;
                }
                var o = new InstallOpts { Cat = Str(a, "cat"), Variant = StrOrNull(a, "variant"), SoundToGta = S.ModSoundGta };
                var r = await Task.Run(() => Mods.Install(files, o));
                if (r.Mods.Count > 0) Log($"🧩 {r.Mods.Count} Mod(s) installiert");
                return r.Ui(files);
            }
            case "mods.toggle":
            {
                var mk = Str(a, "id") is { Length: > 0 } id ? id : Str(a, "name");
                var (err, off) = await Task.Run(() => Mods.Toggle(mk, Bool(a, "on")));
                if (err == null) Log($"🧩 {(Str(a, "title") is { Length: > 0 } t ? t : mk)} {(Bool(a, "on") ? "aktiviert" : "deaktiviert")}");
                return new { error = err, off };
            }
            case "mods.delete":
            {
                var mk = Str(a, "id") is { Length: > 0 } id ? id : Str(a, "name");
                var err = await Task.Run(() => Mods.Delete(mk));
                if (err == null) Log($"🗑 Mod entfernt: {(Str(a, "title") is { Length: > 0 } t ? t : mk)}");
                return new { error = err };
            }
            case "mods.repair":
            {
                var r = await Task.Run(() => Mods.Repair(Str(a, "id"), S.ModSoundGta));
                if (r.Error == null) Log($"🧩 Mod repariert: {r.Mods.FirstOrDefault()?.Title}");
                return r.Ui();
            }
            case "mods.downloads": return await Task.Run(ModDownloads.List);
            case "mods.installDl":
            {
                var file = Path.Combine(ModDownloads.Dir, Path.GetFileName(Str(a, "name")));
                var o = new InstallOpts { Cat = Str(a, "cat"), Variant = StrOrNull(a, "variant"), SoundToGta = S.ModSoundGta };
                var r = await Task.Run(() => Mods.Install(new[] { file }, o));
                if (r.Mods.Count > 0) Log($"🧩 Mod aus Downloads installiert: {r.Mods[0].Title}");
                return r.Ui();
            }
            case "downloads.open": Proc.Open(ModDownloads.Dir); return null;

            // ---------- reshade ----------
            case "reshade.status": return await Task.Run(ReShade.Status);
            case "reshade.install":
                try
                {
                    var err = await ReShade.Install(Str(a, "preset"), (pct, text) => Emit("reshade", new { pct, text }));
                    if (err == null) Log("🎨 ReShade installiert");
                    return new { error = err };
                }
                catch (Exception ex) { return new { error = "Download fehlgeschlagen: " + ex.Message }; }
            case "reshade.uninstall":
            {
                var err = await Task.Run(ReShade.Uninstall);
                if (err == null) Log("🎨 ReShade entfernt");
                return new { error = err };
            }
            // ---------- account (access code + HWID) ----------
            case "auth.info": return new { hwid = Account.Hwid[..12], role = S.AuthRole, loggedIn = _authed, plan = PlanInfo };
            case "auth.check":
            {
                if (string.IsNullOrEmpty(S.AuthToken)) return new { ok = false };
                var res = await Account.Call(Account.Api, "GET", "/api/me", null, S.AuthToken);
                if (res is JsonElement me && me.TryGetProperty("role", out var role))
                {
                    // Windows reinstalled on the same PC: the server moved the key over and sends a token for the new HWID
                    if (me.TryGetProperty("token", out var nt0) && nt0.ValueKind == JsonValueKind.String) S.AuthToken = nt0.GetString();
                    TakePlan(me);
                    OnAuthed(role.GetString());
                    return new { ok = true, role = S.AuthRole, note = me.TryGetProperty("note", out var nt) ? nt.GetString() : null, plan = PlanInfo };
                }
                var msg = res is JsonElement e3 && e3.TryGetProperty("error", out var er3) ? er3.GetString() : null;
                // server unreachable: keep working for a few days with the last confirmed login (never past the key's end)
                if (msg != null && msg.StartsWith("Server nicht erreichbar") && S.AuthOkUntil > DateTime.Now && !KeyExpired)
                { _authed = true; OnAuthed(S.AuthRole); return new { ok = true, role = S.AuthRole, offline = true, plan = PlanInfo }; }
                if (msg == "Nicht angemeldet" || msg == "Dein Key ist abgelaufen") Lock();
                return new { ok = false, error = msg };
            }
            case "auth.redeem":
            {
                var payload = JsonSerializer.SerializeToElement(new { code = Str(a, "code").Trim().ToUpperInvariant(), hwid = Account.Hwid, hw2 = Account.Hw2 });
                var res = await Account.Call(Account.Api, "POST", "/api/redeem", payload, null);
                if (res is JsonElement je && je.TryGetProperty("token", out var tk))
                {
                    S.AuthToken = tk.GetString();
                    TakePlan(je);
                    OnAuthed(je.TryGetProperty("role", out var rl) ? rl.GetString() : "user");
                    return new { ok = true, role = S.AuthRole, note = je.TryGetProperty("note", out var nt) ? nt.GetString() : null, plan = PlanInfo,
                        rebound = je.TryGetProperty("rebound", out var rb) && rb.ValueKind == JsonValueKind.True };
                }
                return res;
            }
            case "auth.logout": Lock(); return new { ok = true };
            case "srv":
            {
                var res = await Account.Call(Account.Api, Str(a, "method") == "POST" ? "POST" : "GET", Str(a, "path"),
                    a.TryGetProperty("body", out var bd) ? bd : null, S.AuthToken);
                // token no longer valid (revoked / other PC) -> sign out locally
                if (res is JsonElement e2 && e2.TryGetProperty("error", out var er) && er.GetString() is "Nicht angemeldet" or "Dein Key ist abgelaufen" && S.AuthToken != null)
                { bool exp = er.GetString() != "Nicht angemeldet"; Lock(); Emit("locked", new { reason = exp ? "expired" : "revoked" }); }
                return res;
            }

            case "reshade.cfg.get": return await Task.Run(ReShade.GetCfg);
            case "reshade.cfg.set":
            {
                var err = ReShade.SetCfg(a.GetProperty("cfg"));
                if (err == null) Log("🎨 ReShade-Einstellungen gespeichert");
                return new { error = err };
            }
            case "reshade.shots": { var d = ReShade.ScreenshotDir; Directory.CreateDirectory(d); Proc.Open(d); return null; }

            // ---------- mod library ----------
            case "mods.lib": return new { installed = await Task.Run(Mods.CatalogIds) };
            case "mods.get":
            {
                var id = Str(a, "id"); var url = Str(a, "url"); var sha = Str(a, "sha256").ToLowerInvariant(); var cat = Str(a, "cat");
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return new { error = "Nur sichere https-Downloads erlaubt" };
                if (FiveM.IsRunning) return new { error = "Bitte FiveM zuerst schließen" };
                var dir = Path.Combine(Path.GetTempPath(), "OrbitTweak-lib"); Directory.CreateDirectory(dir);
                var name = Path.GetFileName(new Uri(url).LocalPath); if (string.IsNullOrWhiteSpace(name)) name = id + ".zip";
                var file = Path.Combine(dir, name);
                try
                {
                    using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                    {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitTweak");
                        using var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        resp.EnsureSuccessStatusCode();
                        var total = resp.Content.Headers.ContentLength ?? 0;
                        await using var src = await resp.Content.ReadAsStreamAsync();
                        await using var dst = File.Create(file);
                        var buf = new byte[81920]; long done = 0; int n, lastPct = -1;
                        while ((n = await src.ReadAsync(buf)) > 0)
                        {
                            await dst.WriteAsync(buf.AsMemory(0, n)); done += n;
                            var pct = total > 0 ? (int)(done * 100 / total) : 0;
                            if (pct != lastPct) { lastPct = pct; Emit("modget", new { id, pct }); }
                        }
                    }
                    if (sha.Length == 64)
                    {
                        await using var fs = File.OpenRead(file);
                        var h = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(fs)).ToLowerInvariant();
                        if (h != sha) return new { error = "Prüfsumme stimmt nicht, Download verworfen" };
                    }
                    var o = new InstallOpts { Cat = cat, Title = Str(a, "name"), CatalogId = id, AutoVariant = true, SoundToGta = S.ModSoundGta };
                    var r = await Task.Run(() => Mods.Install(new[] { file }, o));
                    if (r.Error != null) return new { error = r.Error };
                    Log($"🧩 Mod installiert: {Str(a, "name")}");
                    return r.Ui();
                }
                catch (Exception ex) { return new { error = "Download fehlgeschlagen: " + ex.Message }; }
                finally { try { File.Delete(file); } catch { } }
            }
            case "mods.remove":
            {
                var id = Str(a, "id");
                var err = await Task.Run(() => Mods.RemoveCatalog(id));
                return err == null ? new { ok = true } : (object)new { error = err };
            }

            case "reshade.preset":
            {
                var err = ReShade.SetPreset(Str(a, "id"));
                if (err == null) Log($"🎨 ReShade-Preset: {Str(a, "id")}");
                return new { error = err };
            }

            // ---------- thumbnails & GTA backgrounds ----------
            case "thumb": return new { url = await Thumbs.DataUrl(Str(a, "path")) };
            case "clip.thumb": return new { url = Clipper.Resolve(Str(a, "rel")) is { } clipFile ? await Thumbs.DataUrl(clipFile) : null };
            case "bg.list": return await Task.Run(Backgrounds.Candidates);
            case "bg.set":
            {
                var path = Str(a, "path");
                if (Str(a, "rel") is { Length: > 0 } rel) path = Clipper.Resolve(rel) ?? "";
                var err = await Backgrounds.Set(path);
                if (err == null) { S.BgVersion = DateTime.Now.Ticks; S.Save(); }
                return new { error = err, v = S.BgVersion };
            }
            case "bg.pick":
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "GTA-Bild auswählen", Filter = "Bilder (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png" };
                if (dlg.ShowDialog(this) != true) return new { cancelled = true };
                var err = await Backgrounds.Set(dlg.FileName);
                if (err == null) { S.BgVersion = DateTime.Now.Ticks; S.Save(); }
                return new { error = err, v = S.BgVersion };
            }
            case "bg.clear":
                try { File.Delete(Backgrounds.File_); } catch { }
                S.BgVersion = 0; S.Save();
                return new { v = 0 };

            case "mods.browse":
            {
                if (_env == null) return null;
                var url = Str(a, "url");
                if (!url.StartsWith("https://")) url = ModBrowser.Sources[0].url;
                new ModBrowser(_env, url, Str(a, "cat"), Str(a, "accent") is { Length: 7 } ac ? ac : "#FF7A3D", S, Log, d => Emit("modInstalled", d)) { Owner = this }.Show();
                return null;
            }

            // ---------- controller display ----------
            case "pad.set":
            {
                var was = S.Pad.Mode;
                S.Pad = a.GetProperty("cfg").Deserialize<PadCfg>(JIn) ?? new PadCfg();
                S.Save();
                if (a.TryGetProperty("packs", out var packs) && packs.ValueKind == JsonValueKind.Object)
                    foreach (var pr in packs.EnumerateObject())
                        if (pr.Value.Deserialize<PadPack>(JIn) is { } pk) { pk.Prepare(); _pad.View.Packs[pr.Name] = pk; }
                _pad.Configure(S.Pad, S.Pad.Layout == "auto" ? (PadInput.State.T == "ps" ? "ps" : "xbox") : S.Pad.Layout);
                var keys = RegisterKeys();
                LiveTick();
                if (was != S.Pad.Mode) Log(S.Pad.Mode switch { "on" => "🎮 Controller-Anzeige: immer an", "auto" => "🎮 Controller-Anzeige: automatisch", _ => "🎮 Controller-Anzeige aus" });
                return new { keyOk = keys.pad };
            }
            case "pad.watch":
            {
                var on = Bool(a, "on");
                if (on != _padWatch) { _padWatch = on; PadInput.Use(on); if (on) PadInput.Changed += PadToUi; else PadInput.Changed -= PadToUi; }
                return new { state = PadInput.State, active = PadInput.Active };
            }

            case "mods.folder": if (Mods.Dir is { } md) { Directory.CreateDirectory(md); Proc.Open(md); } return null;

            // ---------- network quality ----------
            case "net.test":
            {
                var target = Str(a, "target");
                string host = "servers-frontend.fivem.net"; int port = 443;
                if (target == "cloudflare") host = "1.1.1.1";
                else if (target.Contains(':') && System.Text.RegularExpressions.Regex.IsMatch(target, @"^[\w\.\-]+:\d{1,5}$")) { var i = target.LastIndexOf(':'); host = target[..i]; port = int.Parse(target[(i + 1)..]); }
                return await Net.Quality(host, port);
            }

            // ---------- F8 console ----------
            case "f8.send":
            {
                var cmds = a.GetProperty("cmds").EnumerateArray().Select(c => (c.GetString() ?? "").Trim()).Where(c => c.Length > 0).ToList();
                foreach (var c in cmds) { var sp = c.IndexOf(' '); if (sp > 0) S.F8[c[..sp]] = c[(sp + 1)..]; }
                S.Save();
                if (GameWindow.Find() == IntPtr.Zero) return new { sent = false, queued = true };
                var err = await ConsoleSender.Run(cmds, new WindowInteropHelper(this).Handle);
                if (err == null) Log("⌨ F8: " + string.Join(" · ", cmds));
                return err == null ? new { sent = true, queued = false } : (object)new { sent = false, queued = true, error = err };
            }
            case "f8.values": return S.F8;

            // ---------- live display mode ----------
            case "gw.info": return await Task.Run(GameWindow.Info);
            case "gw.borderless":
            {
                var err = GameWindow.MakeBorderless();
                if (err == null) Log("🖥 Randloses Vollbild aktiviert (live)");
                return err == null ? new { ok = true } : new { error = err };
            }
            case "gw.windowed":
            {
                var err = GameWindow.MakeWindowed();
                if (err == null) Log("🖥 Fenstermodus aktiviert (live)");
                return err == null ? new { ok = true } : new { error = err };
            }

            // ---------- screen color ----------
            case "display.get":
                return new { cfg = S.Display, nvidia = await Task.Run(() => NvVibrance.Available), unlocked = ScreenColor.RangeUnlocked, active = _dispActive, gammaOk = _gammaOk };
            case "display.set":
            {
                var before = S.Display.On;
                S.Display = a.GetProperty("cfg").Deserialize<DisplayCfg>(JIn) ?? new DisplayCfg();
                S.Save();
                var keys = RegisterKeys();
                SyncDisplay(GameFocus.IsGame(), true);
                if (before != S.Display.On) Log(S.Display.On ? "🎨 Farbprofil aktiviert" : "🎨 Farbprofil aus");
                return new { active = _dispActive, gammaOk = _gammaOk, vibOk = _vibOk, keyOk = keys.display };
            }
            case "display.preview":
                _preview = Bool(a, "on");
                SyncDisplay(GameFocus.IsGame(), true);
                return new { active = _dispActive, gammaOk = _gammaOk, vibOk = _vibOk };
            case "display.unlock":
                ScreenColor.UnlockRange();
                Log("🎨 Erweiterter Gamma-Bereich freigeschaltet (nach Neustart aktiv)");
                return new { ok = ScreenColor.RangeUnlocked };

            // ---------- cleaner ----------
            case "clean.scan":
                return await Task.Run(() => Cleaner.Targets().Select(t => new { id = t.Id, name = t.Name, desc = t.Desc, icon = t.Icon, size = Cleaner.Measure(t), sel = t.Selected }).ToList());
            case "clean.run":
            {
                var ids = a.GetProperty("ids").EnumerateArray().Select(x => x.GetString()).ToHashSet();
                var running = FiveM.IsRunning;
                bool skipped = false;
                long freed = await Task.Run(() =>
                {
                    long f = 0;
                    foreach (var t in Cleaner.Targets().Where(t => ids.Contains(t.Id)))
                    {
                        if (running && (t.Id == "fm" || t.Id == "logs")) { skipped = true; continue; }
                        f += Cleaner.Clean(t);
                    }
                    return f;
                });
                Log($"🧹 Bereinigt: {SysInfo.Bytes(freed)}");
                return new { freed, skipped };
            }
            case "ram.clean":
            {
                var f = await Task.Run(() => Native.CleanMemory(S.PurgeStandby));
                Log($"♻ RAM freigegeben: {SysInfo.Bytes(f)}");
                return new { freed = f };
            }

            // ---------- updates ----------
            case "update.check": return await Updater.Check();
            case "update.download":
                try
                {
                    await Updater.Download((pct, done, total) => Emit("update", new { pct, done, total }));
                    return new { ok = true };
                }
                catch (Exception ex) { return new { error = ex.Message }; }
            case "update.install":
                S.Save();
                Updater.Install();
                return null;

            // ---------- diagnose ----------
            case "health": return await Task.Run(() => Health.Run(_tweaks));
            case "health.fix":
            {
                var fix = Str(a, "fix");
                var ok = await Task.Run(() =>
                {
                    try
                    {
                        if (fix.StartsWith("tweak:"))
                        {
                            var t = _tweaks.FirstOrDefault(x => x.Id == fix[6..]);
                            if (t == null || !t.IsUsable) return false;
                            t.Apply();
                            if (!t.SafeCheck()) return false;
                            Marks.Set(t.Id);
                            if (t.Id == "winkey") Dispatcher.Invoke(SnapApply);
                            return true;
                        }
                        if (fix == "cache") { if (FiveM.IsRunning) return false; Cleaner.Clean(Target("fm")); return true; }
                        if (fix == "killbg") { FiveM.KillBackgroundApps(); return true; }
                        if (fix == "clean") { Cleaner.Clean(Target("temp")); Cleaner.Clean(Target("wu")); return true; }
                        // only opens Windows Security › Gerätesicherheit › Kernisolierung: memory integrity is the user's call there
                        if (fix == "coreisolation") { using var _ = Process.Start(new ProcessStartInfo("windowsdefender://coreisolation/") { UseShellExecute = true }); return true; }
                    }
                    catch { }
                    return false;
                });
                return new { ok };
            }
            case "logs": return await Task.Run(Health.Logs);
            case "restore":
            {
                var r = await Task.Run(Catalog.CreateRestorePoint);
                return new { ok = r.Contains("OK") };
            }
        }
        // 2.27: features that live in their own partial files (MainWindow.Stats.cs, MainWindow.Autostart.cs)
        if (cmd.StartsWith("stats.", StringComparison.Ordinal)) return await StatsCmd(cmd, a);
        if (cmd.StartsWith("autostart.", StringComparison.Ordinal)) return await AutostartCmd(cmd, a);
        if (cmd.StartsWith("og.", StringComparison.Ordinal)) return await OgCmd(cmd, a);   // 2.30: MainWindow.OtherGames.cs
        if (cmd.StartsWith("sw.", StringComparison.Ordinal)) return await SwCmd(cmd, a);   // 2.31: MainWindow.ServerWatch.cs
        throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
    }

    // =====================================================================
    //  LIVE: crosshair overlay, screen color, hotkeys
    // =====================================================================
    private DateTime? _gameFgSince;
    private bool _mmPreview;
    private uint _f8Pid, _reticlePid;

    private void ShrinkReticle()
    {
        if (!S.XhairShrink) return;
        S.F8["profile_reticuleSize"] = "0"; S.Save();
        if (GameWindow.Find() == IntPtr.Zero) return;           // applied automatically at next game start
        var pid = GameFocus.Pid;
        if (pid != 0 && pid == _reticlePid) return;
        _reticlePid = pid;
        var back = GameFocus.IsGame() ? IntPtr.Zero : new WindowInteropHelper(this).Handle;
        _ = ConsoleSender.Run(new[] { "profile_reticuleSize 0" }, back).ContinueWith(t => { if (t.Result == null) Log("⌨ GTA-Fadenkreuz minimiert"); });
    }

    private void LiveTick()
    {
        // a key with a runtime ends while the app is open: lock right away (the server refuses it from now on as well)
        if (_authed && KeyExpired) { Lock(); Emit("locked", new { reason = "expired" }); }
        if (!_authed)
        {
            _xhair.Update(false); _minimap.Update(false); _flash.Update(false); _banner.Update(false); _lagFlash.Update(false); _hud.Update(false); _pad.Update(false);
            if (_dispActive) { ScreenColor.Restore(); NvVibrance.Restore(); _dispActive = false; }
            RecTick();
            return;
        }
        bool game = GameFocus.IsGame();
        // since when this game has been running (for the playing time in the counter and in Discord)
        if (GameFocus.Window == IntPtr.Zero) { _playSince = null; _playPid = 0; }
        else if (game && GameFocus.Pid != _playPid) { _playPid = GameFocus.Pid; _playSince = DateTime.Now; }
        SnapTap.GameInFront = game;
        AutoClip.GameInFront = game;
        RecTick();
        // after the game has been in front for 20 s, apply the saved F8 settings once per game session
        if (game)
        {
            _gameFgSince ??= DateTime.Now;
            var pid = GameFocus.Pid;
            if (S.F8Auto && S.F8.Count > 0 && pid != 0 && pid != _f8Pid && DateTime.Now - _gameFgSince > TimeSpan.FromSeconds(20))
            {
                _f8Pid = pid; _reticlePid = pid;
                var cmds = S.F8.Select(kv => kv.Key + " " + kv.Value).ToList();
                _ = ConsoleSender.Run(cmds, IntPtr.Zero).ContinueWith(t => { if (t.Result == null) Log($"⌨ {cmds.Count} F8-Einstellungen automatisch gesetzt"); });
            }
        }
        else _gameFgSince = null;
        _xhair.Update(S.Xhair.On && (!S.Xhair.OnlyGame || game));
        // the picture over the minimap: while the game is in front, or while it is being placed (preview)
        _minimap.Update(_mmPreview || (S.Minimap.On && game));
        _banner.Update(DateTime.Now < _bannerUntil);
        _flash.Update(DateTime.Now < _flashUntil && DateTime.Now >= _bannerUntil);   // the kill note goes first, both sit in the same place
        _lagFlash.Update(DateTime.Now < _lagFlashUntil);
        // the counter in the corner: only in the game, while the kill clipper with the built-in recording is on
        bool hud = S.Clip.Hud && S.Clip.Kills && S.Clip.Recorder == "orbit" && game;
        if (hud)
        {
            int kills = GameFocus.Pid == _roundPid ? _roundKills : 0;
            var extra = new List<string>();
            if (S.Clip.HudClock) extra.Add(DateTime.Now.ToString("HH:mm"));
            if (S.Clip.HudTime && _playSince is DateTime ps) { var m = (int)(DateTime.Now - ps).TotalMinutes; extra.Add(m < 60 ? $"{m} min" : $"{m / 60} h {m % 60:00}"); }
            if (S.Lag.On) extra.Add("LAG");
            var hk = $"{kills}|{_rec.Running}|{_accentHex}|{string.Join('|', extra)}";
            if (hk != _hudKey) { try { _hud.SetImage(KillBanner.Hud(kills, _rec.Running, _accentHex, extra), 16, 44); _hudKey = hk; } catch { } }
        }
        _hud.Update(hud);
        _pad.Tick();
        // re-apply when the game gets focus (fullscreen games may reset the gamma ramp)
        if (game != _lastGame) Emit("gamefront", new { on = game });   // the page stops its effects while the game is being played
        bool focusGained = game && !_lastGame;
        bool changed = game != _lastGame;
        _lastGame = game;
        if (changed) WebRest();
        SyncDisplay(game, focusGained);
    }

    private void SyncDisplay(bool game, bool force)
    {
        var d = S.Display;
        bool want = d.On && (!d.OnlyGame || game || _preview);
        if (want && (force || !_dispActive))
        {
            if (d.GammaNeutral) { ScreenColor.Restore(); _gammaOk = true; }
            else _gammaOk = ScreenColor.Apply(d);
            if (d.Vibrance > 0) _vibOk = NvVibrance.Set(d.Vibrance);
            else { NvVibrance.Restore(); _vibOk = null; }
            _dispActive = true;
        }
        else if (!want && _dispActive)
        {
            ScreenColor.Restore(); NvVibrance.Restore();
            _dispActive = false;
        }
    }

    private void StopLive()
    {
        _live.Stop();
        BgThrottle.Restore();   // never leave other programs slowed down when the app closes
        WlanGame.Restore(); GameTimer.Release();
        _xhair.Close(); _minimap.Close(); _snap.Apply(false, false, 0, 0); _autoClip.Enable(false); _killFeed.Enable(false); _killMark.Enable(false); _pad.Close();
        _recBusy = true; _ = _rec.Stop();
        ScreenColor.Restore(); NvVibrance.Restore();
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) { UnregisterHotKey(h, HK_XHAIR); UnregisterHotKey(h, HK_DISPLAY); UnregisterHotKey(h, HK_CLIP); UnregisterHotKey(h, HK_LAG); }
        _flash.Close(); _lagFlash.Close(); _banner.Close(); _hud.Close();
    }

    private (bool xhair, bool display, bool clip, bool pad) RegisterKeys()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return (true, true, true, true);
        UnregisterHotKey(h, HK_XHAIR); UnregisterHotKey(h, HK_DISPLAY);
        bool x = S.Xhair.Key <= 0 || RegisterHotKey(h, HK_XHAIR, 0x4000, (uint)S.Xhair.Key);
        bool d = S.Display.Key <= 0 || (S.Display.Key != S.Xhair.Key && RegisterHotKey(h, HK_DISPLAY, 0x4000, (uint)S.Display.Key));
        UnregisterHotKey(h, HK_CLIP); UnregisterHotKey(h, HK_PAD);
        bool pk = S.Pad.Key <= 0 || (new[] { S.Xhair.Key, S.Display.Key, S.Clip.Key }.Contains(S.Pad.Key) ? false : RegisterHotKey(h, HK_PAD, 0x4000, (uint)S.Pad.Key));
        bool c = S.Clip.Key <= 0 || (S.Clip.Key != S.Xhair.Key && S.Clip.Key != S.Display.Key && RegisterHotKey(h, HK_CLIP, 0x4000, (uint)S.Clip.Key));
        UnregisterHotKey(h, HK_LAG);
        _lagKeyOk = S.Lag.Key <= 0 || (!new[] { S.Xhair.Key, S.Display.Key, S.Clip.Key, S.Pad.Key }.Contains(S.Lag.Key) && RegisterHotKey(h, HK_LAG, 0x4000, (uint)S.Lag.Key));
        return (x, d, c, pk);
    }

    private int _padPending;
    private void PadToUi()
    {
        // at most 30 updates a second; a change inside that window is sent once it has passed (see PadOverlay.OnInput)
        var wait = TimeSpan.FromMilliseconds(33) - (DateTime.Now - _padEmit);
        if (wait > TimeSpan.Zero)
        {
            if (Interlocked.Exchange(ref _padPending, 1) == 0)
                Task.Delay(wait).ContinueWith(_ => { Volatile.Write(ref _padPending, 0); PadToUi(); });
            return;
        }
        _padEmit = DateTime.Now;
        Emit("pad", PadInput.State);
    }

    /// <summary>State of the WASD tweak (see <see cref="WasdTweak"/>). "pending": the buffer was changed here since Windows started, so the change is not active yet.</summary>
    private object WasdState(string? error = null)
    {
        var (lag, delay, repeat) = WasdTweak.Lag();
        var boot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        return new { size = WasdTweak.Buffer, pending = S.WasdSetAt is DateTime t && t > boot, lag, delay, repeat, chop = new { on = S.Lag.On, gap = S.Lag.Gap, every = S.Lag.Every, key = S.Lag.Key, keyOk = _lagKeyOk }, error };
    }

    /// <summary>Snap Tap and WASD "Laggen" share one keyboard hook; both only work with a valid key and while the game is in front.</summary>
    private void SnapApply() => _snap.Apply(_authed && S.SnapTap, _authed && S.Lag.On, S.Lag.Gap, S.Lag.Every, _authed && S.BlockWinKey);

    private DateTime _lastKillAt, _lastFightClip, _lastDeathAt;
    private readonly List<DateTime> _kfTimes = new();   // when the kills of the clip that is about to be saved happened
    private int _statKills, _statFights;
    /// <summary>Safety net of the kill clipper: a firefight in which no kill was noticed is clipped anyway.</summary>
    private bool NetOn => S.Clip.Kills && S.Clip.Detect != "feed" && S.Clip.Net;
    /// <summary>How long after a noticed kill the safety net keeps quiet (the detection demonstrably works where the player is).</summary>
    private const double NetQuietMinutes = 30;
    private bool NetQuiet => S.Clip.ProvenAt is DateTime p && (DateTime.Now - p).TotalMinutes is >= 0 and < NetQuietMinutes;

    // ---------- AI check of firefights (OpenRouter) ----------
    private readonly FightEye _eye = new();
    private int _aiAsked, _aiKills; private string? _aiLast, _aiError; private DateTime _aiQuotaUntil;
    /// <summary>AI check: on, a key is there, and kills are noticed at the crosshair (not by the kill feed).</summary>
    private bool AiOn => S.Clip.Kills && S.Clip.Detect != "feed" && S.Clip.Ai && S.Clip.Recorder == "orbit" && AiKey.Get() != null;

    /// <summary>
    /// Lets the vision model look at the firefight that just ended. True = decided here (kill saved, or no kill so
    /// nothing to save); false = no answer, the safety net decides as before.
    /// </summary>
    private async Task<bool> AiJudge(string n, double span, DateTime started, bool died)
    {
        if (DateTime.Now < _aiQuotaUntil) return false;   // the account's limit was hit a moment ago
        var to = DateTime.UtcNow; var from = to.AddSeconds(-(span + 4.0));
        var frames = _eye.Take(from, to);   // their times count from "from"
        if (frames.Count < 3) { ClipLog.Add($"Gefecht ({n}, {span:0.0} s): KI-Prüfung ohne Bilder nicht möglich"); return false; }
        KillJudge.Verdict v;
        try
        {
            _aiAsked++;
            v = await KillJudge.Ask(AiKey.Get()!, S.Clip.AiModels, frames);
            _aiError = null;
        }
        catch (KillJudge.JudgeException ex)
        {
            _aiError = ex.Message;
            if (ex.Quota) _aiQuotaUntil = DateTime.Now.AddMinutes(10);
            ClipLog.Add($"Gefecht ({n}, {span:0.0} s): KI-Prüfung fehlgeschlagen ({ex.Message}){(NetOn ? ", das Sicherheitsnetz entscheidet" : "")}");
            return false;
        }
        catch (Exception ex) { _aiError = ex.Message; ClipLog.Add($"Gefecht ({n}, {span:0.0} s): KI-Prüfung fehlgeschlagen ({ex.Message})"); return false; }
        var model = v.Model.Split('/').Last().Replace(":free", "");
        if (v.Kills <= 0)
        {
            _aiLast = $"{DateTime.Now:HH:mm:ss} · {(v.Died ? "Tod" : "kein Kill")} · {v.Seconds:0} s";
            ClipLog.Add($"Gefecht ({n}, {span:0.0} s): KI sieht {(v.Died ? "deinen Tod" : "keinen Kill")}, nicht gespeichert ({model}, {v.Seconds:0.0} s{(v.Reason.Length > 0 ? ": " + v.Reason : "")})");
            return true;
        }
        if (_lastKillAt > started.AddSeconds(-1)) return true;   // noticed at the crosshair in the meantime: already being saved
        _aiKills += v.Kills; _statKills += v.Kills; _lastKillAt = DateTime.Now;
        _aiLast = $"{DateTime.Now:HH:mm:ss} · {(v.Kills == 1 ? "Kill" : v.Kills + " Kills")}{(v.Text.Length > 0 ? $" („{v.Text}“)" : "")} · {v.Seconds:0} s";
        ClipLog.Add($"Kill erkannt (KI {model}, {v.Seconds:0.0} s: {(v.Text.Length > 0 ? $"„{v.Text}“" : v.Reason)})");
        AiLearn(v);
        Emit("kill", new { kill = true });
        var pid = GameFocus.Pid;
        if (pid != _roundPid) { _roundPid = pid; _roundKills = 0; }
        _roundKills += v.Kills;
        if (S.Clip.Banner)
        {
            try { _banner.SetImage(KillBanner.Png(_roundKills, v.Kills, _accentHex), 0, 40); _bannerUntil = DateTime.Now.AddSeconds(1.8); LiveTick(); }
            catch { }
        }
        // from "Before" seconds ahead of the first kill (or of the first shot, if the model did not say when) up to now
        var killAt = v.FirstKillT is double t ? from.AddSeconds(t - 0.6).ToLocalTime() : started;
        // … and it ends "After" seconds after the fight, not now: the model answers up to half a minute later, that
        // would leave dead footage at the end (an end in the past is fine for the recorder, the kill marks follow it)
        var fightEnd = started.AddSeconds(span);
        var endAt = (killAt > fightEnd ? killAt : fightEnd).AddSeconds(Math.Max(2, S.Clip.After));
        double after = Math.Min(S.Clip.After, (endAt - DateTime.Now).TotalSeconds);
        // the player died in this fight as well (the model saw it, or the death message was read)
        var what = (v.Kills == 1 ? "Kill" : $"{v.Kills} Kills") + (v.Died || died ? " + Tod" : "");
        await SaveClip($"Auto-Clip: {what} erkannt (KI)", what,
            S.Clip.Before + Math.Max(0, (DateTime.Now - killAt).TotalSeconds), after, new List<DateTime> { killAt });
        return true;
    }

    /// <summary>
    /// The model read the kill message of this server: its kill word joins the word list, so from now on the message is
    /// noticed right at the crosshair, at once and without asking the model.
    /// </summary>
    private void AiLearn(KillJudge.Verdict v)
    {
        var w = KillText.Fold(v.Word);
        if (w.Length is < 4 or > 24 || !w.All(char.IsLetter) || !KillText.Fold(v.Text).Contains(w, StringComparison.Ordinal)) return;
        var have = (string.IsNullOrWhiteSpace(S.Clip.Words) ? KillText.DefaultWords : S.Clip.Words).Split(',').Select(x => KillText.Fold(x)).ToList();
        if (have.Contains(w) || have.Count >= 12) return;
        S.Clip.Words = (string.IsNullOrWhiteSpace(S.Clip.Words) ? KillText.DefaultWords : S.Clip.Words) + ", " + v.Word.Trim().ToUpperInvariant();
        S.Save(); KfApply();
        ClipLog.Add($"Kill-Wort „{v.Word.Trim().ToUpperInvariant()}“ von der KI gelernt: wird ab jetzt direkt am Fadenkreuz erkannt");
        Emit("clipwords", new { words = S.Clip.Words });
    }

    /// <summary>
    /// The montage options that are kept with the clip settings, where given: the big line of the intro (empty = in-game
    /// name), intro/outro on or off, kill counter on or off (clip.cfg names them montageTitle…, clip.montage title…).
    /// </summary>
    private void MontageOpts(JsonElement a, string title = "montageTitle", string intro = "montageIntro", string counter = "montageCounter")
    {
        if (a.ValueKind != JsonValueKind.Object) return;
        if (a.TryGetProperty(title, out var mt) && mt.ValueKind == JsonValueKind.String) { var t = (mt.GetString() ?? "").Trim(); S.Clip.MontageTitle = t.Length > 32 ? t[..32] : t; }
        if (a.TryGetProperty(intro, out var mi) && mi.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.MontageIntro = mi.GetBoolean();
        if (a.TryGetProperty(counter, out var mc) && mc.ValueKind is JsonValueKind.True or JsonValueKind.False) S.Clip.MontageCounter = mc.GetBoolean();
    }

    /// <summary>Switches on the chosen way of noticing kills: at the crosshair (kill message or red X, default) or the kill feed.</summary>
    private void KfApply()
    {
        _killFeed.Name = S.Clip.Name; _killFeed.Area = S.Clip.Area; _killFeed.Deaths = S.Clip.Deaths;
        _killMark.Words = string.IsNullOrWhiteSpace(S.Clip.Words) ? KillText.DefaultWords : S.Clip.Words;
        bool feed = S.Clip.Detect == "feed";
        _killFeed.Enable(S.Clip.Kills && feed && _authed && S.Clip.Name.Length >= 3);
        _killMark.Enable(S.Clip.Kills && !feed && _authed);
        // firefights are watched for "after fights" clips and for the safety net
        _autoClip.Sens = S.Clip.Sens;
        _autoClip.Enable(_authed && (S.Clip.Auto || NetOn || AiOn));
        _eye.Enable(_authed && AiOn);
    }

    /// <summary>A kill (or death) was noticed: several in a row end up in one clip, saved once nothing new happened for a moment.</summary>
    private async void OnKill(bool kill, string? how = null, DateTime? at = null)
    {
        if (!_authed || !S.Clip.Kills) return;
        if (kill) { _kfKills++; _statKills++; } else { _kfDeaths++; if (_kfKills > 0) _kfDied = true; }
        if (kill) _kfTimes.Add(at ?? DateTime.Now.AddSeconds(-KillLogic.LagMessage));   // when the kill itself happened, a moment before it was noticed
        _lastKillAt = DateTime.Now;
        ClipLog.Add(kill ? (how != null ? $"Kill erkannt ({how})" : "Kill im Kill-Feed erkannt") : "Tod im Kill-Feed erkannt");
        // noticed at the crosshair: the detection works here, so the safety net can keep quiet for a while
        if (kill && how != null)
        {
            bool save = S.Clip.ProvenAt is not DateTime p || (DateTime.Now - p).TotalSeconds > 60;
            S.Clip.ProvenAt = DateTime.Now;
            if (save) S.Save();
        }
        Emit("kill", new { kill });
        if (kill)
        {
            // kills are counted per round: a new game process starts at one again
            var pid = GameFocus.Pid;
            if (pid != _roundPid) { _roundPid = pid; _roundKills = 0; }
            _roundKills++;
            if (S.Clip.Banner)
            {
                try { _banner.SetImage(KillBanner.Png(_roundKills, _kfKills, _accentHex), 0, 40); _bannerUntil = DateTime.Now.AddSeconds(1.8); LiveTick(); }
                catch { }
            }
        }
        bool own = S.Clip.Recorder == "orbit";
        _kfSaveAt = DateTime.Now.AddSeconds(own ? Math.Max(2, S.Clip.After) : 3);
        if (_kfPending) return;
        _kfPending = true; _kfFirst = DateTime.Now;
        while (DateTime.Now < _kfSaveAt && DateTime.Now - _kfFirst < TimeSpan.FromSeconds(50)) await Task.Delay(250);
        var what = (_kfKills > 1 ? $"{_kfKills} Kills" : _kfKills == 1 ? "Kill" : "Tod") + (_kfKills > 0 && _kfDied ? " + Tod" : "");
        // the first kill itself, not when it was noticed (a red X counts only once it is gone again, a second or more later)
        var firstAt = _kfTimes.Count > 0 && _kfTimes.Min() < _kfFirst ? _kfTimes.Min() : _kfFirst;
        var since = (DateTime.Now - firstAt).TotalSeconds;
        _kfKills = 0; _kfDeaths = 0; _kfDied = false; _kfPending = false;
        // built-in recorder: from "Before" seconds ahead of the first kill up to now (the seconds after are already over)
        var killTimes = _kfTimes.ToList(); _kfTimes.Clear();
        await SaveClip($"Auto-Clip: {what} erkannt", what, S.Clip.Before + since, 0, killTimes);
    }

    /// <summary>
    /// The player's own death was read at the crosshair ("KILLED BY …", "WASTED"). A kill clip that is about to be saved
    /// becomes "Kill + Tod" and runs a moment longer so the death is in it; a fight with this death is no safety-net clip.
    /// </summary>
    private void OnDeath(string text)
    {
        if (!_authed || !S.Clip.Kills) return;
        _lastDeathAt = DateTime.Now;
        ClipLog.Add($"Tod erkannt („{text}“ am Fadenkreuz){(_kfPending && _kfKills > 0 ? ", der Kill-Clip heißt „Kill + Tod“" : "")}");
        if (!_kfPending || _kfKills == 0) return;
        _kfDied = true;
        var end = DateTime.Now.AddSeconds(2);
        if (end > _kfSaveAt) _kfSaveAt = end;
    }

    /// <summary>
    /// The game was closed. If wanted, the kill clips saved since it was started are joined into a highlight video
    /// (in the helper process, with low priority).
    /// </summary>
    private async void RoundEnded()
    {
        var clips = _roundClips.Where(File.Exists).Select(f => new FileInfo(f)).ToList();
        _roundClips.Clear();
        if (!_authed || !S.Clip.AutoHighlight || clips.Count < 2 || _hlBusy) return;
        _hlBusy = true;
        try
        {
            ClipLog.Add($"Runde beendet: Highlight-Video aus {clips.Count} Kill-Clips wird erstellt");
            var (file, err, n) = await Highlight.Make(clips, S.Clip.HighlightTail, (pct, text) => Emit("highlight", new { pct, text }), S.Clip.After);
            if (file != null)
            {
                ClipLog.Add($"Highlight-Video aus {n} Kill-Clips: {Path.GetFileName(file)}");
                Log($"🎬 Highlight-Video der Runde aus {n} Clips → {Path.GetFileName(file)}");
            }
            else ClipLog.Add($"Highlight-Video nicht erstellt: {err}");
            Emit("highlightDone", new { file = file != null ? Path.GetFileName(file) : null, error = err, clips = n });
        }
        catch (Exception ex) { ClipLog.Add("Highlight-Video nicht erstellt: " + ex.Message); }
        finally { _hlBusy = false; }
    }

    /// <summary>
    /// Saves a clip. Built-in recorder: "before" seconds back from now plus "after" seconds (0 = the usual clip length).
    /// Other recorders: their save shortcut is pressed. Returns an error text or null.
    /// </summary>
    private async Task<string?> SaveClip(string? auto = null, string kind = "Clip", double before = 0, double after = 0, List<DateTime>? kills = null)
    {
        if (S.Clip.Recorder == "orbit")
        {
            // "active" also covers the moment right after the recording ended by itself (game closed or minimized):
            // the last seconds are still there until the recording is cleared away
            if (!_recActive && !_rec.Running)
            {
                var why = _recError ?? (GameFocus.Window == IntPtr.Zero ? "FiveM läuft nicht oder war noch nicht im Vordergrund" : "Die Aufnahme startet gerade, gleich nochmal versuchen");
                Log("🎬 Kein Clip: " + why);
                Emit("clip", new { recorder = "orbit", auto, error = why });
                return why;
            }
            if (before <= 0) before = S.Clip.Before + S.Clip.After;
            if (S.Clip.Flash) { _flashUntil = DateTime.Now.AddSeconds(1.8); LiveTick(); }
            string? file;
            _saving++;
            var cutAt = DateTime.Now.AddSeconds(after);   // where the clip ends
            try { file = await _rec.Save(Math.Min(before, 90), after, Clipper.OwnDir, $"{kind} {DateTime.Now:yyyy-MM-dd HH-mm-ss}"); }
            finally { _saving--; }
            if (file == null)
            {
                var why = _rec.Error ?? "unbekannter Fehler";
                Log("🎬 Clip fehlgeschlagen: " + why);
                ClipLog.Add($"Clip „{kind}“ fehlgeschlagen: {why}");
                Emit("clip", new { recorder = "orbit", auto, error = why });
                return why;
            }
            ClipLog.Add($"Clip gespeichert: {Path.GetFileName(file)} ({before:0} s davor, {(after < 0 ? $"endet {-after:0} s vor dem Speichern" : $"{after:0} s danach")})");
            var killKind = kind.EndsWith(" + Tod") ? kind[..^6] : kind;
            if (killKind == "Kill" || killKind.EndsWith(" Kills")) _roundClips.Add(file);   // for the highlight video of this round
            if (kills is { Count: > 0 }) ClipMarks.Add(file, kills.Select(k => (cutAt - k).TotalSeconds));   // where the kills sit, for the slow motion of a montage
            Log($"🎬 {auto ?? "Clip gespeichert"} → {Path.GetFileName(file)}");
            Emit("clip", new { recorder = "orbit", auto, file = Path.GetFileName(file) });
            StatsClip();
            return null;
        }
        await Clipper.Save(S.Clip.Recorder);
        Log(auto != null ? $"🎬 {auto}" : $"🎬 Clip gespeichert ({Clipper.Recorders[S.Clip.Recorder].label})");
        Emit("clip", new { recorder = S.Clip.Recorder, auto });
        StatsClip();
        if (S.Clip.Flash) _flashUntil = DateTime.Now.AddSeconds(1.8);
        LiveTick();
        return null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg != 0x0312) return IntPtr.Zero; // WM_HOTKEY
        var id = wp.ToInt32();
        if (id == HK_XHAIR)
        {
            S.Xhair.On = !S.Xhair.On; S.Save(); LiveTick();
            if (S.Xhair.On) ShrinkReticle();
            Emit("xhair", new { on = S.Xhair.On });
            handled = true;
        }
        else if (id == HK_PAD)
        {
            S.Pad.Mode = S.Pad.Mode == "off" ? "auto" : "off"; S.Save();
            _pad.Configure(S.Pad, _pad.View.Layout); LiveTick();
            Emit("padon", new { mode = S.Pad.Mode });
            handled = true;
        }
        else if (id == HK_CLIP)
        {
            _ = SaveClip();
            handled = true;
        }
        else if (id == HK_DISPLAY)
        {
            S.Display.On = !S.Display.On; S.Save(); SyncDisplay(GameFocus.IsGame(), true);
            Emit("display", new { on = S.Display.On, active = _dispActive });
            handled = true;
        }
        else if (id == HK_LAG)
        {
            // WASD "Laggen" on or off from inside the game, with a short note at the top of the picture
            if (_authed)
            {
                S.Lag.On = !S.Lag.On; S.Save(); SnapApply();
                var png = S.Lag.On ? _lagPngOn : _lagPngOff;
                if (png != null) { try { _lagFlash.SetImage(png, 0, 104); _lagFlashUntil = DateTime.Now.AddSeconds(1.4); } catch { } }
                LiveTick();
                Log(S.Lag.On ? "⌨ WASD-Laggen an (Taste)" : "⌨ WASD-Laggen aus (Taste)");
                Emit("lag", new { on = S.Lag.On });
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static CleanTarget Target(string id) => Cleaner.Targets().First(t => t.Id == id);
    private static object FiveMInfo() => new { found = FiveM.Root != null, path = FiveM.Root };

    private async Task<object> TweakList()
    {
        var st = await Task.Run(() => _tweaks.Select(t => (applied: t.SafeCheck(), rec: t.IsRecommended, usable: t.IsUsable, hint: t.SafeHint())).ToList());
        return _tweaks.Select((t, i) => new { id = t.Id, cat = t.Cat, name = t.Name, sum = t.Sum, desc = t.Desc, icon = t.Icon, badge = t.Badge, rec = st[i].rec, applied = st[i].applied, usable = st[i].usable, hint = st[i].hint }).ToList();
    }

    // =====================================================================
    //  BOOST
    // =====================================================================
    private async Task<object> Boost()
    {
        long freed = 0; bool restart = false; int applied = 0;
        var steps = new List<(string text, Action run)>();
        if (S.RestoreBeforeBoost)
            steps.Add(("Wiederherstellungspunkt wird erstellt…", () =>
            {
                var r = Catalog.CreateRestorePoint();
                Log(r.Contains("OK") ? "✔ Wiederherstellungspunkt erstellt" : "⚠ Wiederherstellungspunkt übersprungen");
            }));
        var todo = await Task.Run(() => _tweaks.Where(t => t.IsRecommended && t.IsUsable && !t.SafeCheck()).ToList());
        foreach (var t in todo)
            steps.Add((t.Name + "…", () =>
            {
                try { t.Apply(); if (t.SafeCheck()) { applied++; Marks.Set(t.Id); if (t.NeedsRestart) restart = true; } }
                catch (Exception ex) { Log($"✖ {t.Name}: {ex.Message}"); }
            }));
        steps.Add(("Temporäre Dateien werden gelöscht…", () => freed += Cleaner.Clean(Target("temp"))));
        steps.Add(("DNS-Cache wird geleert…", () => Proc.Run("ipconfig", "/flushdns")));
        steps.Add(("Arbeitsspeicher wird freigegeben…", () => freed += Native.CleanMemory(S.PurgeStandby)));

        for (int i = 0; i < steps.Count; i++)
        {
            Emit("boost", new { pct = i * 100 / steps.Count, text = steps[i].text });
            await Task.Run(steps[i].run);
        }
        Emit("boost", new { pct = 100, text = "Fertig!" });
        S.LastBoost = DateTime.Now; S.Save();
        return new { freed, restart, applied };
    }

    // =====================================================================
    //  LAUNCH
    // =====================================================================
    private async Task<object> Launch(JsonElement a)
    {
        if (FiveM.Exe is not { } exe) return new { error = "FiveM nicht gefunden – Pfad im Launcher wählen" };
        if (FiveM.IsRunning) return new { error = "FiveM läuft bereits" };
        var build = Str(a, "build");
        var pure = a.TryGetProperty("pure", out var pv) && pv.ValueKind == JsonValueKind.Number ? pv.GetInt32() : 0;
        var connect = Str(a, "connect").Trim();
        bool Pre(string k) => a.TryGetProperty("pre", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;

        long freed = 0; var done = new List<string>();
        await Task.Run(() =>
        {
            if (Pre("kill")) { var n = FiveM.KillBackgroundApps(); done.Add($"{n} Apps beendet"); }
            if (Pre("cache")) { freed += Cleaner.Clean(Target("fm")); done.Add("Cache geleert"); }
            if (Pre("ram")) { freed += Native.CleanMemory(S.PurgeStandby); done.Add("RAM frei"); }
            if (Pre("dns")) { Proc.Run("ipconfig", "/flushdns"); done.Add("DNS geleert"); }
        });

        var args = new List<string>();
        if (!string.IsNullOrEmpty(build) && build != "Auto" && build.All(char.IsDigit)) args.Add("-b" + build);
        if (pure is 1 or 2) args.Add("-pure_" + pure);
        if (connect.Length > 0)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(connect, @"^[\w\.\-:/]+$")) return new { error = "Ungültige Server-Adresse" };
            args.Add("+connect " + connect);
        }
        var argStr = string.Join(" ", args);
        var dir = Path.GetDirectoryName(exe)!;

        // FiveM refuses to run elevated → start it as the normal desktop user
        bool ok = Unelevated.Start(exe, argStr, dir);
        if (!ok)
        {
            if (args.Count == 0) Process.Start("explorer.exe", $"\"{exe}\"");
            else if (connect.Length > 0 && args.Count == 1) Process.Start("explorer.exe", $"\"fivem://connect/{connect}\"");
            else Process.Start(new ProcessStartInfo(exe, argStr) { UseShellExecute = true, WorkingDirectory = dir });
        }
        var summary = done.Count > 0 ? string.Join(" · ", done) : "Spiel startet";
        Log($"▶ FiveM gestartet {argStr} ({summary})");
        return new { freed, summary };
    }

    // =====================================================================
    //  DISCORD STATUS
    // =====================================================================
    private string DiscordAppId => S.DiscordAppId is { Length: > 0 } id ? id : DiscordPresence.DefaultAppId;

    /// <summary>Works out what the Discord profile shows (every 2 s from the live timer). DiscordPresence sends it only
    /// when it changed, at most every 15 s, and never on the UI thread.</summary>
    private void DiscordTick()
    {
        var id = DiscordAppId;
        if (!S.DiscordOn || id.Length == 0 || !_authed) { _dcLast = null; _dc.Set(id, null); return; }
        if (!_dcProbed) return;   // game state not known yet: no "Nicht im Spiel" for 15 s right after the start
        var p = new Presence
        {
            LargeImage = "logo",                                   // asset key uploaded in the Developer Portal
            LargeText = $"OrbitTweak {Updater.Current.ToString(3)}",
            Buttons = new[] { ("OrbitTweak", $"https://github.com/{Updater.Repo}/releases/latest") },
        };
        if (_dcGamePid != 0)
        {
            // the server: looked up again every 30 s (and at once for a new game), never while it is hidden
            if (_dcSrv.pid != _dcGamePid) _dcSrv = (null, null, _dcGamePid, default);
            if (S.DiscordServer && !_dcSrvBusy && DateTime.Now - _dcSrv.at > TimeSpan.FromSeconds(30)) _ = DiscordServerLookup(_dcGamePid);
            var srv = S.DiscordServer ? _dcSrv.name : null;
            // kills are counted per game process (_roundPid); _playPid stays while the game is only tabbed out
            uint rp = _roundPid;
            int kills = S.DiscordKills && rp != 0 && (rp == (uint)_dcGamePid || rp == _playPid) ? _roundKills : 0;
            var state = kills > 0 ? $"Im Spiel · {kills} {(kills == 1 ? "Kill" : "Kills")}" : "Im Spiel";
            p = p with
            {
                Details = srv ?? "FiveM", State = state, Since = _dcGameStart ?? _playSince,
                SmallImage = (S.DiscordServer ? _dcSrv.icon : null) ?? "ingame", SmallText = srv ?? "Im Spiel",
            };
        }
        else if (_dcFm) p = p with { Details = "FiveM", State = "Launcher offen", SmallImage = "launcher", SmallText = "Launcher offen" };
        else p = p with { State = "Nicht im Spiel" };
        _dcLast = p;
        _dc.Set(id, p);
    }

    /// <summary>Name and icon of the server the game is on: FiveM's last server (crashometry), if it was written after
    /// the game started, looked up at Cfx.re (cached there for 60 s). Same rule as "server.current".</summary>
    private async Task DiscordServerLookup(int pid)
    {
        _dcSrvBusy = true;
        try
        {
            var start = _dcGameStart;
            var (url, at) = await Task.Run(Servers.LastServer);
            string? key = null;
            if (!string.IsNullOrWhiteSpace(url) && start is DateTime gs && at >= gs.AddSeconds(-5)) try { key = Servers.Parse(url); } catch { }
            string? name = null, icon = null;
            if (key != null)
            {
                var el = JsonSerializer.SerializeToElement(await Servers.Fetch(key));
                string? Get(string n) => el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (el.TryGetProperty("online", out var on) && on.ValueKind == JsonValueKind.True && Get("name") is { } n) name = CleanServerName(n);
                icon = Get("icon");
            }
            if (_dcSrv.pid == pid) _dcSrv = (name, icon, pid, DateTime.Now);
        }
        catch { if (_dcSrv.pid == pid) _dcSrv = (null, null, pid, DateTime.Now); }
        finally { _dcSrvBusy = false; }
    }

    /// <summary>"^1Nova^7City ~r~RP" → "NovaCity RP": FiveM/GTA colour codes and tags out.</summary>
    private static string? CleanServerName(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\^\d|~[a-zA-Z]~|<[^>]{1,40}>", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length >= 2 ? s : null;
    }

    /// <summary>For the card in Einstellungen › Verhalten: status line and what Discord shows right now.</summary>
    private object DiscordState() => new
    {
        status = !S.DiscordOn ? "off" : DiscordAppId.Length == 0 ? "noid" : _dc.Connected ? "on" : _dc.Refused ? "refused" : "nodiscord",
        user = _dc.User, error = _dc.LastError, appId = S.DiscordAppId, builtIn = DiscordPresence.DefaultAppId.Length > 0,
        preview = _dcLast == null ? null : new { details = _dcLast.Details, state = _dcLast.State, since = _dcLast.Since },
    };

    // =====================================================================
    //  LIVE STATS
    // =====================================================================
    private void Timer_Tick(object? sender, EventArgs e)
    {
        _tick++;
        var cpu = (int)Math.Round(Native.CpuUsage());
        var (total, avail, load) = Native.Memory();
        Emit("stats", new
        {
            cpu, ram = load,
            ramUsed = (total - avail) / 1073741824.0,
            ramTotal = total / 1073741824.0,
            fm = _fm
        });
        if (_tick % 2 == 0 && !_probing) _ = ProbeFiveM();
        if (_tick % 2 == 1) DiscordTick();
    }

    private async Task ProbeFiveM()
    {
        _probing = true;
        try
        {
            bool fmOpen = false; int gamePid = 0; DateTime? gameStart = null;   // for the Discord status
            _fm = await Task.Run(() =>
            {
                var procs = FiveM.Processes();
                try
                {
                    var game = procs.FirstOrDefault(p => p.ProcessName.Contains("GTAProcess", StringComparison.OrdinalIgnoreCase));
                    string? build = null, prio = null; long ram = 0;
                    if (game != null)
                    {
                        build = game.ProcessName.Split('_').FirstOrDefault(x => x.Length > 1 && x[0] == 'b' && char.IsDigit(x[1]));
                        try
                        {
                            // the old "Auto-Priorität Hoch" (before 2.26) left the game at High; helpers in the Cfx.re forum advise Normal
                            if (S.PrioWasAuto && !_prioLooked && game.PriorityClass == ProcessPriorityClass.High)
                            {
                                game.PriorityClass = ProcessPriorityClass.Normal;
                                Log("↺ GTA-Prozess wieder auf normaler Priorität (im Cfx.re-Forum wird von „Hoch“ abgeraten)");
                            }
                            // tagged "full speed" once per game start; the CPU priority is never touched
                            if (_authed && S.GameNoThrottle && game.Id != _qosPid)
                            {
                                _qosPid = game.Id;
                                if (Qos.High(game.Id)) Log("⚡ FiveM wird von Windows nicht mehr gedrosselt");
                            }
                            if (_authed && S.TimerRes && !GameTimer.Held && GameTimer.Hold() is double now)
                                Log($"⏱ Timer 0,5 ms angefordert (Windows läuft gerade mit {now:0.###} ms)");
                            // heavy programs next to the game go behind it; ones started later are caught on the next look
                            if (_authed && S.BgThrottle)
                            {
                                bool first = !BgThrottle.Active;
                                int slowed = BgThrottle.Apply();
                                if (slowed > 0 && first) Log($"⚡ Hintergrund ausgebremst: {slowed} Prozesse hinter das Spiel gestellt");
                            }
                            if (_authed && S.WifiGameMode && WlanGame.Apply()) Log("📶 WLAN-Spielmodus: Hintergrund-Scans pausiert");
                            game.Refresh();
                            prio = game.PriorityClass switch
                            {
                                ProcessPriorityClass.High => "Hoch", ProcessPriorityClass.AboveNormal => "Höher",
                                ProcessPriorityClass.RealTime => "Echtzeit", ProcessPriorityClass.BelowNormal => "Niedriger",
                                ProcessPriorityClass.Idle => "Niedrig", _ => "Normal"
                            };
                            ram = game.WorkingSet64;
                        }
                        catch { prio = "?"; }
                    }
                    _prioLooked = true;   // only the first look after the update (S.PrioWasAuto stays for the note of Catalog.Migrate)
                    if (game == null || !S.GameNoThrottle) _qosPid = 0;
                    // the game is gone (or the tweak was switched off, or you signed out): every program gets back what it had
                    if (BgThrottle.Active && (game == null || !S.BgThrottle || !_authed))
                    {
                        int back = BgThrottle.Restore();
                        if (back > 0) Log($"⚡ Hintergrund wieder normal: {back} Prozesse zurückgestellt");
                    }
                    if (GameTimer.Held && (game == null || !S.TimerRes || !_authed)) GameTimer.Release();
                    if (WlanGame.Active && (game == null || !S.WifiGameMode || !_authed)) { if (WlanGame.Restore()) Log("📶 WLAN wieder normal: Hintergrund-Scans erlaubt"); }
                    // a new FiveM build brings new exes: they get the per-app graphics fields that are switched on, before the game starts if possible
                    if (_authed && procs.Length > 0 && DateTime.Now - _prefUpkeep > TimeSpan.FromSeconds(5))
                    {
                        _prefUpkeep = DateTime.Now;
                        int added = Catalog.PrefUpkeep();
                        if (added > 0) Log($"🖥 Grafikeinstellung für {added} neue FiveM-Datei(en) übernommen");
                    }
                    // the FPS limit too, on every look: a new build's exe should be in the driver profile before the game
                    // process makes its graphics device (cheap until the list of exes changes or FiveM starts again)
                    if (_authed && procs.Length > 0)
                        try
                        {
                            var (fps, profs) = FpsLimit.Upkeep(game?.ProcessName);
                            if (fps > 0) Log($"🎯 FPS-Limit für {fps} neue FiveM-Datei(en) übernommen");
                            // a profile of someone else caps everything in it: the log names each one OrbitTweak set on its own
                            if (profs.Count > 0) Log($"🎯 FPS-Limit jetzt auch {(profs.Count == 1 ? "im Treiberprofil" : "in den Treiberprofilen")} {string.Join(", ", profs.Select(p => $"„{p}“"))}, es gilt {(profs.Count == 1 ? "dort" : "jeweils")} für das ganze Profil");
                        }
                        catch (Exception ex) { Log($"✖ FPS-Limit nicht übernommen: {ex.Message}"); }   // once per start of FiveM, then again on the next
                    if (procs.Length == 0) FpsLimit.Rearm();
                    if (_authed && game != null && S.AutoBorderless)
                    {
                        try
                        {
                            var gh = game.MainWindowHandle;
                            if (gh != IntPtr.Zero && gh != GameWindow.LastAuto && GameWindow.Mode(gh) == "windowed")
                            {
                                GameWindow.LastAuto = gh;
                                Dispatcher.Invoke(() => { if (GameWindow.MakeBorderless(gh) == null) Log("🖥 Automatisch randlos gemacht"); });
                            }
                        }
                        catch { }
                    }
                    fmOpen = procs.Length > 0;
                    if (game != null) { gamePid = game.Id; try { gameStart = game.StartTime; } catch { } }
                    return (object)new { running = procs.Length > 0, game = game != null, build, prio, gameRam = ram };
                }
                finally { foreach (var p in procs) p.Dispose(); }
            });
            _dcFm = fmOpen; _dcGamePid = gamePid; _dcGameStart = gameStart; _dcProbed = true;
            StatsTick(gamePid, gameStart);   // play time: a round starts and ends with the game process
        }
        finally { _probing = false; }
    }
}
