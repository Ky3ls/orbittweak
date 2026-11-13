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
    private DateTime _lastAutoClean = DateTime.MinValue;
    private double _zoom = 1;
    private readonly CrosshairOverlay _xhair = new();
    private readonly CrosshairOverlay _minimap = new() { Anchor = Anchor.BottomLeft };
    private readonly SnapTap _snap = new();
    private readonly PadOverlay _pad = new();
    private CoreWebView2Environment? _env;
    private bool _padWatch;
    private DateTime _padEmit;
    private const int HK_PAD = 0xB03;
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _dispActive, _preview, _lastGame;
    private bool? _gammaOk, _vibOk;
    private const int HK_XHAIR = 0xB00, HK_DISPLAY = 0xB01, HK_CLIP = 0xB02;
    private readonly CrosshairOverlay _flash = new() { Anchor = Anchor.TopCenter };
    private DateTime _flashUntil;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public MainWindow()
    {
        FiveM.Settings = S;
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
            RegisterKeys();
        };
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        Loaded += async (_, _) => await InitWeb();
        Closing += (_, _) => { S.Save(); Catalog.SetTimerResolution(false); StopLive(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { ScreenColor.Restore(); NvVibrance.Restore(); };
        Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
        _live.Tick += (_, _) => LiveTick();
        if (S.SnapTap) _snap.Enable(true);
        _pad.Configure(S.Pad, S.Pad.Layout == "auto" ? "xbox" : S.Pad.Layout);
        _live.Start();

        if (S.TimerRes) Catalog.SetTimerResolution(true);
        Task.Run(Catalog.UndoKeyboardTurbo);
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
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitTweak");
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
    private static bool Bool(JsonElement a, string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
    private static int Int(JsonElement a, string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : 0;

    // =====================================================================
    //  COMMANDS
    // =====================================================================
    private async Task<object?> Handle(string cmd, JsonElement a)
    {
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
                return new { user = Environment.UserName, admin = Proc.IsAdmin(), version = Updater.Current.ToString(3), sys, fivem = FiveMInfo(), settings = S };
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
                    case "autoRamClean": S.AutoRamClean = val.GetBoolean(); break;
                    case "autoPriority": S.AutoPriority = val.GetBoolean(); break;
                    case "firstRun": S.FirstRun = val.GetBoolean(); break;
                    case "xhairAck": S.XhairAck = val.GetBoolean(); break;
                    case "autoBorderless": S.AutoBorderless = val.GetBoolean(); break;
                    case "f8Auto": S.F8Auto = val.GetBoolean(); break;
                    case "xhairShrink": S.XhairShrink = val.GetBoolean(); break;
                    case "modsAck": S.ModsAck = val.GetBoolean(); break;
                    case "bgPreset": S.BgPreset = val.GetString(); break;
                    case "snapTap": S.SnapTap = val.GetBoolean(); _snap.Enable(S.SnapTap); Log(S.SnapTap ? "⌨ Snap Tap aktiv (nur im Spiel)" : "⌨ Snap Tap aus"); break;
                    case "launch": S.Launch = val.Deserialize<LaunchCfg>(JIn) ?? new LaunchCfg(); break;
                }
                S.Save();
                return null;

            // ---------- tweaks ----------
            case "tweaks.list": return await TweakList();
            case "tweak.set":
            {
                var t = _tweaks.First(x => x.Id == Str(a, "id"));
                var on = Bool(a, "on");
                string? err = null;
                await Task.Run(() => { try { if (on) t.Apply(); else t.Revert(); } catch (Exception ex) { err = ex.Message; } });
                var applied = await Task.Run(t.SafeCheck);
                Log(err != null ? $"✖ {t.Name}: {err}" : $"{(on ? "✔" : "↺")} {t.Name}");
                return new { applied, error = err };
            }
            case "tweaks.revertAll":
                await Task.Run(() => { foreach (var t in _tweaks) if (t.SafeCheck()) try { t.Revert(); } catch { } });
                Log("↺ Alle Tweaks zurückgesetzt");
                return null;
            case "boost": return await Boost();

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
                try { Gfx.Save(a.GetProperty("values")); Log("🎨 Grafik-Einstellungen gespeichert"); return new { ok = true }; }
                catch (Exception ex) { return new { error = ex.Message }; }
            case "gfx.restore":
                try { Gfx.Restore(); Log("↺ Grafik-Backup wiederhergestellt"); return new { ok = true }; }
                catch (Exception ex) { return new { error = ex.Message }; }

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
                var rec = Str(a, "recorder"); if (Clipper.Recorders.ContainsKey(rec)) S.Clip.Recorder = rec;
                var png = Str(a, "png"); if (png.Length > 0) _flash.SetImage(Convert.FromBase64String(png), 0, 40);
                S.Save();
                var keys = RegisterKeys();
                return new { keyOk = keys.clip, combo = Clipper.Recorders[S.Clip.Recorder].label };
            }
            case "clip.save": await SaveClip(); return new { ok = true };
            case "clip.list": return await Task.Run(Clipper.List);
            case "clip.open":
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
                try { File.Delete(f); Log("🗑 Clip gelöscht: " + Path.GetFileName(f)); return new { ok = true }; }
                catch (Exception ex) { return new { ok = false, error = ex.Message }; }
            }
            case "clip.gamebar":
                Clipper.EnableGameBarCapture();
                Log("🎬 Game-Bar-Aufnahme im Hintergrund aktiviert");
                return new { ok = true };

            // ---------- mods ----------
            case "mods.list": return await Task.Run(() => Mods.List(S.ModCats));
            case "mods.install":
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Mod auswählen (.rpf oder .zip)", Filter = "FiveM-Mods (*.rpf;*.zip)|*.rpf;*.zip", Multiselect = true };
                if (dlg.ShowDialog(this) != true) return new { count = 0, cancelled = true };
                var cat = Str(a, "cat");
                var (count, error) = await Task.Run(() => Mods.Install(dlg.FileNames, cat, S.ModCats));
                S.Save();
                if (count > 0) Log($"🧩 {count} Mod(s) installiert");
                return new { count, error };
            }
            case "mods.toggle":
            {
                var err = Mods.Toggle(Str(a, "name"), Bool(a, "on"));
                if (err == null) Log($"🧩 {Str(a, "name")} {(Bool(a, "on") ? "aktiviert" : "deaktiviert")}");
                return new { error = err };
            }
            case "mods.delete":
            {
                var err = Mods.Delete(Str(a, "name"));
                if (err == null) { S.ModCats.Remove(Str(a, "name")); S.Save(); Log($"🗑 Mod entfernt: {Str(a, "name")}"); }
                return new { error = err };
            }
            case "mods.downloads": return await Task.Run(ModDownloads.List);
            case "mods.installDl":
            {
                var (files, temp, err) = await Task.Run(() => ModDownloads.Prepare(Str(a, "name")));
                int count = 0;
                if (err == null) (count, err) = await Task.Run(() => Mods.Install(files, Str(a, "cat"), S.ModCats));
                if (temp != null) try { Directory.Delete(temp, true); } catch { }
                S.Save();
                if (count > 0) Log($"🧩 {count} Mod(s) aus Downloads installiert");
                return new { count, error = err };
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
                var was = S.Pad.On;
                S.Pad = a.GetProperty("cfg").Deserialize<PadCfg>(JIn) ?? new PadCfg();
                S.Save();
                if (a.TryGetProperty("art", out var art) && art.ValueKind == JsonValueKind.Object)
                    foreach (var pr in art.EnumerateObject())
                        if (pr.Value.GetString() is { Length: > 100 } b64)
                        {
                            var bi = new System.Windows.Media.Imaging.BitmapImage();
                            bi.BeginInit(); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(Convert.FromBase64String(b64)); bi.EndInit(); bi.Freeze();
                            _pad.View.Art[pr.Name] = bi;
                        }
                if (a.TryGetProperty("dyn", out var dyn) && dyn.ValueKind == JsonValueKind.Object)
                    foreach (var pr in dyn.EnumerateObject())
                        _pad.View.Dyn[pr.Name] = pr.Value.Deserialize<List<PadDyn>>(JIn) ?? new();
                _pad.Configure(S.Pad, S.Pad.Layout == "auto" ? (PadInput.State.T == "ps" ? "ps" : "xbox") : S.Pad.Layout);
                var keys = RegisterKeys();
                LiveTick();
                if (was != S.Pad.On) Log(S.Pad.On ? "🎮 Controller-Anzeige an" : "🎮 Controller-Anzeige aus");
                return new { keyOk = keys.pad };
            }
            case "pad.watch":
            {
                var on = Bool(a, "on");
                if (on != _padWatch) { _padWatch = on; PadInput.Use(on); if (on) PadInput.Changed += PadToUi; else PadInput.Changed -= PadToUi; }
                return new { state = PadInput.State };
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
                        if (fix.StartsWith("tweak:")) { var t = _tweaks.First(x => x.Id == fix[6..]); t.Apply(); return t.SafeCheck(); }
                        if (fix == "cache") { if (FiveM.IsRunning) return false; Cleaner.Clean(Target("fm")); return true; }
                        if (fix == "killbg") { FiveM.KillBackgroundApps(); return true; }
                        if (fix == "clean") { Cleaner.Clean(Target("temp")); Cleaner.Clean(Target("wu")); return true; }
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
        bool game = GameFocus.IsGame();
        SnapTap.GameInFront = game;
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
        _minimap.Update(S.Minimap.On && (game || _mmPreview));
        _flash.Update(DateTime.Now < _flashUntil);
        _pad.Update(S.Pad.On);
        // re-apply when the game gets focus (fullscreen games may reset the gamma ramp)
        bool focusGained = game && !_lastGame;
        _lastGame = game;
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
        _xhair.Close(); _minimap.Close(); _snap.Enable(false); _pad.Close();
        ScreenColor.Restore(); NvVibrance.Restore();
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) { UnregisterHotKey(h, HK_XHAIR); UnregisterHotKey(h, HK_DISPLAY); UnregisterHotKey(h, HK_CLIP); }
        _flash.Close();
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
        return (x, d, c, pk);
    }

    private void PadToUi()
    {
        if (DateTime.Now - _padEmit < TimeSpan.FromMilliseconds(33)) return;
        _padEmit = DateTime.Now;
        Emit("pad", PadInput.State);
    }

    private async Task SaveClip()
    {
        await Clipper.Save(S.Clip.Recorder);
        Log($"🎬 Clip gespeichert ({Clipper.Recorders[S.Clip.Recorder].label})");
        Emit("clip", new { recorder = S.Clip.Recorder });
        if (S.Clip.Flash) _flashUntil = DateTime.Now.AddSeconds(1.8);
        LiveTick();
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
            S.Pad.On = !S.Pad.On; S.Save(); LiveTick();
            Emit("padon", new { on = S.Pad.On });
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
        return IntPtr.Zero;
    }

    private static CleanTarget Target(string id) => Cleaner.Targets().First(t => t.Id == id);
    private static object FiveMInfo() => new { found = FiveM.Root != null, path = FiveM.Root };

    private async Task<object> TweakList()
    {
        var states = await Task.Run(() => _tweaks.Select(t => t.SafeCheck()).ToList());
        return _tweaks.Select((t, i) => new { id = t.Id, cat = t.Cat, name = t.Name, desc = t.Desc, icon = t.Icon, badge = t.Badge, rec = t.Recommended, applied = states[i] }).ToList();
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
        var todo = await Task.Run(() => _tweaks.Where(t => t.Recommended && !t.SafeCheck()).ToList());
        foreach (var t in todo)
            steps.Add((t.Name + "…", () =>
            {
                try { t.Apply(); if (t.SafeCheck()) { applied++; if (t.NeedsRestart) restart = true; } }
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
        if (Pre("prio") && !S.AutoPriority) { S.AutoPriority = true; S.Save(); }

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
    }

    private async Task ProbeFiveM()
    {
        _probing = true;
        try
        {
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
                            if (S.AutoPriority && game.PriorityClass is not (ProcessPriorityClass.High or ProcessPriorityClass.RealTime))
                            {
                                game.PriorityClass = ProcessPriorityClass.High;
                                Log("⚡ Auto-Priorität: GTA-Prozess auf HOCH");
                            }
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

                        if (S.AutoRamClean && DateTime.Now - _lastAutoClean > TimeSpan.FromMinutes(5))
                        {
                            _lastAutoClean = DateTime.Now;
                            var f = Native.CleanMemory(S.PurgeStandby);
                            Log($"♻ Auto-RAM-Clean: {SysInfo.Bytes(f)}");
                        }
                    }
                    if (game != null && S.AutoBorderless)
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
                    return (object)new { running = procs.Length > 0, game = game != null, build, prio, gameRam = ram };
                }
                finally { foreach (var p in procs) p.Dispose(); }
            });
        }
        finally { _probing = false; }
    }
}
