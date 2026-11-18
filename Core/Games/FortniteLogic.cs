using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OrbitTweak.Core;

// =====================================================================
//  Fortnite (2.30), the pure rules: which keys of Fortnite's own GameUserSettings.ini each item reads and writes, how
//  values map to the page, what is allowed, the backups, the texts, finding the install from Epic's records and the
//  game log, and reading/writing the file byte for byte. No Windows calls, so tests/GameLogic.Tests/FortniteTests.cs
//  checks it anywhere. The Windows side (processes, files, registry, GPU) is Core/Games/Fortnite.cs.
//
//  Sources for the keys: Unreal Engine 5.7 WindowsDynamicRHI.cpp (GetPreferredFeatureLevel / ChoosePreferredRHI read
//  [D3DRHIPreference] PreferredRHI dx12|dx11|vulkan|opengl and PreferredFeatureLevel sm5|sm6|es31 from
//  GameUserSettings.ini; PreferredFeatureLevel wins over the legacy bPreferFeatureLevelES31), GameUserSettings.cpp
//  (FrameRateLimit, bUseVSync), Fortnite configs 2023-2026 and a 23.20 SDK dump (LatencyTweak2, bShowFPS,
//  sg.ShadowQuality). Epic does not document the file itself.
// =====================================================================

/// <summary>The pure rules of the Fortnite tweaks (no Windows calls): tests/GameLogic.Tests/FortniteTests.cs checks them.</summary>
public static class FortniteLogic
{
    public const string Id = "fortnite";

    // ---------- the file and its sections ----------
    public const string SecUser = "/Script/FortniteGame.FortGameUserSettings";
    public const string SecScalability = "ScalabilityGroups";
    public const string SecRhi = "D3DRHIPreference";

    /// <summary>Backups in OrbitTweak's backup store: "og.fortnite.&lt;key&gt;" = the raw value before the first change,
    /// "-" when the key was not there.</summary>
    public const string BackupPrefix = "og.fortnite.";
    /// <summary>The per-app GPU preference keeps its backup per exe under this name (GpuPrefs).</summary>
    public const string GpuBackup = "og.fortnite.gpu";
    public const string GpuField = "GpuPreference";
    public const string NotThere = "-";

    public static string BackupKey(string key) => BackupPrefix + key;

    /// <summary>
    /// Every process of the 2026 launch chain (FortniteBootstrapper → FortniteLauncher → the anti-cheat bootstrapper
    /// _EAC_EOS, _EAC or _BE → FortniteClient-Win64-Shipping). Any of them running means the game may still save the
    /// settings it holds in memory over the file. Matched by name only: no handle is opened on them.
    /// </summary>
    public static readonly string[] ProcessNames =
    {
        "FortniteClient-Win64-Shipping", "FortniteClient-Win64-Shipping_EAC_EOS", "FortniteClient-Win64-Shipping_EAC",
        "FortniteClient-Win64-Shipping_BE", "FortniteLauncher", "FortniteBootstrapper",
    };

    /// <summary>The exe that renders (the GPU preference target and the proof that a folder is a Fortnite install).</summary>
    public const string ExeRel = @"FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe";

    // ---------- German texts ----------
    public const string MsgRunning = "Fortnite läuft gerade. Schließ das Spiel ganz, sonst überschreibt Fortnite die Änderung beim Beenden.";
    public const string MsgNoConfig = "Starte Fortnite einmal und schließ es wieder, dann legt es seine Einstellungen an.";
    public const string MsgNoNvidia = "Nur mit NVIDIA-Grafikkarte (GeForce GTX 900 oder neuer).";
    public const string MsgPerfShadows = "Im Leistungsmodus hat Fortnite keine Schatten-Einstellung.";
    public const string MsgOneGpu = "Nur bei PCs mit zwei Grafikkarten, z. B. Laptop mit Onboard- und Gaming-GPU.";
    public const string MsgNoInstall = "Fortnite-Installation nicht gefunden.";
    public const string MsgUnreadable = "Fortnites Einstellungsdatei lässt sich gerade nicht lesen. Versuch es gleich noch mal.";
    public const string MsgLocked = "Fortnites Einstellungsdatei ist gerade gesperrt. Versuch es gleich noch mal.";
    public const string MsgNoAccess = "Kein Schreibzugriff auf Fortnites Einstellungsdatei.";
    public const string MsgUnknown = "Unbekannte Einstellung.";
    public const string MsgBadValue = "Diesen Wert gibt es hier nicht.";
    public const string MsgBadFps = "Erlaubt sind „Unbegrenzt“ oder 30 bis 360 FPS.";
    public const string AntiCheat = "Fortnite nutzt Easy Anti-Cheat. OrbitTweak ändert nur Werte, die Fortnite selbst in seinem Grafikmenü speichert "
        + "(in der GameUserSettings.ini und nur bei geschlossenem Spiel), und die Windows-Grafikeinstellung pro App. "
        + "Spieldateien und das laufende Spiel fasst es nie an.";

    /// <summary>The one line for the page.</summary>
    public static string Note(bool installed, bool configFound, bool running)
    {
        if (running) return "Fortnite läuft gerade. Schließ das Spiel ganz, bevor du hier etwas änderst.";
        if (!installed && !configFound) return "Fortnite wurde auf diesem PC nicht gefunden. Ist es installiert, starte es einmal und schließ es wieder.";
        if (!configFound) return MsgNoConfig;
        if (!installed) return "Installationsordner nicht gefunden. Die Spieleinstellungen gehen trotzdem, nur „Starke Grafikkarte erzwingen“ braucht ihn.";
        return "Die Änderungen wirken ab dem nächsten Start von Fortnite.";
    }

    // ---------- the items ----------
    public sealed record Def(string Id, string Title, string Sum, string Desc, string Kind, string Effect, string Source, bool Rec, bool NeedsClosed);

    /// <summary>The items in page order. Everything but "gpu" lives in GameUserSettings.ini, which Fortnite rewrites on exit.</summary>
    public static readonly Def[] Defs =
    {
        new("render", "Rendermodus",
            "Leistung bringt meist mehr FPS, DirectX 12 das schönere Bild.",
            "Seit Update 40.10 (April 2026) hat Fortnite nur noch zwei Rendermodi: DirectX 12 und Leistung. Der Leistungsmodus läuft seit 37.00 "
            + "ebenfalls über DirectX 12, rechnet aber mit einfacher Grafik: kein Lumen, kein Nanite, keine Schatten und einfachere Modelle. "
            + "Auf schwächeren Grafikkarten, Laptops und Onboard-Grafik bringt das deutlich mehr FPS und weniger Ruckler durch Shader-Kompilierung. "
            + "DirectX 12 sieht viel besser aus, kostet aber mehr Leistung, und nach Spiel- oder Treiber-Updates ruckeln dort die ersten Runden, "
            + "solange Fortnite seine Shader neu baut. Wie groß der Unterschied auf deinem PC ist, hängt von Hardware und Treiber ab. "
            + "Vergleich beide Modi am besten selbst mit „FPS anzeigen“. Die Änderung wirkt ab dem nächsten Start von Fortnite.",
            "choice", "fps",
            "Epic: Patchnotes 37.00 (DX12-Leistungsmodus) und 40.10 (DX11 entfernt); die Unreal Engine liest den Modus aus [D3DRHIPreference].",
            false, true),
        new("reflex", "NVIDIA Reflex",
            "Senkt die Eingabeverzögerung deutlich, die FPS bleiben praktisch gleich.",
            "Reflex hält die Warteschlange zwischen Prozessor und Grafikkarte leer, damit deine Eingabe schneller auf dem Bildschirm landet. "
            + "Am meisten bringt es, wenn die Grafikkarte voll ausgelastet ist. In einem Test von GameGPU (Oktober 2026, RTX 5090, Epic-Grafik, 1440p) "
            + "sank die Systemlatenz in Fortnite von 41 auf 21 ms. Hängen deine FPS an einem Limit fest oder bremst der Prozessor, ist der Gewinn klein. "
            + "„An + Boost“ hält zusätzlich den Grafiktakt hoch. Das hilft nur, wenn der Prozessor bremst, und kostet mehr Strom und Wärme. "
            + "Auf Laptops nimmst du besser „An“. Reflex gibt es nur mit einer NVIDIA-Grafikkarte ab GeForce GTX 900.",
            "choice", "latency",
            "NVIDIA (Reflex in Fortnite) und GameGPU-Latenztest 10/2026; Schlüssel aus Fortnites eigener GameUserSettings.ini.",
            true, true),
        new("vsync", "V-Sync",
            "Aus heißt weniger Verzögerung, aber Bildrisse sind möglich.",
            "Mit V-Sync wartet jedes Bild auf den Monitor. Das verhindert Bildrisse (Tearing), staut aber Bilder auf und erhöht die Eingabeverzögerung spürbar. "
            + "Außerdem können die FPS dann nicht über die Hz-Zahl deines Monitors steigen. Für schnelle Shooter ist V-Sync aus die übliche Wahl. "
            + "Der Preis: Ohne G-Sync oder FreeSync kann das Bild horizontal reißen. Mit G-Sync oder FreeSync bleibt das Bild ruhig, solange die FPS "
            + "unter der Hz-Zahl bleiben. Setz dafür ein FPS-Limit ein paar FPS unter die Hz-Zahl.",
            "switch", "latency",
            "Epic-Hilfe: V-Sync bindet die FPS an den Monitor. NVIDIA-Latenzleitfaden: V-Sync staut Bilder auf.",
            true, true),
        new("fpslimit", "FPS-Limit",
            "Ein festes Limit glättet die Bildzeiten und spart Strom.",
            "Ohne Limit rendert Fortnite so viele Bilder wie möglich. Schwanken deine FPS stark, fühlt sich ein Limit knapp unter dem, was dein PC "
            + "stabil schafft, oft ruhiger an. Die Bildzeiten werden gleichmäßiger, und der PC wird weniger heiß und laut, auf Laptops hält der Akku länger. "
            + "Mit G-Sync oder FreeSync stellst du das Limit ein paar FPS unter die Hz-Zahl des Monitors, dann bleibt das Bild ohne Tearing. "
            + "Erkennt OrbitTweak deinen Monitor, bietet dir die Liste dafür auch seine Hz-Zahl und einen Wert 3 FPS darunter an. "
            + "Der Preis: Mehr Bilder als das Limit bekommst du nicht, und ein unnötig tiefes Limit erhöht die Verzögerung leicht. "
            + "Im Spielmenü geht das Limit bis 360 FPS. „Unbegrenzt“ hebt es auf, dafür muss V-Sync aus sein.",
            "choice", "stability",
            "Epic-Hilfe „FPS gesperrt oder begrenzt“ (max. 360, Unbegrenzt) und Blur Busters G-SYNC 101 (Limit unter der Hz-Zahl).",
            false, true),
        new("shadows", "Schatten",
            "In DirectX 12 einer der größten FPS-Fresser: Aus bringt viel.",
            "Auf hohen Stufen nutzt Fortnite in DirectX 12 virtuelle Schattenkarten (Virtual Shadow Maps), die die Grafikkarte stark belasten. "
            + "In einer Community-Messung aus Kapitel 4 (2023) kosteten Schatten auf „Episch“ rund 40 % der FPS. Mit Schatten aus fällt diese Arbeit "
            + "fast ganz weg, und viele Profis spielen so. Der Preis: Das Bild wirkt flacher, und kein Schatten verrät dir mehr einen Gegner hinter einer Kante. "
            + "Die Voreinstellung im Spiel springt danach auf „Benutzerdefiniert“, das ist normal. Im Leistungsmodus gibt es keine Schatten-Einstellung.",
            "choice", "fps",
            "Epic-Techblog zu Virtual Shadow Maps in Fortnite. Die FPS-Zahl stammt aus einer älteren Community-Messung (2023).",
            false, true),
        new("showfps", "FPS anzeigen",
            "Zeigt dir die FPS, damit du jeden Tweak selbst prüfen kannst.",
            "Das ist Fortnites eigener FPS-Zähler aus den Videoeinstellungen. Er kostet praktisch keine Leistung. So siehst du am einfachsten, "
            + "ob eine Änderung auf deinem PC wirklich etwas bringt, zum Beispiel Leistungsmodus gegen DirectX 12 oder Schatten aus.",
            "switch", "comfort",
            "Fortnites eigene Videoeinstellung „FPS anzeigen“.",
            false, true),
        new("gpu", "Starke Grafikkarte erzwingen",
            "Auf PCs mit zwei GPUs läuft Fortnite sicher auf der schnellen.",
            "Auf PCs mit zwei Grafikchips entscheidet Windows, welcher ein Spiel rendert. Das sind meist Laptops mit Onboard-Grafik und einer "
            + "zusätzlichen NVIDIA- oder AMD-Karte. Diese Einstellung ist dieselbe wie unter Einstellungen > System > Anzeige > Grafik > „Hohe Leistung“, "
            + "nur für Fortnite. Meistens wählt der Treiber schon die richtige Karte. Landet Fortnite aber auf der Onboard-Grafik, hast du nur einen "
            + "Bruchteil der FPS. Der Preis: mehr Akkuverbrauch. Die Einstellung wirkt ab dem nächsten Start, und an Fortnite selbst ändert sich nichts.",
            "switch", "fps",
            "Windows-Grafikeinstellung pro App („Hohe Leistung“). Die gleiche Einstellung nutzt OrbitTweak schon für FiveM.",
            true, false),
    };

    public static Def? Find(string? id) => Defs.FirstOrDefault(d => d.Id == id);

    /// <summary>The item lives in GameUserSettings.ini (everything but the Windows GPU preference).</summary>
    public static bool IsIni(string id) => id is "render" or "reflex" or "vsync" or "fpslimit" or "shadows" or "showfps";

    /// <summary>The ini keys an item writes (each with its own backup). Empty for "gpu" and unknown ids.</summary>
    public static IReadOnlyList<(string Section, string Key)> KeysOf(string id) => id switch
    {
        // PreferredRHI first: UE picks the RHI, then the feature level for it
        "render" => new[] { (SecRhi, "PreferredRHI"), (SecRhi, "PreferredFeatureLevel") },
        "reflex" => new[] { (SecUser, "LatencyTweak2") },
        "vsync" => new[] { (SecUser, "bUseVSync") },
        "fpslimit" => new[] { (SecUser, "FrameRateLimit") },
        "shadows" => new[] { (SecScalability, "sg.ShadowQuality") },
        "showfps" => new[] { (SecUser, "bShowFPS") },
        _ => Array.Empty<(string, string)>(),
    };

    // ---------- reading ----------

    /// <summary>
    /// "perf" (Leistung), "dx12" or null (not set: Fortnite picks by hardware on its first start). es31 is the
    /// Performance mode, also the old "dx11 + es31"; PreferredRHI=dx11 alone is the DirectX 11 mode Epic removed in 40.10
    /// and moves to Leistung. Without PreferredFeatureLevel UE still reads the legacy bPreferFeatureLevelES31.
    /// </summary>
    public static string? Render(string? ini)
    {
        var lvl = IniText.Get(ini, SecRhi, "PreferredFeatureLevel")?.ToLowerInvariant();
        var rhi = IniText.Get(ini, SecRhi, "PreferredRHI")?.ToLowerInvariant();
        if (rhi is not (null or "dx12" or "dx11")) return null;   // vulkan, opengl: no Fortnite mode
        if (lvl == "es31" || rhi == "dx11") return "perf";
        if (lvl is "sm6" or "sm5") return "dx12";
        if (lvl == null && Bool(IniText.Get(ini, SecRhi, "bPreferFeatureLevelES31")) == true) return "perf";
        return null;
    }

    /// <summary>Schatten exist only outside the Performance mode.</summary>
    public static bool ShadowsAvailable(string? ini) => Render(ini) != "perf";

    /// <summary>UE's FCString::ToBool words; null when the value is missing or none of them.</summary>
    public static bool? Bool(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" => true,
        "false" or "no" or "off" or "0" => false,
        _ => null,
    };

    /// <summary>A whole number as the page's choice value ("2"), null when it is none.</summary>
    public static string? Int(string? raw)
        => int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>FrameRateLimit ("144.000000") as a whole number ("144"); null when it is no number.</summary>
    public static string? Fps(string? raw)
    {
        if (!double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v)) return null;
        if (v > int.MaxValue || v < int.MinValue) return null;
        return ((int)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The page value of an ini item: a string for choices, a bool for switches, null when the key is not set.</summary>
    public static object? Read(string id, string? ini) => id switch
    {
        "render" => Render(ini),
        "reflex" => Int(IniText.Get(ini, SecUser, "LatencyTweak2")),
        "vsync" => Bool(IniText.Get(ini, SecUser, "bUseVSync")),
        "fpslimit" => Fps(IniText.Get(ini, SecUser, "FrameRateLimit")),
        "shadows" => Int(IniText.Get(ini, SecScalability, "sg.ShadowQuality")),
        "showfps" => Bool(IniText.Get(ini, SecUser, "bShowFPS")),
        _ => null,
    };

    // ---------- choices ----------
    public static readonly (string V, string T)[] RenderOptions = { ("perf", "Leistung"), ("dx12", "DirectX 12") };
    public static readonly (string V, string T)[] ReflexOptions = { ("0", "Aus"), ("1", "An"), ("2", "An + Boost") };
    public static readonly (string V, string T)[] ShadowOptions = { ("0", "Aus"), ("1", "Mittel"), ("2", "Hoch"), ("3", "Episch") };
    public static readonly int[] FpsSteps = { 0, 30, 60, 120, 144, 165, 240, 360 };
    public const int FpsMin = 30, FpsMax = 360;

    /// <summary>
    /// The choices of an item, null for switches. A current value that is not in the list is added so the page can show
    /// it. The FPS list also gets the main monitor's refresh rate and 3 FPS below it (the G-Sync/FreeSync cap from
    /// Blur Busters' G-SYNC 101), both only when they are allowed values.
    /// </summary>
    public static List<(string V, string T)>? Options(string id, object? current, int monitorHz)
    {
        var cur = current as string;
        switch (id)
        {
            case "render": return RenderOptions.ToList();
            case "reflex": return WithCurrent(ReflexOptions, cur, v => "Wert " + v);
            case "shadows": return WithCurrent(ShadowOptions, cur, v => "Stufe " + v);
            case "fpslimit":
            {
                var steps = new SortedDictionary<int, string>();
                foreach (var s in FpsSteps) steps[s] = FpsLabel(s);
                if (monitorHz >= FpsMin && monitorHz <= FpsMax) steps[monitorHz] = monitorHz + " FPS (Monitor)";
                int vrr = monitorHz - 3;
                if (monitorHz >= 60 && vrr >= FpsMin && vrr <= FpsMax && !steps.ContainsKey(vrr)) steps[vrr] = vrr + " FPS (G-Sync/FreeSync)";
                if (cur != null && int.TryParse(cur, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) && !steps.ContainsKey(c)) steps[c] = FpsLabel(c);
                // 0 (Unbegrenzt) first, then up; a negative current value would sort before it, so it goes last
                return steps.OrderBy(kv => kv.Key == 0 ? 0 : kv.Key < 0 ? 2 : 1).ThenBy(kv => kv.Key)
                    .Select(kv => (kv.Key.ToString(CultureInfo.InvariantCulture), kv.Value)).ToList();
            }
            default: return null;
        }
    }

    private static string FpsLabel(int fps) => fps == 0 ? "Unbegrenzt" : fps + " FPS";

    private static List<(string V, string T)> WithCurrent((string V, string T)[] list, string? cur, Func<string, string> label)
    {
        var l = list.ToList();
        if (cur != null && !l.Any(o => o.V == cur)) l.Add((cur, label(cur)));
        return l;
    }

    /// <summary>"Empfohlene anwenden": Reflex "An" (an existing "An + Boost" stays), V-Sync off, the strong GPU on.</summary>
    public static object? RecValue(string id, object? current) => id switch
    {
        "reflex" => current as string == "2" ? "2" : "1",
        "vsync" => false,
        "gpu" => true,
        _ => null,
    };

    // ---------- writing ----------

    /// <summary>The key lines for a value from the page, exactly as Fortnite writes them (True/False, lowercase RHI names,
    /// FrameRateLimit with six decimals). Throws InvalidOperationException with a German message for anything else.</summary>
    public static List<(string Section, string Key, string Value)> Writes(string id, JsonElement value)
    {
        switch (id)
        {
            case "render":
                return Choice(value) switch
                {
                    "perf" => new() { (SecRhi, "PreferredRHI", "dx12"), (SecRhi, "PreferredFeatureLevel", "es31") },
                    "dx12" => new() { (SecRhi, "PreferredRHI", "dx12"), (SecRhi, "PreferredFeatureLevel", "sm6") },
                    _ => throw Bad(MsgBadValue),
                };
            case "reflex":
            {
                var v = Choice(value);
                if (v is not ("0" or "1" or "2")) throw Bad(MsgBadValue);
                return new() { (SecUser, "LatencyTweak2", v) };
            }
            case "vsync": return new() { (SecUser, "bUseVSync", BoolText(Switch(value))) };
            case "showfps": return new() { (SecUser, "bShowFPS", BoolText(Switch(value))) };
            case "fpslimit": return new() { (SecUser, "FrameRateLimit", FpsText(FpsValue(value))) };
            case "shadows":
            {
                var v = Choice(value);
                if (v is not ("0" or "1" or "2" or "3")) throw Bad(MsgBadValue);
                return new() { (SecScalability, "sg.ShadowQuality", v) };
            }
            default: throw Bad(MsgUnknown);
        }
    }

    public static string BoolText(bool on) => on ? "True" : "False";

    /// <summary>"144.000000": UE writes float properties with six decimals, always with a dot.</summary>
    public static string FpsText(int fps) => ((double)fps).ToString("F6", CultureInfo.InvariantCulture);

    /// <summary>A switch value from the page: true/false (also as text).</summary>
    public static bool Switch(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(v.GetString()?.Trim(), out var b) => b,
        _ => throw Bad(MsgBadValue),
    };

    /// <summary>A choice value from the page: text, or a number as it was sent.</summary>
    public static string Choice(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString()?.Trim() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        _ => throw Bad(MsgBadValue),
    };

    /// <summary>The FPS limit from the page: 0 (Unbegrenzt) or a whole number from 30 to 360 (Fortnite's menu goes up to 360).</summary>
    public static int FpsValue(JsonElement v)
    {
        var s = v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? Choice(v) : throw Bad(MsgBadFps);
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d) || d != Math.Floor(d)) throw Bad(MsgBadFps);
        if (d != 0 && (d < FpsMin || d > FpsMax)) throw Bad(MsgBadFps);
        return (int)d;
    }

    private static InvalidOperationException Bad(string msg) => new(msg);

    /// <summary>
    /// Plans one change: the text with the keys set and the backups to make before the file is written (backup key →
    /// raw value before, "-" when the key was not there). A key that already has a backup keeps it: the first one is
    /// the real one. Keys that already hold the value are left alone and get no backup.
    /// </summary>
    public static (string Text, List<(string Backup, string Before)> NewBackups) PlanSet(string ini,
        IEnumerable<(string Section, string Key, string Value)> writes, Func<string, bool> hasBackup)
    {
        var text = ini;
        var made = new List<(string, string)>();
        foreach (var (sec, key, val) in writes)
        {
            var cur = IniText.Get(text, sec, key);
            if (cur == val) continue;
            var bk = BackupKey(key);
            if (!hasBackup(bk) && !made.Any(m => m.Item1 == bk)) made.Add((bk, cur ?? NotThere));
            text = IniText.Set(text, sec, key, val);
        }
        return (text, made);
    }

    /// <summary>The backups that can go after a write: the file holds exactly the value from before again, so nothing
    /// of OrbitTweak's is left in that key.</summary>
    public static List<string> Settled(IEnumerable<(string Section, string Key, string Value)> writes, Func<string, string?> backupOf)
        => writes.Where(w => backupOf(BackupKey(w.Key)) == w.Value).Select(w => BackupKey(w.Key)).Distinct().ToList();

    /// <summary>
    /// The text with the backed-up keys restored: the old raw value, or the key line removed when it was not there
    /// ("-" or null). An empty section OrbitTweak added stays as an empty header; Fortnite rewrites the file anyway.
    /// </summary>
    public static string PlanRevert(string ini, IEnumerable<(string Section, string Key, string? Backup)> restores)
    {
        var text = ini;
        foreach (var (sec, key, b) in restores)
            text = b is null or NotThere ? IniText.Remove(text, sec, key) : IniText.Set(text, sec, key, b);
        return text;
    }

    // ---------- finding the install ----------

    /// <summary>A folder as a Windows path without the slash at the end ("C:/x/" → "C:\x").</summary>
    public static string NormalizeRoot(string path)
    {
        var p = path.Trim().Trim('"').Replace('/', '\\');
        while (p.Length > 3 && p.EndsWith('\\')) p = p[..^1];
        return p;
    }

    public static string ExePath(string root)
    {
        var r = NormalizeRoot(root);
        return (r.EndsWith('\\') ? r : r + "\\") + ExeRel;
    }

    private static readonly JsonDocumentOptions JsonOpt = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private static bool IsFortnite(JsonElement e)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("AppName", out var a) && a.ValueKind == JsonValueKind.String
           && string.Equals(a.GetString(), "Fortnite", StringComparison.OrdinalIgnoreCase);

    private static string? Location(JsonElement e)
        => e.TryGetProperty("InstallLocation", out var l) && l.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(l.GetString())
            ? NormalizeRoot(l.GetString()!) : null;

    /// <summary>
    /// The install folder from one Epic Games Launcher manifest (%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item):
    /// only the game itself (AppName "Fortnite", add-ons have their own AppName) and only a finished install.
    /// </summary>
    public static string? ManifestRoot(string? json)
    {
        try
        {
            using var d = JsonDocument.Parse(json ?? "", JsonOpt);
            var r = d.RootElement;
            if (!IsFortnite(r)) return null;
            if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) return null;
            return Location(r);
        }
        catch { return null; }
    }

    /// <summary>Fortnite's folders in %ProgramData%\Epic\UnrealEngineLauncher\LauncherInstalled.dat ({"InstallationList":[…]}).</summary>
    public static List<string> LauncherInstalledRoots(string? json)
    {
        var list = new List<string>();
        try
        {
            using var d = JsonDocument.Parse(json ?? "", JsonOpt);
            if (d.RootElement.ValueKind != JsonValueKind.Object || !d.RootElement.TryGetProperty("InstallationList", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in arr.EnumerateArray())
                if (IsFortnite(e) && Location(e) is { } l && !list.Contains(l, StringComparer.OrdinalIgnoreCase)) list.Add(l);
        }
        catch { }
        return list;
    }

    private const string BaseDirTag = "LogInit: Base Directory:";
    private const string BinRel = @"\FortniteGame\Binaries\Win64";

    /// <summary>
    /// The install folder from the start of FortniteGame.log: Unreal's line "LogInit: Base Directory: C:/…/Fortnite/FortniteGame/Binaries/Win64/"
    /// (with or without the [time][frame] prefix). Nothing else of the log is read. Null when the line is missing or
    /// the folder is not a Fortnite layout.
    /// </summary>
    public static string? LogRoot(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            int i = line.IndexOf(BaseDirTag, StringComparison.Ordinal);
            if (i < 0) continue;
            var dir = NormalizeRoot(line[(i + BaseDirTag.Length)..]);
            if (!dir.EndsWith(BinRel, StringComparison.OrdinalIgnoreCase)) return null;
            var root = dir[..^BinRel.Length];
            return root.Length >= 2 ? root : null;
        }
        return null;
    }

    /// <summary>PCI vendor id of NVIDIA (DXGI_ADAPTER_DESC1.VendorId).</summary>
    public static bool IsNvidia(uint vendorId) => vendorId == 0x10DE;

    // ---------- the file, byte for byte ----------
    public enum TextEnc { Utf8, Utf8Bom, Utf16Le, Utf16LeBom, Utf16BeBom }

    /// <summary>
    /// The file as text. UTF-8 (UE 5.7 writes it without BOM) is read as Latin-1, one character per byte, so every byte
    /// that is not touched goes back exactly as it was, whatever it is. Very old files can be UTF-16 (with or without BOM).
    /// </summary>
    public static (string Text, TextEnc Enc) Decode(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return (Encoding.Latin1.GetString(b, 3, b.Length - 3), TextEnc.Utf8Bom);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return (Encoding.Unicode.GetString(b, 2, b.Length - 2), TextEnc.Utf16LeBom);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return (Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2), TextEnc.Utf16BeBom);
        if (LooksUtf16Le(b)) return (Encoding.Unicode.GetString(b), TextEnc.Utf16Le);
        return (Encoding.Latin1.GetString(b), TextEnc.Utf8);
    }

    public static byte[] Encode(string text, TextEnc enc) => enc switch
    {
        TextEnc.Utf8Bom => new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.Latin1.GetBytes(text)).ToArray(),
        TextEnc.Utf16LeBom => new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(text)).ToArray(),
        TextEnc.Utf16BeBom => new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes(text)).ToArray(),
        TextEnc.Utf16Le => Encoding.Unicode.GetBytes(text),
        _ => Encoding.Latin1.GetBytes(text),
    };

    /// <summary>UTF-16 LE without BOM: an INI starts with ASCII, so every second byte of the start is 0.</summary>
    private static bool LooksUtf16Le(byte[] b)
    {
        if (b.Length < 4 || b.Length % 2 != 0) return false;
        int n = Math.Min(b.Length, 64);
        bool text = false;
        for (int i = 0; i + 1 < n; i += 2)
        {
            if (b[i + 1] != 0) return false;
            if (b[i] != 0) text = true;
        }
        return text;
    }
}
