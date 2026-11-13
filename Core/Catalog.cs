using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>All tweaks. Every tweak is reversible and only touches documented Windows settings – never game files.</summary>
internal static class Catalog
{
    private const string MM = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GAMES = MM + @"\Tasks\Games";
    private const string GCS = @"HKCU\System\GameConfigStore";
    private const string TCPIF = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string GPUPREF = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    private const string LAYERS = @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string OWN = @"HKLM\SOFTWARE\OrbitTweak";
    private const string KERNEL = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel";

    private const string BALANCED = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string POWERSAVER = "a1841308-3541-4fab-bc81-f71556f20b4a";
    private const string HIGHPERF = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string ULTIMATE = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string USB_SUB = "2a737441-1930-4402-8d77-b2bebba308a3";
    private const string USB_SUSPEND = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";

    private const string NICCLASS = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const string PROC_SUB = "54533251-82be-4824-96c1-47b60b740d00";
    private const string CPMINCORES = "0cc5b647-c1df-4637-891a-dec35c318583";
    // latency-relevant adapter settings → value for "off"
    private static readonly (string name, string off)[] NicVals =
    {
        ("*InterruptModeration", "0"), ("*EEE", "0"), ("EnableGreenEthernet", "0"), ("PowerSavingMode", "0"),
        ("GigaLite", "0"), ("AdvancedEEE", "0"), ("EEELinkAdvertisement", "0"), ("*SelectiveSuspend", "0"), ("ULPMode", "0"),
    };

    private static IEnumerable<string> Nics()
    {
        foreach (var k in Reg.SubKeys(NICCLASS))
        {
            if (!k.All(char.IsDigit)) continue;
            var p = $@"{NICCLASS}\{k}";
            var t = Reg.Get(p, "*IfType")?.ToString();
            if (t is "6" or "71") yield return p;
        }
    }

    private static int PowerIndex(string sub, string setting)
    {
        var o = Proc.Run("powercfg", $"/q SCHEME_CURRENT {sub} {setting}");
        var m = Regex.Matches(o, @"0x([0-9a-fA-F]{8})");
        return m.Count >= 2 ? Convert.ToInt32(m[^2].Groups[1].Value, 16) : -1;
    }

    private static readonly Regex Guid = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desired, bool set, out uint current);

    /// <summary>Requests 0.5 ms system timer resolution for as long as OrbitTweak runs.</summary>
    public static void SetTimerResolution(bool on) { try { NtSetTimerResolution(5000, on, out _); } catch { } }

    private static IEnumerable<string> ActiveInterfaces()
    {
        foreach (var id in Reg.SubKeys(TCPIF))
        {
            var p = $@"{TCPIF}\{id}";
            var dhcp = Reg.Get(p, "DhcpIPAddress") as string;
            var ip = Reg.Get(p, "IPAddress") as string[];
            if ((!string.IsNullOrEmpty(dhcp) && dhcp != "0.0.0.0") || (ip != null && ip.Any(x => x != "0.0.0.0")))
                yield return p;
        }
    }

    public static List<Tweak> All(Settings s) => new()
    {
        // ============================== FIVEM ==============================
        new Tweak
        {
            Id = "gpu", Cat = "fivem", Icon = "monitor", Name = "Hochleistungs-GPU erzwingen",
            Desc = "FiveM & der GTA-Prozess laufen immer auf der dedizierten Grafikkarte statt der iGPU.",
            Check = () => FiveM.Exe is { } e && Reg.Is(GPUPREF, e, "GpuPreference=2;"),
            Apply = () =>
            {
                var exes = FiveM.GameExecutables();
                if (exes.Count == 0) throw new InvalidOperationException("FiveM nicht gefunden");
                foreach (var e in exes) Reg.SetStr(GPUPREF, e, "GpuPreference=2;");
            },
            Revert = () => { foreach (var e in FiveM.GameExecutables()) Reg.Del(GPUPREF, e); }
        },
        new Tweak
        {
            Id = "fso", Cat = "fivem", Icon = "max", Name = "Vollbild-Optimierung für FiveM aus",
            Desc = "Kompatibilitäts-Flag direkt auf FiveM: echter Exclusive-Fullscreen, weniger Input-Lag.",
            Check = () => FiveM.Exe is { } e && (Reg.Get(LAYERS, e) as string ?? "").Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE"),
            Apply = () =>
            {
                var exes = FiveM.GameExecutables();
                if (exes.Count == 0) throw new InvalidOperationException("FiveM nicht gefunden");
                foreach (var e in exes) Reg.SetStr(LAYERS, e, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE");
            },
            Revert = () => { foreach (var e in FiveM.GameExecutables()) Reg.Del(LAYERS, e); }
        },
        new Tweak
        {
            Id = "prio", Cat = "fivem", Icon = "gauge", Badge = "LIVE", Name = "Auto-Priorität „Hoch“",
            Desc = "Setzt den GTA-Prozess automatisch auf hohe CPU-Priorität, sobald du joinst.",
            Check = () => s.AutoPriority,
            Apply = () => { s.AutoPriority = true; s.Save(); },
            Revert = () => { s.AutoPriority = false; s.Save(); }
        },
        new Tweak
        {
            Id = "autoram", Cat = "fivem", Icon = "ram", Badge = "LIVE", Recommended = false, Name = "Auto-RAM-Clean im Spiel",
            Desc = "Leert alle 5 Minuten Standby-Liste & Working-Sets, solange FiveM läuft. Hilft gegen Texture-Loss.",
            Check = () => s.AutoRamClean,
            Apply = () => { s.AutoRamClean = true; s.Save(); },
            Revert = () => { s.AutoRamClean = false; s.Save(); }
        },
        new Tweak
        {
            Id = "timer", Cat = "fivem", Icon = "timer", Badge = "LIVE", Name = "Timer-Auflösung 0.5 ms",
            Desc = "Präziserer System-Timer für stabilere Frametimes & weniger Mikroruckler, solange OrbitTweak läuft.",
            Check = () => s.TimerRes,
            Apply = () =>
            {
                Reg.Set(KERNEL, "GlobalTimerResolutionRequests", 1);
                s.TimerRes = true; s.Save(); SetTimerResolution(true);
            },
            Revert = () =>
            {
                Reg.Del(KERNEL, "GlobalTimerResolutionRequests");
                s.TimerRes = false; s.Save(); SetTimerResolution(false);
            }
        },

        // ============================== SYSTEM ==============================
        new Tweak
        {
            Id = "ult", Cat = "system", Icon = "zap", Name = "Ultimate Performance Plan",
            Desc = "Versteckter Höchstleistungs-Energieplan: kein CPU-Parking, kein Heruntertakten.",
            Check = () =>
            {
                var m = Guid.Match(Proc.Run("powercfg", "/getactivescheme"));
                return m.Success && !m.Value.Equals(BALANCED, StringComparison.OrdinalIgnoreCase)
                                 && !m.Value.Equals(POWERSAVER, StringComparison.OrdinalIgnoreCase);
            },
            Apply = () =>
            {
                string? guid = null;
                foreach (var line in Proc.Run("powercfg", "/list").Split('\n'))
                    if (line.Contains("Ultimat", StringComparison.OrdinalIgnoreCase) || line.Contains("OrbitTweak"))
                    { var m = Guid.Match(line); if (m.Success) { guid = m.Value; break; } }
                if (guid == null)
                {
                    var m = Guid.Match(Proc.Run("powercfg", $"-duplicatescheme {ULTIMATE}"));
                    guid = m.Success ? m.Value : HIGHPERF;
                    if (m.Success) Proc.Run("powercfg", $"-changename {guid} \"OrbitTweak Ultimate\"");
                }
                Proc.Run("powercfg", $"/setactive {guid}");
            },
            Revert = () => Proc.Run("powercfg", $"/setactive {BALANCED}")
        },
        new Tweak
        {
            Id = "gm", Cat = "system", Icon = "gamepad", Name = "Spielmodus",
            Desc = "Windows priorisiert das laufende Spiel und pausiert Updates & Hintergrund-Wartung.",
            Check = () => Reg.Is(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
            Apply = () => { Reg.Set(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1); Reg.Set(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 1); },
            Revert = () => Reg.Set(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 0)
        },
        new Tweak
        {
            Id = "dvr", Cat = "system", Icon = "videooff", Name = "Xbox Game DVR aus",
            Desc = "Keine Hintergrund-Aufnahme mehr: mehr FPS, weniger Ruckler.",
            Check = () => Reg.Is(GCS, "GameDVR_Enabled", 0),
            Apply = () =>
            {
                Reg.Set(GCS, "GameDVR_Enabled", 0);
                Reg.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                Reg.Set(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
            },
            Revert = () =>
            {
                Reg.Set(GCS, "GameDVR_Enabled", 1);
                Reg.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1);
                Reg.Del(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
            }
        },
        new Tweak
        {
            Id = "fseglobal", Cat = "system", Icon = "max", Name = "Vollbild-Optimierungen global aus",
            Desc = "Echter exklusiver Vollbildmodus für alle DirectX-Spiele.",
            Check = () => Reg.Is(GCS, "GameDVR_FSEBehaviorMode", 2),
            Apply = () =>
            {
                Reg.Set(GCS, "GameDVR_FSEBehaviorMode", 2); Reg.Set(GCS, "GameDVR_FSEBehavior", 2);
                Reg.Set(GCS, "GameDVR_HonorUserFSEBehaviorMode", 1); Reg.Set(GCS, "GameDVR_DXGIHonorFSEWindowsCompatible", 1);
            },
            Revert = () =>
            {
                Reg.Set(GCS, "GameDVR_FSEBehaviorMode", 0); Reg.Set(GCS, "GameDVR_FSEBehavior", 0);
                Reg.Set(GCS, "GameDVR_HonorUserFSEBehaviorMode", 0); Reg.Set(GCS, "GameDVR_DXGIHonorFSEWindowsCompatible", 0);
            }
        },
        new Tweak
        {
            Id = "hags", Cat = "system", Icon = "monitor", Badge = "NEUSTART", Name = "Hardware-GPU-Planung",
            Desc = "Die GPU verwaltet ihren Speicher selbst: weniger Latenz, stabilere Frametimes.",
            Check = () => Reg.Is(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2),
            Apply = () => Reg.Set(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2),
            Revert = () => Reg.Set(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1)
        },
        new Tweak
        {
            Id = "mmcss", Cat = "system", Icon = "cpu", Name = "Spiele-Priorität (MMCSS)",
            Desc = "Maximale CPU- & GPU-Priorität für Spiele im Multimedia-Scheduler.",
            Check = () => Reg.Is(MM, "SystemResponsiveness", 0) && Reg.Is(GAMES, "Priority", 6),
            Apply = () =>
            {
                Reg.Set(MM, "SystemResponsiveness", 0); Reg.Set(GAMES, "GPU Priority", 8); Reg.Set(GAMES, "Priority", 6);
                Reg.SetStr(GAMES, "Scheduling Category", "High"); Reg.SetStr(GAMES, "SFIO Priority", "High");
            },
            Revert = () =>
            {
                Reg.Set(MM, "SystemResponsiveness", 20); Reg.Set(GAMES, "GPU Priority", 8); Reg.Set(GAMES, "Priority", 2);
                Reg.SetStr(GAMES, "Scheduling Category", "Medium"); Reg.SetStr(GAMES, "SFIO Priority", "Normal");
            }
        },
        new Tweak
        {
            Id = "w32", Cat = "system", Icon = "gauge", Name = "Vordergrund-Boost",
            Desc = "Kürzere, variable CPU-Zeitscheiben für das aktive Fenster: direkteres Spielgefühl.",
            Check = () => Reg.Is(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38),
            Apply = () => Reg.Set(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38),
            Revert = () => Reg.Set(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2)
        },
        new Tweak
        {
            Id = "throttle", Cat = "system", Icon = "power", Badge = "NEUSTART", Name = "Power Throttling aus",
            Desc = "Verhindert, dass Windows Prozesse künstlich drosselt (wichtig bei Laptops).",
            Check = () => Reg.Is(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1),
            Apply = () => Reg.Set(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1),
            Revert = () => Reg.Del(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff")
        },
        new Tweak
        {
            Id = "vfx", Cat = "system", Icon = "sparkle", Name = "Visuelle Effekte reduzieren",
            Desc = "Schnellere Menüs, weniger Desktop-Animationen: mehr Ressourcen fürs Spiel.",
            Check = () => Reg.Is(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2),
            Apply = () => { Reg.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2); Reg.SetStr(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "0"); },
            Revert = () => { Reg.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 0); Reg.SetStr(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "400"); }
        },
        new Tweak
        {
            Id = "bgapps", Cat = "system", Icon = "ban", Name = "Hintergrund-Apps blockieren",
            Desc = "Store-Apps dürfen nicht mehr im Hintergrund laufen.",
            Check = () => Reg.Is(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1),
            Apply = () => Reg.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1),
            Revert = () => Reg.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0)
        },
        new Tweak
        {
            Id = "telemetry", Cat = "system", Icon = "shield", Recommended = false, Name = "Telemetrie minimieren",
            Desc = "Weniger Datensammlung & Hintergrund-Uploads von Windows.",
            Check = () => Reg.Is(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
            Apply = () => Reg.Set(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
            Revert = () => Reg.Del(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry")
        },

        new Tweak
        {
            Id = "cpark", Cat = "system", Icon = "cpu", Name = "Core Parking aus",
            Desc = "Alle CPU-Kerne bleiben wach, keine Mikroruckler, wenn FiveM plötzlich mehr Kerne braucht.",
            Check = () => Reg.Is(OWN, "CoreParkOff", 1),
            Apply = () =>
            {
                var cur = PowerIndex(PROC_SUB, CPMINCORES);
                if (cur >= 0 && Reg.Get(OWN, "CoreParkOrig") == null) Reg.Set(OWN, "CoreParkOrig", cur);
                Proc.Run("powercfg", $"/setacvalueindex SCHEME_CURRENT {PROC_SUB} {CPMINCORES} 100");
                Proc.Run("powercfg", "/setactive SCHEME_CURRENT");
                Reg.Set(OWN, "CoreParkOff", 1);
            },
            Revert = () =>
            {
                var orig = Reg.Get(OWN, "CoreParkOrig") is int o ? o : 10;
                Proc.Run("powercfg", $"/setacvalueindex SCHEME_CURRENT {PROC_SUB} {CPMINCORES} {orig}");
                Proc.Run("powercfg", "/setactive SCHEME_CURRENT");
                Reg.Set(OWN, "CoreParkOff", 0);
            }
        },
        new Tweak
        {
            Id = "dyntick", Cat = "system", Icon = "timer", Badge = "NEUSTART", Name = "Dynamic Tick aus",
            Desc = "Gleichmäßiger System-Takt statt Energiespar-Ticks: stabilere Frametimes.",
            Check = () => Reg.Is(OWN, "DynTickOff", 1),
            Apply = () => { Proc.Run("bcdedit", "/set disabledynamictick yes"); Reg.Set(OWN, "DynTickOff", 1); },
            Revert = () => { Proc.Run("bcdedit", "/deletevalue disabledynamictick"); Reg.Set(OWN, "DynTickOff", 0); }
        },
        new Tweak
        {
            Id = "sysmain", Cat = "system", Icon = "ram", Recommended = false, Name = "SysMain (Superfetch) aus",
            Desc = "Kein Vorladen von Programmen im Hintergrund: weniger Festplatten- und RAM-Last beim Spielen.",
            Check = () => Reg.Is(@"HKLM\SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4),
            Apply = () => { Proc.Run("sc", "stop SysMain"); Proc.Run("sc", "config SysMain start= disabled"); },
            Revert = () => { Proc.Run("sc", "config SysMain start= auto"); Proc.Run("sc", "start SysMain"); }
        },

        // ============================== NETWORK ==============================
        new Tweak
        {
            Id = "nagle", Cat = "network", Icon = "wifi", Name = "Nagle-Algorithmus aus",
            Desc = "Pakete werden sofort gesendet statt gebündelt: bessere Hitreg, weniger Delay bei Schüssen.",
            Check = () => ActiveInterfaces().Any() && ActiveInterfaces().All(p => Reg.Is(p, "TCPNoDelay", 1)),
            Apply = () =>
            {
                foreach (var p in ActiveInterfaces()) { Reg.Set(p, "TcpAckFrequency", 1); Reg.Set(p, "TCPNoDelay", 1); Reg.Set(p, "TcpDelAckTicks", 0); }
            },
            Revert = () =>
            {
                foreach (var id in Reg.SubKeys(TCPIF)) { var p = $@"{TCPIF}\{id}"; Reg.Del(p, "TcpAckFrequency"); Reg.Del(p, "TCPNoDelay"); Reg.Del(p, "TcpDelAckTicks"); }
            }
        },
        new Tweak
        {
            Id = "nthrottle", Cat = "network", Icon = "activity", Name = "Netzwerk-Drosselung aus",
            Desc = "Hebt die Paketdrosselung auf, die Windows bei Multimedia-Last aktiviert.",
            Check = () => Reg.Is(MM, "NetworkThrottlingIndex", -1),
            Apply = () => Reg.Set(MM, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)),
            Revert = () => Reg.Set(MM, "NetworkThrottlingIndex", 10)
        },
        new Tweak
        {
            Id = "qos", Cat = "network", Icon = "gauge", Name = "QoS-Bandbreitenreserve aus",
            Desc = "Gibt die von Windows reservierte Bandbreite vollständig frei.",
            Check = () => Reg.Is(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0),
            Apply = () => Reg.Set(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0),
            Revert = () => Reg.Del(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit")
        },
        new Tweak
        {
            Id = "tcp", Cat = "network", Icon = "refresh", Name = "TCP-Stack-Tuning",
            Desc = "Auto-Tuning normal, Heuristiken aus, RSS an: stabilere Verbindung zum Server.",
            Check = () => Reg.Is(OWN, "TcpOptimized", 1),
            Apply = () =>
            {
                Proc.Run("netsh", "int tcp set global autotuninglevel=normal");
                Proc.Run("netsh", "int tcp set heuristics disabled");
                Proc.Run("netsh", "int tcp set global rss=enabled");
                Proc.Run("netsh", "int tcp set global ecncapability=disabled");
                Reg.Set(OWN, "TcpOptimized", 1);
            },
            Revert = () =>
            {
                Proc.Run("netsh", "int tcp set heuristics default");
                Proc.Run("netsh", "int tcp set global ecncapability=default");
                Reg.Set(OWN, "TcpOptimized", 0);
            }
        },

        new Tweak
        {
            Id = "nic", Cat = "network", Icon = "wifi", Badge = "NEUSTART", Name = "Netzwerkkarte auf Latenz (Hitreg)",
            Desc = "Interrupt-Moderation und Energiespar-Funktionen (EEE, Green Ethernet) der Netzwerkkarte aus: Pakete gehen ohne Verzögerung raus und rein.",
            Check = () => Reg.Is(OWN, "NicTuned", 1),
            Apply = () =>
            {
                int n = 0;
                foreach (var p in Nics())
                {
                    var id = p[(p.LastIndexOf('\\') + 1)..];
                    foreach (var (name, off) in NicVals)
                    {
                        if (Reg.Get(p, name) is not string cur) continue;
                        if (Reg.Get($@"{OWN}\Nic\{id}", name) == null) Reg.SetStr($@"{OWN}\Nic\{id}", name, cur);
                        Reg.SetStr(p, name, off); n++;
                    }
                    if (Reg.Get($@"{OWN}\Nic\{id}", "PnPCapabilities") == null) Reg.Set($@"{OWN}\Nic\{id}", "PnPCapabilities", Reg.Get(p, "PnPCapabilities") is int pc ? pc : -1);
                    Reg.Set(p, "PnPCapabilities", 24); // Windows may not switch the adapter off to save power
                }
                Reg.Set(OWN, "NicTuned", 1);
            },
            Revert = () =>
            {
                foreach (var p in Nics())
                {
                    var id = p[(p.LastIndexOf('\\') + 1)..];
                    var bk = $@"{OWN}\Nic\{id}";
                    foreach (var (name, _) in NicVals) if (Reg.Get(bk, name) is string orig) Reg.SetStr(p, name, orig);
                    if (Reg.Get(bk, "PnPCapabilities") is int pc) { if (pc < 0) Reg.Del(p, "PnPCapabilities"); else Reg.Set(p, "PnPCapabilities", pc); }
                }
                Reg.Set(OWN, "NicTuned", 0);
            }
        },
        new Tweak
        {
            Id = "netprio", Cat = "network", Icon = "globe", Name = "Namensauflösung priorisieren",
            Desc = "Lokale Auflösung, Hosts und DNS werden vor NetBIOS abgefragt: schnelleres Verbinden zu Servern.",
            Check = () => Reg.Is(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "DnsPriority", 6),
            Apply = () =>
            {
                const string k = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider";
                Reg.Set(k, "LocalPriority", 4); Reg.Set(k, "HostsPriority", 5); Reg.Set(k, "DnsPriority", 6); Reg.Set(k, "NetbtPriority", 7);
            },
            Revert = () =>
            {
                const string k = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider";
                Reg.Set(k, "LocalPriority", 499); Reg.Set(k, "HostsPriority", 500); Reg.Set(k, "DnsPriority", 2000); Reg.Set(k, "NetbtPriority", 2001);
            }
        },

        // ============================== INPUT (WASD) ==============================
        new Tweak
        {
            Id = "kbd", Cat = "input", Icon = "keyboard", Name = "Tastatur-Verzögerung 0",
            Desc = "Minimale Wiederholverzögerung & maximale Repeat-Rate: Tasten reagieren sofort.",
            Check = () => Reg.Is(@"HKCU\Control Panel\Keyboard", "KeyboardDelay", "0") && Reg.Is(@"HKCU\Control Panel\Keyboard", "KeyboardSpeed", "31"),
            Apply = () => { Reg.SetStr(@"HKCU\Control Panel\Keyboard", "KeyboardDelay", "0"); Reg.SetStr(@"HKCU\Control Panel\Keyboard", "KeyboardSpeed", "31"); },
            Revert = () => { Reg.SetStr(@"HKCU\Control Panel\Keyboard", "KeyboardDelay", "1"); Reg.SetStr(@"HKCU\Control Panel\Keyboard", "KeyboardSpeed", "31"); }
        },
        new Tweak
        {
            Id = "queue", Cat = "input", Icon = "mouse", Badge = "NEUSTART", Name = "Input-Puffer optimieren",
            Desc = "Kleinere Maus- & Tastatur-Datenwarteschlange (100 → 50): Eingaben werden direkter verarbeitet.",
            Check = () => Reg.Is(@"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", 50),
            Apply = () =>
            {
                Reg.Set(@"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", 50);
                Reg.Set(@"HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", 50);
            },
            Revert = () =>
            {
                Reg.Del(@"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize");
                Reg.Del(@"HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize");
            }
        },
        new Tweak
        {
            Id = "accel", Cat = "input", Icon = "target", Name = "Mausbeschleunigung aus",
            Desc = "1:1 Raw-Gefühl beim Zielen: deaktiviert „Zeigerpräzision verbessern“.",
            Check = () => Reg.Is(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0"),
            Apply = () => { Reg.SetStr(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0"); Reg.SetStr(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0"); Reg.SetStr(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0"); },
            Revert = () => { Reg.SetStr(@"HKCU\Control Panel\Mouse", "MouseSpeed", "1"); Reg.SetStr(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "6"); Reg.SetStr(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "10"); }
        },
        new Tweak
        {
            Id = "usb", Cat = "input", Icon = "usb", Name = "USB-Energiesparen aus",
            Desc = "Verhindert, dass Maus, Tastatur & Headset in den Stromsparmodus gehen (Input-Aussetzer).",
            Check = () => Reg.Is(OWN, "UsbSuspendOff", 1),
            Apply = () =>
            {
                Proc.Run("powercfg", $"/setacvalueindex SCHEME_CURRENT {USB_SUB} {USB_SUSPEND} 0");
                Proc.Run("powercfg", $"/setdcvalueindex SCHEME_CURRENT {USB_SUB} {USB_SUSPEND} 0");
                Proc.Run("powercfg", "/setactive SCHEME_CURRENT");
                Reg.Set(OWN, "UsbSuspendOff", 1);
            },
            Revert = () =>
            {
                Proc.Run("powercfg", $"/setacvalueindex SCHEME_CURRENT {USB_SUB} {USB_SUSPEND} 1");
                Proc.Run("powercfg", $"/setdcvalueindex SCHEME_CURRENT {USB_SUB} {USB_SUSPEND} 1");
                Proc.Run("powercfg", "/setactive SCHEME_CURRENT");
                Reg.Set(OWN, "UsbSuspendOff", 0);
            }
        },
        new Tweak
        {
            Id = "sticky", Cat = "input", Icon = "ban", Name = "Einrastfunktion-Popup aus",
            Desc = "Kein Popup mehr, das dich aus dem Spiel wirft, wenn du 5× Shift drückst.",
            Check = () => Reg.Is(@"HKCU\Control Panel\Accessibility\StickyKeys", "Flags", "506"),
            Apply = () =>
            {
                Reg.SetStr(@"HKCU\Control Panel\Accessibility\StickyKeys", "Flags", "506");
                Reg.SetStr(@"HKCU\Control Panel\Accessibility\ToggleKeys", "Flags", "58");
                Reg.SetStr(@"HKCU\Control Panel\Accessibility\Keyboard Response", "Flags", "122");
            },
            Revert = () =>
            {
                Reg.SetStr(@"HKCU\Control Panel\Accessibility\StickyKeys", "Flags", "510");
                Reg.SetStr(@"HKCU\Control Panel\Accessibility\ToggleKeys", "Flags", "62");
                Reg.SetStr(@"HKCU\Control Panel\Accessibility\Keyboard Response", "Flags", "126");
            }
        },
        new Tweak
        {
            Id = "inprio", Cat = "input", Icon = "keyboard", Badge = "NEUSTART", Name = "Input-Thread-Priorität",
            Desc = "Maus- und Tastaturtreiber laufen mit höchster Priorität: WASD und Klicks werden auch bei voller CPU sofort verarbeitet.",
            Check = () => Reg.Is(@"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "ThreadPriority", 31),
            Apply = () =>
            {
                Reg.Set(@"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "ThreadPriority", 31);
                Reg.Set(@"HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "ThreadPriority", 31);
            },
            Revert = () =>
            {
                Reg.Del(@"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "ThreadPriority");
                Reg.Del(@"HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "ThreadPriority");
            }
        },
        new Tweak
        {
            Id = "gbbtn", Cat = "input", Icon = "gamepad", Name = "Xbox-Taste ohne Game Bar",
            Desc = "Die Guide-Taste am Controller öffnet nicht mehr die Game Bar mitten im Spiel.",
            Check = () => Reg.Is(@"HKCU\Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0),
            Apply = () => Reg.Set(@"HKCU\Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0),
            Revert = () => Reg.Set(@"HKCU\Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 1)
        },
    };

    /// <summary>2.9.0: undo the FilterKeys "keyboard turbo" of 2.8.0 – it could make WASD feel off.</summary>
    public static void UndoKeyboardTurbo()
    {
        const string k = @"HKCU\Control Panel\Accessibility\Keyboard Response";
        if (!(Reg.Is(k, "AutoRepeatRate", "6") && Reg.Is(k, "Flags", "59"))) return;
        Reg.SetStr(k, "AutoRepeatDelay", "1000"); Reg.SetStr(k, "AutoRepeatRate", "500");
        Reg.SetStr(k, "DelayBeforeAcceptance", "1000"); Reg.SetStr(k, "BounceTime", "0");
        Reg.SetStr(k, "Flags", Reg.Is(@"HKCU\Control Panel\Accessibility\StickyKeys", "Flags", "506") ? "122" : "126");
    }

    public static string CreateRestorePoint()
    {
        Reg.Set(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0);
        var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var ps = $"-NoProfile -ExecutionPolicy Bypass -Command \"Enable-ComputerRestore -Drive '{drive}' -ErrorAction SilentlyContinue; Checkpoint-Computer -Description 'OrbitTweak Backup' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK'\"";
        return Proc.Run("powershell", ps, 180_000);
    }
}
