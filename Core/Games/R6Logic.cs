using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>
/// One Rainbow Six Siege setting as the page shows it. Section/Key name the line in GameSettings.ini; the GPU item
/// (Windows per-app graphics setting for RainbowSix.exe) has neither.
/// </summary>
public sealed record R6Spec(
    string Id, string Kind, string? Section, string? Key,
    string Title, string Sum, string Desc, string Effect, string Source,
    bool Rec = false, object? RecValue = null, (string V, string T)[]? Options = null,
    double? Min = null, double? Max = null, double? Step = null, string? Unit = null)
{
    /// <summary>The Windows graphics setting, not a line of GameSettings.ini.</summary>
    public bool IsGpu => Section == null;
    /// <summary>The game rewrites GameSettings.ini when it exits, so every INI item needs it closed.</summary>
    public bool NeedsClosed => !IsGpu;
}

/// <summary>
/// The pure rules of the Rainbow Six Siege tweaks (no Windows calls): tests/GameLogic.Tests/R6Tests.cs checks them.
///   Settings file: Documents\My Games\Rainbow Six - Siege\&lt;Ubisoft profile GUID&gt;\GameSettings.ini (ASCII, CRLF),
///   the file the game's own settings menu writes. The game rewrites it on exit, so it is only written while the game
///   is closed. Every key keeps a backup "og.r6.&lt;Key&gt;" (old value or "-") plus "og.r6.file" (the file edited).
///   GPU: HKCU\Software\Microsoft\DirectX\UserGpuPreferences for &lt;InstallDir&gt;\RainbowSix.exe (backup "og.r6.gpu").
/// </summary>
public static class R6Logic
{
    public const string Name = "Rainbow Six Siege";
    public const string UbiProduct = "635";
    public const string SteamApp = "359550";
    /// <summary>The game itself (DirectX 12 only since Y10S1). RainbowSix_BE.exe is the BattlEye launcher: never touched.</summary>
    public const string Exe = "RainbowSix.exe";
    public const string SettingsFile = "GameSettings.ini";

    /// <summary>Process names that mean "the game is running" (names only, the process itself is never opened).
    /// RainbowSixHelper.exe ships next to RainbowSix.exe as a byte-identical copy (Ubisoft manifests of 2026), so it is the
    /// game too. RainbowSix_Vulkan is gone since Y9S3 but is still checked for old installs.</summary>
    public static readonly string[] ProcessNames = { "RainbowSix", "RainbowSix_BE", "RainbowSixHelper", "RainbowSix_Vulkan" };

    public const string BkPrefix = "og.r6.";
    /// <summary>The full path of the GameSettings.ini the backups belong to.</summary>
    public const string BkFile = "og.r6.file";
    /// <summary>Backup subkey of GpuPrefs.SetField / RestoreField.</summary>
    public const string BkGpu = "og.r6.gpu";
    public static string BkKey(R6Spec s) => BkPrefix + (s.Key ?? s.Id);

    public const string RunningError = "Schließ Rainbow Six Siege zuerst, sonst überschreibt das Spiel die Einstellung beim Beenden.";
    public const string NoConfig = "Starte Rainbow Six Siege einmal, dann legt es seine Einstellungen an";
    public const string NoNvidia = "Nur mit NVIDIA-Grafikkarte (ab GeForce GTX 10)";
    /// <summary>Two GPUs and the game last ran on the other one (an Optimus laptop on the processor graphics …).</summary>
    public const string NoNvidiaHybrid = "Siege lief zuletzt nicht auf einer NVIDIA-Grafikkarte: hast du eine, schalte „Starke Grafikkarte für Siege“ ein und starte Siege einmal";
    public const string OneGpu = "Nur eine Grafikkarte erkannt: hier ändert sich nichts";
    public const string NoExe = "Spielordner nicht gefunden: OrbitTweak kennt den Pfad zu RainbowSix.exe nicht";
    /// <summary>The note when only GameSettings.ini was found (Siege uninstalled, or an install OrbitTweak does not know).</summary>
    public const string NoFolder = "Den Spielordner hat OrbitTweak nicht gefunden: die Spieleinstellungen gehen trotzdem, nur „Starke Grafikkarte für Siege“ braucht ihn";
    public const string FpsRange = "Das FPS-Limit ist 0 (aus) oder mindestens 30.";
    public const string FpsMax = "Das FPS-Limit geht in OrbitTweak bis 500.";
    public const string FpsWhole = "Das FPS-Limit ist eine ganze Zahl.";
    public const string ReadOnly = "Deine GameSettings.ini ist schreibgeschützt (das raten manche Anleitungen). Nimm in den Dateieigenschaften den Haken bei „Schreibgeschützt“ raus, dann klappt es.";
    public const string Locked = "Die GameSettings.ini ist gerade gesperrt. Schließ Rainbow Six Siege und versuch es noch mal.";
    /// <summary>Access denied while nothing holds the file: Windows' ransomware protection guards Documents, or the file's permissions.</summary>
    public const string NoAccess = "Windows lässt OrbitTweak nicht an die GameSettings.ini. Ist im Windows-Sicherheitscenter der „Überwachte Ordnerzugriff“ an, erlaube OrbitTweak dort und versuch es noch mal.";

    public const string AntiCheat = "BattlEye und seit 2026 Ubisofts eigenes Kernel-Anti-Cheat. OrbitTweak ändert nur die GameSettings.ini, "
        + "die auch das Einstellungsmenü des Spiels schreibt, und den Windows-Grafikeintrag für RainbowSix.exe. Spieldateien "
        + "und das laufende Spiel fasst es nie an, und es steuert nie Maus oder Tastatur. Die GameSettings.ini schreibt es nur, "
        + "wenn Siege geschlossen ist.";

    private const int FpsMin = 30, FpsTop = 500;

    // ------------------------------------------------------------------ the items
    public static readonly R6Spec[] Specs =
    {
        new("reflex", "choice", "DISPLAY", "NVReflex",
            "NVIDIA Reflex",
            "Weniger Eingabeverzögerung auf GeForce-Karten, aber nicht mehr FPS",
            "Stellt im Spiel „NVIDIA Reflex Low Latency“ ein. Reflex lässt Prozessor und Grafikkarte keine Bilder auf Vorrat "
            + "stapeln, so kommt deine Mausbewegung schneller auf den Bildschirm. NVIDIA nannte für Siege bis zu 25 % weniger "
            + "Systemlatenz (2021, damals noch mit der Vulkan-Version gemessen), am meisten, wenn die Grafikkarte voll ausgelastet "
            + "ist. Mehr FPS bringt es nicht, manchmal sind es sogar ein paar weniger. „An + Boost“ hält zusätzlich den Grafiktakt "
            + "hoch, wenn der Prozessor bremst (in Siege bei hohen FPS häufig). Das spart vielleicht noch etwas Verzögerung, kostet "
            + "aber Strom und Wärme. Geht nur mit einer NVIDIA-Grafikkarte ab GeForce GTX 10. Mit G-Sync und V-Sync im Treiber hält "
            + "Reflex die FPS außerdem von selbst knapp unter der Bildwiederholrate.",
            "latency",
            "NVIDIA: bis zu 25 % weniger Latenz in Siege, ab GeForce GTX 10; Schlüssel und Werte aus dem Kommentar der GameSettings.ini (2026)",
            Rec: true, RecValue: "1",
            Options: new[] { ("0", "Aus"), ("1", "An"), ("2", "An + Boost") }),

        new("vsync", "choice", "DISPLAY_SETTINGS", "VSync",
            "V-Sync",
            "Aus spart Eingabeverzögerung, dafür kann das Bild reißen",
            "Mit V-Sync wartet jedes fertige Bild auf den nächsten Bildaufbau des Monitors. Das verhindert Bildrisse, hält aber "
            + "Bilder zurück und erhöht so die Verzögerung. Deshalb ist es in Shootern meist aus. Der Preis: Ohne V-Sync kann das "
            + "Bild bei schnellen Drehungen reißen. Hast du einen G-Sync- oder FreeSync-Monitor, lass V-Sync hier aus und schalte "
            + "es bei Bedarf in der Software deiner Grafikkarte ein, zusammen mit Reflex oder einem FPS-Limit knapp unter deiner "
            + "Bildwiederholrate. „Jedes zweite Bild“ halbiert die Bildrate und ist nur etwas für sehr schwache PCs.",
            "latency",
            "Ubisoft (2018): mit V-Sync wartet das Spiel auf den Bildschirm; Werte laut Kommentar in der GameSettings.ini",
            Rec: true, RecValue: "0",
            Options: new[] { ("0", "Aus"), ("1", "An"), ("2", "Jedes zweite Bild") }),

        new("fpslimit", "number", "DISPLAY", "FPSLimit",
            "FPS-Limit",
            "Deckelt die Bildrate: gleichmäßiger, kühler, weniger Prozessorlast",
            "Setzt das eingebaute Bildraten-Limit von Siege. Ubisoft rät selbst dazu, weil Siege ohne Limit und ohne V-Sync den "
            + "Prozessor bis zum Anschlag auslasten kann. Mit Limit nutzt es nur so viel, wie es für die Ziel-FPS braucht. Das "
            + "ergibt gleichmäßigere Bildzeiten, weniger Hitze und leisere Lüfter. Mit G-Sync oder FreeSync ist ein Limit ein paar "
            + "FPS unter der Bildwiederholrate sinnvoll (z. B. 141 bei 144 Hz), damit der Monitor im variablen Bereich bleibt. "
            + "Erreichst du das Limit nie, ändert es nichts. Liegt es weit unter dem, was dein PC schafft, kostet es FPS und ein "
            + "wenig Reaktionszeit. 0 schaltet das Limit aus, sonst nimmt das Spiel mindestens 30.",
            "stability",
            "Ubisoft-Beitrag „100% CPU usage“ (2018): FPSLimit in [DISPLAY] senkt die Prozessorlast; Mindestwert 30 laut Kommentar in der Datei",
            Min: 0, Max: FpsTop, Step: 1, Unit: "FPS"),

        new("rawinput", "switch", "INPUT", "RawInputMouseKeyboard",
            "Raw Input",
            "Siege liest die Maus direkt, ohne Windows-Beschleunigung",
            "Mit Raw Input liest Siege Maus und Tastatur direkt, statt den Umweg über den Windows-Mauszeiger zu nehmen. Die "
            + "Zeigergeschwindigkeit und „Zeigerbeschleunigung verbessern“ aus Windows wirken dann nicht mehr aufs Zielen, jede "
            + "Bewegung kommt eins zu eins an. Das empfehlen auch Seiten mit Profi-Einstellungen. Mehr FPS bringt es nicht. Hast "
            + "du in Windows eine andere Zeigergeschwindigkeit als die Standardstufe, fühlt sich deine Empfindlichkeit danach "
            + "anders an. Vereinzelt berichten Spieler, dass sich das Zielen mit Raw Input ungewohnt anfühlt. Dann schalte es "
            + "wieder aus.",
            "comfort",
            "Option aus dem Spielmenü; ProSettings (Nov. 2025) empfiehlt An",
            Rec: true, RecValue: true),

        new("windowmode", "choice", "DISPLAY_SETTINGS", "WindowMode",
            "Anzeigemodus",
            "Vollbild reagiert am direktesten, Rahmenlos ist am bequemsten",
            "Siege läuft seit 2025 nur noch mit DirectX 12. Im Vollbild gehört der Bildschirm dem Spiel allein: "
            + "Bildwiederholrate, G-Sync und FreeSync greifen zuverlässig, die Verzögerung ist am niedrigsten. Rahmenlos ist unter "
            + "Windows 11 mit DirectX 12 meist fast genauso schnell und Alt-Tab geht sofort. Es kann aber etwas Verzögerung kosten, "
            + "wenn Overlays oder ein zweiter Bildschirm dazwischenfunken. Fenster ist nur etwas für nebenbei. Spürbar mehr FPS "
            + "bringt keiner der Modi.",
            "latency",
            "Werte laut Kommentar in der GameSettings.ini; nur noch DirectX 12 seit Y10S1 (Ubisoft)",
            Options: new[] { ("0", "Vollbild"), ("2", "Rahmenlos"), ("1", "Fenster") }),

        new("gpu", "switch", null, null,
            "Starke Grafikkarte für Siege",
            "Bei zwei Grafikchips läuft Siege sicher auf der starken Karte",
            "Trägt RainbowSix.exe in den Windows-Grafikeinstellungen auf „Hohe Leistung“ ein. Auf Laptops und PCs mit zwei "
            + "Grafikchips (Prozessorgrafik und Grafikkarte) verhindert das, dass Siege auf dem schwachen Chip startet. Dort "
            + "hättest du nur einen Bruchteil der FPS. Meist wählt Windows die starke Karte schon selbst, das hier ist das "
            + "Sicherheitsnetz. Mit nur einer Grafikkarte ändert sich nichts. Es wirkt ab dem nächsten Spielstart.",
            "fps",
            "Windows-Grafikeinstellung „Hohe Leistung“ pro App, wie beim FiveM-Tweak von OrbitTweak",
            Rec: true, RecValue: true),
    };

    public static R6Spec? Find(string? id) => Specs.FirstOrDefault(s => s.Id == id);

    /// <summary>What "Empfohlene anwenden" sets with this page value now: the spec's RecValue, but an existing Reflex
    /// "An + Boost" stays (the same rule as FortniteLogic.RecValue and Cs2Logic.ReflexRec).</summary>
    public static object? RecFor(R6Spec s, object? value) => s.Id == "reflex" && value as string == "2" ? "2" : s.RecValue;

    // ------------------------------------------------------------------ values: file ↔ page
    /// <summary>The page value of a line in the file: bool (switch), option value (choice), int (number); null when the key
    /// is missing or holds something the game does not document.</summary>
    public static object? ToPage(R6Spec s, string? raw)
    {
        if (raw == null || !TryNum(raw, out var d) || Math.Abs(d) >= 1e9) return null;
        switch (s.Kind)
        {
            case "switch":
                return Whole(d) ? d != 0 : null;
            case "choice":
                if (!Whole(d)) return null;
                var v = ((long)d).ToString(CultureInfo.InvariantCulture);
                return s.Options!.Any(o => o.V == v) ? v : null;
            default:
                // the game treats anything below 30 as "no limit"
                var n = (int)Math.Round(d, MidpointRounding.AwayFromZero);
                return n < FpsMin ? 0 : n;
        }
    }

    /// <summary>The text written to the file for a value from the page; throws InvalidOperationException in German.</summary>
    public static string ToIni(R6Spec s, JsonElement v)
    {
        switch (s.Kind)
        {
            case "switch":
                return ToBool(v, s.Title) ? "1" : "0";
            case "choice":
            {
                string? key = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    _ => null,
                };
                if (key != null && TryNum(key, out var d) && Whole(d))
                {
                    var c = ((long)d).ToString(CultureInfo.InvariantCulture);
                    if (s.Options!.Any(o => o.V == c)) return c;
                }
                throw new InvalidOperationException($"Für {s.Title} gibt es diese Auswahl nicht.");
            }
            default:
            {
                double d;
                if (v.ValueKind == JsonValueKind.Number) d = v.GetDouble();
                else if (v.ValueKind == JsonValueKind.String && TryNum(v.GetString(), out var p)) d = p;
                else throw new InvalidOperationException("Gib für das FPS-Limit eine Zahl an.");
                if (!Whole(d)) throw new InvalidOperationException(FpsWhole);
                if (d != 0 && d < FpsMin) throw new InvalidOperationException(FpsRange);
                if (d > FpsTop) throw new InvalidOperationException(FpsMax);
                return ((long)d).ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>An on/off value from the page: true/false, 1/0 or the same as text.</summary>
    public static bool ToBool(JsonElement v, string title = "diese Einstellung")
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number when v.TryGetDouble(out var d) && (d == 0 || d == 1): return d == 1;
            case JsonValueKind.String:
                var s = v.GetString()?.Trim().ToLowerInvariant();
                if (s is "1" or "true") return true;
                if (s is "0" or "false") return false;
                break;
        }
        throw new InvalidOperationException($"Für {title} geht nur an oder aus.");
    }

    /// <summary>Two values of the file mean the same (null = key missing; "1" and "1.000000" are the same number).</summary>
    public static bool Same(string? a, string? b)
    {
        if (a == null || b == null) return a == b;
        if (TryNum(a, out var x) && TryNum(b, out var y)) return x == y;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>OrbitTweak changed it: there is a backup ("-" = was not there) and the value now is another one.</summary>
    public static bool IsChanged(string? backup, string? current)
        => backup != null && !Same(backup == "-" ? null : backup, current);

    /// <summary>The text with one key back as it was: the old value, or the line gone when it was not there.</summary>
    public static string Restore(string text, R6Spec s, string? backup)
    {
        if (backup == null || s.IsGpu) return text;
        return backup == "-" ? IniText.Remove(text, s.Section!, s.Key!) : IniText.Set(text, s.Section!, s.Key!, backup);
    }

    /// <summary>
    /// The text after setting a key to <paramref name="target"/>; backup is the key's backup (null = none yet, "-" = the key
    /// was not there). A target that means what the backup holds puts the file's original bytes back (<see cref="Restore"/>)
    /// and the backup is no longer needed (<c>drop</c>), so a change set back here does not keep OrbitTweak on the profile.
    /// A key that was not there has no meaning OrbitTweak knows: setting it never drops the backup, and since the key now
    /// exists it counts as changed, so Zurücksetzen (which removes the line again) stays reachable.
    /// </summary>
    public static (string next, bool drop) Apply(string text, R6Spec s, string target, string? backup)
    {
        if (s.IsGpu) return (text, false);
        if (backup is { } b && b != "-" && Same(b, target)) return (Restore(text, s, b), true);
        return (IniText.Set(text, s.Section!, s.Key!, target), false);
    }

    /// <summary>
    /// A key backup that can no longer do anything: its file was read (text not null), the key's section is there and the
    /// value means what the backup holds again (Siege's own menu set it back, or Siege reset the profile). Such a backup
    /// changes nothing on Zurücksetzen but would keep OrbitTweak on its profile (<see cref="PickFile"/>), so it goes before
    /// the file is picked. A file that could not be read, or one without the key's section, keeps every backup.
    /// </summary>
    public static bool Stale(string? text, R6Spec s, string? backup)
        => backup != null && !s.IsGpu && text != null && HasSection(text, s.Section!) && !IsChanged(backup, IniText.Get(text, s.Section!, s.Key!));

    /// <summary>
    /// The GameSettings.ini to read and write: while OrbitTweak holds backups of a file that is still there (stale ones are
    /// dropped before, <see cref="Stale"/>), that one, so Zurücksetzen and further changes stay with the same profile;
    /// otherwise the one played last (<see cref="Newest"/>). backed: the backups belong to the file picked. older: the kept
    /// file is not the one played last (another account played since).
    /// </summary>
    public static (string? file, bool backed, bool older) PickFile(string? pin, bool anyBackup, bool pinExists, string? newest)
    {
        if (anyBackup && pin != null && pinExists)
            return (pin, true, newest != null && !newest.Equals(pin, StringComparison.OrdinalIgnoreCase));
        return (newest, false, false);
    }

    // ------------------------------------------------------------------ availability
    /// <summary>Why an INI item cannot be set with this file ("" = it can). switchable: Windows can choose between two GPUs,
    /// so a non-NVIDIA vendor may only mean the game last ran on the weaker chip.</summary>
    public static string WhyNot(R6Spec s, string? text, bool switchable = false)
    {
        if (s.IsGpu) return "";
        if (text == null) return NoConfig;
        if (!HasSection(text, s.Section!))
            return $"In deiner GameSettings.ini fehlt der Abschnitt [{s.Section}]: starte Rainbow Six Siege einmal und beende es wieder";
        if (s.Id == "reflex" && !MaybeNvidia(text)) return switchable ? NoNvidiaHybrid : NoNvidia;
        return "";
    }

    /// <summary>The game writes the vendor of the card it renders on to [HARDWARE_INFO] GPUVendor. Unknown counts as
    /// possible (the game ignores Reflex on other cards anyway).</summary>
    public static bool MaybeNvidia(string? text)
    {
        var v = IniText.Get(text, "HARDWARE_INFO", "GPUVendor");
        return string.IsNullOrWhiteSpace(v) || v.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The text has a [section] line (with or without keys).</summary>
    public static bool HasSection(string? text, string section)
    {
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim().TrimStart('\uFEFF').Trim();
            if (line.Length >= 2 && line[0] == '[' && line[^1] == ']' && line[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>What to do with the Windows graphics field for "on" / "off". backup: null = no backup, "-" = the field was not there.</summary>
    public enum GpuStep { None, High, Restore, Default }

    public static GpuStep GpuPlan(bool on, string? current, string? backup)
    {
        if (on) return current == "2" ? GpuStep.None : GpuStep.High;
        if (current != "2") return GpuStep.None;
        // it was already "high performance" before OrbitTweak (or there is no backup): "off" means "Windows decides" (0),
        // the backup keeps the old value for Zurücksetzen
        return backup != null && backup != "2" ? GpuStep.Restore : GpuStep.Default;
    }

    // ------------------------------------------------------------------ the file's bytes
    /// <summary>The file's text with every byte kept (Latin-1 maps each byte to one char); a UTF-8 BOM is taken off.</summary>
    public static (string text, bool bom) Decode(byte[] b)
    {
        bool bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
        return (Encoding.Latin1.GetString(b, bom ? 3 : 0, b.Length - (bom ? 3 : 0)), bom);
    }

    public static byte[] Encode(string text, bool bom)
    {
        var body = Encoding.Latin1.GetBytes(text);
        return bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
    }

    // ------------------------------------------------------------------ finding the game
    /// <summary>A process of the game, by name only.</summary>
    public static bool IsGameProcess(string? name)
        => name != null && ProcessNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static readonly Regex ProfileId = new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A profile folder under "My Games\Rainbow Six - Siege" is the Ubisoft account ID (other folders such as "Benchmark" are not).</summary>
    public static bool IsProfileFolder(string? name) => name != null && ProfileId.IsMatch(name);

    /// <summary>The GameSettings.ini written last: the account that played last.</summary>
    public static string? Newest(IEnumerable<(string path, DateTime time)> files)
        => files.OrderByDescending(f => f.time).ThenBy(f => f.path, StringComparer.OrdinalIgnoreCase).Select(f => f.path).FirstOrDefault();

    /// <summary>An install folder from the registry ("C:/…/Tom Clancy's Rainbow Six Siege/") as a Windows path without the trailing slash.</summary>
    public static string? NormalizeDir(string? raw)
    {
        var d = raw?.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        if (string.IsNullOrEmpty(d)) return null;
        return d.EndsWith(':') ? d + "\\" : d;
    }

    /// <summary>A name or folder of Siege itself (not the Test Server, not Extraction); ® and ™ do not count.</summary>
    public static bool IsSiegeName(string? s)
    {
        if (s == null) return false;
        var t = Regex.Replace(s.Replace("®", "").Replace("™", ""), @"\s+", " ");
        if (t.Contains("Test Server", StringComparison.OrdinalIgnoreCase) || t.Contains("TestServer", StringComparison.OrdinalIgnoreCase)) return false;
        return t.Contains("Rainbow Six Siege", StringComparison.OrdinalIgnoreCase) || t.Contains("RainbowSixSiege", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex VdfPair = new(@"""((?:[^""\\]|\\.)*)""[ \t]+""((?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant);

    /// <summary>The library folders of Steam's steamapps\libraryfolders.vdf ("path" entries, or the old "1" "D:\\…" form).</summary>
    public static List<string> SteamLibraries(string? vdf)
    {
        var list = new List<string>();
        foreach (Match m in VdfPair.Matches(vdf ?? ""))
        {
            var k = Unescape(m.Groups[1].Value);
            var v = Unescape(m.Groups[2].Value);
            bool path = k.Equals("path", StringComparison.OrdinalIgnoreCase) || (k.All(char.IsDigit) && v.Contains(':'));
            if (path && NormalizeDir(v) is { } d && !list.Contains(d, StringComparer.OrdinalIgnoreCase)) list.Add(d);
        }
        return list;
    }

    /// <summary>"installdir" of a Steam appmanifest_*.acf: the folder under steamapps\common.</summary>
    public static string? AcfInstallDir(string? acf)
    {
        foreach (Match m in VdfPair.Matches(acf ?? ""))
            if (Unescape(m.Groups[1].Value).Equals("installdir", StringComparison.OrdinalIgnoreCase))
            {
                var v = Unescape(m.Groups[2].Value).Trim();
                return v.Length == 0 || v.Contains("..") || v.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 ? null : v;
            }
        return null;
    }

    /// <summary>InstallLocation of an Epic manifest (*.item, JSON) when it is Siege.</summary>
    public static string? EpicInstall(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            var name = r.TryGetProperty("DisplayName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (!IsSiegeName(name)) return null;
            return r.TryGetProperty("InstallLocation", out var l) && l.ValueKind == JsonValueKind.String ? NormalizeDir(l.GetString()) : null;
        }
        catch (JsonException) { return null; }
    }

    // ------------------------------------------------------------------ the page's line
    /// <summary>The one line under the game's name. older: OrbitTweak keeps to the profile it changed first, and that is
    /// not the one played last. Without the game folder (only the settings file found) it says so, like Fortnite and CS2.</summary>
    public static string Note(bool exeFound, bool configFound, bool running, int profiles, bool older)
    {
        if (!exeFound && !configFound) return "Rainbow Six Siege nicht gefunden (Ubisoft Connect, Steam und Epic geprüft)";
        if (!configFound) return NoConfig;
        if (running) return "Siege läuft gerade: schließ es, dann kannst du die Spieleinstellungen ändern";
        var line = older ? "OrbitTweak bleibt beim Ubisoft-Profil, das es zuerst geändert hat: für ein anderes setz erst zurück"
                 : profiles > 1 ? $"{profiles} Ubisoft-Profile gefunden: OrbitTweak ändert nur das zuletzt gespielte"
                 : null;
        if (exeFound) return line ?? "Änderungen wirken ab dem nächsten Start von Siege";
        return line == null ? NoFolder : line + ". Den Spielordner hat OrbitTweak nicht gefunden, deshalb geht „Starke Grafikkarte für Siege“ nicht";
    }

    // ------------------------------------------------------------------ helpers
    private static bool TryNum(string? s, out double d)
        => double.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && double.IsFinite(d);

    private static bool Whole(double d) => d == Math.Floor(d) && Math.Abs(d) < 1e9;

    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                var c = s[++i];
                sb.Append(c switch { 'n' => '\n', 't' => '\t', _ => c });
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
