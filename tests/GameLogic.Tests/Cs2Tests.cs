using OrbitTweak.Core;

/// <summary>Checks of Cs2Logic. Called from Program.cs.</summary>
internal static class Cs2Tests
{
    // as CS2 writes them (TAB"key"TAB TAB"value"), video file in LF, convars in CRLF
    private const string Video =
        "\"video.cfg\"\n{\n" +
        "\t\"Version\"\t\t\"16\"\n" +
        "\t\"VendorID\"\t\t\"4318\"\n" +
        "\t\"DeviceID\"\t\t\"11266\"\n" +
        "\t\"setting.fullscreen\"\t\t\"0\"\n" +
        "\t\"setting.coop_fullscreen\"\t\t\"1\"\n" +
        "\t\"setting.mat_vsync\"\t\t\"0\"\n" +
        "\t\"setting.r_low_latency\"\t\t\"1\"\n" +
        "\t\"setting.aspectratiomode\"\t\t\"1\"\n" +
        "}\n";

    private const string Conv =
        "\"config\"\r\n{\r\n\t\"convars\"\r\n\t{\r\n" +
        "\t\t\"cl_hud_telemetry_frametime_show\"\t\t\"1\"\r\n" +
        "\t\t\"cl_net_buffer_ticks\"\t\t\"0\"\r\n" +
        "\t\t\"fps_max\"\t\t\"400.000000\"\r\n" +
        "\t\t\"fps_max_ui$2\"\t\t\"120\"\r\n" +
        "\t\t\"r_player_visibility_mode\"\t\t\"1\"\r\n" +
        "\t\t\"snd_deathcamera_volume$4\"\t\t\"0.1\"\r\n" +
        "\t}\r\n}\r\n";

    private const string Libs =
        "\"libraryfolders\"\n{\n" +
        "\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n" +
        "\t\t\"apps\"\n\t\t{\n\t\t\t\"228980\"\t\t\"123\"\n\t\t}\n\t}\n" +
        "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"M:\\\\Steam\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"730\"\t\t\"43520184820\"\n\t\t}\n\t}\n" +
        "\t\"2\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Games\"\n\t}\n" +
        "}\n";

    private const string Users =
        "\"users\"\n{\n" +
        "\t\"76561198000000001\"\n\t{\n\t\t\"AccountName\"\t\t\"a\"\n\t\t\"PersonaName\"\t\t\"Kai\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t\t\"Timestamp\"\t\t\"1700000000\"\n\t}\n" +
        "\t\"76561198000000002\"\n\t{\n\t\t\"PersonaName\"\t\t\"Smurf \\\"2\\\"\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t\t\"Timestamp\"\t\t\"1600000000\"\n\t}\n" +
        "}\n";

    private static readonly string[] V = Cs2Logic.VideoBlock, C = Cs2Logic.ConvarBlock;

    public static void Run(Action<bool, string> Check)
    {
        KeyValues(Check);
        Editing(Check);
        Versions(Check);
        Backups(Check);
        Bytes(Check);
        Values(Check);
        Steam(Check);
        Page(Check);
    }

    private static void KeyValues(Action<bool, string> Check)
    {
        Check(Cs2Logic.Get(Video, V, "setting.r_low_latency", false) == "1", "cs2: read a video value");
        Check(Cs2Logic.Get(Video, new[] { "VIDEO.CFG" }, "vendorid", false) == "4318", "cs2: blocks and keys without case");
        Check(Cs2Logic.Get(Video, V, "setting.nope", false) == null, "cs2: a missing key is null");
        Check(Cs2Logic.Get(Video, C, "fps_max", true) == null, "cs2: a missing block is null");
        Check(Cs2Logic.Get(null, V, "x", false) == null && Cs2Logic.Get("", V, "x", false) == null, "cs2: no text, no value");
        Check(Cs2Logic.Get(Conv, C, "fps_max", true) == "400.000000", "cs2: read a convar in the nested block");
        Check(Cs2Logic.Get("\"a\"\n{\n\t// \"k\" \"no\"\n\t\"k\" \"yes\" // note\n}\n", new[] { "a" }, "k", false) == "yes", "cs2: // comments are skipped");
        Check(Cs2Logic.Get("\"a\" { \"k\" \"1\" [$WIN32] \"j\" \"2\" }", new[] { "a" }, "j", false) == "2", "cs2: [$WIN32] conditions are skipped");
        Check(Cs2Logic.Get("\"a\" { { \"k\" \"inner\" } \"j\" \"2\" }", new[] { "a" }, "k", false) == null, "cs2: a block without a key does not lend its keys");
        Check(Cs2Logic.Get("\"a\" { k bare }", new[] { "a" }, "k", false) == "bare", "cs2: bare tokens are values");
        Check(Cs2Logic.Get("\"a\" { \"k\" \"1\" \"k\" \"2\" }", new[] { "a" }, "k", false) == "1", "cs2: the first matching key counts");
        Check(Cs2Logic.Get("\uFEFF" + Video, V, "VendorID", false) == "4318", "cs2: a BOM is skipped");
        var root = Cs2Logic.Parse(Libs);
        Check(root.Block("libraryfolders")?.Block("1")?.Get("path") == @"M:\Steam", "cs2: escaped backslashes are read as one");
        Check(Cs2Logic.Parse("\"a\" { \"k\" ").Block("a")?.Close == -1, "cs2: a block cut off has no closing brace");
        Check(Cs2Logic.Parse("} } \"a\" \"1\"").Get("a") == "1", "cs2: stray closing braces at the top do not break reading");
    }

    private static void Editing(Action<bool, string> Check)
    {
        var s = Cs2Logic.Set(Video, V, "setting.r_low_latency", "2", false);
        Check(s == Video.Replace("\"setting.r_low_latency\"\t\t\"1\"", "\"setting.r_low_latency\"\t\t\"2\""), "cs2: a value changes in place, every other byte stays");
        var noKey = Video.Replace("\t\"setting.r_low_latency\"\t\t\"1\"\n", "");
        var add = Cs2Logic.Set(noKey, V, "setting.r_low_latency", "0", false);
        Check(add == noKey.Replace("\t\"setting.aspectratiomode\"\t\t\"1\"\n}", "\t\"setting.aspectratiomode\"\t\t\"1\"\n\t\"setting.r_low_latency\"\t\t\"0\"\n}"),
            "cs2: a missing video key goes before the closing brace with one tab");
        var convAdd = Cs2Logic.Set(Conv, C, "cl_hud_telemetry_ping_show", "2", true);
        Check(convAdd == Conv.Replace("\"0.1\"\r\n\t}", "\"0.1\"\r\n\t\t\"cl_hud_telemetry_ping_show\"\t\t\"2\"\r\n\t}"),
            "cs2: a missing convar goes in \"convars\" with two tabs and CRLF");
        Check(Cs2Logic.Set("\"a\"\n{\n  \"x\" \"1\"\n}\n", new[] { "a" }, "y", "2", false) == "\"a\"\n{\n  \"x\" \"1\"\n\t\"y\" \"2\"\n}\n",
            "cs2: a new line takes the separator of the block's other lines");
        Check(Cs2Logic.Set("\"a\" { \"x\" \"1\" }", new[] { "a" }, "y", "2", false) == "\"a\" { \"x\" \"1\" \"y\" \"2\" }", "cs2: a block on one line gets the pair before its brace");
        Check(Cs2Logic.Set("\"a\"\n{\n}\n", new[] { "a" }, "y", "2", false) == "\"a\"\n{\n\t\"y\"\t\t\"2\"\n}\n", "cs2: an empty block gets its first line");
        Check(Cs2Logic.Set("\"a\" { k 1 }", new[] { "a" }, "k", "20", false) == "\"a\" { k 20 }", "cs2: a bare value stays bare");
        Check(Cs2Logic.Set("\"a\" { k 1 }", new[] { "a" }, "k", "a b", false) == "\"a\" { k \"a b\" }", "cs2: a bare value with a space gets quotes");
        Check(Cs2Logic.Set("\"a\" { \"k\" \"1\" }", new[] { "a" }, "k", "x\"y", false) == "\"a\" { \"k\" \"x\\\"y\" }", "cs2: a quote in a value is escaped");
        Check(Cs2Logic.Set(Video, C, "fps_max", "1", true) == null, "cs2: no block, no change (unknown file)");
        Check(Cs2Logic.Set("\"video.cfg\"\n{\n\t\"a\" \"1\"\n", V, "b", "2", false) == null, "cs2: a block cut off is not edited");
        Check(Cs2Logic.Set("", V, "b", "2", false) == null, "cs2: an empty file is not edited");
        var bom = "\uFEFF" + Video;
        Check(Cs2Logic.Set(bom, V, "setting.mat_vsync", "1", false) == "\uFEFF" + Video.Replace("\"setting.mat_vsync\"\t\t\"0\"", "\"setting.mat_vsync\"\t\t\"1\""), "cs2: a BOM stays");
        Check(Cs2Logic.Set("\"a\"\n{\n\t\"k\"\t\"1\"\n}", new[] { "a" }, "j", "2", false) == "\"a\"\n{\n\t\"k\"\t\"1\"\n\t\"j\"\t\"2\"\n}", "cs2: no newline at the end stays so");

        var rm = Cs2Logic.Remove(Video, V, "setting.mat_vsync", false);
        Check(rm == Video.Replace("\t\"setting.mat_vsync\"\t\t\"0\"\n", ""), "cs2: remove takes the whole line");
        Check(Cs2Logic.Remove(Conv, C, "fps_max", true) == Conv.Replace("\t\t\"fps_max\"\t\t\"400.000000\"\r\n", ""), "cs2: remove takes the line with its CRLF");
        Check(Cs2Logic.Remove(Video, V, "setting.nope", false) == Video, "cs2: remove of a missing key changes nothing");
        Check(Cs2Logic.Remove("\"a\" { \"x\" \"1\" \"y\" \"2\" }", new[] { "a" }, "y", false) == "\"a\" { \"x\" \"1\" }", "cs2: remove on a shared line takes only the pair");
        Check(Cs2Logic.Remove(null, V, "x", false) == "", "cs2: remove of nothing is nothing");
        Check(Cs2Logic.Remove(add!, V, "setting.r_low_latency", false) == noKey, "cs2: adding and removing a key gives the same bytes");
    }

    private static void Versions(Action<bool, string> Check)
    {
        Check(Cs2Logic.VersionOf("fps_max", "fps_max") == 0, "cs2: the plain name is version 0");
        Check(Cs2Logic.VersionOf("FPS_MAX$3", "fps_max") == 3, "cs2: name$N is version N, any case");
        Check(Cs2Logic.VersionOf("fps_max_ui$2", "fps_max") == -1, "cs2: fps_max_ui is not fps_max");
        Check(Cs2Logic.VersionOf("fps_max$", "fps_max") == -1 && Cs2Logic.VersionOf("fps_max$x", "fps_max") == -1 && Cs2Logic.VersionOf("fps_max$-1", "fps_max") == -1,
            "cs2: a $ without a number is no version");
        var two = Conv.Replace("\t\t\"r_player_visibility_mode\"\t\t\"1\"\r\n", "\t\t\"r_player_visibility_mode\"\t\t\"1\"\r\n\t\t\"fps_max$2\"\t\t\"240\"\r\n");
        Check(Cs2Logic.Get(two, C, "fps_max", true) == "240", "cs2: the highest version counts");
        Check(Cs2Logic.Get(two, C, "fps_max", false) == "400.000000", "cs2: without versions the plain key counts");
        Check(Cs2Logic.Get(Conv, C, "fps_max_ui", true) == "120", "cs2: a versioned convar is found by its name");
        var set = Cs2Logic.Set(two, C, "fps_max", "144", true);
        Check(set == two.Replace("\"fps_max$2\"\t\t\"240\"", "\"fps_max$2\"\t\t\"144\""), "cs2: set changes the highest version only");
        Check(Cs2Logic.Remove(two, C, "fps_max", true) == Conv, "cs2: remove takes the highest version only");
        Check(Cs2Logic.Get("\"config\" { \"convars\" { \"x$2\" \"a\" \"x$2\" \"b\" } }", C, "x", true) == "a", "cs2: of two equal versions the first counts");
        Check(Cs2Logic.Set(Video.Replace("\"setting.mat_vsync\"", "\"setting.mat_vsync$2\""), V, "setting.mat_vsync", "1", false)!.Contains("\t\"setting.mat_vsync\"\t\t\"1\"\n}"),
            "cs2: video keys have no versions");
    }

    private static void Backups(Action<bool, string> Check)
    {
        Check(Cs2Logic.BackupOf(null) == "-" && Cs2Logic.BackupOf("1") == "1", "cs2: a missing line backs up as '-'");
        Check(Cs2Logic.Restore(Video, V, "setting.mat_vsync", false, "-") == Video.Replace("\t\"setting.mat_vsync\"\t\t\"0\"\n", ""), "cs2: restore of '-' removes the line");
        Check(Cs2Logic.Restore(Conv, C, "fps_max", true, "300.000000") == Conv.Replace("400.000000", "300.000000"), "cs2: restore writes the old raw value");
        Check(Cs2Logic.Restore("x", C, "fps_max", true, "1") == "x", "cs2: restore into an unknown file changes nothing");

        // first change: the backup is what was there
        var (a, drop) = Cs2Logic.Apply(Conv, C, "fps_max", true, "240", Cs2Logic.BackupOf(Cs2Logic.Get(Conv, C, "fps_max", true)));
        Check(!drop && a == Conv.Replace("400.000000", "240"), "cs2: apply writes a new value and keeps the backup");
        // back to what it meant before: the file is byte for byte the old one
        var (b, drop2) = Cs2Logic.Apply(a, C, "fps_max", true, "400", "400.000000");
        Check(drop2 && b == Conv, "cs2: apply of the old meaning restores the exact old bytes and drops the backup");
        // a missing line and its default
        var noKey = Video.Replace("\t\"setting.r_low_latency\"\t\t\"1\"\n", "");
        var (c, drop3) = Cs2Logic.Apply(noKey, V, "setting.r_low_latency", false, "1", "-");
        Check(drop3 && c == noKey, "cs2: the default on a missing line changes nothing");
        var (d, drop4) = Cs2Logic.Apply(noKey, V, "setting.r_low_latency", false, "2", "-");
        Check(!drop4 && Cs2Logic.Get(d, V, "setting.r_low_latency", false) == "2", "cs2: a new value on a missing line adds it");
        var (e, drop5) = Cs2Logic.Apply(d, V, "setting.r_low_latency", false, "1", "-");
        Check(drop5 && e == noKey, "cs2: back to the default removes the added line");
        Check(Cs2Logic.Apply("junk", V, "setting.mat_vsync", false, "1", "-").next == null, "cs2: apply on an unknown file gives null");
        Check(Cs2Logic.Apply("junk", V, "setting.mat_vsync", false, "0", "-").next == null, "cs2: apply on an unknown file gives null, also for the old value");

        // a whole round: several items changed, then each backup put back
        var bv = Cs2Logic.BackupOf(Cs2Logic.Get(Video, V, Cs2Logic.KeyVsync, false));
        var br = Cs2Logic.BackupOf(Cs2Logic.Get(Video, V, Cs2Logic.KeyReflex, false));
        var t = Cs2Logic.Apply(Video, V, Cs2Logic.KeyVsync, false, "1", bv).next!;
        t = Cs2Logic.Apply(t, V, Cs2Logic.KeyReflex, false, "2", br).next!;
        Check(t != Video && Cs2Logic.Get(t, V, Cs2Logic.KeyVsync, false) == "1" && Cs2Logic.Get(t, V, Cs2Logic.KeyReflex, false) == "2", "cs2: two video items changed");
        t = Cs2Logic.Restore(Cs2Logic.Restore(t, V, Cs2Logic.KeyVsync, false, bv), V, Cs2Logic.KeyReflex, false, br);
        Check(t == Video, "cs2: revert gives the exact old file");
        var bp = Cs2Logic.BackupOf(Cs2Logic.Get(Conv, C, Cs2Logic.CvTelePing, true));
        var bf = Cs2Logic.BackupOf(Cs2Logic.Get(Conv, C, Cs2Logic.CvTeleFps, true));
        var u = Cs2Logic.Apply(Conv, C, Cs2Logic.CvTeleFps, true, "2", bf).next!;
        u = Cs2Logic.Apply(u, C, Cs2Logic.CvTelePing, true, "2", bp).next!;
        Check(bp == "-" && Cs2Logic.Get(u, C, Cs2Logic.CvTelePing, true) == "2" && Cs2Logic.Get(u, C, Cs2Logic.CvTeleFps, true) == "2", "cs2: telemetry sets both keys");
        Check(Cs2Logic.Restore(Cs2Logic.Restore(u, C, Cs2Logic.CvTeleFps, true, bf), C, Cs2Logic.CvTelePing, true, bp) == Conv, "cs2: telemetry revert removes the added ping line");

        Check(Cs2Logic.Canon(Cs2Logic.CvFps, "400.000000") == Cs2Logic.Canon(Cs2Logic.CvFps, "400") && Cs2Logic.Canon(Cs2Logic.CvFps, null) == "400", "cs2: fps_max 400.000000, 400 and missing mean the same");
        Check(Cs2Logic.Canon(Cs2Logic.KeyVsync, null) == "0" && Cs2Logic.Canon(Cs2Logic.KeyReflex, null) == "1", "cs2: missing V-Sync is off, missing Reflex is on");
        Check(Cs2Logic.Canon(Cs2Logic.CvContrast, null) == "1" && Cs2Logic.Canon(Cs2Logic.CvContrast, "false") == "0", "cs2: missing contrast is on");
        Check(Cs2Logic.Canon(Cs2Logic.CvNetBuffer, null) == "0" && Cs2Logic.Canon(Cs2Logic.CvTeleFps, null) == "1" && Cs2Logic.Canon(Cs2Logic.CvTelePing, null) == "1", "cs2: defaults of buffer and telemetry");
        Check(Cs2Logic.Canon("other", null) == "-" && Cs2Logic.Canon("other", "x") == "x", "cs2: an unknown key compares raw");

        // "Changed" and the count of "Zurücksetzen" go by meaning, not by the raw text CS2 saved
        Check(!Cs2Logic.Differs(Cs2Logic.CvFps, "400.000000", "400") && !Cs2Logic.Differs(Cs2Logic.CvFps, "-", "400.000000"), "cs2: fps_max saved in another form is not changed");
        Check(Cs2Logic.Differs(Cs2Logic.CvFps, "400.000000", "240") && Cs2Logic.Differs(Cs2Logic.CvFps, "-", "0"), "cs2: another fps_max is changed");
        Check(!Cs2Logic.Differs(Cs2Logic.KeyReflex, "-", "1") && Cs2Logic.Differs(Cs2Logic.KeyReflex, "-", "2"), "cs2: a missing Reflex line and \"1\" mean the same");
        Check(!Cs2Logic.Differs(Cs2Logic.CvContrast, "1", "true") && Cs2Logic.Differs(Cs2Logic.CvContrast, "1", "0"), "cs2: contrast by meaning");
        Check(Cs2Logic.Differs(Cs2Logic.CvTelePing, "-", "2") && !Cs2Logic.Differs(Cs2Logic.CvTelePing, "-", null), "cs2: telemetry by meaning");

        // stale backups: the file means what the backup holds again (CS2's menu set it back, or CS2 reset the file)
        Check(Cs2Logic.Stale(Conv, C, Cs2Logic.CvFps, true, "400") && Cs2Logic.Stale(Conv, C, Cs2Logic.CvFps, true, "-"), "cs2: an fps_max backup equal in meaning (400 vs 400.000000, or no line) is stale");
        Check(Cs2Logic.Stale(Video, V, Cs2Logic.KeyReflex, false, "-") && Cs2Logic.Stale(Video, V, Cs2Logic.KeyReflex, false, "1.000000"), "cs2: a Reflex backup \"-\" or 1 is stale when the file says 1");
        Check(!Cs2Logic.Stale(Conv, C, Cs2Logic.CvFps, true, "240") && !Cs2Logic.Stale(Video, V, Cs2Logic.KeyReflex, false, "2"), "cs2: a backup that still differs is kept");
        var noReflex = Video.Replace("\t\"setting.r_low_latency\"\t\t\"1\"\n", "");
        Check(Cs2Logic.Stale(noReflex, V, Cs2Logic.KeyReflex, false, "-") && !Cs2Logic.Stale(noReflex, V, Cs2Logic.KeyReflex, false, "0"), "cs2: a missing Reflex line counts as its default 1");
        Check(!Cs2Logic.Stale(null, V, Cs2Logic.KeyReflex, false, "-") && !Cs2Logic.Stale(null, C, Cs2Logic.CvFps, true, "400"), "cs2: a file that could not be read keeps its backups");
        var half = Video[..Video.IndexOf("\t\"setting.r_low_latency\"", StringComparison.Ordinal)];
        Check(!Cs2Logic.Stale(half, V, Cs2Logic.KeyReflex, false, "-") && !Cs2Logic.Stale("", V, Cs2Logic.KeyReflex, false, "-") && !Cs2Logic.Stale(Video, C, Cs2Logic.CvFps, true, "-"),
            "cs2: half a file (CS2 writing it), an empty file or a missing block keep the backups");
        var v2 = Conv.Replace("\t\t\"r_player_visibility_mode\"", "\t\t\"fps_max$2\"\t\t\"240\"\r\n\t\t\"r_player_visibility_mode\"");
        Check(!Cs2Logic.Stale(v2, C, Cs2Logic.CvFps, true, "400.000000") && Cs2Logic.Stale(v2, C, Cs2Logic.CvFps, true, "240"), "cs2: a stale check reads the newest $N version");

        // the pin follows: once every file backup is stale the page goes back to the account found now
        var held = new Dictionary<(string text, string[] block, string key, bool conv), string>
        {
            [(Video, V, Cs2Logic.KeyReflex, false)] = "-",
            [(Conv, C, Cs2Logic.CvFps, true)] = "400.000000",
        };
        var left = held.Where(kv => !Cs2Logic.Stale(kv.Key.text, kv.Key.block, kv.Key.key, kv.Key.conv, kv.Value)).ToList();
        Check(left.Count == 0 && Cs2Logic.PickCfg(@"C:\a", left.Count > 0, @"C:\b") == @"C:\b", "cs2: all backups stale: the page follows the account found now");
        held[(Conv, C, Cs2Logic.CvFps, true)] = "240";
        left = held.Where(kv => !Cs2Logic.Stale(kv.Key.text, kv.Key.block, kv.Key.key, kv.Key.conv, kv.Value)).ToList();
        Check(left.Count == 1 && Cs2Logic.PickCfg(@"C:\a", left.Count > 0, @"C:\b") == @"C:\a", "cs2: one backup still changes something: the page stays with its account");
    }

    private static void Bytes(Action<bool, string> Check)
    {
        // a UTF-8 microphone name, a byte that is no valid UTF-8 (0xFF), NBSP and NEL bytes inside values, BOM and CRLF
        var head = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = System.Text.Encoding.ASCII.GetBytes("\"config\"\r\n{\r\n\t\"convars\"\r\n\t{\r\n\t\t\"fps_max\"\t\t\"400.000000\"\r\n\t\t\"voice_device_override\"\t\t\"");
        var name = new byte[] { 0xD0, 0x9C, 0xD0, 0xB8, 0xD0, 0xBA, 0x20, 0xFF, 0xC2, 0xA0, 0x85 };
        var tail = System.Text.Encoding.ASCII.GetBytes("\"\r\n\t}\r\n}\r\n");
        var file = head.Concat(body).Concat(name).Concat(tail).ToArray();
        var (text, bom) = Cs2Logic.Decode(file);
        Check(bom && text.Length == file.Length - 3, "cs2: decode keeps one char per byte and finds the BOM");
        Check(Cs2Logic.Encode(text, bom).SequenceEqual(file), "cs2: decode and encode give the same bytes");
        Check(Cs2Logic.Get(text, C, "fps_max", true) == "400.000000" && Cs2Logic.Get(text, C, "voice_device_override", true)!.Length == name.Length, "cs2: values with non-ASCII bytes are read whole");
        var set = Cs2Logic.Set(text, C, "fps_max", "240", true)!;
        var want = head.Concat(body).Concat(name).Concat(tail).ToArray();
        var at = Array.IndexOf(want, (byte)'4', 3 + "\"config\"\r\n{\r\n\t\"convars\"\r\n\t{\r\n\t\t\"fps_max\"\t\t\"".Length);
        var expect = want.Take(at).Concat(System.Text.Encoding.ASCII.GetBytes("240")).Concat(want.Skip(at + "400.000000".Length)).ToArray();
        Check(Cs2Logic.Encode(set, bom).SequenceEqual(expect), "cs2: an edit keeps every other byte, also invalid UTF-8");
        var add = Cs2Logic.Set(text, C, "cl_net_buffer_ticks", "1", true)!;
        Check(Cs2Logic.Encode(Cs2Logic.Remove(add, C, "cl_net_buffer_ticks", true), bom).SequenceEqual(file), "cs2: add and remove give the same bytes with non-ASCII in the file");
        Check(Cs2Logic.Decode(Array.Empty<byte>()) == ("", false) && Cs2Logic.Encode("", false).Length == 0, "cs2: an empty file");
        // NBSP (0xA0) and NEL (0x85) are no white space for KeyValues: a bare token stays whole
        Check(Cs2Logic.Get("\"a\" { k x\u00A0y }", new[] { "a" }, "k", false) == "x\u00A0y" && Cs2Logic.Get("\"a\" { k x\u0085y }", new[] { "a" }, "k", false) == "x\u0085y",
            "cs2: only ASCII white space splits tokens");
    }

    private static void Values(Action<bool, string> Check)
    {
        Check(Cs2Logic.Num("1") == 1 && Cs2Logic.Num(" 400.000000 ") == 400 && Cs2Logic.Num("true") == 1 && Cs2Logic.Num("FALSE") == 0, "cs2: numbers, floats and bools");
        Check(Cs2Logic.Num(null) == null && Cs2Logic.Num("") == null && Cs2Logic.Num("abc") == null && Cs2Logic.Num("NaN") == null && Cs2Logic.Num("1,5") == null, "cs2: no number is null");
        Check(Cs2Logic.On("1", false) && !Cs2Logic.On("0", true) && Cs2Logic.On(null, true) && !Cs2Logic.On("x", false), "cs2: switches with defaults");
        Check(Cs2Logic.Level("2.000000", 1) == "2" && Cs2Logic.Level("5", 1) == "2" && Cs2Logic.Level("-1", 1) == "0" && Cs2Logic.Level(null, 0) == "0" && Cs2Logic.Level("x", 1) == "1",
            "cs2: 0/1/2 levels are kept in range");

        Check(Cs2Logic.Vendor("4318") == Cs2Logic.Gpu.Nvidia && Cs2Logic.Vendor("4098") == Cs2Logic.Gpu.Amd && Cs2Logic.Vendor("32902") == Cs2Logic.Gpu.Intel, "cs2: vendor ids are decimal");
        Check(Cs2Logic.Vendor("0x10DE") == Cs2Logic.Gpu.Nvidia && Cs2Logic.Vendor("0x1002") == Cs2Logic.Gpu.Amd, "cs2: vendor ids in hex too");
        Check(Cs2Logic.Vendor(null) == Cs2Logic.Gpu.Unknown && Cs2Logic.Vendor("5140") == Cs2Logic.Gpu.Unknown && Cs2Logic.Vendor("0xZZ") == Cs2Logic.Gpu.Unknown, "cs2: other vendors are unknown");
        Check(Cs2Logic.HasLowLatency(Cs2Logic.Gpu.Nvidia) && Cs2Logic.HasLowLatency(Cs2Logic.Gpu.Amd) && !Cs2Logic.HasLowLatency(Cs2Logic.Gpu.Intel) && !Cs2Logic.HasLowLatency(Cs2Logic.Gpu.Unknown),
            "cs2: Reflex or Anti-Lag only on NVIDIA and AMD");
        Check(Cs2Logic.ReflexTitle(Cs2Logic.Gpu.Nvidia) == "NVIDIA Reflex" && Cs2Logic.ReflexTitle(Cs2Logic.Gpu.Amd) == "AMD Anti-Lag 2", "cs2: latency title by vendor");
        Check(string.Join(",", Cs2Logic.ReflexOptions(Cs2Logic.Gpu.Nvidia).Select(o => o.v)) == "0,1,2", "cs2: NVIDIA has off, on, boost");
        Check(string.Join(",", Cs2Logic.ReflexOptions(Cs2Logic.Gpu.Amd).Select(o => o.v)) == "0,1", "cs2: AMD has no boost");
        Check(Cs2Logic.Reflex(null, Cs2Logic.Gpu.Nvidia) == "1" && Cs2Logic.Reflex("0", Cs2Logic.Gpu.Nvidia) == "0" && Cs2Logic.Reflex("2", Cs2Logic.Gpu.Nvidia) == "2", "cs2: Reflex values");
        Check(Cs2Logic.Reflex("2", Cs2Logic.Gpu.Amd) == "1", "cs2: a boost left on AMD shows as on");
        Check(Cs2Logic.ReflexRec("2") == "2" && Cs2Logic.ReflexRec("0") == "1" && Cs2Logic.ReflexRec("1") == "1", "cs2: recommended keeps a chosen boost");
        Check(Cs2Logic.ReflexError("1", Cs2Logic.Gpu.Intel) != null && Cs2Logic.ReflexError("1", Cs2Logic.Gpu.Unknown) != null, "cs2: no Reflex without NVIDIA or AMD");
        Check(Cs2Logic.ReflexError("2", Cs2Logic.Gpu.Amd)!.Contains("Boost") && Cs2Logic.ReflexError("1", Cs2Logic.Gpu.Amd) == null, "cs2: boost refused on AMD");
        Check(Cs2Logic.ReflexError("2", Cs2Logic.Gpu.Nvidia) == null && Cs2Logic.ReflexError("0", Cs2Logic.Gpu.Nvidia) == null, "cs2: boost and off fine on NVIDIA");
        Check(Cs2Logic.ReflexError("3", Cs2Logic.Gpu.Nvidia) != null && Cs2Logic.ReflexError(null, Cs2Logic.Gpu.Nvidia) != null && Cs2Logic.ReflexError("1.0", Cs2Logic.Gpu.Nvidia) != null,
            "cs2: other Reflex values refused");

        // Reflex from GTX 900, Anti-Lag 2 from RDNA: only clearly old families by DeviceID (decimal as CS2 writes it)
        Check(Cs2Logic.PciId("26591") == 0x67DFu && Cs2Logic.PciId("0x15BF") == 0x15BFu && Cs2Logic.PciId(" 4487 ") == 0x1187u, "cs2: PCI ids decimal or hex");
        Check(Cs2Logic.PciId(null) == null && Cs2Logic.PciId("") == null && Cs2Logic.PciId("0xZZ") == null && Cs2Logic.PciId("-1") == null, "cs2: no PCI id from junk");
        Check(!Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "26591"), "cs2: RX 580 (Polaris 0x67DF) has no Anti-Lag 2");
        Check(!Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Nvidia, "4487") && !Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Nvidia, "4100") && !Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Nvidia, "4747"),
            "cs2: GTX 760, GTX 780 and GT 710 (Kepler) have no Reflex");
        Check(Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Nvidia, "7171") && Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Nvidia, "11266"), "cs2: GTX 1060 and a new GeForce have Reflex");
        Check(Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Nvidia, "4928") && !Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Nvidia, "4927"), "cs2: Maxwell from 0x1340 stays available, below is Kepler");
        Check(Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "29772") && Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "0x15BF") && Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "29631"),
            "cs2: RDNA cards and an RDNA APU (Phoenix) have Anti-Lag 2");
        Check(!Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "26751") && !Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "26287") && !Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "29440"),
            "cs2: Vega 64, Radeon VII and Fiji have no Anti-Lag 2");
        Check(Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "29456") && !Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "29455"), "cs2: Navi 10 (0x7310) right after Fiji (up to 0x730F) has it");
        Check(!Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "5597") && !Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "0x1638") && !Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "39140"),
            "cs2: Vega and older GCN APUs (Raven, Cezanne, Stoney) have no Anti-Lag 2");
        Check(Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Nvidia, null) && Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Amd, "") && Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Nvidia, "garbage"),
            "cs2: an unknown DeviceID keeps the item available");
        Check(!Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Intel, "7171") && !Cs2Logic.LowLatencySupported(Cs2Logic.Gpu.Unknown, null), "cs2: never without NVIDIA or AMD");
        Check(Cs2Logic.ReflexError("1", Cs2Logic.Gpu.Nvidia, "4487") == Cs2Logic.LowLatencyWhy(Cs2Logic.Gpu.Nvidia) + "." && Cs2Logic.ReflexError("0", Cs2Logic.Gpu.Amd, "26591") != null,
            "cs2: an old card refuses every latency value with the reason");
        Check(Cs2Logic.ReflexError("2", Cs2Logic.Gpu.Nvidia, "7171") == null && Cs2Logic.ReflexError("1", Cs2Logic.Gpu.Amd, "29772") == null, "cs2: a supported card takes them");
        Check(Cs2Logic.LowLatencyWhy(Cs2Logic.Gpu.Nvidia).Contains("GTX 900") && Cs2Logic.LowLatencyWhy(Cs2Logic.Gpu.Amd).Contains("RX 5000")
              && Cs2Logic.LowLatencyWhy(Cs2Logic.Gpu.Intel) == "Nur mit NVIDIA- oder AMD-Grafikkarte", "cs2: the reason names what the card needs");
        foreach (var g in new[] { Cs2Logic.Gpu.Nvidia, Cs2Logic.Gpu.Amd, Cs2Logic.Gpu.Intel, Cs2Logic.Gpu.Unknown })
        {
            var why = Cs2Logic.LowLatencyWhy(g);
            Check(!why.EndsWith('.') && !why.Contains(" Sie ") && !why.Contains('\n'), $"cs2: reason is one du-line without full stop: {why}");
            Check(Cs2Logic.ReflexSum(g).StartsWith("Senkt die Verzögerung") && Cs2Logic.ReflexSum(g).Contains("ab ") && !Cs2Logic.ReflexSum(g).EndsWith('.'), $"cs2: the short line says what it needs ({g})");
        }
        Check(Cs2Logic.Get(Video, V, Cs2Logic.KeyDevice, false) == "11266", "cs2: DeviceID is read from cs2_video.txt");

        Check(Cs2Logic.FpsMax(null) == 400 && Cs2Logic.FpsMax("400.000000") == 400 && Cs2Logic.FpsMax("0") == 0 && Cs2Logic.FpsMax("143.6") == 144, "cs2: fps_max read");
        Check(Cs2Logic.FpsMax("-5") == 0 && Cs2Logic.FpsMax("1e9") == 100_000, "cs2: fps_max read in range");
        Check(Cs2Logic.FpsError(0) == null && Cs2Logic.FpsError(64) == null && Cs2Logic.FpsError(240) == null && Cs2Logic.FpsError(1000) == null, "cs2: 0 and 64 to 1000 FPS are fine");
        Check(Cs2Logic.FpsError(63) == Cs2Logic.FpsRange && Cs2Logic.FpsError(1001) != null && Cs2Logic.FpsError(-1) != null && Cs2Logic.FpsError(1) != null, "cs2: other limits refused");
        Check(Cs2Logic.FpsError(120.5) != null && Cs2Logic.FpsError(double.NaN) != null && Cs2Logic.FpsError(double.PositiveInfinity) != null, "cs2: no fractions, NaN or infinity");
        Check(Cs2Logic.FpsText(240) == "240" && Cs2Logic.FpsText(0) == "0", "cs2: fps_max written as a whole number");
        Check(Cs2Logic.LevelError("0", "m") == null && Cs2Logic.LevelError("2", "m") == null && Cs2Logic.LevelError("3", "m") == "m" && Cs2Logic.LevelError(null, "m") == "m", "cs2: 0/1/2 choices");
        Check(Cs2Logic.NetBufferOptions().Count == 3 && Cs2Logic.TelemetryOptions().Select(o => o.v).SequenceEqual(new[] { "0", "1", "2" }), "cs2: choice lists");
    }

    private static void Steam(Action<bool, string> Check)
    {
        Check(Cs2Logic.SteamPath("c:/program files (x86)/steam") == @"c:\program files (x86)\steam", "cs2: SteamPath slashes turned");
        Check(Cs2Logic.SteamPath("D:/Steam/") == @"D:\Steam" && Cs2Logic.SteamPath(null) == null && Cs2Logic.SteamPath("  ") == null, "cs2: SteamPath trimmed, empty is null");

        var libs = Cs2Logic.Libraries(Libs);
        Check(libs.Count == 3 && libs[0] == @"M:\Steam" && libs[1] == @"C:\Program Files (x86)\Steam" && libs[2] == @"D:\Games", "cs2: the library with app 730 comes first");
        var old = "\"LibraryFolders\"\n{\n\t\"TimeNextStatsReport\"\t\t\"1\"\n\t\"ContentStatsID\"\t\t\"-1\"\n\t\"1\"\t\t\"E:\\\\SteamLibrary\"\n}\n";
        Check(Cs2Logic.Libraries(old).SequenceEqual(new[] { @"E:\SteamLibrary" }), "cs2: the old library format is read");
        Check(Cs2Logic.Libraries(null).Count == 0 && Cs2Logic.Libraries("junk").Count == 0, "cs2: no libraries in junk");
        var dup = "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\S\" \"apps\" { \"730\" \"1\" } } \"1\" { \"path\" \"c:\\\\s\" } }";
        Check(Cs2Logic.Libraries(dup).Count == 1, "cs2: the same library twice counts once");

        var acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"Counter-Strike 2\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n\t\"UserConfig\"\n\t{\n\t\t\"language\"\t\t\"german\"\n\t}\n}\n";
        Check(Cs2Logic.Manifest(acf) == ("Counter-Strike Global Offensive", 4), "cs2: app manifest install folder and state");
        Check(Cs2Logic.Manifest(null) == (null, 0) && Cs2Logic.Manifest("\"AppState\" { \"installdir\" \"\" }") == (null, 0), "cs2: no manifest, no folder");

        Check(Cs2Logic.AccountId("76561198000000001") == 39734273u, "cs2: SteamID64 to account id");
        Check(Cs2Logic.AccountId("12345") == null && Cs2Logic.AccountId("x") == null && Cs2Logic.AccountId(null) == null && Cs2Logic.AccountId("76561197960265728") == null, "cs2: no account id from junk");

        var users = Cs2Logic.LoginUsers(Users);
        Check(users.Count == 2 && users[0].Persona == "Kai" && users[1].Persona == "Smurf \"2\"" && users[1].MostRecent && !users[0].MostRecent, "cs2: loginusers read");
        Check(Cs2Logic.MostRecent(users) == 39734274u, "cs2: MostRecent wins over a newer timestamp");
        Check(Cs2Logic.MostRecent(Cs2Logic.LoginUsers(Users.Replace("\"MostRecent\"\t\t\"1\"", "\"MostRecent\"\t\t\"0\""))) == 39734273u, "cs2: without MostRecent the newest timestamp wins");
        Check(Cs2Logic.MostRecent(Cs2Logic.LoginUsers(null)) == null, "cs2: no users, no account");

        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var with = new List<(uint, DateTime)> { (11, t0), (22, t0.AddDays(3)) };
        Check(Cs2Logic.PickAccount(33, 11, with) == 33, "cs2: the signed-in account wins");
        Check(Cs2Logic.PickAccount(0, 11, with) == 11, "cs2: then the most recent account with settings");
        Check(Cs2Logic.PickAccount(0, 44, with) == 22, "cs2: a most recent account without settings yields to the newest settings");
        Check(Cs2Logic.PickAccount(0, 44, new List<(uint, DateTime)>()) == 44, "cs2: with no settings at all the most recent account");
        Check(Cs2Logic.PickAccount(0, null, new List<(uint, DateTime)>()) == null, "cs2: nothing known, no account");

        Check(Cs2Logic.PickCfg(@"C:\a", true, @"C:\b") == @"C:\a" && Cs2Logic.PickCfg(@"C:\a", false, @"C:\b") == @"C:\b", "cs2: the backed-up folder wins only while backups exist");
        Check(Cs2Logic.PickCfg(null, true, @"C:\b") == @"C:\b" && Cs2Logic.PickCfg(" ", true, null) == null, "cs2: no stored folder, the detected one");

        Check(Cs2Logic.AccountOfCfg(@"C:\Steam\userdata\39734273\730\local\cfg") == 39734273u, "cs2: account id of a cfg path");
        Check(Cs2Logic.AccountOfCfg(@"c:/steam/USERDATA/5/730/LOCAL/CFG/") == 5u, "cs2: account id of a cfg path, slashes and case");
        Check(Cs2Logic.AccountOfCfg(@"D:\cs2\cfg") == null && Cs2Logic.AccountOfCfg(@"C:\Steam\userdata\0\730\local\cfg") == null
              && Cs2Logic.AccountOfCfg(@"C:\Steam\userdata\5\440\local\cfg") == null && Cs2Logic.AccountOfCfg(null) == null, "cs2: no account in other paths");
    }

    private static void Page(Action<bool, string> Check)
    {
        Check(Cs2Logic.Who(5, "Kai") == "dein Steam-Konto „Kai“" && Cs2Logic.Who(5, null) == "das Steam-Konto 5", "cs2: who the settings belong to");
        Check(Cs2Logic.Note(false, false, false, null, false, false).StartsWith(Cs2Logic.NotFound), "cs2: note when CS2 is missing");
        Check(Cs2Logic.Note(true, false, false, null, false, false) == Cs2Logic.StartOnce, "cs2: note when the settings are missing");
        Check(Cs2Logic.Note(true, true, true, "x", false, false).StartsWith("CS2 läuft gerade"), "cs2: note while CS2 runs");
        Check(Cs2Logic.Note(true, true, false, "dein Steam-Konto „Kai“", false, false) == "Gilt für dein Steam-Konto „Kai“. CS2 übernimmt die Werte beim nächsten Start.", "cs2: note with the account");
        Check(Cs2Logic.Note(true, true, false, null, true, false).Contains("USRLOCALCSGO"), "cs2: note for a USRLOCALCSGO folder");
        Check(Cs2Logic.Note(true, true, false, "das Steam-Konto 5", false, true).Contains("das Steam-Konto 5 geändert"), "cs2: note when the page stays with a changed account");
        Check(Cs2Logic.Note(false, true, false, "das Steam-Konto 5", false, false).Contains("Grafikkarten-Einstellung"), "cs2: note when only the settings were found");
        foreach (var n in new[] { Cs2Logic.NotFound, Cs2Logic.StartOnce, Cs2Logic.CloseFirst, Cs2Logic.FpsRange, Cs2Logic.NetBufferRange, Cs2Logic.TelemetryRange })
            Check(n.Length > 0 && char.IsUpper(n[0]) && n.EndsWith('.') && !n.Contains(" Sie ") && !n.Contains("Ihr"), "cs2: German message in du: " + n);
    }
}
