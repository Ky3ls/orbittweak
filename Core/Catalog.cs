using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OrbitTweak.Core;

/// <summary>
/// All tweaks. Each one is a standard Windows setting (or a runtime tag of the game process that ends with it), never
/// touches game files and switches off on its own: switching it off gives back what was there before, from a backup.
/// What older versions switched on and that is gone now is undone once at start (<see cref="Migrate"/>) or offered
/// in "Entfernte Tweaks" (<see cref="Old"/>).
/// </summary>
internal static class Catalog
{
    private const string OWN = @"HKLM\SOFTWARE\OrbitTweak";
    private const string GCS = @"HKCU\System\GameConfigStore";
    private const string MM = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GAMES = MM + @"\Tasks\Games";
    private const string TCPIF = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string LAYERS = @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string KERNEL = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string MEM = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const string GB = @"HKCU\Software\Microsoft\GameBar";
    private const string GDVR = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string DVR_POL = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR";
    private const string DVR_BK = Bk.User + @"\dvr";
    private const string GD = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string DO = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    private const string ADV = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string VFX = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string VFX_BK = @"HKCU\Software\OrbitTweak\Vfx";
    private const string DESK = @"HKCU\Control Panel\Desktop";
    private const string LANG = @"HKCU\Keyboard Layout\Toggle";
    private const string SYSMAIN = @"HKLM\SYSTEM\CurrentControlSet\Services\SysMain";
    private const string THROTTLE = @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string KBDCLASS = @"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters";
    private const string MOUCLASS = @"HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters";
    private const string PSCHED = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Psched";
    private const string NAMEPRIO = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider";

    private const string USB_SUB = "2a737441-1930-4402-8d77-b2bebba308a3";
    private const string USB_SUSPEND = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    private const string PROC_SUB = "54533251-82be-4824-96c1-47b60b740d00";
    private const string CPMINCORES = "0cc5b647-c1df-4637-891a-dec35c318583";
    private const string PCIE_SUB = "501a4d13-42af-4429-9fd1-a8218c268e20";
    private const string ASPM = "ee12f906-d277-404b-b6da-e5fa1a576df5";

    private const string GPU_BK = "GpuPreference", WINOPT_BK = "SwapEffectUpgrade";
    private const string FSO_OLD = "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE";

    private const string NICCLASS = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    /// <summary>Power-saving modes of the network adapter → value for "off" (*EEE and *SelectiveSuspend are Microsoft keywords, the rest Intel/Realtek).</summary>
    private static readonly (string kw, string off)[] NicPower =
    {
        ("*EEE", "0"), ("EEELinkAdvertisement", "0"), ("AdvancedEEE", "0"), ("EnableGreenEthernet", "0"), ("PowerSavingMode", "0"), ("*SelectiveSuspend", "0"),
    };
    /// <summary>What versions before 2.26 also switched off on the adapter (no gain, costs CPU or drops frames): restored once.</summary>
    private static readonly HashSet<string> NicLegacy = new(StringComparer.OrdinalIgnoreCase)
    {
        "*InterruptModeration", "GigaLite", "ULPMode", "*FlowControl", "*LsoV2IPv4", "*LsoV2IPv6", "*RscIPv4", "*RscIPv6", "PnPCapabilities",
    };

    // ======================================================================================================
    //  THE TWEAKS
    // ======================================================================================================
    public static List<Tweak> All(Settings s)
    {
        var list = new List<Tweak>
        {
            // ============================== FIVEM ==============================
            new Tweak
            {
                Id = "gpu", Cat = "fivem", Icon = "monitor", Name = "Starke Grafikkarte für FiveM",
                Sum = "Bei zwei Grafikchips läuft FiveM sicher auf der starken Karte",
                Desc = "Trägt FiveM in den Windows-Grafikeinstellungen auf „Hohe Leistung“ ein – auf Laptops und PCs mit zwei Grafikchips ein Sicherheitsnetz, das Cfx.re bei endlosem Laden oder fehlender Spiel-UI empfiehlt (FiveM wählt die starke Karte meist schon selbst), mit nur einer Grafikkarte ändert sich nichts.",
                RecWhen = () => Hw.SwitchableGpus,
                Hint = () => Hw.SwitchableGpus ? null : "Nur eine Grafikkarte erkannt: hier ändert sich nichts",
                // the game renders in the GTAProcess exe of its build, not in FiveM.exe: a build downloaded without the
                // field (versions before 2.26 never added it to new builds) shows the tweak as off, so it can be applied again
                Check = () => GpuPrefs.Main() is { } e && GpuPrefs.Get(e, "GpuPreference") == "2",
                Owned = () => GpuPrefs.Backed(GPU_BK).Length > 0,
                Apply = () =>
                {
                    var t = GpuPrefs.Targets();
                    if (t.Count == 0) throw new InvalidOperationException("FiveM nicht gefunden");
                    foreach (var e in t) GpuPrefs.SetField(e, "GpuPreference", "2", GPU_BK);
                },
                Revert = () => GpuPrefs.RestoreAll("GpuPreference", GPU_BK, "2")
            },
            new Tweak
            {
                Id = "winopt", Cat = "fivem", Icon = "max", Recommended = false, Name = "Fenster-Optimierung für FiveM",
                Sum = "Weniger Verzögerung im Randlos- und Fenstermodus",
                Desc = "Schaltet die Windows-11-„Optimierungen für Spiele im Fenstermodus“ nur für FiveM ein, damit die Bildausgabe im Randlos- oder Fenstermodus mit weniger Verzögerung läuft und G-Sync/FreeSync greifen kann – im echten Vollbild und bei FiveM für GTA V Enhanced (DirectX 12) bringt es nichts, und wenn Alt-Tab oder das Umschalten danach hakt, schalte es wieder aus.",
                Hint = () => WinOptByWindows() ? "Windows hat die Optimierung schon für alle Spiele eingeschaltet"
                           : Hw.SwitchableGpus ? "Laptop oder PC mit zwei Grafikchips: hier hakt das Umschalten am ehesten" : null,
                Check = () =>
                {
                    var f = GpuPrefs.Main() is { } m ? GpuPrefs.Get(m, "SwapEffectUpgradeEnable") : null;
                    return f == "1" || (WinOptGlobal() && f != "0");
                },
                Owned = () => GpuPrefs.Backed(WINOPT_BK).Length > 0,
                Apply = () =>
                {
                    if (WinOptByWindows()) return;
                    var t = GpuPrefs.Targets();
                    if (t.Count == 0) throw new InvalidOperationException("FiveM nicht gefunden");
                    foreach (var e in t) GpuPrefs.SetField(e, "SwapEffectUpgradeEnable", "1", WINOPT_BK);
                },
                Revert = () =>
                {
                    if (GpuPrefs.Backed(WINOPT_BK).Length == 0 && WinOptByWindows())
                        throw new InvalidOperationException("Windows hat das für alle Spiele eingeschaltet – ausschalten unter Einstellungen › System › Anzeige › Grafik");
                    GpuPrefs.RestoreAll("SwapEffectUpgradeEnable", WINOPT_BK, null);
                }
            },
            new Tweak
            {
                Id = "nothrottle", Cat = "fivem", Icon = "gauge", Badge = "LIVE", Name = "Kein Energiesparen fürs Spiel",
                Sum = "Am Akku schickt Windows FiveM nicht mehr in den Sparmodus",
                Desc = "Verhindert auf Laptops am Akku, dass Windows FiveM in den Sparmodus schickt, wenn das Spiel nicht im Vordergrund ist oder du eine Weile nichts drückst – am Netzteil mit dem Spiel vorne ändert sich nichts, und die CPU-Priorität bleibt bewusst auf „Normal“, weil „Hoch“ laut Cfx.re Ladehänger und Voice-Lag auslösen kann.",
                RecWhen = () => Hw.HasBattery,
                Check = () => s.GameNoThrottle,
                Apply = () => { s.GameNoThrottle = true; s.Save(); },
                Revert = () =>
                {
                    s.GameNoThrottle = false; s.Save();
                    if (FiveM.GameProcess() is { } g) using (g) Qos.Reset(g.Id);
                }
            },
            new Tweak
            {
                Id = "bgbrake", Cat = "fivem", Icon = "cpu", Badge = "LIVE", Name = "Hintergrund-Apps ausbremsen",
                Sum = "FiveM geht vor Browser und Launcher – spürbar nur bei voller CPU",
                Desc = "Solange FiveM läuft, stellt OrbitTweak Browser, Launcher und Cloud-Apps hinter das Spiel (niedrigere Priorität, unter Windows 11 zusätzlich Effizienzmodus, bei CPUs mit E-Kernen landen sie dort), was spürbar nur hilft, wenn deine CPU im Spiel ausgelastet ist – Voice-Apps, Apps, die gerade Ton abspielen, und das Fenster, das du gerade benutzt, bleiben unangetastet, und nach dem Spiel ist alles wie vorher.",
                Check = () => s.BgThrottle,
                Apply = () => { s.BgThrottle = true; s.Save(); },
                Revert = () => { s.BgThrottle = false; s.Save(); BgThrottle.Restore(); }
            },
            new Tweak
            {
                Id = "timer", Cat = "fivem", Icon = "timer", Badge = "LIVE", Recommended = false, Name = "Timer 0,5 ms im Spiel (Experiment)",
                Sum = "Feinster Windows-Takt im Spiel – ein Gewinn ist nicht belegt",
                Desc = "Fordert, solange FiveM läuft, den feinsten Windows-Takt an, der beim Spiel aber nur ankommt, wenn FiveM selbst einen feineren Takt anfordert – ein spürbarer Gewinn ist nicht belegt, dafür braucht dein PC etwas mehr Strom.",
                Check = () => s.TimerRes,
                Apply = () => { s.TimerRes = true; s.Save(); },
                Revert = () => { s.TimerRes = false; s.Save(); GameTimer.Release(); }
            },

            // ============================== SYSTEM ==============================
            new Tweak
            {
                Id = "ult", Cat = "system", Icon = "zap", Name = "Energieplan Höchstleistung",
                Sum = "CPU muss nicht erst hochtakten, der Effekt ist meist klein",
                Desc = "Wechselt auf einen eigenen Höchstleistungs-Energieplan, damit die CPU nicht erst hochtakten muss, und holt beim Ausschalten deinen bisherigen Plan zurück – auf aktuellen Desktops ist der Effekt klein, und im Leerlauf braucht der PC etwas mehr Strom.",
                RecWhen = () => !Hw.DualCcdX3D && !Hw.HasBattery && !Hw.ModernStandby,
                Usable = () => !Hw.DualCcdX3D && !Hw.ModernStandby,
                Hint = () => Hw.DualCcdX3D ? "Ryzen X3D mit zwei Chiplets: Der AMD-Treiber braucht „Ausbalanciert“"
                           : Hw.ModernStandby ? "Dein Gerät erlaubt nur „Ausbalanciert“: stell unter Einstellungen › Energie den Energiemodus auf „Beste Leistung“"
                           : Hw.HasBattery ? "Laptop: kostet am Akku spürbar Laufzeit" : null,
                Check = () =>
                {
                    var a = Power.Active();
                    return a != null && (a == Reg.Get(OWN, "UltScheme") as string || a == Power.HIGHPERF || a == Power.ULTIMATE);
                },
                Owned = () => Reg.Get(OWN, "UltPrev") != null,
                Apply = UltApply,
                Revert = UltRevert
            },
            new Tweak
            {
                Id = "gm", Cat = "system", Icon = "gamepad", Name = "Spielmodus",
                Sum = "Keine Treiber-Updates und Neustart-Hinweise beim Spielen",
                Desc = "Sorgt dafür, dass der Windows-Spielmodus an ist (Standard): Während du spielst, hält Windows Update Treiber-Installationen und Neustart-Hinweise zurück – mehr FPS bringt er kaum, für Ryzen-X3D-CPUs mit zwei Chiplets ist er aber Pflicht.",
                Hint = () => Hw.DualCcdX3D ? "Ryzen X3D: Ohne Spielmodus legt der AMD-Treiber FiveM nicht auf den Cache-Chiplet" : null,
                // a missing value is Windows' default: on
                Check = () => !(Reg.Get(GB, "AutoGameModeEnabled") is int v && v == 0),
                Owned = () => false,   // on is the Windows default: only undone when OrbitTweak switched it on (Marks)
                Apply = () => { Reg.Set(GB, "AutoGameModeEnabled", 1); Reg.Del(GB, "AllowAutoGameMode"); },
                // Apply only runs from 0 (see Check), so 0 is exactly what was there before
                Revert = () => Reg.Set(GB, "AutoGameModeEnabled", 0)
            },
            new Tweak
            {
                Id = "dvr", Cat = "system", Icon = "videooff", Name = "Game-Bar-Aufnahme aus",
                Sum = "Keine Game-Bar-Aufnahme mehr, auch nicht im Hintergrund",
                Desc = "Schaltet die Aufnahme der Xbox Game Bar samt „Im Hintergrund aufnehmen“ ab – mehr FPS gibt es nur, wenn die Hintergrundaufnahme vorher lief, und wenn du Clips mit der Game Bar aufnimmst, lass diesen Schalter aus.",
                RecWhen = () => s.Clip.Recorder != "gamebar",
                Usable = () => s.Clip.Recorder != "gamebar",
                Hint = () => s.Clip.Recorder == "gamebar" ? "Dein Clipper nimmt über die Game Bar auf: erst dort einen anderen Recorder wählen" : null,
                Check = () => !Reg.Is(GDVR, "HistoricalCaptureEnabled", 1)
                              && (Hw.DualCcdX3D || (Reg.Is(GCS, "GameDVR_Enabled", 0) && !Reg.Is(GDVR, "AppCaptureEnabled", 1))),
                Owned = () => Reg.ValueNames(DVR_BK).Length > 0,
                Apply = () =>
                {
                    if (s.Clip.Recorder == "gamebar") throw new InvalidOperationException("Dein Clipper nimmt über die Game Bar auf – wähl dort erst einen anderen Recorder");
                    SaveOrig(DVR_BK, GDVR, "HistoricalCaptureEnabled");
                    SaveOrig(DVR_BK, GDVR, "AppCaptureEnabled");
                    SaveOrig(DVR_BK, GCS, "GameDVR_Enabled");
                    Reg.Set(GDVR, "HistoricalCaptureEnabled", 0);
                    // dual-CCD X3D: AMD's driver finds games through the Game Bar, so its capture stays
                    if (!Hw.DualCcdX3D) { Reg.Set(GDVR, "AppCaptureEnabled", 0); Reg.Set(GCS, "GameDVR_Enabled", 0); }
                },
                Revert = () =>
                {
                    RestoreOrig(DVR_BK, GDVR, "HistoricalCaptureEnabled", null);
                    RestoreOrig(DVR_BK, GDVR, "AppCaptureEnabled", 1);
                    RestoreOrig(DVR_BK, GCS, "GameDVR_Enabled", 1);
                    if (Reg.Is(DVR_POL, "AllowGameDVR", 0)) Reg.Del(DVR_POL, "AllowGameDVR");   // written by versions before 2.26
                    Reg.DelKey(DVR_BK);
                }
            },
            new Tweak
            {
                Id = "hags", Cat = "system", Icon = "monitor", Badge = "NEUSTART", Recommended = false, Name = "Hardware-GPU-Planung",
                Sum = "Grafikkarte plant selbst – meist kein spürbarer Unterschied",
                Desc = "Schaltet die Windows-Einstellung „Hardwarebeschleunigte GPU-Planung“ ein, wenn Grafikkarte und Treiber sie unterstützen – laut Microsoft merkt man meist keinen Unterschied, in Tests waren es 0–4 % mehr FPS, und bei Abstürzen oder Rucklern schaltest du sie besser wieder aus.",
                // the setting exists since Windows 10 2004 (build 19041); on older builds the value is read by nothing
                Usable = () => SysInfo.Build >= 19041 && Hw.Hags() is var h && (!h.known || h.supported),
                Hint = () =>
                {
                    if (SysInfo.Build < 19041) return "Gibt es erst ab Windows 10 Version 2004";
                    var h = Hw.Hags();
                    if (h.known && !h.supported) return "Grafikkarte oder Treiber unterstützt das nicht";
                    if (Reg.Is(GD, "HwSchMode", 2) && h.known && !h.enabled) return "Aktiv nach dem nächsten Neustart";
                    if (Reg.Is(GD, "HwSchMode", 1) && h.known && h.enabled) return "Aus nach dem nächsten Neustart";
                    return null;
                },
                Check = () =>
                {
                    if (SysInfo.Build < 19041) return false;
                    var h = Hw.Hags();
                    if (h.known && !h.supported) return false;
                    return Reg.Get(GD, "HwSchMode") is int m ? m == 2 : h.enabled;
                },
                Owned = () => Reg.Get(OWN, "HagsPrev") != null,
                Apply = () =>
                {
                    if (SysInfo.Build < 19041) throw new InvalidOperationException("Gibt es erst ab Windows 10 Version 2004");
                    var h = Hw.Hags();
                    if (h.known && !h.supported) throw new InvalidOperationException("Grafikkarte oder Treiber unterstützt das nicht");
                    // Apply only runs while HwSchMode is not 2 (see Check): that is the real "before", also over an older backup
                    Reg.Set(OWN, "HagsPrev", Reg.Get(GD, "HwSchMode") is int p ? p : -1);
                    Reg.Set(GD, "HwSchMode", 2);
                },
                Revert = () =>
                {
                    int p = Reg.Get(OWN, "HagsPrev") is int v ? v : -1;
                    Reg.Del(OWN, "HagsPrev");
                    if (p == -1 && !Hw.Hags().byDefault) Reg.Del(GD, "HwSchMode");
                    else Reg.Set(GD, "HwSchMode", p is 1 or 2 ? p : 1);
                }
            },
            new Tweak
            {
                Id = "vfx", Cat = "system", Icon = "sparkle", Recommended = false, Name = "Windows-Animationen aus",
                Sum = "Fenster und Menüs ohne Animation – im Spiel ändert sich nichts",
                Desc = "Schaltet Fenster-, Menü- und Taskleisten-Animationen von Windows ab, damit sich der Desktop direkter anfühlt (Taskleiste erst nach Ab- und Anmelden) – im Spiel selbst bringt das keine FPS.",
                Check = () => Spi.GetMinAnimate() == 0 && Spi.Get(0x1002) == 0 && Spi.Get(0x1016) == 0 && Spi.Get(0x1004) == 0 && Reg.Is(ADV, "TaskbarAnimations", 0),
                Owned = () => Reg.Get(VFX_BK, "Saved") != null,
                Apply = VfxApply,
                Revert = VfxRevert
            },
            new Tweak
            {
                Id = "pagefile", Cat = "system", Icon = "ram", Badge = "NEUSTART", Name = "Auslagerungsdatei absichern",
                Sum = "Schützt FiveM vor Abstürzen bei vollem Arbeitsspeicher",
                Desc = "Stellt die Auslagerungsdatei wieder auf „von Windows verwaltet“, falls sie abgeschaltet oder zu klein ist, damit FiveM bei vollem Arbeitsspeicher nicht abstürzt – mehr FPS bringt das nicht.",
                // "in Ordnung" is also a big enough page file of your own, not only the Windows default
                Hint = () => PagefileOk() && Reg.Get(OWN, "PagefileBackup") == null ? "Bei dir schon in Ordnung" : null,
                Check = PagefileOk,
                Owned = () => Reg.Get(OWN, "PagefileBackup") != null,
                Apply = () =>
                {
                    if (PagefileOk()) return;
                    // only reached while the page file is off or too small: that is the real "before", also over an older backup
                    Reg.Set(OWN, "PagefileBackup", Reg.Get(MEM, "PagingFiles") as string[] ?? Array.Empty<string>(), RegistryValueKind.MultiString);
                    Reg.Set(MEM, "PagingFiles", new[] { @"?:\pagefile.sys" }, RegistryValueKind.MultiString);
                },
                Revert = () =>
                {
                    if (Reg.Get(OWN, "PagefileBackup") is not string[] b) throw new InvalidOperationException("OrbitTweak hat die Auslagerungsdatei nicht geändert – nichts zurückzusetzen");
                    Reg.Set(MEM, "PagingFiles", b, RegistryValueKind.MultiString);
                    Reg.Del(OWN, "PagefileBackup");
                }
            },

            // ============================== NETWORK ==============================
            new Tweak
            {
                Id = "nic", Cat = "network", Icon = "wifi", Badge = "NEUSTART", Name = "LAN-Energiesparen aus",
                Sum = "Netzwerkkarte schläft nicht mehr ein – am Ping kaum spürbar",
                Desc = "Schaltet Energy Efficient Ethernet und ähnliche Stromsparmodi deiner Netzwerkkarte ab, damit sie zwischen Paketen nicht kurz einschläft und die von Intel dokumentierten EEE-Geschwindigkeitseinbrüche beim I225-V ausbleiben; am Ping merkst du das kaum, und es wirkt erst nach einem Neustart.",
                Usable = () => NicTargets().Any() || NicOwned(),
                Hint = () => NicTargets().Any() ? null : "Deine Netzwerkkarte hat keinen dieser Stromsparmodi",
                Check = () => { var t = NicTargets().ToList(); return t.Count > 0 && t.All(x => x.cur == x.off); },
                Owned = NicOwned,
                Apply = () =>
                {
                    var t = NicTargets().ToList();
                    if (t.Count == 0) throw new InvalidOperationException("Deine Netzwerkkarte hat keinen dieser Stromsparmodi");
                    foreach (var (p, kw, off, cur) in t)
                    {
                        var bk = NicBk(p);
                        if (Reg.Get(bk, kw) == null) Reg.SetStr(bk, kw, Reg.Get(p, kw) is string v ? v : "<none>");
                        if (cur != off) Reg.SetStr(p, kw, off);
                    }
                },
                Revert = () =>
                {
                    foreach (var p in Nics())
                    {
                        var bk = NicBk(p);
                        foreach (var name in Reg.ValueNames(bk))
                        {
                            var v = Reg.Get(bk, name);
                            if (name.Equals("PnPCapabilities", StringComparison.OrdinalIgnoreCase) && v is int pc) { if (pc < 0) Reg.Del(p, name); else Reg.Set(p, name, pc); }
                            else if (v is "<none>") Reg.Del(p, name);
                            else if (v is string sv) Reg.SetStr(p, name, sv);
                        }
                        Reg.DelKey(bk);
                    }
                    Reg.Del(OWN, "NicTuned");
                }
            },
            new Tweak
            {
                Id = "wifiscan", Cat = "network", Icon = "wifi", Badge = "LIVE", Name = "WLAN-Scans im Spiel aus",
                Sum = "Kann Ping-Zacken über WLAN verringern, solange FiveM läuft",
                Desc = "Solange FiveM läuft, sucht Windows über WLAN nicht mehr im Hintergrund nach anderen Netzen, was die typischen periodischen Ping-Zacken über WLAN verringern kann; nach dem Spiel ist alles wie vorher, und mit LAN-Kabel passiert gar nichts.",
                RecWhen = NetLink.HasWifi,
                Hint = () => NetLink.DefaultIsWifi() == false ? "Du bist per Kabel verbunden: hier ändert sich nichts" : null,
                Check = () => s.WifiGameMode,
                Apply = () => { s.WifiGameMode = true; s.Save(); },
                Revert = () => { s.WifiGameMode = false; s.Save(); WlanGame.Restore(); }
            },
            new Tweak
            {
                Id = "delopt", Cat = "network", Icon = "download", Recommended = false, Name = "Update-Verteilung im Heimnetz aus",
                Sum = "Dein PC verteilt keine Windows-Updates mehr an andere Geräte",
                Desc = "Dein PC gibt Windows-Updates nicht mehr an andere Geräte weiter; standardmäßig teilt Windows ohnehin nur im eigenen Heimnetz, das lohnt sich also vor allem, wenn mehrere Windows-PCs bei dir im WLAN hängen oder du „Geräte im Internet“ erlaubt hattest.",
                Check = () => Reg.Is(DO, "DODownloadMode", 0),
                // a policy 0 without a backup can be an admin's or another tool's: "Alle zurücksetzen" leaves it
                Owned = () => Reg.Get(OWN, "DoModeBak") != null,
                Apply = () =>
                {
                    // Apply only runs while the value is not 0 (see Check): that is the real "before", also over an older backup
                    Reg.Set(OWN, "DoModeBak", Reg.Get(DO, "DODownloadMode") is int v ? v : -1);
                    Reg.Set(DO, "DODownloadMode", 0);
                },
                Revert = () =>
                {
                    // installs of versions before 2.26 have no backup: deleting is "not configured", right for nearly everyone
                    if (Reg.Get(OWN, "DoModeBak") is int b && b >= 0) Reg.Set(DO, "DODownloadMode", b);
                    else Reg.Del(DO, "DODownloadMode");
                    Reg.Del(OWN, "DoModeBak");
                }
            },

            // ============================== INPUT ==============================
            new Tweak
            {
                Id = "sticky", Cat = "input", Icon = "ban", Name = "Bedienungshilfe-Popups aus",
                Sum = "Keine Bedienungshilfe-Fenster mitten im Spiel",
                Desc = "5× Shift, langes Halten von Shift rechts oder Num und Alt+Shift+Num öffnen keine Bedienungshilfe-Fenster mehr, die dich mitten im Spiel rauswerfen; Funktionen, die du selbst eingeschaltet hast, behalten ihr Kürzel.",
                Check = () => Access.Take(3).All(a => Spi.GetStruct(a.get, a.words) is { } f && ((f[1] & TweakLogic.HOTKEYACTIVE) == 0 || UsesFeature(a, f[1]))),
                Owned = () => Bk.HasPrefix("sticky."),
                Apply = () =>
                {
                    foreach (var a in Access)
                    {
                        if (Spi.GetStruct(a.get, a.words) is not { } f || UsesFeature(a, f[1])) continue;   // Microsoft: only when the feature itself is off
                        if ((f[1] & TweakLogic.HOTKEYACTIVE) == 0) continue;
                        Reg.SetStr(Bk.User, "sticky." + a.name, f[1].ToString());
                        f[1] = TweakLogic.WithoutHotkey(f[1]);
                        Spi.SetStruct(a.set, f);
                    }
                },
                Revert = () =>
                {
                    // a shortcut without a backup already was off before Apply (Apply skips those): it stays off;
                    // only with no backup at all (switched off by hand or by an older version) do they go back to the Windows default
                    bool any = Bk.HasPrefix("sticky.");
                    foreach (var a in Access)
                    {
                        if (Spi.GetStruct(a.get, a.words) is not { } f) continue;
                        if (uint.TryParse(Reg.Get(Bk.User, "sticky." + a.name) as string, out var saved))
                        {
                            f[1] = TweakLogic.RestoreHotkey(f[1], saved);
                            Spi.SetStruct(a.set, f);
                            Bk.Drop("sticky." + a.name);
                        }
                        else if (!any && !TweakRun.All && (f[1] & TweakLogic.HOTKEYACTIVE) == 0)
                        {
                            f[1] |= TweakLogic.HOTKEYACTIVE;   // switched off by hand: back to the Windows default
                            Spi.SetStruct(a.set, f);
                        }
                    }
                }
            },
            new Tweak
            {
                Id = "winkey", Cat = "input", Icon = "ban", Badge = "LIVE", Recommended = false, Name = "Windows-Taste im Spiel sperren",
                Sum = "Startmenü öffnet sich im Spiel nicht mehr",
                Desc = "Solange FiveM im Vordergrund ist, öffnet ein versehentlicher Druck auf die Windows-Taste neben Strg und Alt nicht mehr das Startmenü und wirft dich nicht aus dem Vollbild; außerhalb des Spiels funktioniert sie ganz normal.",
                Check = () => s.BlockWinKey,
                Apply = () => { s.BlockWinKey = true; s.Save(); },
                Revert = () => { s.BlockWinKey = false; s.Save(); }
            },
            new Tweak
            {
                Id = "accel", Cat = "input", Icon = "target", Name = "Zeigerbeschleunigung aus",
                Sum = "Mauszeiger folgt in Menüs und auf dem Desktop 1:1",
                Desc = "Schaltet „Zeigerpräzision verbessern“ sofort aus, damit der Mauszeiger in FiveM-Menüs wie Inventar und Handy und auf dem Desktop 1:1 deiner Hand folgt; das Zielen mit Raw Input oder DirectInput ist ohnehin unbeschleunigt.",
                Hint = () => "Für unbeschleunigtes Zielen in GTA: Einstellungen › Tastatur/Maus › Maus-Eingabemethode „Raw Input“",
                Check = () => Spi.GetMouse() is { } m && m[2] == 0,
                Owned = () => Bk.Has("accel"),
                Apply = () =>
                {
                    var m = Spi.GetMouse() ?? throw new InvalidOperationException("Mauseinstellung nicht lesbar");
                    Bk.Keep("accel", $"{m[0]},{m[1]},{m[2]}", "0,0,0");
                    Spi.SetMouse(new[] { 0, 0, 0 });
                },
                Revert = () =>
                {
                    Spi.SetMouse(TweakLogic.Ints3(Bk.Take("accel")) ?? new[] { 6, 10, 1 });
                    Bk.Drop("accel");
                }
            },
            new Tweak
            {
                Id = "usb", Cat = "input", Icon = "usb", Name = "USB-Energiesparen aus (Netzbetrieb)",
                Sum = "Hilft, wenn Headset oder Empfänger kurz aussetzen",
                Desc = "Im aktiven Energieplan schickt Windows am Netzteil keine USB-Geräte mehr in den Stromsparmodus; das hilft, wenn Headset, Funk-Empfänger oder USB-Hub nach kurzer Pause kurz aussetzen, Maus und Tastatur legt Windows ohnehin meist nicht schlafen.",
                Usable = () => Power.Active() is { } a && UsbAc(a) >= 0,
                Hint = () => Power.Active() is { } a && UsbAc(a) >= 0 ? null : "Diese Einstellung gibt es in deinem Energieplan nicht",
                Check = () => Power.Active() is { } a && UsbAc(a) == 0,
                Owned = UsbOwned,
                Apply = () => UsbApplyTo(Power.Active() ?? throw new InvalidOperationException("Kein aktiver Energieplan")),
                Revert = UsbRevert
            },
            new Tweak
            {
                Id = "kbd", Cat = "input", Icon = "keyboard", Recommended = false, Name = "Kürzere Wiederholverzögerung",
                Sum = "Tasten wiederholen sich nach etwa 250 statt 500 ms",
                Desc = "Gehaltene Tasten wiederholen sich schon nach etwa 250 statt 500 ms, praktisch beim Löschen im Chat oder im Handy-Menü; aufs Laufen und Zielen im Spiel hat das keinen Einfluss.",
                Hint = () => WasdTweak.Lag().on ? "Solange die schnelle Tastenwiederholung an ist, gilt deren Timing" : null,
                Check = () => Spi.Get(0x0016) == 0 && Spi.Get(0x000A) == 31,
                Owned = () => Bk.Has("kbd.delay") || Bk.Has("kbd.speed"),
                Apply = () =>
                {
                    uint d = Spi.Get(0x0016), sp = Spi.Get(0x000A);   // SPI_GETKEYBOARDDELAY, SPI_GETKEYBOARDSPEED
                    if (d != uint.MaxValue) Bk.Keep("kbd.delay", d.ToString(), "0");
                    if (sp != uint.MaxValue) Bk.Keep("kbd.speed", sp.ToString(), "31");
                    Spi.SetParam(0x0017, 0);    // SPI_SETKEYBOARDDELAY: about 250 ms
                    Spi.SetParam(0x000B, 31);   // SPI_SETKEYBOARDSPEED: about 30 per second (Windows default)
                },
                Revert = () =>
                {
                    // a value without a backup already was at the target before Apply (Bk.Keep skips those): it stays;
                    // with no backup at all (set by hand or by an older version) both go back to Windows' defaults
                    bool any = Bk.Has("kbd.delay") || Bk.Has("kbd.speed");
                    if (!any || Bk.Has("kbd.delay")) Spi.SetParam(0x0017, uint.TryParse(Bk.Take("kbd.delay"), out var d) ? d : 1);
                    if (!any || Bk.Has("kbd.speed")) Spi.SetParam(0x000B, uint.TryParse(Bk.Take("kbd.speed"), out var sp) ? sp : 31);
                    Bk.Drop("kbd.delay"); Bk.Drop("kbd.speed");
                }
            },
            new Tweak
            {
                Id = "gbbtn", Cat = "input", Icon = "gamepad", Recommended = false, Name = "Xbox-Taste öffnet keine Game Bar",
                Sum = "Mit Controller keine Game Bar mehr mitten im Spiel",
                Desc = "Mit Controller öffnet die Xbox-Taste nicht mehr versehentlich die Game Bar mitten im Spiel, Win+G funktioniert weiter; ohne Controller ändert sich nichts.",
                Check = () => Reg.Is(GB, "UseNexusForGameBarEnabled", 0),
                Owned = () => Bk.Has("gbbtn"),
                Apply = () =>
                {
                    Bk.Keep("gbbtn", Reg.Get(GB, "UseNexusForGameBarEnabled")?.ToString(), "0");
                    Reg.Set(GB, "UseNexusForGameBarEnabled", 0);
                },
                Revert = () =>
                {
                    if (int.TryParse(Bk.Take("gbbtn"), out var v)) Reg.Set(GB, "UseNexusForGameBarEnabled", v);
                    else Reg.Del(GB, "UseNexusForGameBarEnabled");
                    Bk.Drop("gbbtn");
                }
            },
            new Tweak
            {
                Id = "langkey", Cat = "input", Icon = "keyboard", Recommended = false, Name = "Sprachwechsel per Alt+Shift aus",
                Sum = "Y und Z tauschen nicht mehr plötzlich die Plätze",
                Desc = "Alt+Shift und Strg+Shift schalten nicht mehr nebenbei die Tastatursprache um (plötzlich Y und Z vertauscht), Windows+Leertaste geht weiter; nur sinnvoll, wenn du mehr als ein Tastaturlayout installiert hast.",
                Check = () => Reg.Is(LANG, "Language Hotkey", "3") && Reg.Is(LANG, "Layout Hotkey", "3"),
                Owned = () => Bk.HasPrefix("langkey."),
                Apply = () =>
                {
                    foreach (var n in LangNames) { Bk.Keep("langkey." + n, Reg.Get(LANG, n) as string, "3"); Reg.SetStr(LANG, n, "3"); }
                    Spi.LangToggle();
                },
                Revert = () =>
                {
                    // a value without a backup already was "3" before Apply (Bk.Keep skips those): it stays;
                    // with no backup at all every value goes back to the Windows default (deleted)
                    bool any = Bk.HasPrefix("langkey.");
                    foreach (var n in LangNames)
                    {
                        if (any && !Bk.Has("langkey." + n)) continue;
                        var v = Bk.Take("langkey." + n);
                        if (v == null) Reg.Del(LANG, n); else Reg.SetStr(LANG, n, v);
                        Bk.Drop("langkey." + n);
                    }
                    Spi.LangToggle();
                }
            },
        };
        // Windows 11 only (the per-app setting does not exist on Windows 10); kept while it is still set, so it can be switched off
        if (SysInfo.Build < 22621 && GpuPrefs.Backed(WINOPT_BK).Length == 0) list.RemoveAll(t => t.Id == "winopt");
        // only with more than one keyboard layout (or while it is still on)
        if (!(KeyboardLayouts() > 1 || Bk.HasPrefix("langkey."))) list.RemoveAll(t => t.Id == "langkey");
        return list;
    }

    // ---------- gpu / winopt ----------
    /// <summary>"Optimizations for windowed games" switched on for all games in Settings (Auto HDR switches it on too).</summary>
    private static bool WinOptGlobal() => GpuPrefs.GetGlobal("SwapEffectUpgradeEnable") == "1" || GpuPrefs.GetGlobal("AutoHDREnable") == "1";
    /// <summary>On for FiveM because Windows has it on for all games (and not because of this tweak).</summary>
    private static bool WinOptByWindows()
        => WinOptGlobal() && GpuPrefs.Backed(WINOPT_BK).Length == 0 && (GpuPrefs.Main() is not { } m || GpuPrefs.Get(m, "SwapEffectUpgradeEnable") != "0");

    /// <summary>Gives exes of a new FiveM build the per-app fields that are switched on. Returns how many exes got something.</summary>
    public static int PrefUpkeep()
    {
        int n = 0;
        try { n += GpuPrefs.Upkeep("GpuPreference", "2", GPU_BK); } catch { }
        try { if (!WinOptByWindows()) n += GpuPrefs.Upkeep("SwapEffectUpgradeEnable", "1", WINOPT_BK); } catch { }
        return n;
    }

    // ---------- power plan ----------
    private static void UltApply()
    {
        if (Hw.DualCcdX3D) throw new InvalidOperationException("Ryzen X3D mit zwei Chiplets: Der AMD-Treiber braucht „Ausbalanciert“ – Plan bleibt.");
        if (Hw.ModernStandby)
        {
            if (TweakRun.Explicit) Proc.Open("ms-settings:powersleep");
            throw new InvalidOperationException("Dein Gerät erlaubt nur „Ausbalanciert“ – stell den Energiemodus auf „Beste Leistung“.");
        }
        var cur = Power.Active();
        var g = Reg.Get(OWN, "UltScheme") as string;
        if (g == null || !Power.Exists(g))
        {
            g = OldUltScheme();
            if (g == null)
            {
                // the copy gets a new GUID; an error text that only repeats the template's GUID is no copy
                string? Copy(string from) => Power.Guid_.Match(Proc.Run("powercfg", $"/duplicatescheme {from}")) is { Success: true } m && !BuiltIn(m.Value) ? m.Value.ToLowerInvariant() : null;
                g = Copy(Power.ULTIMATE) ?? Copy(Power.HIGHPERF) ?? throw new InvalidOperationException("Windows lässt keinen Höchstleistungsplan zu");
            }
            Proc.Run("powercfg", $"/changename {g} \"OrbitTweak Höchstleistung\"");
            Reg.SetStr(OWN, "UltScheme", g);
        }
        // Apply only runs while another plan is active (see Check): that plan is the real "before", also over an older backup
        // (the user may have picked another plan since the last time)
        if (cur != null && cur != g) Reg.SetStr(OWN, "UltPrev", cur);
        Proc.Run("powercfg", $"/setactive {g}");
        if (Power.Active() != g) throw new InvalidOperationException("Energieplan ließ sich nicht aktivieren");
        if (UsbOwned()) try { UsbApplyTo(g); } catch { }   // USB power saving stays off in the new plan
    }

    private static void UltRevert()
    {
        var g = Reg.Get(OWN, "UltScheme") as string;
        // decided before anything is deleted: when "USB-Energiesparen aus" is on in the plan that goes away
        // (its backup may only exist for that plan), it stays on in the plan that comes back
        bool usbOn = UsbOwned() || (g != null && Power.Exists(g) && UsbAc(g) == 0 && Marks.Has("usb"));
        var prev = Reg.Get(OWN, "UltPrev") as string;
        if (prev == null || !Power.Exists(prev)) prev = Power.BALANCED;
        Proc.Run("powercfg", $"/setactive {prev}");
        if (Power.Active() == prev)
        {
            if (g != null && !BuiltIn(g) && Power.Exists(g)) Proc.Run("powercfg", $"/delete {g}");
            if (g != null) Reg.Del(Bk.Machine, "usb." + g);
            Reg.Del(OWN, "UltScheme"); Reg.Del(OWN, "UltPrev");
        }
        if (!TweakRun.All && usbOn) try { UsbApplyTo(prev); } catch { }
    }

    /// <summary>Plans of Windows itself (Balanced, High performance, Ultimate, Power saver): never deleted.</summary>
    private static bool BuiltIn(string g) => g.ToLowerInvariant() is Power.BALANCED or Power.HIGHPERF or Power.ULTIMATE or "a1841308-3541-4fab-bc81-f71556f20b4a";

    /// <summary>The plan versions before 2.26 created ("OrbitTweak Ultimate", the name was not translated).</summary>
    private static string? OldUltScheme()
    {
        foreach (var line in Power.ListLines())
            if (line.Contains("OrbitTweak Ultimate", StringComparison.Ordinal) && Power.Guid_.Match(line) is { Success: true } m) return m.Value.ToLowerInvariant();
        return null;
    }

    // ---------- USB selective suspend (AC only, per plan) ----------
    private static int UsbAc(string scheme) => Power.Read(scheme, USB_SUB, USB_SUSPEND, true);
    private static bool UsbOwned() => Bk.HasPrefix("usb.", Bk.Machine) || Reg.Is(OWN, "UsbSuspendOff", 1);

    private static void UsbApplyTo(string scheme)
    {
        int cur = UsbAc(scheme);
        if (cur < 0) throw new InvalidOperationException("Diese Einstellung gibt es in deinem Energieplan nicht");
        if (cur == 0) return;
        Bk.Keep("usb." + scheme, cur.ToString(), "0", Bk.Machine);
        Power.Write(scheme, USB_SUB, USB_SUSPEND, true, 0);
        Power.Refresh();
    }

    private static void UsbRevert()
    {
        var active = Power.Active();
        bool hadActive = false;
        var schemes = Power.Schemes();
        foreach (var n in Reg.ValueNames(Bk.Machine).Where(n => n.StartsWith("usb.", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var g = n[4..];
            if (int.TryParse(Reg.Get(Bk.Machine, n) as string, out var idx) && schemes.Contains(g)) Power.Write(g, USB_SUB, USB_SUSPEND, true, idx);
            if (g == active) hadActive = true;
            Reg.Del(Bk.Machine, n);
        }
        // switched off by hand without a backup for this plan: back to the Windows default (enabled)
        if (!hadActive && !TweakRun.All && active != null && UsbAc(active) == 0) Power.Write(active, USB_SUB, USB_SUSPEND, true, 1);
        if (Reg.Is(OWN, "UsbSuspendOff", 1))
        {
            // versions before 2.26 also switched it off on battery
            Proc.Run("powercfg", $"/setdcvalueindex SCHEME_CURRENT {USB_SUB} {USB_SUSPEND} 1");
            Reg.Del(OWN, "UsbSuspendOff");
        }
        Power.Refresh();
    }

    // ---------- Game Bar capture ----------
    private static void SaveOrig(string bk, string path, string name)
    {
        if (Reg.Get(bk, name) != null) return;
        Reg.SetStr(bk, name, Reg.Get(path, name) is int v ? v.ToString() : "<absent>");
    }

    private static void RestoreOrig(string bk, string path, string name, int? fallback)
    {
        var b = Reg.Get(bk, name) as string;
        if (b == "<absent>") Reg.Del(path, name);
        else if (int.TryParse(b, out var v)) Reg.Set(path, name, v);
        else if (fallback is int f) Reg.Set(path, name, f);
        else Reg.Del(path, name);
        Reg.Del(bk, name);
    }

    /// <summary>The Game Bar recorder of the clipper needs the capture back: the tweak is then off, and nothing is left to restore.</summary>
    public static void ForgetDvr() { Reg.DelKey(DVR_BK); Marks.Clear("dvr"); if (Reg.Is(DVR_POL, "AllowGameDVR", 0)) Reg.Del(DVR_POL, "AllowGameDVR"); }

    // ---------- Windows animations ----------
    // SPI_GET/SET: MENUANIMATION 0x1002/0x1003, COMBOBOXANIMATION 0x1004/0x1005, LISTBOXSMOOTHSCROLLING 0x1006/0x1007, TOOLTIPANIMATION 0x1016/0x1017.
    // SPI_SETCLIENTAREAANIMATION is left alone on purpose: browsers, Discord and this app's own page read it as "reduce motion".
    private static readonly (string name, uint get, uint set)[] Anims = { ("Menu", 0x1002, 0x1003), ("Combo", 0x1004, 0x1005), ("ListSmooth", 0x1006, 0x1007), ("Tooltip", 0x1016, 0x1017) };

    private static void VfxApply()
    {
        if (Reg.Get(VFX_BK, "Saved") == null)
        {
            Reg.Set(VFX_BK, "MinAnimate", Spi.GetMinAnimate() == 0 ? 0 : 1);
            foreach (var (name, get, _) in Anims) Reg.Set(VFX_BK, name, Spi.Get(get) == 0 ? 0 : 1);
            Reg.Set(VFX_BK, "Taskbar", Reg.Get(ADV, "TaskbarAnimations") is int t ? t : -1);
            Reg.Set(VFX_BK, "FxSetting", Reg.Get(VFX, "VisualFXSetting") is int v ? v : -1);
            Reg.Set(VFX_BK, "Saved", 1);
        }
        Spi.SetMinAnimate(0);
        foreach (var (_, _, set) in Anims) Spi.SetBool(set, false);
        Reg.Set(ADV, "TaskbarAnimations", 0);
        Reg.Set(VFX, "VisualFXSetting", 3);   // "custom", so the Performance Options dialog shows what is set
    }

    private static void VfxRevert()
    {
        bool saved = Reg.Get(VFX_BK, "Saved") != null;
        int B(string n) => saved && Reg.Get(VFX_BK, n) is int v ? v : 1;
        Spi.SetMinAnimate(B("MinAnimate"));
        foreach (var (name, _, set) in Anims) Spi.SetBool(set, B(name) != 0);
        int tb = saved && Reg.Get(VFX_BK, "Taskbar") is int t ? t : -1;
        if (tb < 0) Reg.Del(ADV, "TaskbarAnimations"); else Reg.Set(ADV, "TaskbarAnimations", tb);
        int fx = saved && Reg.Get(VFX_BK, "FxSetting") is int f ? f : 0;
        if (fx < 0) Reg.Del(VFX, "VisualFXSetting"); else Reg.Set(VFX, "VisualFXSetting", fx);
        Reg.DelKey(VFX_BK);
    }

    // ---------- page file ----------
    public static bool PagefileOk() => TweakLogic.PagefileOk(Reg.Get(MEM, "PagingFiles") as string[], Native.Memory().total / 1048576.0);

    // ---------- network adapter ----------
    /// <summary>Network adapters (class keys). lanOnly: Ethernet only; Wi-Fi power settings stay at their defaults (Intel advises against changing them).</summary>
    private static IEnumerable<string> Nics(bool lanOnly = false)
    {
        foreach (var k in Reg.SubKeys(NICCLASS))
        {
            if (!k.All(char.IsDigit)) continue;
            var p = $@"{NICCLASS}\{k}";
            var t = Reg.Get(p, "*IfType")?.ToString();
            if (t == "6" || (!lanOnly && t == "71")) yield return p;
        }
    }

    /// <summary>Backups per adapter GUID (a class index like 0001 can belong to another adapter after hardware changes).</summary>
    private static string NicBk(string p) => $@"{OWN}\Nic\{Reg.Get(p, "NetCfgInstanceId") as string ?? p[(p.LastIndexOf('\\') + 1)..]}";

    /// <summary>The power-saving keywords this adapter's driver offers with an "off" option, and their value now (the driver default when unset).</summary>
    private static IEnumerable<(string p, string kw, string off, string cur)> NicTargets()
    {
        foreach (var p in Nics(lanOnly: true))
            foreach (var (kw, off) in NicPower)
            {
                if (Reg.Get($@"{p}\Ndi\params\{kw}\enum", off) == null) continue;   // the driver has no such option
                var cur = Reg.Get(p, kw) as string ?? Reg.Get($@"{p}\Ndi\params\{kw}", "default") as string;
                if (cur != null) yield return (p, kw, off, cur);
            }
    }

    private static bool NicOwned() => Reg.Is(OWN, "NicTuned", 1) || Nics().Any(p => Reg.ValueNames(NicBk(p)).Length > 0);

    // ---------- accessibility shortcuts ----------
    /// <summary>name, SPI get/set, struct size in DWORDs, "feature on" bit. Sticky, toggle and filter keys count for Check; mouse keys only get their shortcut off.</summary>
    private static readonly (string name, uint get, uint set, int words, uint onBit)[] Access =
    {
        ("sticky", 0x003A, 0x003B, 2, 0x1), ("toggle", 0x0034, 0x0035, 2, 0x1), ("filter", 0x0032, 0x0033, 6, 0x1), ("mouse", 0x0036, 0x0037, 7, 0x1),
    };

    /// <summary>The user has the feature on. OrbitTweak's own fast key repeat (filter keys) is no accessibility use.</summary>
    private static bool UsesFeature((string name, uint get, uint set, int words, uint onBit) a, uint flags)
        => (flags & a.onBit) != 0 && !(a.name == "filter" && WasdTweak.Lag().on);

    // ---------- keyboard layouts ----------
    private static readonly string[] LangNames = { "Hotkey", "Language Hotkey", "Layout Hotkey" };
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int n, IntPtr list);
    private static int KeyboardLayouts() { try { return GetKeyboardLayoutList(0, IntPtr.Zero); } catch { return 0; } }

    // ======================================================================================================
    //  REMOVED TWEAKS: still active ones are shown under System › "Entfernte Tweaks" with a reset button
    // ======================================================================================================
    /// <param name="Sum">one short line under the name; <paramref name="Desc"/> opens under "Details"</param>
    /// <param name="Own">true when Check reads OrbitTweak's own flag, so the value is surely ours; only those are part of "Alle zurücksetzen"</param>
    public sealed record Legacy(string Id, string Name, string Sum, string Desc, Func<bool> Check, Action Revert, string? Hint = null, bool Own = false)
    {
        public bool SafeCheck() { try { return Check(); } catch { return false; } }
    }

    /// <summary>SysMain comes before the memory compression, which needs it. "Alle zurücksetzen" only takes the <see cref="Legacy.Own"/> ones.</summary>
    public static readonly List<Legacy> Old = new()
    {
        new("sysmain", "SysMain aus (entfernt)",
            "Bringt auf SSDs nichts und schaltet die Speicherkomprimierung ab",
            "Entfernt, weil es auf SSDs keine FPS bringt, Ladezeiten von Festplatten verlängert und nebenbei die Speicherkomprimierung abschaltet – ist es bei dir noch aktiv, kannst du es hier zurücksetzen.",
            () => Reg.Is(SYSMAIN, "Start", 4),
            () =>
            {
                Proc.Run("sc", "config SysMain start= auto");
                Proc.Run("sc", "start SysMain");
                if (!Reg.Is(SYSMAIN, "Start", 2)) throw new InvalidOperationException("SysMain konnte nicht aktiviert werden");
            }),
        new("memcomp", "Speicher-Komprimierung aus (entfernt)",
            "Ohne Komprimierung lagert Windows auf die Festplatte aus",
            "Entfernt, weil Windows nur komprimiert, wenn der RAM knapp wird, und ohne Komprimierung dann auf die Festplatte auslagert, was mehr ruckelt – OrbitTweak konnte sie nicht von selbst wieder einschalten, weil SysMain aus ist.",
            () => Reg.Is(OWN, "MemCompOff", 1),
            () =>
            {
                if (Reg.Is(SYSMAIN, "Start", 4)) throw new InvalidOperationException("Erst „SysMain aus“ zurücksetzen");
                if (!EnableMemoryCompression()) throw new InvalidOperationException("Windows hat die Speicher-Komprimierung nicht eingeschaltet");
                Reg.Del(OWN, "MemCompOff");
            }, "Wirkt nach einem Neustart", Own: true),
        new("bgapps", "Hintergrund-Apps blockieren (entfernt)",
            "Traf nur Store-Apps und konnte Benachrichtigungen abschalten",
            "Entfernt, weil es nur Store-Apps traf (nicht Discord, Browser oder Launcher) und dir dabei Benachrichtigungen abschalten konnte – ist es bei dir noch aktiv, kannst du es hier zurücksetzen.",
            () => Reg.Is(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1),
            () => Reg.Del(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"),
            "Wirkt nach dem Ab- und Anmelden"),
        new("telemetry", "Telemetrie minimieren (entfernt)",
            "Bringt keine Leistung – das geht auch in den Einstellungen",
            "Entfernt, weil es keine Leistung bringt und auf Home und Pro nur „Erforderliche Diagnosedaten“ erzwingt, was du auch selbst in den Einstellungen wählen kannst – ist es bei dir noch aktiv, kannst du es hier zurücksetzen.",
            () => Reg.Is(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
            () => Reg.Del(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry")),
        new("fseglobal", "Vollbild-Optimierungen global aus (entfernt)",
            "Undokumentierte Werte ohne nachweisbaren Vorteil",
            "Entfernt, weil diese undokumentierten Werte alle DirectX-Spiele ohne nachweisbaren Vorteil ändern; laut Microsoft ist die Vollbild-Optimierung im Schnitt genauso schnell wie echtes Vollbild. Die Werte können auch von einem anderen Tool stammen.",
            // GameDVR_FSEBehavior is not there on a stock profile: the old switch-off wrote it too (as 0)
            () => Reg.Is(GCS, "GameDVR_HonorUserFSEBehaviorMode", 1) || Reg.Is(GCS, "GameDVR_DXGIHonorFSEWindowsCompatible", 1)
                  || Reg.Get(GCS, "GameDVR_FSEBehavior") != null,
            () =>
            {
                Reg.Set(GCS, "GameDVR_FSEBehaviorMode", 2); Reg.Set(GCS, "GameDVR_HonorUserFSEBehaviorMode", 0);
                Reg.Set(GCS, "GameDVR_DXGIHonorFSEWindowsCompatible", 0); Reg.Del(GCS, "GameDVR_FSEBehavior");
            }),
        new("mmcss", "Spiele-Priorität (MMCSS) (entfernt)",
            "Windows setzt den Wert zurück, für FiveM ändert sich nichts",
            "Entfernt: Laut Microsoft wird der Wert 0 auf 20 zurückgesetzt, und die übrigen Einträge gelten nur für Programme, die sich beim Multimedia-Dienst anmelden – für FiveM ändert sich dadurch nichts.",
            () => Reg.Is(MM, "SystemResponsiveness", 0) && Reg.Is(GAMES, "Scheduling Category", "High"),
            () =>
            {
                Reg.Set(MM, "SystemResponsiveness", 20); Reg.Set(GAMES, "Priority", 2);
                Reg.SetStr(GAMES, "Scheduling Category", "Medium"); Reg.SetStr(GAMES, "SFIO Priority", "Normal");
            }),
        new("throttle", "Energiedrosselung aus (entfernt)",
            "Ersetzt durch „Kein Energiesparen fürs Spiel“",
            "Entfernt, weil der systemweite Schalter das Spiel im Vordergrund ohnehin nicht drosselt und nur Hintergrund-Apps das Stromsparen nimmt – ersetzt durch „Kein Energiesparen fürs Spiel“. Der Wert kann auch per Gruppenrichtlinie gesetzt sein.",
            () => Reg.Is(THROTTLE, "PowerThrottlingOff", 1),
            () => Reg.Del(THROTTLE, "PowerThrottlingOff"), "Wirkt nach einem Neustart"),
        new("vfxold", "Visuelle Effekte reduzieren (alte Version)",
            "Ersetzt durch „Windows-Animationen aus“",
            "Die alte Version hat nur die Auswahl im Leistungsdialog umgestellt und Untermenüs ohne Verzögerung aufklappen lassen; beides geht hier auf den Windows-Standard zurück. Die neue Version heißt „Windows-Animationen aus“.",
            () => Reg.Is(VFX, "VisualFXSetting", 2) && Reg.Is(DESK, "MenuShowDelay", "0"),
            () => { Reg.SetStr(DESK, "MenuShowDelay", "400"); Reg.Set(VFX, "VisualFXSetting", 0); }, "Wirkt nach dem Ab- und Anmelden"),
    };

    private static bool EnableMemoryCompression()
    {
        var o = Proc.Run("powershell", "-NoProfile -NonInteractive -Command \"Enable-MMAgent -MemoryCompression -ErrorAction Stop; (Get-MMAgent).MemoryCompression\"", 60_000);
        return o.TrimEnd().EndsWith("True", StringComparison.Ordinal);   // booleans print as True in every language
    }

    // ======================================================================================================
    //  ONE-TIME CLEAN-UP of what older versions switched on
    // ======================================================================================================
    public sealed class MigrationResult
    {
        public List<string> Log { get; } = new();
        /// <summary>Short names for the one-time note after the update.</summary>
        public List<string> Undone { get; } = new();
        public bool Reboot { get; set; }
    }

    /// <summary>
    /// Runs at every start. Steps keyed to OrbitTweak's own flags or backups act whenever those are there and tidy up
    /// after themselves, so they do something once. Steps that can only recognise an old tweak by the values it wrote
    /// (other tools and guides write the same values) only run on the first start after the update from an older
    /// version (s.TweaksMigrated): on a fresh install, or on later starts, such values are someone else's and stay.
    /// </summary>
    public static MigrationResult Migrate(Settings s)
    {
        var r = new MigrationResult();
        void Step(Action a) { try { a(); } catch { } }
        // read now: the first look at the game (ProbeFiveM) may run before this is done
        bool upgrade = s.TweaksMigrated, prioWasAuto = s.PrioWasAuto;

        // ---------- input ----------
        if (upgrade)
        {
            Step(() =>
            {
                // "Input-Puffer optimieren": mouse 50 is its signature (the WASD lab never touches the mouse); a keyboard 50 only goes
                // when the WASD lab was never used, because 50 is also its first level
                if (Reg.Is(MOUCLASS, "MouseDataQueueSize", 50))
                {
                    Reg.Del(MOUCLASS, "MouseDataQueueSize");
                    if (Reg.Is(KBDCLASS, "KeyboardDataQueueSize", 50) && s.WasdSetAt == null) Reg.Del(KBDCLASS, "KeyboardDataQueueSize");
                    r.Log.Add("↺ Input-Puffer auf Windows-Standard zurückgesetzt (wirkt nach Neustart)");
                    r.Undone.Add("Input-Puffer"); r.Reboot = true;
                }
            });
            Step(() =>
            {
                // "Input-Thread-Priorität": the drivers never read the value
                foreach (var p in new[] { KBDCLASS, MOUCLASS }) if (Reg.Is(p, "ThreadPriority", 31)) Reg.Del(p, "ThreadPriority");
            });
        }
        Step(() =>
        {
            // "USB-Energiesparen aus" also switched it off on battery: back on there, the AC part stays with a backup
            if (!Reg.Is(OWN, "UsbSuspendOff", 1)) return;
            Proc.Run("powercfg", $"/setdcvalueindex SCHEME_CURRENT {USB_SUB} {USB_SUSPEND} 1");
            if (Power.Active() is { } a && UsbAc(a) == 0 && !Bk.Has("usb." + a, Bk.Machine)) Reg.SetStr(Bk.Machine, "usb." + a, "1");
            Reg.Del(OWN, "UsbSuspendOff");
            Power.Refresh();
        });

        // ---------- network ----------
        if (!Reg.Is(OWN, "LegacyNetUndone", 1))
        {
            bool net = false, nic = false;
            if (upgrade)
            {
                Step(() =>
                {
                    // "Nagle-Algorithmus aus": exactly the three values it wrote, on every interface it may have run on
                    foreach (var id in Reg.SubKeys(TCPIF))
                    {
                        var p = $@"{TCPIF}\{id}";
                        if (Reg.Is(p, "TcpAckFrequency", 1) && Reg.Is(p, "TCPNoDelay", 1) && Reg.Is(p, "TcpDelAckTicks", 0))
                        { Reg.Del(p, "TcpAckFrequency"); Reg.Del(p, "TCPNoDelay"); Reg.Del(p, "TcpDelAckTicks"); net = true; }
                    }
                });
                Step(() => { if (Reg.Get(MM, "NetworkThrottlingIndex") is int v && v == -1) { Reg.Set(MM, "NetworkThrottlingIndex", 10); net = true; } });
                Step(() => { if (Reg.Is(PSCHED, "NonBestEffortLimit", 0)) { Reg.Del(PSCHED, "NonBestEffortLimit"); net = true; } });
                Step(() =>
                {
                    if (Reg.Is(NAMEPRIO, "LocalPriority", 4) && Reg.Is(NAMEPRIO, "HostsPriority", 5) && Reg.Is(NAMEPRIO, "DnsPriority", 6) && Reg.Is(NAMEPRIO, "NetbtPriority", 7))
                    {
                        Reg.Set(NAMEPRIO, "LocalPriority", 499); Reg.Set(NAMEPRIO, "HostsPriority", 500); Reg.Set(NAMEPRIO, "DnsPriority", 2000); Reg.Set(NAMEPRIO, "NetbtPriority", 2001);
                        net = true;
                    }
                });
            }
            Step(() =>
            {
                if (Reg.Is(OWN, "TcpOptimized", 1))
                {
                    Proc.Run("netsh", "int tcp set heuristics default");
                    Proc.Run("netsh", "int tcp set global ecncapability=default");
                    net = true;
                }
                Reg.Del(OWN, "TcpOptimized");
            });
            Step(() =>
            {
                // "Netzwerkkarte auf Latenz": offloads, interrupt moderation, flow control, ULP and the power-off checkbox back
                // to what they were; its power-saving backups move to the per-adapter key of "LAN-Energiesparen aus"
                bool tuned = Reg.Is(OWN, "NicTuned", 1);
                foreach (var p in Nics())
                {
                    var old = $@"{OWN}\Nic\{p[(p.LastIndexOf('\\') + 1)..]}";
                    var names = Reg.ValueNames(old);
                    if (names.Length == 0) continue;
                    if (tuned)
                    {
                        var nb = NicBk(p);
                        bool lan = Reg.Get(p, "*IfType")?.ToString() == "6";   // Wi-Fi adapters are no longer touched at all: everything back
                        foreach (var n in names)
                        {
                            var v = Reg.Get(old, n);
                            if (NicLegacy.Contains(n) || !lan)
                            {
                                if (n.Equals("PnPCapabilities", StringComparison.OrdinalIgnoreCase)) { if (v is int pc) { if (pc < 0) Reg.Del(p, n); else Reg.Set(p, n, pc); } }
                                else if (v is string sv) Reg.SetStr(p, n, sv);
                                nic = true;
                            }
                            else if (NicPower.Any(x => x.kw.Equals(n, StringComparison.OrdinalIgnoreCase)) && v is string pv && Reg.Get(nb, n) == null) Reg.SetStr(nb, n, pv);
                        }
                    }
                    Reg.DelKey(old);
                }
                Reg.Del(OWN, "NicTuned");
            });
            Reg.Set(OWN, "LegacyNetUndone", 1);
            if (net) { r.Log.Add("↺ Alte Netzwerk-Tweaks auf Windows-Standard zurückgesetzt"); r.Undone.Add("Netzwerk-Tweaks"); }
            if (nic) { r.Log.Add("↺ Netzwerkkarte: Offloads, Interrupt-Moderation und Flusskontrolle wieder auf Treiberstandard (wirkt nach Neustart)"); r.Undone.Add("Netzwerkkarten-Tuning"); r.Reboot = true; }
        }

        // ---------- system ----------
        Step(() =>
        {
            // "Kern-Parken aus" (also when it was switched off again: its revert could write the wrong plan's value)
            if (Reg.Get(OWN, "CoreParkOff") == null) return;
            var orig = Reg.Get(OWN, "CoreParkOrig") as int?;
            var active = Power.Active();
            if (Power.RestoreDefaultWhere(PROC_SUB, CPMINCORES, 100, true, g => g == active && orig is int o && o != 100 ? o : null)) { r.Undone.Add("Kern-Parken"); r.Log.Add("↺ Kern-Parken wieder auf Windows-Standard"); }
            Reg.Del(OWN, "CoreParkOff"); Reg.Del(OWN, "CoreParkOrig");
        });
        Step(() =>
        {
            // "PCIe-Energiesparen aus" (AC and DC)
            if (Reg.Get(OWN, "AspmOff") == null) return;
            int? Fallback(string g, bool ac) => g == Power.BALANCED ? (ac ? 1 : 2) : null;
            bool c = Power.RestoreDefaultWhere(PCIE_SUB, ASPM, 0, true, g => Fallback(g, true)) | Power.RestoreDefaultWhere(PCIE_SUB, ASPM, 0, false, g => Fallback(g, false));
            if (c) { r.Undone.Add("PCIe-Energiesparen"); r.Log.Add("↺ PCIe-Energiesparen wieder auf Windows-Standard"); }
            Reg.Del(OWN, "AspmOff");
        });
        Step(() =>
        {
            // "Dynamic Tick aus": the flag only goes once the boot entry really has no such value any more
            if (Reg.Get(OWN, "DynTickOff") == null) return;
            bool Set() => Proc.Run("bcdedit", "/enum {current}").Contains("disabledynamictick", StringComparison.OrdinalIgnoreCase);
            if (Set())
            {
                Proc.Run("bcdedit", "/deletevalue {current} disabledynamictick");
                if (!Set()) { r.Undone.Add("Dynamic Tick"); r.Reboot = true; r.Log.Add("↺ Dynamic Tick wieder an (wirkt nach Neustart)"); }
            }
            if (!Set()) Reg.Del(OWN, "DynTickOff");
        });
        Step(() =>
        {
            // "Speicher-Komprimierung aus": Enable-MMAgent fails while SysMain is disabled, then it is offered under "Entfernte Tweaks"
            if (Reg.Get(OWN, "MemCompOff") is not int f) return;
            if (f == 0) { Reg.Del(OWN, "MemCompOff"); return; }
            if (Reg.Is(SYSMAIN, "Start", 4)) return;
            if (EnableMemoryCompression())
            {
                Reg.Del(OWN, "MemCompOff");
                r.Undone.Add("Speicher-Komprimierung"); r.Reboot = true; r.Log.Add("↺ Speicher-Komprimierung wieder an (wirkt nach Neustart)");
            }
        });
        if (upgrade)
        {
            Step(() =>
            {
                // "Xbox Game DVR aus" wrote a machine-wide policy that also blocked the Game Bar recorder of the clipper
                // (an admin or another tool may set the same policy: only on the update from a version that wrote it)
                if (!Reg.Is(DVR_POL, "AllowGameDVR", 0)) return;
                Reg.Del(DVR_POL, "AllowGameDVR");
                Marks.Set("dvr");   // the rest of what it set is now the tweak "Game-Bar-Aufnahme aus"
            });
            Step(() => { if (Reg.Is(GB, "AllowAutoGameMode", 1)) Reg.Del(GB, "AllowAutoGameMode"); });   // a Windows 10 1703 value of the old "Spielmodus"
        }
        Step(() =>
        {
            // the plan of the old "Höchstleistungs-Energieplan" becomes the plan of "Energieplan Höchstleistung"
            if (Reg.Get(OWN, "UltScheme") != null || OldUltScheme() is not { } g) return;
            Reg.SetStr(OWN, "UltScheme", g);
            Proc.Run("powercfg", $"/changename {g} \"OrbitTweak Höchstleistung\"");
            if (Power.Active() == g) Marks.Set("ult");   // "Alle zurücksetzen" goes back to "Ausbalanciert", as before
        });

        if (upgrade)
        {
            Step(() =>
            {
                // "Timer-Auflösung 0,5 ms" and "… auch für das Spiel" wrote a debug switch that makes every program's timer
                // request global; only removed where OrbitTweak may have written it
                if (Reg.Is(KERNEL, "GlobalTimerResolutionRequests", 1) && (s.TimerRes || s.LastBoost != null))
                {
                    Reg.Del(KERNEL, "GlobalTimerResolutionRequests");
                    r.Log.Add("↺ Systemweite Timer-Freigabe entfernt (wirkt nach Neustart)");
                    r.Undone.Add("Systemweite Timer-Freigabe"); r.Reboot = true;
                }
            });
            Step(() =>
            {
                // "Vollbild-Optimierung für FiveM aus": FiveM removes such flags itself on every start; only the exact old value goes
                foreach (var e in FiveM.GameExecutables()) if (Reg.Is(LAYERS, e, FSO_OLD)) Reg.Del(LAYERS, e);
            });
            if (prioWasAuto) r.Undone.Add("Auto-Priorität „Hoch“");
        }
        return r;
    }

    /// <summary>2.9.0: undo the FilterKeys "keyboard turbo" of 2.8.0 – it could make WASD feel off.</summary>
    public static void UndoKeyboardTurbo()
    {
        const string k = @"HKCU\Control Panel\Accessibility\Keyboard Response";
        if (!(Reg.Is(k, "AutoRepeatRate", "6") && Reg.Is(k, "Flags", "59"))) return;
        Reg.SetStr(k, "AutoRepeatDelay", "1000"); Reg.SetStr(k, "AutoRepeatRate", "500");
        Reg.SetStr(k, "DelayBeforeAcceptance", "1000"); Reg.SetStr(k, "BounceTime", "0");
        bool stickyHotkeyOff = int.TryParse(Reg.Get(@"HKCU\Control Panel\Accessibility\StickyKeys", "Flags") as string, out var sf) && (sf & 4) == 0;
        Reg.SetStr(k, "Flags", stickyHotkeyOff ? "122" : "126");
    }

    public static string CreateRestorePoint()
    {
        Reg.Set(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0);
        var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var ps = $"-NoProfile -ExecutionPolicy Bypass -Command \"Enable-ComputerRestore -Drive '{drive}' -ErrorAction SilentlyContinue; Checkpoint-Computer -Description 'OrbitTweak Backup' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK'\"";
        return Proc.Run("powershell", ps, 180_000);
    }
}
