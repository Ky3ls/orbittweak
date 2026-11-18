using System.Text;
using System.Text.Json;
using OrbitTweak.Core;

/// <summary>Checks of R6Logic. Called from Program.cs.</summary>
internal static class R6Tests
{
    /// <summary>A GameSettings.ini as Siege writes it in 2026 (DirectX 12 build, NVIDIA card), shortened, CRLF, no BOM.</summary>
    private static readonly string Sample = string.Join("\r\n", new[]
    {
        "[GENERAL]",
        "Version=1",
        "",
        "[DISPLAY]",
        ";GPUAdapterInfo => Video adapter hash DeviceInstanceID",
        ";GPUAdapterSelectMode => 0 select by index (GPUAdapter) / 1 select by GPUAdapterInfo",
        ";FPSLimit => Limit the game's fps. Minimum of 30fps. Anything below will disable the fps limit.",
        ";NVReflex. 0 : Disable, 1 : ReflexOn, 2 - ReflexWithBoost",
        ";NVReflexIndicator => Enables the in-game latency metrics indication.",
        "GPUInfo=170648556",
        "GPUAdapter=0",
        "GPUAdapterSelectMode=0",
        "Brightness=60.000000",
        "FPSLimit=0",
        "NVReflex=2",
        "NVReflexIndicator=0",
        "Console=0",
        "",
        "[HARDWARE_INFO]",
        "GPUVendor=NVIDIA",
        "GPUDeviceId=0x2b85",
        "",
        "[DISPLAY_SETTINGS]",
        ";WindowMode       => 0 fullscreen / 1 windowed / 2 borderless",
        ";VSync            => 0 disabled / 1 frame / 2 frames",
        "Monitor=0",
        "ResolutionWidth=3840",
        "ResolutionHeight=2160",
        "RefreshRate=240.000000",
        "WindowMode=0",
        "AspectRatio=0",
        "VSync=1",
        "MaxGPUBufferedFrame=1",
        "",
        "[QUALITY]",
        "OverallQualityLevelName=Custom",
        "",
        "[INPUT]",
        "RawInputMouseKeyboard=0",
        "MouseSensitivity=14",
        "",
        "[ONLINE]",
        ";DataCenterHint => ",
        ";    default (ping based)",
        "DataCenterHint=default",
        "UseProxyAutoDiscovery=0",
        "",
    });

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static JsonElement V(object? o) => JsonSerializer.SerializeToElement(o);

    /// <summary>The German message of an InvalidOperationException, null when nothing was thrown.</summary>
    private static string? Err(Action a)
    {
        try { a(); return null; }
        catch (InvalidOperationException e) { return e.Message; }
    }

    private static R6Spec S(string id) => R6Logic.Find(id)!;

    public static void Run(Action<bool, string> Check)
    {
        Specs(Check);
        Values(Check);
        Editing(Check);
        Availability(Check);
        Gpu(Check);
        Bytes(Check);
        Finding(Check);
        Notes(Check);
    }

    // ------------------------------------------------------------------ the items
    private static void Specs(Action<bool, string> Check)
    {
        var ids = R6Logic.Specs.Select(s => s.Id).ToArray();
        Check(ids.SequenceEqual(new[] { "reflex", "vsync", "fpslimit", "rawinput", "windowmode", "gpu" }), "r6: the six items in page order");
        Check(R6Logic.Find("nope") == null && R6Logic.Find(null) == null, "r6: unknown ids are not found");
        Check(R6Logic.Specs.Select(R6Logic.BkKey).Distinct().Count() == R6Logic.Specs.Length, "r6: every item has its own backup key");
        Check(R6Logic.Specs.All(s => R6Logic.BkKey(s).StartsWith("og.r6.")) && R6Logic.BkFile.StartsWith("og.r6.") && R6Logic.BkGpu.StartsWith("og.r6."), "r6: backup keys start with og.r6.");
        Check(R6Logic.BkKey(S("reflex")) == "og.r6.NVReflex" && R6Logic.BkKey(S("rawinput")) == "og.r6.RawInputMouseKeyboard", "r6: INI backups are named after the key");

        var where = new Dictionary<string, (string sec, string key)>
        {
            ["reflex"] = ("DISPLAY", "NVReflex"), ["vsync"] = ("DISPLAY_SETTINGS", "VSync"), ["fpslimit"] = ("DISPLAY", "FPSLimit"),
            ["rawinput"] = ("INPUT", "RawInputMouseKeyboard"), ["windowmode"] = ("DISPLAY_SETTINGS", "WindowMode"),
        };
        foreach (var (id, (sec, key)) in where)
            Check(S(id).Section == sec && S(id).Key == key && !S(id).IsGpu && S(id).NeedsClosed, $"r6: {id} is [{sec}] {key} and needs the game closed");
        Check(S("gpu").IsGpu && !S("gpu").NeedsClosed && S("gpu").Key == null, "r6: the GPU item is no INI line and works while the game runs");

        var kinds = new[] { "switch", "choice", "number" };
        var effects = new[] { "fps", "latency", "comfort", "stability" };
        foreach (var s in R6Logic.Specs)
        {
            Check(kinds.Contains(s.Kind) && effects.Contains(s.Effect), $"r6: {s.Id} has a known kind and effect");
            Check(s.Title.Length > 0 && s.Sum.Length is > 0 and <= 80 && s.Desc.Length > s.Sum.Length * 2 && s.Source.Length > 0, $"r6: {s.Id} has title, a short sum, a full desc and a source");
            Check(!s.Sum.EndsWith('.') && !s.Title.EndsWith('.'), $"r6: {s.Id} title and sum have no full stop");
            var formal = new[] { " Sie ", " Ihre ", " Ihr ", " Ihnen " };
            Check(!formal.Any(f => (" " + s.Sum + " " + s.Desc + " ").Contains(f)), $"r6: {s.Id} speaks with du, not Sie");
            Check((s.Kind == "choice") == (s.Options != null), $"r6: {s.Id} has options exactly when it is a choice");
            Check((s.Kind == "number") == (s.Min != null && s.Max != null && s.Step != null && s.Unit != null), $"r6: {s.Id} has min/max/step/unit exactly when it is a number");
            if (s.Options != null)
                Check(s.Options.Select(o => o.V).Distinct().Count() == s.Options.Length && s.Options.All(o => o.T.Length > 0), $"r6: {s.Id} options are distinct and labelled");
            Check(s.Rec == (s.RecValue != null), $"r6: {s.Id} has a recommended value exactly when it is recommended");
            if (s.Rec && !s.IsGpu)
            {
                // "Empfohlene anwenden" compares the JSON of Value and RecValue: the value read back after writing RecValue must be the same
                var ini = R6Logic.ToIni(s, V(s.RecValue));
                Check(JsonSerializer.Serialize(R6Logic.ToPage(s, ini)) == JsonSerializer.Serialize(s.RecValue), $"r6: {s.Id} reads back its recommended value unchanged");
            }
        }
        Check(S("reflex").RecValue as string == "1" && S("vsync").RecValue as string == "0" && S("rawinput").RecValue is true && S("gpu").RecValue is true, "r6: recommended: Reflex on (no boost), V-Sync off, Raw Input on, strong GPU");
        Check(!S("fpslimit").Rec && !S("windowmode").Rec, "r6: FPS limit and display mode are a matter of taste, not recommended");
        Check(S("windowmode").Options!.Select(o => o.V).SequenceEqual(new[] { "0", "2", "1" }), "r6: display mode lists fullscreen, borderless, windowed");
        Check(S("fpslimit").Min == 0 && S("fpslimit").Max == 500 && S("fpslimit").Step == 1 && S("fpslimit").Unit == "FPS", "r6: FPS limit 0–500 in steps of 1");
        Check(R6Logic.ProcessNames.Contains("RainbowSix") && R6Logic.ProcessNames.Contains("RainbowSix_BE"), "r6: the game and its BattlEye launcher count as running");
        Check(R6Logic.AntiCheat.Contains("BattlEye") && R6Logic.AntiCheat.Contains("GameSettings.ini"), "r6: the anti-cheat line names BattlEye and what is changed");
        Check(R6Logic.AntiCheat.Contains("Die GameSettings.ini schreibt es nur, wenn Siege geschlossen ist"), "r6: the anti-cheat line limits \"only while closed\" to the INI (the GPU item works while Siege runs)");
    }

    // ------------------------------------------------------------------ values
    private static void Values(Action<bool, string> Check)
    {
        var reflex = S("reflex"); var vsync = S("vsync"); var fps = S("fpslimit"); var raw = S("rawinput"); var mode = S("windowmode");

        // file → page
        Check(R6Logic.ToPage(raw, "1") is true && R6Logic.ToPage(raw, "0") is false, "r6: Raw Input 1/0 is on/off");
        Check(R6Logic.ToPage(raw, "1.000000") is true, "r6: a float 1 is on too");
        Check(R6Logic.ToPage(raw, null) == null && R6Logic.ToPage(raw, "x") == null && R6Logic.ToPage(raw, "0.5") == null, "r6: missing or odd switch values are unknown");
        Check(R6Logic.ToPage(reflex, "2") as string == "2" && R6Logic.ToPage(reflex, " 1 ") as string == "1", "r6: a choice reads its option value");
        Check(R6Logic.ToPage(vsync, "1.000000") as string == "1", "r6: a float choice value maps to its option");
        Check(R6Logic.ToPage(reflex, "3") == null && R6Logic.ToPage(mode, "-1") == null && R6Logic.ToPage(vsync, "") == null, "r6: values the game does not document are unknown");
        Check(R6Logic.ToPage(fps, "0") is 0 && R6Logic.ToPage(fps, "141") is 141 && R6Logic.ToPage(fps, "60.000000") is 60, "r6: FPS limit reads as a whole number");
        Check(R6Logic.ToPage(fps, "20") is 0 && R6Logic.ToPage(fps, "-5") is 0, "r6: below 30 the game has no limit: shown as 0");
        Check(R6Logic.ToPage(fps, "999") is 999, "r6: a limit above the app's 500 still shows what the file says");
        Check(R6Logic.ToPage(fps, "1e12") == null && R6Logic.ToPage(fps, "NaN") == null, "r6: absurd numbers are unknown");

        // page → file
        Check(R6Logic.ToIni(raw, J("true")) == "1" && R6Logic.ToIni(raw, J("false")) == "0", "r6: on/off writes 1/0");
        Check(R6Logic.ToIni(raw, J("1")) == "1" && R6Logic.ToIni(raw, J("\"0\"")) == "0" && R6Logic.ToIni(raw, J("\"True\"")) == "1", "r6: on/off also as 1/0 or text");
        Check(Err(() => R6Logic.ToIni(raw, J("2"))) == "Für Raw Input geht nur an oder aus.", "r6: a switch refuses 2");
        Check(Err(() => R6Logic.ToIni(raw, J("\"ja\""))) != null && Err(() => R6Logic.ToIni(raw, J("null"))) != null && Err(() => R6Logic.ToIni(raw, default)) != null, "r6: a switch refuses text, null and no value");

        Check(R6Logic.ToIni(reflex, J("\"2\"")) == "2" && R6Logic.ToIni(reflex, J("1")) == "1", "r6: a choice takes its option as text or number");
        Check(R6Logic.ToIni(mode, J("\"2.0\"")) == "2", "r6: a choice writes the plain integer");
        Check(Err(() => R6Logic.ToIni(reflex, J("\"3\""))) == "Für NVIDIA Reflex gibt es diese Auswahl nicht.", "r6: a choice refuses values that are no option");
        foreach (var bad in new[] { "\"\"", "\"an\"", "1.5", "true", "null", "-1", "[]" })
            Check(Err(() => R6Logic.ToIni(vsync, J(bad))) != null, $"r6: V-Sync refuses {bad}");
        Check(Err(() => R6Logic.ToIni(vsync, default)) != null, "r6: a choice refuses no value");

        Check(R6Logic.ToIni(fps, J("0")) == "0" && R6Logic.ToIni(fps, J("30")) == "30" && R6Logic.ToIni(fps, J("141")) == "141" && R6Logic.ToIni(fps, J("500")) == "500", "r6: FPS limit 0, 30, 141, 500 are fine");
        Check(R6Logic.ToIni(fps, J("\"144\"")) == "144" && R6Logic.ToIni(fps, J("240.0")) == "240", "r6: FPS limit as text or whole float");
        Check(Err(() => R6Logic.ToIni(fps, J("29"))) == R6Logic.FpsRange && Err(() => R6Logic.ToIni(fps, J("1"))) == R6Logic.FpsRange, "r6: 1–29 is refused: the game would switch the limit off");
        Check(R6Logic.FpsRange == "Das FPS-Limit ist 0 (aus) oder mindestens 30.", "r6: the exact German refusal for 1–29");
        Check(Err(() => R6Logic.ToIni(fps, J("-1"))) == R6Logic.FpsRange, "r6: negative limits are refused");
        Check(Err(() => R6Logic.ToIni(fps, J("501"))) == R6Logic.FpsMax, "r6: more than 500 is refused");
        Check(Err(() => R6Logic.ToIni(fps, J("141.5"))) == R6Logic.FpsWhole, "r6: no half frames");
        Check(Err(() => R6Logic.ToIni(fps, J("\"viel\""))) != null && Err(() => R6Logic.ToIni(fps, J("true"))) != null && Err(() => R6Logic.ToIni(fps, default)) != null, "r6: FPS limit refuses text, bool and no value");

        Check(R6Logic.ToBool(J("true")) && !R6Logic.ToBool(J("0")) && R6Logic.ToBool(J("\"1\"")), "r6: ToBool reads true/0/\"1\"");
        Check(Err(() => R6Logic.ToBool(J("\"vielleicht\""))) != null, "r6: ToBool refuses anything else");

        // Same / IsChanged
        Check(R6Logic.Same("1", "1.000000") && R6Logic.Same(null, null) && R6Logic.Same("default", "Default"), "r6: same values: numbers by value, text without case");
        Check(!R6Logic.Same("1", null) && !R6Logic.Same(null, "0") && !R6Logic.Same("0", "1"), "r6: different values");
        Check(!R6Logic.IsChanged(null, "1"), "r6: no backup: not changed by OrbitTweak");
        Check(!R6Logic.IsChanged("-", null) && R6Logic.IsChanged("-", "1"), "r6: was not there: changed while the key exists");
        Check(!R6Logic.IsChanged("0", "0") && !R6Logic.IsChanged("1", "1.000000") && R6Logic.IsChanged("0", "1") && R6Logic.IsChanged("1", null), "r6: changed compares the value with the backup");
    }

    // ------------------------------------------------------------------ editing the file
    private static void Editing(Action<bool, string> Check)
    {
        var reflex = S("reflex"); var fps = S("fpslimit"); var raw = S("rawinput"); var vsync = S("vsync");

        Check(R6Logic.ToPage(reflex, IniText.Get(Sample, "DISPLAY", "NVReflex")) as string == "2", "r6: sample: Reflex + Boost");
        Check(R6Logic.ToPage(vsync, IniText.Get(Sample, "DISPLAY_SETTINGS", "VSync")) as string == "1", "r6: sample: V-Sync on");
        Check(R6Logic.ToPage(raw, IniText.Get(Sample, "INPUT", "RawInputMouseKeyboard")) is false, "r6: sample: Raw Input off");
        Check(R6Logic.ToPage(fps, IniText.Get(Sample, "DISPLAY", "FPSLimit")) is 0, "r6: sample: no FPS limit");
        Check(IniText.Get(Sample, "DISPLAY", "NVReflexIndicator") == "0", "r6: NVReflexIndicator is its own key, not NVReflex");

        // one change touches exactly one line, the CRLF stays
        var set = IniText.Set(Sample, "DISPLAY", "NVReflex", R6Logic.ToIni(reflex, V("1")));
        Check(set == Sample.Replace("NVReflex=2\r\n", "NVReflex=1\r\n"), "r6: Reflex is replaced in place, every other byte stays");
        Check(IniText.Get(set, "DISPLAY", "NVReflexIndicator") == "0", "r6: the latency overlay stays as it was");
        Check(!set.Replace("\r\n", "").Contains('\n'), "r6: the file keeps CRLF only");

        // apply every recommendation the way the host does, with backups, then revert: the file is byte for byte back
        var text = Sample;
        var backups = new Dictionary<string, string>();
        foreach (var s in R6Logic.Specs.Where(s => !s.IsGpu))
        {
            object? want = s.Rec ? s.RecValue : s.Id == "fpslimit" ? 141 : "2";
            var target = R6Logic.ToIni(s, V(want));
            var cur = IniText.Get(text, s.Section!, s.Key!);
            if (R6Logic.Same(cur, target)) continue;
            if (!backups.ContainsKey(R6Logic.BkKey(s))) backups[R6Logic.BkKey(s)] = cur ?? "-";
            text = IniText.Set(text, s.Section!, s.Key!, target);
        }
        Check(IniText.Get(text, "DISPLAY", "FPSLimit") == "141" && IniText.Get(text, "DISPLAY_SETTINGS", "WindowMode") == "2"
              && IniText.Get(text, "DISPLAY", "NVReflex") == "1" && IniText.Get(text, "DISPLAY_SETTINGS", "VSync") == "0"
              && IniText.Get(text, "INPUT", "RawInputMouseKeyboard") == "1", "r6: all five INI items written");
        Check(backups.Count == 5 && backups["og.r6.NVReflex"] == "2" && backups["og.r6.FPSLimit"] == "0", "r6: each key backed up with its old value");
        Check(R6Logic.Specs.Where(s => !s.IsGpu).All(s => R6Logic.IsChanged(backups[R6Logic.BkKey(s)], IniText.Get(text, s.Section!, s.Key!))), "r6: every written item shows as changed");
        var back = text;
        foreach (var s in R6Logic.Specs.Where(s => !s.IsGpu)) back = R6Logic.Restore(back, s, backups.GetValueOrDefault(R6Logic.BkKey(s)));
        Check(back == Sample, "r6: revert gives back the file byte for byte");

        // a key that was not there: added at the end of its section, removed again by the revert
        var noKey = Sample.Replace("NVReflex=2\r\n", "");
        var added = IniText.Set(noKey, "DISPLAY", "NVReflex", "1");
        Check(added == noKey.Replace("Console=0\r\n", "Console=0\r\nNVReflex=1\r\n"), "r6: a missing key goes to the end of its section with CRLF");
        Check(R6Logic.Restore(added, reflex, "-") == noKey, "r6: revert of \"was not there\" removes the line again");
        Check(R6Logic.Restore(noKey, reflex, "-") == noKey, "r6: revert of \"was not there\" when it still is not there changes nothing");
        Check(R6Logic.Restore(Sample, reflex, null) == Sample, "r6: no backup: revert changes nothing");
        Check(R6Logic.Restore(Sample, S("gpu"), "2") == Sample, "r6: the GPU item never touches the INI");
        Check(R6Logic.Restore(set, reflex, "2") == Sample, "r6: revert to the old value");

        // the game's own spelling survives: a float value goes back as it was
        var flt = Sample.Replace("FPSLimit=0\r\n", "FPSLimit=0.000000\r\n");
        var flt2 = IniText.Set(flt, "DISPLAY", "FPSLimit", "141");
        Check(R6Logic.Restore(flt2, fps, IniText.Get(flt, "DISPLAY", "FPSLimit")) == flt, "r6: revert keeps the old spelling 0.000000");
    }

    // ------------------------------------------------------------------ availability
    private static void Availability(Action<bool, string> Check)
    {
        var reflex = S("reflex");
        Check(R6Logic.WhyNot(reflex, Sample) == "", "r6: Reflex is available on NVIDIA");
        Check(R6Logic.WhyNot(reflex, Sample.Replace("GPUVendor=NVIDIA", "GPUVendor=AMD")) == R6Logic.NoNvidia, "r6: Reflex is not available on AMD");
        Check(R6Logic.WhyNot(reflex, Sample.Replace("GPUVendor=NVIDIA", "GPUVendor=Intel")) == R6Logic.NoNvidia, "r6: Reflex is not available on Intel");
        Check(R6Logic.WhyNot(reflex, Sample.Replace("GPUVendor=NVIDIA", "GPUVendor=nvidia corporation")) == "", "r6: vendor without case");
        Check(R6Logic.WhyNot(reflex, Sample.Replace("GPUVendor=NVIDIA\r\n", "")) == "", "r6: unknown vendor: Reflex stays available");
        Check(R6Logic.NoNvidia == "Nur mit NVIDIA-Grafikkarte (ab GeForce GTX 10)", "r6: the exact German reason for Reflex");
        Check(R6Logic.WhyNot(S("vsync"), Sample.Replace("GPUVendor=NVIDIA", "GPUVendor=AMD")) == "", "r6: V-Sync does not care about the vendor");
        foreach (var s in R6Logic.Specs.Where(s => !s.IsGpu))
            Check(R6Logic.WhyNot(s, null) == R6Logic.NoConfig, $"r6: {s.Id} needs the settings file");
        Check(R6Logic.WhyNot(S("rawinput"), Sample.Replace("[INPUT]\r\n", "")) is var w && w.Contains("[INPUT]"), "r6: a missing section is named, nothing is written blind");
        Check(R6Logic.WhyNot(S("gpu"), null) == "", "r6: the GPU item does not need the settings file");
        Check(R6Logic.HasSection("[A]\r\n[B]\r\n", "b") && R6Logic.HasSection(" [ DISPLAY ] \r\n", "DISPLAY") && R6Logic.HasSection("\uFEFF[GENERAL]\r\n", "GENERAL"), "r6: sections are found also without keys, with spaces, after a BOM");
        Check(!R6Logic.HasSection(";[DISPLAY]\r\n", "DISPLAY") && !R6Logic.HasSection(null, "DISPLAY") && !R6Logic.HasSection("DISPLAY=1", "DISPLAY"), "r6: comments and keys are no sections");
        Check(R6Logic.MaybeNvidia(Sample) && R6Logic.MaybeNvidia("") && !R6Logic.MaybeNvidia("[HARDWARE_INFO]\nGPUVendor=AMD\n"), "r6: MaybeNvidia");

        // two GPUs (Optimus laptop): a non-NVIDIA vendor may only mean Siege last ran on the processor graphics
        var intel = Sample.Replace("GPUVendor=NVIDIA", "GPUVendor=INTEL");
        Check(R6Logic.WhyNot(reflex, intel, switchable: true) == R6Logic.NoNvidiaHybrid && R6Logic.WhyNot(reflex, intel) == R6Logic.NoNvidia, "r6: two GPUs: Reflex points to the GPU item instead of \"NVIDIA only\"");
        Check(R6Logic.WhyNot(reflex, Sample, switchable: true) == "" && R6Logic.WhyNot(reflex, Sample.Replace("GPUVendor=NVIDIA\r\n", ""), switchable: true) == "", "r6: two GPUs, NVIDIA or unknown vendor: Reflex available");
        Check(R6Logic.WhyNot(S("vsync"), intel, switchable: true) == "", "r6: two GPUs: only Reflex cares about the vendor");
        Check(R6Logic.NoNvidiaHybrid.Contains("Starke Grafikkarte für Siege") && R6Logic.NoNvidiaHybrid.Contains(S("gpu").Title) && !R6Logic.NoNvidiaHybrid.EndsWith('.'), "r6: the hint names the GPU item by its title, one line without full stop");
        foreach (var why in new[] { R6Logic.NoNvidia, R6Logic.NoNvidiaHybrid, R6Logic.OneGpu, R6Logic.NoExe, R6Logic.NoConfig })
            Check(!why.EndsWith('.') && !why.Contains(" Sie ") && !why.Contains('\n'), $"r6: reason is one du-line without full stop: {why}");
        Check(R6Logic.NoAccess.Contains("Überwachte Ordnerzugriff") && R6Logic.ReadOnly.Contains("Schreibgeschützt") && R6Logic.Locked.Contains("Schließ"), "r6: the write errors say what to do");
    }

    // ------------------------------------------------------------------ the Windows graphics setting
    private static void Gpu(Action<bool, string> Check)
    {
        Check(R6Logic.GpuPlan(true, null, null) == R6Logic.GpuStep.High && R6Logic.GpuPlan(true, "1", "-") == R6Logic.GpuStep.High, "r6: gpu on sets high performance");
        Check(R6Logic.GpuPlan(true, "2", null) == R6Logic.GpuStep.None, "r6: gpu already high: nothing to do");
        Check(R6Logic.GpuPlan(false, null, null) == R6Logic.GpuStep.None && R6Logic.GpuPlan(false, "1", "-") == R6Logic.GpuStep.None, "r6: gpu already off: nothing to do");
        Check(R6Logic.GpuPlan(false, "2", "-") == R6Logic.GpuStep.Restore && R6Logic.GpuPlan(false, "2", "1") == R6Logic.GpuStep.Restore && R6Logic.GpuPlan(false, "2", "0") == R6Logic.GpuStep.Restore, "r6: gpu off after OrbitTweak set it: the old value comes back");
        Check(R6Logic.GpuPlan(false, "2", null) == R6Logic.GpuStep.Default, "r6: gpu off when the user had set it: Windows decides (0), backed up first");
        Check(R6Logic.GpuPlan(false, "2", "2") == R6Logic.GpuStep.Default, "r6: gpu off when it was high before OrbitTweak: Windows decides, the backup keeps 2");
    }

    // ------------------------------------------------------------------ bytes
    private static void Bytes(Action<bool, string> Check)
    {
        var plain = Encoding.ASCII.GetBytes(Sample);
        var (t, bom) = R6Logic.Decode(plain);
        Check(!bom && t == Sample && R6Logic.Encode(t, bom).SequenceEqual(plain), "r6: ASCII without BOM goes through unchanged");

        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(plain).ToArray();
        var (t2, bom2) = R6Logic.Decode(withBom);
        Check(bom2 && t2 == Sample && R6Logic.Encode(t2, bom2).SequenceEqual(withBom), "r6: a BOM is taken off for editing and put back");

        var odd = plain.Concat(new byte[] { (byte)';', 0xE4, 0xFC, 0x80, 0xFF, 13, 10 }).ToArray();
        var (t3, bom3) = R6Logic.Decode(odd);
        Check(R6Logic.Encode(t3, bom3).SequenceEqual(odd), "r6: any other byte (umlauts, invalid UTF-8) survives the round trip");
        var edited = R6Logic.Encode(IniText.Set(t3, "DISPLAY_SETTINGS", "VSync", "0"), bom3);
        Check(!edited.SequenceEqual(odd) && edited.Length == odd.Length && edited.Skip(plain.Length).SequenceEqual(odd.Skip(plain.Length)), "r6: an edit keeps the bytes after it");
        Check(R6Logic.Decode(Array.Empty<byte>()) == ("", false), "r6: an empty file is an empty text");
    }

    // ------------------------------------------------------------------ finding the game
    private static void Finding(Action<bool, string> Check)
    {
        Check(R6Logic.IsGameProcess("RainbowSix") && R6Logic.IsGameProcess("rainbowsix_be") && R6Logic.IsGameProcess("RainbowSix_Vulkan"), "r6: game, BattlEye launcher and old Vulkan exe count as running");
        Check(R6Logic.IsGameProcess("RainbowSixHelper"), "r6: RainbowSixHelper (a byte-identical copy of the game since 2026) counts as running");
        Check(!R6Logic.IsGameProcess("upc") && !R6Logic.IsGameProcess("UbisoftConnect") && !R6Logic.IsGameProcess("RainbowSixExtraction") && !R6Logic.IsGameProcess(null), "r6: other processes do not");

        Check(R6Logic.IsProfileFolder("3f2c9a1e-7b4d-4c8a-9e21-0a5b6c7d8e9f") && R6Logic.IsProfileFolder("3F2C9A1E-7B4D-4C8A-9E21-0A5B6C7D8E9F"), "r6: a Ubisoft profile GUID is a profile folder");
        Check(!R6Logic.IsProfileFolder("Benchmark") && !R6Logic.IsProfileFolder("3f2c9a1e-7b4d-4c8a-9e21-0a5b6c7d8e9f.bak") && !R6Logic.IsProfileFolder("{3f2c9a1e-7b4d-4c8a-9e21-0a5b6c7d8e9f}") && !R6Logic.IsProfileFolder(null), "r6: other folders are not");

        var t0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        Check(R6Logic.Newest(new[] { ("a", t0), ("b", t0.AddHours(3)), ("c", t0.AddHours(1)) }) == "b", "r6: the file written last wins");
        Check(R6Logic.Newest(Array.Empty<(string, DateTime)>()) == null, "r6: no profiles: no file");
        Check(R6Logic.Newest(new[] { ("y", t0), ("x", t0) }) == "x", "r6: same time: always the same pick");

        Check(R6Logic.NormalizeDir("C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Tom Clancy's Rainbow Six Siege/")
              == @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Tom Clancy's Rainbow Six Siege", "r6: the Ubisoft InstallDir becomes a Windows path without trailing slash");
        Check(R6Logic.NormalizeDir(" \"D:\\Games\\R6\\\" ") == @"D:\Games\R6" && R6Logic.NormalizeDir("C:/") == @"C:\", "r6: quotes and a drive root");
        Check(R6Logic.NormalizeDir(null) == null && R6Logic.NormalizeDir("  ") == null && R6Logic.NormalizeDir("/") == null, "r6: empty install dirs are none");

        Check(R6Logic.IsSiegeName("C:/Ubisoft/games/Tom Clancy's Rainbow Six Siege/") && R6Logic.IsSiegeName("Tom Clancy's Rainbow Six® Siege X") && R6Logic.IsSiegeName(@"D:\Epic\RainbowSixSiege"), "r6: Siege by folder or store name, also with ®");
        Check(!R6Logic.IsSiegeName("Tom Clancy's Rainbow Six Siege - Test Server") && !R6Logic.IsSiegeName("Tom Clancy's Rainbow Six Extraction") && !R6Logic.IsSiegeName(null), "r6: not the Test Server, not Extraction");

        const string vdf = "\"libraryfolders\"\r\n{\r\n\t\"0\"\r\n\t{\r\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\r\n\t\t\"label\"\t\t\"\"\r\n"
            + "\t\t\"apps\"\r\n\t\t{\r\n\t\t\t\"228980\"\t\t\"1234\"\r\n\t\t}\r\n\t}\r\n\t\"1\"\r\n\t{\r\n\t\t\"path\"\t\t\"E:\\\\SteamLibrary\"\r\n"
            + "\t\t\"apps\"\r\n\t\t{\r\n\t\t\t\"359550\"\t\t\"98765432100\"\r\n\t\t}\r\n\t}\r\n}\r\n";
        Check(R6Logic.SteamLibraries(vdf).SequenceEqual(new[] { @"C:\Program Files (x86)\Steam", @"E:\SteamLibrary" }), "r6: Steam libraries from libraryfolders.vdf, without the app sizes");
        const string oldVdf = "\"LibraryFolders\"\n{\n\t\"TimeNextStatsReport\"\t\t\"1600000000\"\n\t\"1\"\t\t\"D:\\\\Games\\\\Steam\"\n}\n";
        Check(R6Logic.SteamLibraries(oldVdf).SequenceEqual(new[] { @"D:\Games\Steam" }), "r6: the old vdf form too");
        Check(R6Logic.SteamLibraries(null).Count == 0 && R6Logic.SteamLibraries("garbage").Count == 0, "r6: no vdf: no libraries");
        Check(R6Logic.SteamLibraries(vdf + vdf).Count == 2, "r6: libraries are listed once");

        const string acf = "\"AppState\"\r\n{\r\n\t\"appid\"\t\t\"359550\"\r\n\t\"name\"\t\t\"Tom Clancy's Rainbow Six Siege\"\r\n\t\"installdir\"\t\t\"Tom Clancy's Rainbow Six Siege\"\r\n}\r\n";
        Check(R6Logic.AcfInstallDir(acf) == "Tom Clancy's Rainbow Six Siege", "r6: installdir from the app manifest");
        Check(R6Logic.AcfInstallDir("\"AppState\" { \"appid\" \"359550\" }") == null && R6Logic.AcfInstallDir(null) == null, "r6: no installdir");
        Check(R6Logic.AcfInstallDir("\"installdir\" \"..\\\\..\\\\Windows\"") == null && R6Logic.AcfInstallDir("\"installdir\" \"C:\\\\x\"") == null, "r6: an installdir that leaves steamapps\\common is refused");

        const string epic = "{\"FormatVersion\":0,\"DisplayName\":\"Tom Clancy's Rainbow Six® Siege X\",\"InstallLocation\":\"D:\\\\Epic Games\\\\RainbowSixSiege\",\"LaunchExecutable\":\"RainbowSix.exe\"}";
        Check(R6Logic.EpicInstall(epic) == @"D:\Epic Games\RainbowSixSiege", "r6: Epic manifest of Siege");
        Check(R6Logic.EpicInstall(epic.Replace("Rainbow Six® Siege X", "Fortnite")) == null, "r6: Epic manifests of other games are skipped");
        Check(R6Logic.EpicInstall("{nope") == null && R6Logic.EpicInstall("[]") == null && R6Logic.EpicInstall(null) == null, "r6: broken Epic manifests are skipped");
        Check(R6Logic.EpicInstall("{\"DisplayName\":\"Rainbow Six Siege\"}") == null, "r6: Epic manifest without InstallLocation");
    }

    // ------------------------------------------------------------------ the page's line
    private static void Notes(Action<bool, string> Check)
    {
        Check(R6Logic.Note(false, false, false, 0, false).Contains("nicht gefunden"), "r6: note: not found");
        Check(R6Logic.Note(true, false, false, 0, false) == R6Logic.NoConfig, "r6: note: installed, never started");
        Check(R6Logic.NoConfig == "Starte Rainbow Six Siege einmal, dann legt es seine Einstellungen an", "r6: the exact German line for a missing file");
        Check(R6Logic.Note(true, true, true, 1, false).Contains("läuft") && R6Logic.Note(true, true, true, 1, false).Contains("Spieleinstellungen"), "r6: note: running (only the game's own settings wait for it)");
        Check(R6Logic.Note(true, true, false, 2, false).StartsWith("2 Ubisoft-Profile") && R6Logic.Note(true, true, false, 2, false).Contains("zuletzt gespielte"), "r6: note: several profiles, only the last played one changes");
        Check(R6Logic.Note(true, true, false, 2, true).Contains("zuerst geändert"), "r6: note: OrbitTweak stays with the profile it changed");
        Check(R6Logic.Note(true, true, false, 1, false).Contains("nächsten Start"), "r6: note: one profile, all fine");
        Check(R6Logic.Note(false, true, false, 1, false).Contains("nächsten Start"), "r6: note: settings found without the install folder still work");
        Check(R6Logic.RunningError == "Schließ Rainbow Six Siege zuerst, sonst überschreibt das Spiel die Einstellung beim Beenden.", "r6: the exact German error while the game runs");
        foreach (var n in new[] { R6Logic.Note(false, false, false, 0, false), R6Logic.Note(true, true, true, 1, false), R6Logic.Note(true, true, false, 3, false), R6Logic.Note(true, true, false, 2, true), R6Logic.Note(true, true, false, 1, false) })
            Check(!n.Contains(" Sie ") && !n.EndsWith('.') && !n.Contains('\n'), $"r6: note is one du-line: {n}");
    }
}
