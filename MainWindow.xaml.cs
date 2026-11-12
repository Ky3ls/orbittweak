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
        };
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        Loaded += async (_, _) => await InitWeb();
        Closing += (_, _) => { S.Save(); Catalog.SetTimerResolution(false); };

        if (S.TimerRes) Catalog.SetTimerResolution(true);
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
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(data, "WebView2"));
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
            c.SetVirtualHostNameToFolderMapping("orbit.local", ExtractWeb(Path.Combine(data, "web")), CoreWebView2HostResourceAccessKind.Allow);
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
                    return (object)new { running = procs.Length > 0, game = game != null, build, prio, gameRam = ram };
                }
                finally { foreach (var p in procs) p.Dispose(); }
            });
        }
        finally { _probing = false; }
    }
}
