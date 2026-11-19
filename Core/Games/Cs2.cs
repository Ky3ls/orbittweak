using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace OrbitTweak.Core;

/// <summary>
/// Counter-Strike 2 (2.30). Pure rules: Core/Games/Cs2Logic.cs.
///
/// Anti-cheat (VAC): only cs2_video.txt and cs2_machine_convars.vcfg of the Steam account are changed, the two files the
/// settings menu of CS2 writes itself, plus the Windows graphics setting of cs2.exe. No game file, nothing of the running
/// game, no launch options; Trusted Mode stays on. Both files are saved by CS2 when it closes, so every file item needs
/// it closed. Backups: HKCU\Software\OrbitTweak\Backup, values "og.cs2.*" (og.cs2.dir = the cfg folder they belong to),
/// the GPU setting under the subkey "og.cs2.gpu" (GpuPrefs).
///
/// Left out on purpose (placebo or not a menu setting): launch options such as -high, -threads, -tickrate, -novid,
/// -nojoy, -vulkan, -allow_third_party_software (Trust Factor), autoexec lines such as cl_updaterate, cl_interp or
/// mat_queue_mode (not in CS2 any more), fullscreen optimizations, Nagle/TCP settings (CS2 plays over UDP).
/// </summary>
internal sealed class Cs2Tweaks : IGameTweaks
{
    public string Id => "cs2";
    public string Name => "Counter-Strike 2";

    private const string BkPre = "og.cs2.";
    private const string DirKey = BkPre + "dir", GpuBk = BkPre + "gpu";
    private static readonly object Gate = new();

    /// <summary>One file setting: item, backup key, file, key and whether it is a convar (name$N versions).</summary>
    private sealed record Field(string Item, string Backup, string File, string Key, bool Convar)
    {
        public IReadOnlyList<string> Block => Convar ? Cs2Logic.ConvarBlock : Cs2Logic.VideoBlock;
    }

    private static readonly Field[] Fields =
    {
        new("reflex", BkPre + "reflex", Cs2Logic.VideoFile, Cs2Logic.KeyReflex, false),
        new("vsync", BkPre + "vsync", Cs2Logic.VideoFile, Cs2Logic.KeyVsync, false),
        new("fpsmax", BkPre + "fpsmax", Cs2Logic.ConvarFile, Cs2Logic.CvFps, true),
        new("contrast", BkPre + "contrast", Cs2Logic.ConvarFile, Cs2Logic.CvContrast, true),
        new("netbuffer", BkPre + "netbuf", Cs2Logic.ConvarFile, Cs2Logic.CvNetBuffer, true),
        new("telemetry", BkPre + "telemetry.fps", Cs2Logic.ConvarFile, Cs2Logic.CvTeleFps, true),
        new("telemetry", BkPre + "telemetry.ping", Cs2Logic.ConvarFile, Cs2Logic.CvTelePing, true),
    };

    private const string AntiCheatNote =
        "Valve Anti-Cheat (VAC): OrbitTweak schreibt nur in cs2_video.txt und cs2_machine_convars.vcfg, also genau die Dateien, " +
        "die das Einstellungsmenü von CS2 selbst schreibt, und setzt eine Windows-Grafikeinstellung. Es ändert keine Spieldateien " +
        "und nichts am laufenden Spiel, setzt keine Startoptionen, und der Trusted Mode bleibt an.";

    // ================================================================== where CS2 is

    private sealed class Where
    {
        public string? Steam, Install, Exe, Cfg;
        public bool ConfigFound, FromEnv, HeldOther;
        public string? Who;
    }

    private static Where Find()
    {
        var w = new Where { Steam = SteamDir() };
        w.Install = InstallDir(w.Steam);
        if (w.Install != null && Exists(Path.Combine(w.Install, "game", "bin", "win64", "cs2.exe")))
            w.Exe = Path.Combine(w.Install, "game", "bin", "win64", "cs2.exe");

        var (detected, fromEnv) = DetectCfg(w.Steam, out var users);
        string? stored;
        bool backed;
        lock (Gate)
        {
            DropStale();
            stored = Reg.Get(Bk.User, DirKey) as string;
            backed = AnyFileBackup();
        }
        w.Cfg = Cs2Logic.PickCfg(stored, backed, detected);
        w.FromEnv = fromEnv && string.Equals(w.Cfg, detected, StringComparison.OrdinalIgnoreCase);
        w.HeldOther = w.Cfg != null && detected != null && !string.Equals(w.Cfg, detected, StringComparison.OrdinalIgnoreCase);
        if (Cs2Logic.AccountOfCfg(w.Cfg) is { } acc) w.Who = Cs2Logic.Who(acc, users.FirstOrDefault(u => u.Account == acc)?.Persona);
        w.ConfigFound = w.Cfg != null && Exists(Path.Combine(w.Cfg, Cs2Logic.VideoFile)) && Exists(Path.Combine(w.Cfg, Cs2Logic.ConvarFile));
        return w;
    }

    private static bool Exists(string p) { try { return File.Exists(p); } catch { return false; } }

    private static string? ReadSmall(string p) { try { return File.Exists(p) ? File.ReadAllText(p) : null; } catch { return null; } }

    /// <summary>HKCU SteamPath (forward slashes), else the InstallPath of the Steam installer; steam.exe has to be there.</summary>
    private static string? SteamDir()
    {
        foreach (var raw in new[]
                 {
                     Reg.Get(@"HKCU\Software\Valve\Steam", "SteamPath") as string,
                     Reg.Get(@"HKLM\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") as string,
                     Reg.Get(@"HKLM\SOFTWARE\Valve\Steam", "InstallPath") as string,
                 })
            if (Cs2Logic.SteamPath(raw) is { } p && Exists(Path.Combine(p, "steam.exe"))) return p;
        return null;
    }

    /// <summary>
    /// The CS2 folder: every Steam library (the one whose libraryfolders.vdf entry lists app 730 first) with its
    /// appmanifest_730.acf, else Steam's uninstall entry "Steam App 730". Steam is a 32-bit program, so that entry can
    /// sit in either registry view. CS2 is there when game\bin\win64\cs2.exe or game\csgo\steam.inf is.
    /// </summary>
    private static string? InstallDir(string? steam)
    {
        static bool IsCs2(string dir) => Exists(Path.Combine(dir, "game", "bin", "win64", "cs2.exe")) || Exists(Path.Combine(dir, "game", "csgo", "steam.inf"));
        var libs = new List<string>();
        if (steam != null)
        {
            foreach (var f in new[] { Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Path.Combine(steam, "config", "libraryfolders.vdf") })
            {
                var l = Cs2Logic.Libraries(ReadSmall(f));
                if (l.Count > 0) { libs.AddRange(l); break; }
            }
            libs.Add(steam);
        }
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var apps = Path.Combine(lib, "steamapps");
                var (dirName, _) = Cs2Logic.Manifest(ReadSmall(Path.Combine(apps, "appmanifest_730.acf")));
                var inst = Path.Combine(apps, "common", dirName ?? "Counter-Strike Global Offensive");
                if (IsCs2(inst)) return inst;
            }
            catch { }
        }
        foreach (var k in new[]
                 {
                     @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 730",
                     @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 730",
                 })
        {
            var loc = (Reg.Get(k, "InstallLocation") as string)?.Trim().Trim('"').TrimEnd('\\');
            try { if (!string.IsNullOrEmpty(loc) && IsCs2(loc)) return loc; } catch { }
        }
        return null;
    }

    /// <summary>
    /// The cfg folder CS2 reads: %USRLOCALCSGO%\cfg when that variable is set (engine2: "USRLOCAL path using environment
    /// setting"), else &lt;Steam&gt;\userdata\&lt;account&gt;\730\local\cfg of the account <see cref="Cs2Logic.PickAccount"/> picks.
    /// </summary>
    private static (string? cfg, bool fromEnv) DetectCfg(string? steam, out List<Cs2Logic.SteamUser> users)
    {
        users = new();
        var env = Environment.GetEnvironmentVariable("USRLOCALCSGO", EnvironmentVariableTarget.User)
                  ?? Environment.GetEnvironmentVariable("USRLOCALCSGO", EnvironmentVariableTarget.Machine);
        if (steam != null) users = Cs2Logic.LoginUsers(ReadSmall(Path.Combine(steam, "config", "loginusers.vdf")));
        if (!string.IsNullOrWhiteSpace(env))
        {
            try { return (Path.Combine(Environment.ExpandEnvironmentVariables(env.Trim().Trim('"')), "cfg"), true); }
            catch { }
        }
        if (steam == null) return (null, false);

        var userdata = Path.Combine(steam, "userdata");
        var withSettings = new List<(uint, DateTime)>();
        try
        {
            if (Directory.Exists(userdata))
                foreach (var d in Directory.GetDirectories(userdata))
                {
                    if (!uint.TryParse(Path.GetFileName(d), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0) continue;
                    var v = Path.Combine(d, "730", "local", "cfg", Cs2Logic.VideoFile);
                    if (Exists(v)) withSettings.Add((id, File.GetLastWriteTimeUtc(v)));
                }
        }
        catch { }
        uint active = Reg.Get(@"HKCU\Software\Valve\Steam\ActiveProcess", "ActiveUser") is int a ? unchecked((uint)a) : 0;
        var acc = Cs2Logic.PickAccount(active, Cs2Logic.MostRecent(users), withSettings);
        return acc is { } x ? (Path.Combine(userdata, x.ToString(CultureInfo.InvariantCulture), "730", "local", "cfg"), false) : (null, false);
    }

    /// <summary>cs2.exe runs (the one exe of DirectX 11 and Vulkan).</summary>
    private static bool Running()
    {
        try
        {
            var ps = Process.GetProcessesByName("cs2");
            foreach (var p in ps) p.Dispose();
            return ps.Length > 0;
        }
        catch { return false; }
    }

    private static bool AnyFileBackup() => Fields.Any(f => Bk.Has(f.Backup));

    /// <summary>
    /// Drops the file backups whose value means the same as the file again (<see cref="Cs2Logic.Stale"/>: CS2's own menu
    /// set it back, or CS2 reset the file), and the backups of a file that is gone (the account's userdata folder or
    /// cs2_video.txt deleted: nothing left to restore, Revert() drops them the same way); then og.cs2.dir once no file
    /// backup is left. Otherwise such a backup would keep the page on the account OrbitTweak changed although nothing
    /// there counts as changed, and Zurücksetzen would be greyed out. A file that is there but cannot be read, or that CS2
    /// is writing right now, keeps its backups; a gone file keeps them while CS2 runs (it may be saving it just then).
    /// Callers hold Gate, so a backup Write() just saved for the file it is about to write is never taken for a stale one.
    /// </summary>
    private static void DropStale()
    {
        var dir = Reg.Get(Bk.User, DirKey) as string;
        if (!string.IsNullOrWhiteSpace(dir))
        {
            bool? running = null;
            foreach (var file in Fields.Where(f => Bk.Has(f.Backup)).GroupBy(f => f.File))
            {
                var path = Path.Combine(dir, file.Key);
                if (Gone(path))
                {
                    if (!(running ??= Running())) foreach (var f in file) Bk.Drop(f.Backup);
                    continue;
                }
                string? text = null;
                try { text = ReadText(path).text; } catch { }
                foreach (var f in file)
                    if (Reg.Get(Bk.User, f.Backup) is string b && Cs2Logic.Stale(text, f.Block, f.Key, f.Convar, b)) Bk.Drop(f.Backup);
            }
        }
        if (dir != null && !AnyFileBackup()) Bk.Drop(DirKey);
    }

    /// <summary>The file is not there: Windows says so (file or folder not found). A file it only cannot get at (no
    /// access, a path it refuses) is not gone, so its backups stay.</summary>
    private static bool Gone(string path)
    {
        try { File.GetAttributes(path); return false; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return true; }
        catch { return false; }
    }

    // ================================================================== the page

    public GameInfo Info()
    {
        var w = Find();
        bool running = Running();
        return new GameInfo
        {
            Id = Id, Name = Name, Installed = w.Install != null, Path = w.Install, Running = running,
            ConfigFound = w.ConfigFound, ConfigPath = w.Cfg,
            Note = Cs2Logic.Note(w.Install != null, w.ConfigFound, running, w.Who, w.FromEnv, w.HeldOther),
            AntiCheat = AntiCheatNote,
        };
    }

    public List<GameItem> Items()
    {
        var w = Find();
        string? video = null, conv = null;
        if (w.Cfg != null)
        {
            // also when one of the two is missing: what OrbitTweak changed in the other one still shows as changed, so
            // Zurücksetzen can bring it back and let the page follow the account in use again
            try { video = ReadText(Path.Combine(w.Cfg, Cs2Logic.VideoFile)).text; } catch { }
            try { conv = ReadText(Path.Combine(w.Cfg, Cs2Logic.ConvarFile)).text; } catch { }
        }
        bool files = w.ConfigFound && video != null && conv != null;
        string fileWhy = files ? ""
                       : w.ConfigFound ? "Die Einstellungsdateien von CS2 lassen sich gerade nicht lesen."
                       : Cs2Logic.NoFiles(w.Install != null, w.HeldOther && Running(), w.HeldOther, w.Who);

        string? V(string key) => video == null ? null : Cs2Logic.Get(video, Cs2Logic.VideoBlock, key, false);
        string? C(string key) => conv == null ? null : Cs2Logic.Get(conv, Cs2Logic.ConvarBlock, key, true);
        bool IsChanged(string item) => Fields.Where(f => f.Item == item).Any(f =>
            Reg.Get(Bk.User, f.Backup) is string b && Cs2Logic.Differs(f.Key, b, f.Convar ? C(f.Key) : V(f.Key)));

        var vendor = Cs2Logic.Vendor(V(Cs2Logic.KeyVendor));
        bool lowLatency = Cs2Logic.LowLatencySupported(vendor, V(Cs2Logic.KeyDevice));
        var reflex = Cs2Logic.Reflex(V(Cs2Logic.KeyReflex), vendor);
        bool gpuBacked = GpuPrefs.Backed(GpuBk).Length > 0;

        return new List<GameItem>
        {
            new()
            {
                Id = "reflex", Title = Cs2Logic.ReflexTitle(vendor), Kind = "choice",
                Sum = Cs2Logic.ReflexSum(vendor),
                Desc = "Schaltet die Latenzbremse, die in CS2 eingebaut ist (Einstellungen › Video › Erweitert › Frame-Pacing). Mit einer NVIDIA-Karte ist das Reflex, mit AMD ist es Anti-Lag 2. CS2 lässt dann keine Bilder mehr in der Warteschlange der Grafikkarte warten. Das senkt die Systemlatenz vor allem, wenn deine Grafikkarte voll ausgelastet ist (NVIDIA nennt bis zu 35 %), und kostet praktisch keine FPS. Standard ist „An“. „An + Boost“ gibt es nur bei NVIDIA: Es hält den Grafiktakt zusätzlich hoch. Das drückt die Verzögerung noch etwas weiter, kann laut Valve aber mehr Strom ziehen und die FPS leicht senken. Es lohnt sich also nur, wenn dir die niedrigste Latenz wichtiger ist als die höchsten FPS. Reflex im Spiel ersetzt den „Modus für niedrige Latenz“ im NVIDIA-Treiber. Du brauchst NVIDIA ab GTX 900 oder AMD ab RX 5000 mit aktuellem Treiber; mit Intel-Grafik bietet CS2 die Option nicht an. Wenn dir damit Ruckler auffallen, schalte es testweise aus.",
                Value = reflex, Options = Cs2Logic.ReflexOptions(vendor).Select(o => new GameOption(o.v, o.t)).ToList(),
                Rec = true, RecValue = Cs2Logic.ReflexRec(reflex), Effect = "latency", NeedsClosed = true,
                Source = "Eigene CS2-Einstellung (setting.r_low_latency). Tooltip von Valve: Boost kann mehr Strom ziehen und FPS kosten. NVIDIA: bis zu 35 % weniger Systemlatenz in CS2. AMD Anti-Lag 2 ist seit Treiber 24.6.1 in CS2 eingebaut.",
                Available = files && lowLatency,
                Why = !files ? fileWhy : lowLatency ? "" : Cs2Logic.LowLatencyWhy(vendor),
                Changed = IsChanged("reflex"),
            },
            new()
            {
                Id = "vsync", Title = "V-Sync", Kind = "switch",
                Sum = "Aus für die geringste Verzögerung, an nur zusammen mit G-Sync und Reflex",
                Desc = "Mit V-Sync wartet CS2 auf den Bildaufbau deines Monitors. Das verhindert Tearing (zerrissene Bilder), bringt aber ohne G-Sync oder FreeSync deutlich mehr Eingabeverzögerung. Deshalb ist es standardmäßig aus, und für die meisten bleibt es so am besten. Die Ausnahme: Mit einem G-Sync-Monitor und einer NVIDIA-Karte empfiehlt Valve im Spiel selbst, G-Sync, V-Sync und Reflex zusammen einzuschalten. Reflex begrenzt die FPS dann knapp unter deine Hz-Zahl. Du bekommst ein ruhiges Bild ohne Tearing bei kaum mehr Verzögerung, aber keine FPS mehr über der Bildwiederholrate. G-Sync selbst schaltest du im NVIDIA-Treiber ein, nicht hier.",
                Value = Cs2Logic.On(V(Cs2Logic.KeyVsync), false),
                Rec = false, Effect = "latency", NeedsClosed = true,
                Source = "Valve-Update vom 25.06.2024 und Hinweis im Spiel: G-Sync, V-Sync und Reflex zusammen empfohlen, sonst V-Sync aus.",
                Available = files, Why = fileWhy, Changed = IsChanged("vsync"),
            },
            new()
            {
                Id = "fpsmax", Title = "FPS-Limit im Spiel", Kind = "number",
                Sum = "Obergrenze für deine FPS im Match, 0 heißt kein Limit",
                Desc = "Das ist „Maximale FPS im Spiel“ aus den erweiterten Videoeinstellungen (fps_max), Standard ist 400. Mehr Bilder pro Sekunde verkürzen die Zeit bis zum nächsten Bild und damit die Verzögerung ein Stück, auch über deiner Hz-Zahl. Ein Limit, das dein PC sicher schafft, hält dafür die Frametimes gleichmäßiger und spart Strom, Hitze und Lüfterlärm. Das lohnt sich vor allem auf Laptops. 0 hebt das Limit ganz auf; im Spielmenü sind 64 bis 1000 einstellbar. Nutzt du G-Sync, V-Sync und Reflex zusammen, begrenzt CS2 ohnehin knapp unter deiner Hz-Zahl, dann ändert ein höheres Limit nichts.",
                Value = Cs2Logic.FpsMax(C(Cs2Logic.CvFps)), Min = 0, Max = 1000, Step = 1, Unit = "FPS",
                Rec = false, Effect = "fps", NeedsClosed = true,
                Source = "Valve-Update vom 25.06.2024 (Regler für fps_max). Standard 400 laut gameinfo.gi von CS2.",
                Available = files, Why = fileWhy, Changed = IsChanged("fpsmax"),
            },
            new()
            {
                Id = "contrast", Title = "Spielerkontrast verstärken", Kind = "switch",
                Sum = "Gegner heben sich in dunklen und kontrastarmen Ecken besser ab",
                Desc = "Das ist eine Option von CS2 selbst (Video › Erweitert): Ein Filter macht Spielermodelle vor kontrastarmem Hintergrund besser erkennbar. Sie ist standardmäßig an. Valve schreibt selbst, dass sie Grafikleistung kosten kann. Saubere Messungen dazu gibt es kaum; Einstellungs-Guides nennen höchstens 1 bis 2 % weniger FPS. Wenn auf einem sehr schwachen PC jedes Bild zählt, kannst du sie ausschalten. Dann siehst du Gegner in dunklen Ecken aber etwas schlechter.",
                Value = Cs2Logic.On(C(Cs2Logic.CvContrast), true),
                Rec = true, RecValue = true, Effect = "comfort", NeedsClosed = true,
                Source = "Tooltip von Valve im Spiel: bessere Erkennbarkeit, kann Leistung kosten. Standard an.",
                Available = files, Why = fileWhy, Changed = IsChanged("contrast"),
            },
            new()
            {
                Id = "netbuffer", Title = "Puffer gegen Paketverlust", Kind = "choice",
                Sum = "Nur bei wackliger Verbindung: weniger Aussetzer, dafür mehr Ping",
                Desc = "Das ist „Pufferung gegen Paketverlust/Jitter“ aus den Spieleinstellungen (cl_net_buffer_ticks). CS2 puffert dann 1 oder 2 Ticks an Daten zwischen dir und dem Server. Laut Valve fallen so weniger Ticks aus, dafür steigt deine effektive Ping-Zeit: bei 64 Ticks pro Sekunde um rund 16 ms je Tick. Das lohnt sich nur, wenn die Telemetrie-Anzeige im Spiel verpasste Ticks oder Paketverlust zeigt, typisch bei WLAN. Mit einer stabilen Verbindung lässt du es auf „Keiner“ (Standard).",
                Value = Cs2Logic.Level(C(Cs2Logic.CvNetBuffer), 0),
                Options = Cs2Logic.NetBufferOptions().Select(o => new GameOption(o.v, o.t)).ToList(),
                Rec = false, Effect = "stability", NeedsClosed = true,
                Source = "Tooltip von Valve im Spiel: weniger verpasste Ticks, dafür höherer effektiver Ping.",
                Available = files, Why = fileWhy, Changed = IsChanged("netbuffer"),
            },
            new()
            {
                Id = "telemetry", Title = "FPS- und Ping-Anzeige", Kind = "choice",
                Sum = "CS2 blendet FPS und Ping selbst im HUD ein",
                Desc = "Schaltet die Telemetrie, die in CS2 eingebaut ist (Einstellungen › Spiel › Telemetrie), für „Frametime und FPS“ und für „Ping“: nie, nur bei schlechten Werten (Standard) oder immer. Das bringt selbst keine FPS. Du siehst aber, ob deine anderen Einstellungen etwas bewirken, und das ohne Overlay-Programme von Drittanbietern, die der Trusted Mode von CS2 oft blockiert.",
                Value = Cs2Logic.Level(C(Cs2Logic.CvTeleFps), 1),
                Options = Cs2Logic.TelemetryOptions().Select(o => new GameOption(o.v, o.t)).ToList(),
                Rec = false, Effect = "comfort", NeedsClosed = true,
                Source = "CS2-Einstellung Spiel › Telemetrie. Laut Steam-Support blockiert der Trusted Mode Dateien von Drittprogrammen, die auf das Spiel zugreifen.",
                Available = files, Why = fileWhy, Changed = IsChanged("telemetry"),
            },
            new()
            {
                Id = "gpu", Title = "Starke Grafikkarte für CS2", Kind = "switch",
                Sum = "Bei zwei Grafikchips läuft CS2 sicher auf der starken Karte",
                Desc = "Trägt cs2.exe in den Windows-Grafikeinstellungen auf „Hohe Leistung“ ein. Auf Laptops und PCs mit internem Grafikchip und zusätzlicher Grafikkarte verhindert das, dass CS2 versehentlich auf dem schwachen Chip läuft und du nur einen Bruchteil der FPS bekommst. Meist wählen Windows oder der Treiber die starke Karte schon selbst, dann ändert sich nichts. Mit nur einer Grafikkarte ist die Einstellung wirkungslos. Sie gilt ab dem nächsten Spielstart.",
                Value = w.Exe != null && GpuPrefs.Get(w.Exe, "GpuPreference") == "2",
                Rec = true, RecValue = true, Effect = "fps", NeedsClosed = false,
                Source = "Windows-Grafikeinstellungen (Microsoft), derselbe Weg wie beim FiveM-Tweak. Wirkt nur mit zwei Grafikchips.",
                Available = w.Exe != null && (Hw.SwitchableGpus || gpuBacked),
                Why = w.Exe == null ? Cs2Logic.NotFound : Hw.SwitchableGpus || gpuBacked ? "" : "Nur eine Grafikkarte erkannt: hier ändert sich nichts",
                Changed = gpuBacked,
            },
        };
    }

    // ================================================================== changes

    public object? Set(string itemId, JsonElement value)
    {
        lock (Gate)
        {
            var w = Find();
            if (itemId == "gpu") return SetGpu(w, Bool(value));
            if (!Fields.Any(f => f.Item == itemId)) throw new InvalidOperationException("Unbekannte Einstellung.");
            if (!w.ConfigFound || w.Cfg == null)
                throw new InvalidOperationException(Cs2Logic.NoFiles(w.Install != null, w.HeldOther && Running(), w.HeldOther, w.Who));
            if (Running()) throw new InvalidOperationException(Cs2Logic.CloseFirst);

            var videoPath = Path.Combine(w.Cfg, Cs2Logic.VideoFile);
            var convPath = Path.Combine(w.Cfg, Cs2Logic.ConvarFile);
            switch (itemId)
            {
                case "reflex":
                {
                    var video = ReadText(videoPath).text;
                    var vendor = Cs2Logic.Vendor(Cs2Logic.Get(video, Cs2Logic.VideoBlock, Cs2Logic.KeyVendor, false));
                    var v = Str(value);
                    if (Cs2Logic.ReflexError(v, vendor, Cs2Logic.Get(video, Cs2Logic.VideoBlock, Cs2Logic.KeyDevice, false)) is { } e) throw new InvalidOperationException(e);
                    Write(w.Cfg, F("reflex"), v);
                    return Cs2Logic.Reflex(Cs2Logic.Get(ReadText(videoPath).text, Cs2Logic.VideoBlock, Cs2Logic.KeyReflex, false), vendor);
                }
                case "vsync":
                    Write(w.Cfg, F("vsync"), Bool(value) ? "1" : "0");
                    return Cs2Logic.On(Cs2Logic.Get(ReadText(videoPath).text, Cs2Logic.VideoBlock, Cs2Logic.KeyVsync, false), false);
                case "fpsmax":
                {
                    var n = Num(value);
                    if (Cs2Logic.FpsError(n) is { } e) throw new InvalidOperationException(e);
                    Write(w.Cfg, F("fpsmax"), Cs2Logic.FpsText(n));
                    return Cs2Logic.FpsMax(Cs2Logic.Get(ReadText(convPath).text, Cs2Logic.ConvarBlock, Cs2Logic.CvFps, true));
                }
                case "contrast":
                    Write(w.Cfg, F("contrast"), Bool(value) ? "1" : "0");
                    return Cs2Logic.On(Cs2Logic.Get(ReadText(convPath).text, Cs2Logic.ConvarBlock, Cs2Logic.CvContrast, true), true);
                case "netbuffer":
                {
                    var v = Str(value);
                    if (Cs2Logic.LevelError(v, Cs2Logic.NetBufferRange) is { } e) throw new InvalidOperationException(e);
                    Write(w.Cfg, F("netbuffer"), v);
                    return Cs2Logic.Level(Cs2Logic.Get(ReadText(convPath).text, Cs2Logic.ConvarBlock, Cs2Logic.CvNetBuffer, true), 0);
                }
                default: // telemetry: FPS and ping together
                {
                    var v = Str(value);
                    if (Cs2Logic.LevelError(v, Cs2Logic.TelemetryRange) is { } e) throw new InvalidOperationException(e);
                    Write(w.Cfg, Fields.Where(f => f.Item == "telemetry").ToArray(), v);
                    return Cs2Logic.Level(Cs2Logic.Get(ReadText(convPath).text, Cs2Logic.ConvarBlock, Cs2Logic.CvTeleFps, true), 1);
                }
            }
        }
    }

    private static Field[] F(string item) => Fields.Where(f => f.Item == item).ToArray();

    /// <summary>
    /// Sets the keys of one item in their file (one read, one write). Before the first change of a key its old value goes
    /// to the backup; og.cs2.dir remembers the folder when it is the first backup of all. A value that means what was
    /// there before puts the file back exactly and drops that backup.
    /// </summary>
    private static void Write(string cfg, Field[] fields, string value)
    {
        var path = Path.Combine(cfg, fields[0].File);
        var (text, bom) = ReadText(path);
        var next = text;
        var backups = new List<(string key, string raw)>();
        var drops = new List<string>();
        foreach (var f in fields)
        {
            var had = Reg.Get(Bk.User, f.Backup) as string;
            var backup = had ?? Cs2Logic.BackupOf(Cs2Logic.Get(next, f.Block, f.Key, f.Convar));
            var (n, drop) = Cs2Logic.Apply(next, f.Block, f.Key, f.Convar, value, backup);
            next = n ?? throw new InvalidOperationException($"{f.File} sieht anders aus als erwartet. " + Cs2Logic.StartOnce);
            if (drop) { if (had != null) drops.Add(f.Backup); }
            else if (had == null) backups.Add((f.Backup, backup));
        }
        if (next != text) CheckWritable(path);
        if (backups.Count > 0)
        {
            if (!AnyFileBackup()) Reg.SetStr(Bk.User, DirKey, cfg);
            foreach (var (k, raw) in backups) Reg.SetStr(Bk.User, k, raw);
        }
        if (next != text) WriteText(path, next, bom);
        foreach (var k in drops) Bk.Drop(k);
        if (!AnyFileBackup()) Bk.Drop(DirKey);
    }

    /// <summary>
    /// On sets cs2.exe to "high performance", off brings back what was there before (or "let Windows decide"). On after an
    /// off that replaced a "high performance" the user had set himself puts that back and drops the backup.
    /// </summary>
    private static bool SetGpu(Where w, bool on)
    {
        if (w.Exe is not { } exe) throw new InvalidOperationException(Cs2Logic.NotFound);
        if (!Hw.SwitchableGpus && GpuPrefs.Backed(GpuBk).Length == 0) throw new InvalidOperationException("Nur eine Grafikkarte erkannt: hier ändert sich nichts.");
        if (on)
        {
            if ((Reg.Get($@"{Bk.User}\{GpuBk}", exe) as string) == "2") GpuPrefs.RestoreField(exe, "GpuPreference", GpuBk);
            else if (GpuPrefs.Get(exe, "GpuPreference") != "2") GpuPrefs.SetField(exe, "GpuPreference", "2", GpuBk);
        }
        else
        {
            if (GpuPrefs.Backed(GpuBk).Contains(exe, StringComparer.OrdinalIgnoreCase)) GpuPrefs.RestoreField(exe, "GpuPreference", GpuBk);
            if (GpuPrefs.Get(exe, "GpuPreference") == "2") GpuPrefs.SetField(exe, "GpuPreference", "0", GpuBk);
        }
        return GpuPrefs.Get(exe, "GpuPreference") == "2";
    }

    public int Revert()
    {
        lock (Gate)
        {
            int n = 0;
            var dir = Reg.Get(Bk.User, DirKey) as string;
            var files = string.IsNullOrWhiteSpace(dir)
                ? new List<IGrouping<string, Field>>()
                : Fields.Where(f => Bk.Has(f.Backup)).GroupBy(f => f.File).Where(g => Exists(Path.Combine(dir, g.Key))).ToList();
            if (files.Count > 0 && Running()) throw new InvalidOperationException(Cs2Logic.CloseFirst);

            if (GpuPrefs.Backed(GpuBk).Length > 0)
            {
                GpuPrefs.RestoreAll("GpuPreference", GpuBk, null);
                n++;
            }
            var back = new HashSet<string>();
            foreach (var file in files)
            {
                var path = Path.Combine(dir!, file.Key);
                var (text, bom) = ReadText(path);
                var next = text;
                foreach (var f in file)
                {
                    var b = Reg.Get(Bk.User, f.Backup) as string ?? Cs2Logic.Missing;
                    var cur = Cs2Logic.Get(next, f.Block, f.Key, f.Convar);
                    if (Cs2Logic.BackupOf(cur) == b) continue;
                    // the exact old bytes come back; it only counts when the value meant something else
                    var r = Cs2Logic.Restore(next, f.Block, f.Key, f.Convar, b);
                    if (r != next && Cs2Logic.Differs(f.Key, b, cur)) back.Add(f.Item);
                    next = r;
                }
                if (next != text) { CheckWritable(path); WriteText(path, next, bom); }
                foreach (var f in file) Bk.Drop(f.Backup);
            }
            n += back.Count;
            // backups of a file or folder that is gone (account or USRLOCALCSGO folder removed): nothing left to restore
            foreach (var f in Fields) Bk.Drop(f.Backup);
            Bk.Drop(DirKey);
            return n;
        }
    }

    // ================================================================== files and values

    /// <summary>
    /// The text of a settings file (one char per byte) and whether it starts with a UTF-8 BOM (kept on write). A file that
    /// cannot be read (gone, locked, no access) is a German error.
    /// </summary>
    private static (string text, bool bom) ReadText(string path)
    {
        try { return Cs2Logic.Decode(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} ließ sich nicht lesen: {ex.Message}");
        }
    }

    private static void CheckWritable(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            throw new InvalidOperationException($"{Path.GetFileName(path)} ist schreibgeschützt. Nimm den Schreibschutz in den Eigenschaften der Datei weg, dann klappt es.");
    }

    /// <summary>Writes "file.orbit.tmp" and moves it over the file, so a failed write never leaves half a settings file.</summary>
    private static void WriteText(string path, string text, bool bom)
    {
        var data = Cs2Logic.Encode(text, bom);
        var tmp = path + ".orbit.tmp";
        try
        {
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch { }
            throw new InvalidOperationException($"{Path.GetFileName(path)} ließ sich nicht schreiben: {ex.Message}");
        }
    }

    private static bool Bool(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b,
        JsonValueKind.Number when v.TryGetInt32(out var i) && i is 0 or 1 => i == 1,
        _ => throw new InvalidOperationException("Ungültiger Wert."),
    };

    private static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        _ => throw new InvalidOperationException("Ungültiger Wert."),
    };

    private static double Num(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetDouble(),
        JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => throw new InvalidOperationException("Ungültiger Wert."),
    };
}
