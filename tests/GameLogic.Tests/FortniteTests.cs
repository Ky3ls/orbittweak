using System.Globalization;
using System.Text;
using System.Text.Json;
using OrbitTweak.Core;
using static OrbitTweak.Core.FortniteLogic;

/// <summary>Checks of FortniteLogic. Called from Program.cs.</summary>
internal static class FortniteTests
{
    /// <summary>A GameUserSettings.ini the way UE 5.7 writes it (CRLF, metadata line, the sections in use).</summary>
    private const string Gus =
        ";METADATA=(Diff=true, UseCommands=true)\r\n"
        + "[/Script/FortniteGame.FortGameUserSettings]\r\n"
        + "bUseVSync=True\r\n"
        + "ResolutionSizeX=1920\r\n"
        + "FrameRateLimit=60.000000\r\n"
        + "FrontendFrameRateLimit=60.000000\r\n"
        + "bShowFPS=False\r\n"
        + "LatencyTweak2=0\r\n"
        + "bLatencyTweak1=False\r\n"
        + "+RecentlyUsedItems=Pickaxe\r\n"
        + "\r\n"
        + "[ScalabilityGroups]\r\n"
        + "sg.ResolutionQuality=100.000000\r\n"
        + "sg.ShadowQuality=3\r\n"
        + "sg.GlobalIlluminationQuality=3\r\n"
        + "\r\n"
        + "[D3DRHIPreference]\r\n"
        + "PreferredRHI=dx12\r\n"
        + "PreferredFeatureLevel=sm6\r\n"
        + "\r\n"
        + "[PerformanceMode]\r\n"
        + "MeshQuality=0\r\n";

    private static JsonElement J(object? v) => JsonSerializer.SerializeToElement(v);

    private static bool Throws(Action a, string? msg = null)
    {
        try { a(); return false; }
        catch (InvalidOperationException e) { return msg == null || e.Message == msg; }
    }

    /// <summary>Set then revert through the pure plan, with an in-memory backup store like the Windows part uses.</summary>
    private static string SetVia(string ini, string id, object value, Dictionary<string, string> store)
    {
        var writes = Writes(id, J(value));
        var (next, made) = PlanSet(ini, writes, store.ContainsKey);
        foreach (var (k, v) in made) store[k] = v;
        foreach (var k in Settled(writes, k => store.TryGetValue(k, out var b) ? b : null)) store.Remove(k);
        return next;
    }

    private static string RevertVia(string ini, Dictionary<string, string> store)
    {
        var keys = Defs.Where(d => IsIni(d.Id)).SelectMany(d => KeysOf(d.Id)).Where(k => store.ContainsKey(BackupKey(k.Key))).ToList();
        var next = PlanRevert(ini, keys.Select(k => (k.Section, k.Key, (string?)store[BackupKey(k.Key)])));
        foreach (var k in keys) store.Remove(BackupKey(k.Key));
        return next;
    }

    /// <summary>A repo file, looked for from the working folder and from the test's output folder upwards.</summary>
    private static string? FindUp(string rel)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, rel))) return Path.Combine(d.FullName, rel);
        return null;
    }

    public static void Run(Action<bool, string> Check)
    {
        // ---------- the items ----------
        Check(string.Join(",", Defs.Select(d => d.Id)) == "render,reflex,vsync,fpslimit,shadows,showfps,gpu", "fortnite: seven items in page order");
        Check(Defs.Select(d => d.Id).Distinct().Count() == Defs.Length, "fortnite: item ids are unique");
        Check(Defs.All(d => d.Kind is "switch" or "choice"), "fortnite: only switches and choices");
        Check(Defs.All(d => d.Effect is "fps" or "latency" or "comfort" or "stability"), "fortnite: every effect is one the page knows");
        Check(Defs.All(d => d.Title.Length > 0 && d.Sum.Length is > 10 and <= 80 && !d.Sum.Contains('\n') && d.Sum.EndsWith('.')), "fortnite: Sum is one short line");
        Check(Defs.All(d => d.Desc.Length > d.Sum.Length && d.Desc.EndsWith('.') && !d.Desc.Contains('\n')), "fortnite: Desc is the longer full text");
        Check(Defs.All(d => d.Source.Length > 0), "fortnite: every item names its source");
        var allText = string.Join(" ", Defs.SelectMany(d => new[] { d.Title, d.Sum, d.Desc, d.Source }).Concat(new[] { AntiCheat, MsgRunning, MsgNoConfig, MsgOneGpu }));
        Check(!allText.Contains(" Sie ") && !allText.Contains(" Ihre ") && !allText.Contains(" Ihr ") && !allText.Contains("Bitte"), "fortnite: German in du, no Sie");
        Check(!allText.Contains(" – ") && !allText.Contains("!"), "fortnite: plain sentences, no dashes or exclamation marks");
        Check(Defs.Where(d => d.Rec).Select(d => d.Id).SequenceEqual(new[] { "reflex", "vsync", "gpu" }), "fortnite: Empfohlene anwenden = Reflex, V-Sync, GPU");
        Check(Defs.All(d => d.NeedsClosed == IsIni(d.Id)), "fortnite: every ini item needs the game closed, the GPU preference does not");
        Check(Find("render")?.Title == "Rendermodus" && Find("nope") == null && Find(null) == null, "fortnite: Find");
        Check(Defs.Where(d => IsIni(d.Id)).All(d => KeysOf(d.Id).Count > 0) && KeysOf("gpu").Count == 0 && KeysOf("x").Count == 0, "fortnite: keys of every ini item, none for gpu");
        Check(KeysOf("render").Count == 2 && KeysOf("render")[0].Key == "PreferredRHI" && KeysOf("render")[1].Key == "PreferredFeatureLevel", "fortnite: render writes RHI and feature level");
        Check(BackupKey("sg.ShadowQuality") == "og.fortnite.sg.ShadowQuality" && GpuBackup == "og.fortnite.gpu", "fortnite: backup keys start with og.fortnite.");
        var allKeys = Defs.SelectMany(d => KeysOf(d.Id)).Select(k => k.Key).ToList();
        Check(allKeys.Distinct().Count() == allKeys.Count, "fortnite: no two items share a key (each backup belongs to one item)");
        Check(!allKeys.Contains("FrontendFrameRateLimit") && !allKeys.Contains("bLatencyTweak1") && !allKeys.Contains("MeshQuality") && !allKeys.Any(k => k.Contains("Nanite") || k.Contains("GlobalIllumination")),
            "fortnite: lobby limit, latency markers, mesh quality, Lumen/Nanite stay untouched");

        // ---------- reading ----------
        Check(Read("render", Gus) as string == "dx12", "render: sm6 is DirectX 12");
        Check(Read("reflex", Gus) as string == "0", "reflex: LatencyTweak2=0");
        Check(Read("vsync", Gus) is true, "vsync: True");
        Check(Read("fpslimit", Gus) as string == "60", "fpslimit: 60.000000 → 60");
        Check(Read("shadows", Gus) as string == "3", "shadows: 3");
        Check(Read("showfps", Gus) is false, "showfps: False");
        Check(Read("gpu", Gus) == null && Read("nope", Gus) == null, "no ini value for gpu or unknown items");
        Check(Defs.Where(d => IsIni(d.Id)).All(d => Read(d.Id, "") == null && Read(d.Id, null) == null), "a missing key reads as null (Fortnite-Standard)");

        string Rhi(string body) => "[D3DRHIPreference]\r\n" + body;
        Check(Render(Rhi("PreferredRHI=dx12\r\nPreferredFeatureLevel=es31\r\n")) == "perf", "render: dx12 + es31 is Leistung");
        Check(Render(Rhi("PreferredRHI=dx12\r\nPreferredFeatureLevel=sm5\r\n")) == "dx12", "render: sm5 is DirectX 12");
        Check(Render(Rhi("PreferredRHI=dx11\r\nPreferredFeatureLevel=es31\r\n")) == "perf", "render: old dx11 + es31 Performance is Leistung");
        Check(Render(Rhi("PreferredRHI=dx11\r\nPreferredFeatureLevel=sm5\r\n")) == "perf", "render: removed DX11 mode reads as Leistung (Epic moves it there)");
        Check(Render(Rhi("PreferredRHI=DX12\r\nPreferredFeatureLevel=SM6\r\n")) == "dx12", "render: values without case");
        Check(Render(Rhi("PreferredRHI=dx12\r\n")) == null, "render: RHI without feature level is not known");
        Check(Render(Rhi("PreferredRHI=dx12\r\nPreferredFeatureLevel=sm7\r\n")) == null, "render: unknown level is not known");
        Check(Render(Rhi("PreferredRHI=vulkan\r\nPreferredFeatureLevel=sm6\r\n")) == null, "render: vulkan is no Fortnite mode");
        Check(Render(Rhi("bPreferFeatureLevelES31=True\r\n")) == "perf", "render: legacy bPreferFeatureLevelES31 without PreferredFeatureLevel");
        Check(Render(Rhi("PreferredFeatureLevel=sm6\r\nbPreferFeatureLevelES31=True\r\n")) == "dx12", "render: PreferredFeatureLevel wins over the legacy flag (as in UE)");
        Check(Render("[Other]\r\nPreferredFeatureLevel=es31\r\n") == null, "render: only [D3DRHIPreference] counts");
        Check(ShadowsAvailable(Gus) && !ShadowsAvailable(Rhi("PreferredRHI=dx12\r\nPreferredFeatureLevel=es31\r\n")) && ShadowsAvailable(""), "shadows: not in Leistung, else available");

        Check(Bool("True") == true && Bool("false") == false && Bool(" TRUE ") == true && Bool("1") == true && Bool("0") == false && Bool("maybe") == null && Bool(null) == null, "bool words like UE");
        Check(Int("2") == "2" && Int(" 1 ") == "1" && Int("-1") == "-1" && Int("2.0") == null && Int("x") == null && Int(null) == null, "whole numbers");
        Check(Fps("144.000000") == "144" && Fps("0.000000") == "0" && Fps("59.940000") == "60" && Fps("165") == "165" && Fps("1e3") == "1000", "fps: float to whole number");
        Check(Fps("abc") == null && Fps("") == null && Fps(null) == null && Fps("NaN") == null && Fps("1e30") == null, "fps: no number reads as null");

        // ---------- culture: Fortnite writes and reads with a dot, whatever Windows is set to ----------
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Check(FpsText(144) == "144.000000" && FpsText(0) == "0.000000", "fps: written with a dot under a German Windows");
            Check(Fps("144.000000") == "144" && Read("fpslimit", Gus) as string == "60", "fps: read with a dot under a German Windows");
            Check(Writes("fpslimit", J("141"))[0].Value == "141.000000", "fps: write under a German Windows");
        }
        finally { CultureInfo.CurrentCulture = old; }

        // ---------- choices ----------
        string Opts(string id, object? cur, int hz) => string.Join("|", Options(id, cur, hz)!.Select(o => o.V + "=" + o.T));
        Check(Opts("render", "dx12", 0) == "perf=Leistung|dx12=DirectX 12", "render: Leistung and DirectX 12, no DX11");
        Check(Opts("reflex", "1", 0) == "0=Aus|1=An|2=An + Boost", "reflex: Aus, An, An + Boost");
        Check(Opts("reflex", "5", 0) == "0=Aus|1=An|2=An + Boost|5=Wert 5", "reflex: an unknown current value is shown too");
        Check(Opts("shadows", "2", 0) == "0=Aus|1=Mittel|2=Hoch|3=Episch", "shadows: Aus to Episch");
        Check(Opts("shadows", "4", 0).EndsWith("|4=Stufe 4"), "shadows: a level outside 0-3 is shown too");
        Check(Opts("fpslimit", "60", 0) == "0=Unbegrenzt|30=30 FPS|60=60 FPS|120=120 FPS|144=144 FPS|165=165 FPS|240=240 FPS|360=360 FPS", "fps: the steps up to 360");
        Check(Opts("fpslimit", "60", 165) == "0=Unbegrenzt|30=30 FPS|60=60 FPS|120=120 FPS|144=144 FPS|162=162 FPS (G-Sync/FreeSync)|165=165 FPS (Monitor)|240=240 FPS|360=360 FPS",
            "fps: the monitor's rate and 3 below it, in order");
        Check(Opts("fpslimit", null, 75) == "0=Unbegrenzt|30=30 FPS|60=60 FPS|72=72 FPS (G-Sync/FreeSync)|75=75 FPS (Monitor)|120=120 FPS|144=144 FPS|165=165 FPS|240=240 FPS|360=360 FPS",
            "fps: a 75 Hz monitor");
        Check(Opts("fpslimit", "100", 144).Contains("|100=100 FPS|120=") && Opts("fpslimit", "100", 144).Contains("|141=141 FPS (G-Sync/FreeSync)|144=144 FPS (Monitor)|"), "fps: current value added in order");
        Check(!Opts("fpslimit", "60", 500).Contains("500") && !Opts("fpslimit", "60", 500).Contains("497"), "fps: a 500 Hz monitor adds nothing above 360");
        Check(Opts("fpslimit", "60", 360).Contains("|357=357 FPS (G-Sync/FreeSync)|360=360 FPS (Monitor)"), "fps: 360 Hz");
        Check(!Opts("fpslimit", "60", 50).Contains("47") && Opts("fpslimit", "60", 50).Contains("50=50 FPS (Monitor)"), "fps: no G-Sync value below 60 Hz");
        Check(Opts("fpslimit", "1000", 0).EndsWith("|1000=1000 FPS") && Opts("fpslimit", "-5", 0).EndsWith("|-5=-5 FPS"), "fps: odd current values are shown, at the end");
        Check(Options("vsync", true, 144) == null && Options("gpu", true, 144) == null, "switches have no options");
        var o165 = Options("fpslimit", "60", 165)!;
        Check(o165.Select(o => o.V).Distinct().Count() == o165.Count, "fps: no value twice");

        Check(RecValue("reflex", "0") as string == "1" && RecValue("reflex", null) as string == "1" && RecValue("reflex", "2") as string == "2", "reflex: recommended An, Boost stays Boost");
        Check(RecValue("vsync", true) is false && RecValue("gpu", false) is true && RecValue("render", "dx12") == null && RecValue("fpslimit", "60") == null, "recommended values");

        // ---------- writing: values exactly as Fortnite writes them ----------
        string W(string id, object v) => string.Join("|", Writes(id, J(v)).Select(w => $"{w.Section}:{w.Key}={w.Value}"));
        Check(W("render", "perf") == "D3DRHIPreference:PreferredRHI=dx12|D3DRHIPreference:PreferredFeatureLevel=es31", "render: Leistung = dx12 + es31");
        Check(W("render", "dx12") == "D3DRHIPreference:PreferredRHI=dx12|D3DRHIPreference:PreferredFeatureLevel=sm6", "render: DirectX 12 = dx12 + sm6");
        Check(Throws(() => Writes("render", J("dx11")), MsgBadValue) && Throws(() => Writes("render", J(12)), MsgBadValue) && Throws(() => Writes("render", J(true)), MsgBadValue), "render: no DX11, nothing else");
        Check(W("reflex", "2") == "/Script/FortniteGame.FortGameUserSettings:LatencyTweak2=2" && W("reflex", 1) == "/Script/FortniteGame.FortGameUserSettings:LatencyTweak2=1", "reflex: text or number");
        Check(Throws(() => Writes("reflex", J("3")), MsgBadValue) && Throws(() => Writes("reflex", J("-1"))) && Throws(() => Writes("reflex", J(1.5))) && Throws(() => Writes("reflex", J(null))), "reflex: only 0, 1, 2");
        Check(W("vsync", false) == "/Script/FortniteGame.FortGameUserSettings:bUseVSync=False" && W("vsync", true) == "/Script/FortniteGame.FortGameUserSettings:bUseVSync=True", "vsync: True/False");
        Check(W("vsync", "false") == "/Script/FortniteGame.FortGameUserSettings:bUseVSync=False", "vsync: also as text");
        Check(Throws(() => Writes("vsync", J(1)), MsgBadValue) && Throws(() => Writes("vsync", J("aus")), MsgBadValue), "vsync: only true/false");
        Check(W("showfps", true) == "/Script/FortniteGame.FortGameUserSettings:bShowFPS=True", "showfps: True");
        Check(W("fpslimit", "144") == "/Script/FortniteGame.FortGameUserSettings:FrameRateLimit=144.000000" && W("fpslimit", 0) == "/Script/FortniteGame.FortGameUserSettings:FrameRateLimit=0.000000", "fpslimit: six decimals, 0 = Unbegrenzt");
        Check(W("fpslimit", 30) != "" && W("fpslimit", "360") != "" && W("fpslimit", "162") != "", "fpslimit: 30, 360 and the G-Sync value are allowed");
        Check(new object?[] { 361, 29, 1, -1, 144.5, "abc", "", null, true, 1e9 }.All(v => Throws(() => Writes("fpslimit", J(v)), MsgBadFps)), "fpslimit: refuses everything outside 0 or 30-360");
        Check(W("shadows", "0") == "ScalabilityGroups:sg.ShadowQuality=0" && W("shadows", 3) == "ScalabilityGroups:sg.ShadowQuality=3", "shadows: 0-3");
        Check(Throws(() => Writes("shadows", J("4")), MsgBadValue), "shadows: no level 4");
        Check(Throws(() => Writes("gpu", J(true)), MsgUnknown) && Throws(() => Writes("nope", J(1)), MsgUnknown), "gpu and unknown ids are no ini writes");

        // ---------- plan: backups first, every other byte stays ----------
        var none = new Func<string, bool>(_ => false);
        var (t1, b1) = PlanSet(Gus, Writes("fpslimit", J(144)), none);
        Check(t1 == Gus.Replace("\r\nFrameRateLimit=60.000000", "\r\nFrameRateLimit=144.000000"), "set: only the value changes, CRLF and the rest stay");
        Check(b1.Count == 1 && b1[0] == ("og.fortnite.FrameRateLimit", "60.000000"), "set: backup of the raw value before");
        Check(t1.Contains("FrontendFrameRateLimit=60.000000"), "set: the lobby limit stays");
        var (t2, b2) = PlanSet(Gus, Writes("fpslimit", J(144)), k => k == "og.fortnite.FrameRateLimit");
        Check(t2 == t1 && b2.Count == 0, "set: an existing backup is kept (the first one is the real one)");
        var (t3, b3) = PlanSet(Gus, Writes("fpslimit", J(60)), none);
        Check(t3 == Gus && b3.Count == 0, "set: the same value changes nothing and needs no backup");
        var (t4, b4) = PlanSet(Gus, Writes("render", J("perf")), none);
        Check(t4 == Gus.Replace("PreferredFeatureLevel=sm6", "PreferredFeatureLevel=es31") && b4.Count == 1 && b4[0] == ("og.fortnite.PreferredFeatureLevel", "sm6"),
            "set: render to Leistung touches only the feature level when the RHI is dx12 already");
        Check(!t4.Contains("MeshQuality=1") && t4.Contains("[PerformanceMode]\r\nMeshQuality=0"), "set: [PerformanceMode] stays");

        const string noRhi = "[/Script/FortniteGame.FortGameUserSettings]\r\nbShowFPS=False\r\n";
        var (t5, b5) = PlanSet(noRhi, Writes("render", J("dx12")), none);
        Check(t5 == noRhi + "[D3DRHIPreference]\r\nPreferredRHI=dx12\r\nPreferredFeatureLevel=sm6\r\n", "set: a missing section goes to the end, with CRLF");
        Check(b5.Count == 2 && b5.All(b => b.Before == NotThere), "set: missing keys are backed up as '-'");
        var (t6, _) = PlanSet(Gus.Replace("LatencyTweak2=0\r\n", ""), Writes("reflex", J("1")), none);
        Check(IniText.Get(t6, SecUser, "LatencyTweak2") == "1" && t6.Contains("+RecentlyUsedItems=Pickaxe\r\nLatencyTweak2=1\r\n\r\n[ScalabilityGroups]"), "set: a missing key goes to the end of its section");

        Check(Settled(Writes("fpslimit", J(60)), k => k == "og.fortnite.FrameRateLimit" ? "60.000000" : null).SequenceEqual(new[] { "og.fortnite.FrameRateLimit" }), "settled: back at the value before, the backup goes");
        Check(Settled(Writes("fpslimit", J(144)), k => "60.000000").Count == 0, "settled: another value keeps the backup");
        Check(Settled(Writes("render", J("dx12")), k => k.EndsWith("PreferredRHI") ? "dx12" : "es31").SequenceEqual(new[] { "og.fortnite.PreferredRHI" }), "settled: per key");
        Check(Settled(Writes("fpslimit", J(60)), k => NotThere).Count == 0 && Settled(Writes("fpslimit", J(60)), k => null).Count == 0, "settled: 'was not there' never settles by a write");

        // ---------- set and revert: exactly what was there ----------
        var store = new Dictionary<string, string>();
        var s1 = SetVia(Gus, "fpslimit", 144, store);
        s1 = SetVia(s1, "fpslimit", 240, store);
        Check(store["og.fortnite.FrameRateLimit"] == "60.000000", "cycle: two changes keep the first backup");
        s1 = SetVia(s1, "render", "perf", store);
        s1 = SetVia(s1, "vsync", false, store);
        s1 = SetVia(s1, "reflex", "1", store);
        s1 = SetVia(s1, "shadows", "0", store);
        s1 = SetVia(s1, "showfps", true, store);
        Check(Read("fpslimit", s1) as string == "240" && Read("render", s1) as string == "perf" && Read("vsync", s1) is false && Read("reflex", s1) as string == "1"
              && Read("shadows", s1) as string == "0" && Read("showfps", s1) is true, "cycle: every value reads back");
        Check(store.Count == 6 && !store.ContainsKey("og.fortnite.PreferredRHI"), "cycle: six backups, none for the RHI that did not change");
        Check(RevertVia(s1, store) == Gus && store.Count == 0, "cycle: revert gives back the file byte for byte");

        var store2 = new Dictionary<string, string>();
        var s2 = SetVia(Gus, "vsync", false, store2);
        s2 = SetVia(s2, "vsync", true, store2);
        Check(s2 == Gus && store2.Count == 0, "cycle: switched back to the value before, nothing of ours is left");

        var store3 = new Dictionary<string, string>();
        var missing = Gus.Replace("bShowFPS=False\r\n", "").Replace("LatencyTweak2=0\r\n", "");
        var s3 = SetVia(missing, "showfps", true, store3);
        s3 = SetVia(s3, "reflex", "2", store3);
        Check(store3["og.fortnite.bShowFPS"] == NotThere && store3["og.fortnite.LatencyTweak2"] == NotThere, "cycle: keys that were not there");
        Check(RevertVia(s3, store3) == missing, "cycle: revert removes keys that were not there");

        var store4 = new Dictionary<string, string>();
        var s4 = SetVia(Gus, "fpslimit", 144, store4);
        s4 = s4.Replace("FrameRateLimit=144.000000", "FrameRateLimit=90.000000");   // Fortnite or the player changed it since
        Check(RevertVia(s4, store4) == Gus, "cycle: revert brings back the value from before, not what is there now");

        Check(PlanRevert(Gus, new[] { (SecUser, "bShowFPS", (string?)null) }) == Gus.Replace("bShowFPS=False\r\n", ""), "revert: null backup removes the key");
        Check(PlanRevert(Gus, new[] { (SecUser, "bShowFPS", (string?)"True") }) == Gus.Replace("bShowFPS=False", "bShowFPS=True"), "revert: a raw value goes back as it was");
        Check(PlanRevert(Gus, Array.Empty<(string, string, string?)>()) == Gus, "revert: nothing backed up, nothing changes");

        // ---------- the file, byte for byte ----------
        var utf8 = Encoding.UTF8.GetBytes(Gus.Replace("ResolutionSizeX=1920", "LastUser=Jürgen☃"));
        var (dt, de) = Decode(utf8);
        Check(de == TextEnc.Utf8 && Encode(dt, de).SequenceEqual(utf8), "utf-8 without BOM: bytes round trip");
        var (dt2, _) = PlanSet(dt, Writes("showfps", J(true)), none);
        Check(Encode(dt2, de).SequenceEqual(Encoding.UTF8.GetBytes(Gus.Replace("ResolutionSizeX=1920", "LastUser=Jürgen☃").Replace("bShowFPS=False", "bShowFPS=True"))),
            "utf-8: a change keeps the non-ASCII bytes elsewhere");
        var bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Gus)).ToArray();
        var (bt, be) = Decode(bom);
        Check(be == TextEnc.Utf8Bom && bt == Gus && Encode(bt, be).SequenceEqual(bom), "utf-8 with BOM: the BOM stays, the text has none");
        Check(Read("vsync", bt) is true && IniText.Get(bt, SecUser, "bUseVSync") == "True", "utf-8 with BOM: keys read");
        var u16 = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(Gus)).ToArray();
        var (ut, ue) = Decode(u16);
        Check(ue == TextEnc.Utf16LeBom && ut == Gus && Encode(ut, ue).SequenceEqual(u16), "utf-16 LE with BOM round trip");
        var u16n = Encoding.Unicode.GetBytes(Gus);
        var (un, une) = Decode(u16n);
        Check(une == TextEnc.Utf16Le && un == Gus && Encode(un, une).SequenceEqual(u16n), "utf-16 LE without BOM round trip");
        var u16b = new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes(Gus)).ToArray();
        var (ub, ube) = Decode(u16b);
        Check(ube == TextEnc.Utf16BeBom && ub == Gus && Encode(ub, ube).SequenceEqual(u16b), "utf-16 BE with BOM round trip");
        Check(Decode(Array.Empty<byte>()) == ("", TextEnc.Utf8) && Decode(new byte[] { 0x41 }).Text == "A", "empty and tiny files");

        // ---------- finding the install ----------
        const string item = "{\"FormatVersion\":0,\"bIsIncompleteInstall\":false,\"LaunchExecutable\":\"FortniteGame/Binaries/Win64/FortniteBootstrapper.exe\","
            + "\"InstallLocation\":\"D:\\\\Games\\\\Epic Games\\\\Fortnite\",\"CatalogNamespace\":\"fn\",\"CatalogItemId\":\"4fe75bbc5a674f4f9b356b5c90567da5\","
            + "\"AppName\":\"Fortnite\",\"MainGameAppName\":\"Fortnite\",}";
        Check(ManifestRoot(item) == @"D:\Games\Epic Games\Fortnite", "manifest: the install folder (trailing comma allowed)");
        Check(ManifestRoot(item.Replace("\"bIsIncompleteInstall\":false", "\"bIsIncompleteInstall\":true")) == null, "manifest: an unfinished install does not count");
        Check(ManifestRoot(item.Replace("\"AppName\":\"Fortnite\"", "\"AppName\":\"FortniteAddon01\"")) == null, "manifest: add-ons do not count");
        Check(ManifestRoot(item.Replace("D:\\\\Games\\\\Epic Games\\\\Fortnite", "C:/Program Files/Epic Games/Fortnite/")) == @"C:\Program Files\Epic Games\Fortnite", "manifest: slashes and the trailing one");
        Check(ManifestRoot(item.Replace("\"InstallLocation\":\"D:\\\\Games\\\\Epic Games\\\\Fortnite\",", "")) == null, "manifest: no folder");
        Check(ManifestRoot("{broken") == null && ManifestRoot("") == null && ManifestRoot(null) == null && ManifestRoot("[1,2]") == null, "manifest: broken files are skipped");

        const string dat = "{\"InstallationList\":[{\"InstallLocation\":\"C:\\\\Epic\\\\UE_5.4\",\"NamespaceId\":\"ue\",\"AppName\":\"UE_5.4\"},"
            + "{\"InstallLocation\":\"E:\\\\Fortnite\",\"NamespaceId\":\"fn\",\"ItemId\":\"4fe75bbc5a674f4f9b356b5c90567da5\",\"ArtifactId\":\"Fortnite\",\"AppVersion\":\"++Fortnite+Release-38.10\",\"AppName\":\"Fortnite\"}]}";
        Check(LauncherInstalledRoots(dat).SequenceEqual(new[] { @"E:\Fortnite" }), "LauncherInstalled.dat: only Fortnite");
        Check(LauncherInstalledRoots("{}").Count == 0 && LauncherInstalledRoots("nope").Count == 0 && LauncherInstalledRoots(null).Count == 0, "LauncherInstalled.dat: broken or empty");

        var log = new[]
        {
            "Log file open, 11/18/26 20:00:00",
            "LogWindows: Failed to load 'aqProf.dll' (GetLastError=126)",
            "LogInit: Display: Running engine for game: FortniteGame",
            "LogInit: Base Directory: C:/Program Files/Epic Games/Fortnite/FortniteGame/Binaries/Win64/",
            "LogInit: Base Directory: X:/Other/FortniteGame/Binaries/Win64/",
        };
        Check(LogRoot(log) == @"C:\Program Files\Epic Games\Fortnite", "log: the first Base Directory line, without FortniteGame/Binaries/Win64");
        Check(LogRoot(new[] { "[2026.11.18-20.00.00:000][  0]LogInit: Base Directory: D:/FN/FortniteGame/Binaries/Win64/" }) == @"D:\FN", "log: with the time prefix");
        Check(LogRoot(new[] { "LogInit: Base Directory: C:/Games/Other/Binaries/Win64/" }) == null, "log: another game's layout does not count");
        Check(LogRoot(new[] { "LogInit: Display: nothing here" }) == null && LogRoot(Array.Empty<string>()) == null, "log: no line, no folder");

        Check(ExePath(@"C:\Program Files\Epic Games\Fortnite") == @"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe", "exe: the Shipping exe renders");
        Check(ExePath(@"D:\FN\") == @"D:\FN\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe" && ExePath("D:/FN") == ExePath(@"D:\FN"), "exe: any slash style");
        Check(NormalizeRoot("\"C:\\Fortnite\\\" ") == @"C:\Fortnite" && NormalizeRoot(@"C:\") == @"C:\", "normalize: quotes, trailing slash, drive root");
        Check(ProcessNames.Contains("FortniteClient-Win64-Shipping") && ProcessNames.Contains("FortniteClient-Win64-Shipping_EAC_EOS") && ProcessNames.Contains("FortniteClient-Win64-Shipping_BE")
              && ProcessNames.Contains("FortniteLauncher") && ProcessNames.Contains("FortniteBootstrapper") && !ProcessNames.Contains("EpicGamesLauncher") && ProcessNames.All(p => !p.EndsWith(".exe")),
            "processes: the whole launch chain by name, not the Epic launcher");
        Check(IsNvidia(0x10DE) && !IsNvidia(0x1002) && !IsNvidia(0x8086), "NVIDIA vendor id");

        // ---------- the browser mock (Web/og-fortnite.js) shows the same texts as the host ----------
        var mock = FindUp(Path.Combine("Web", "og-fortnite.js"));
        if (mock != null)
        {
            var js = File.ReadAllText(mock);
            string Q(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
            Check(Defs.All(d => js.Contains("id: " + Q(d.Id)) && js.Contains(Q(d.Title)) && js.Contains(Q(d.Sum)) && js.Contains(Q(d.Desc)) && js.Contains(Q(d.Source))),
                "mock: every title, Sum, Desc and source as in FortniteLogic");
            Check(js.Contains(Q(AntiCheat)) && js.Contains(Q(MsgRunning)) && js.Contains(Q(MsgPerfShadows)) && js.Contains(Q(MsgOneGpu)), "mock: anti-cheat note and messages as in FortniteLogic");
        }
        else Console.WriteLine("note  Web/og-fortnite.js not found from here, mock text check skipped");

        // ---------- the page line ----------
        Check(Note(true, true, true).StartsWith("Fortnite läuft gerade"), "note: running first");
        Check(Note(true, false, false) == MsgNoConfig, "note: no settings file yet");
        Check(Note(false, false, false).Contains("nicht gefunden"), "note: nothing found");
        Check(Note(false, true, false).Contains("Starke Grafikkarte erzwingen"), "note: no install, the ini items still work");
        Check(Note(true, true, false).Contains("nächsten Start"), "note: all found");
    }
}
