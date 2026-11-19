using System.Globalization;
using System.Text;

namespace OrbitTweak.Core;

/// <summary>
/// The pure rules of the Counter-Strike 2 tweaks (no Windows calls): tests/GameLogic.Tests/Cs2Tests.cs checks them.
///
/// CS2 keeps the settings of its menu per Steam account in &lt;Steam&gt;\userdata\&lt;account&gt;\730\local\cfg (or
/// %USRLOCALCSGO%\cfg), in Valve KeyValues text:
///   cs2_video.txt               "video.cfg" { "VendorID" "4318" "setting.r_low_latency" "1" "setting.mat_vsync" "0" … }
///   cs2_machine_convars.vcfg    "config" { "convars" { "fps_max" "400" "r_player_visibility_mode" "1" … } }
/// Neither has a *_lastclouded twin, so Steam Cloud does not sync them. An edit changes only the one value (or adds one
/// line before the block's closing brace, or removes one line) and keeps every other byte. Steam's own files
/// (libraryfolders.vdf, appmanifest_730.acf, loginusers.vdf) are only read.
/// </summary>
public static class Cs2Logic
{
    // ------------------------------------------------------------------ files, blocks and keys

    public const string VideoFile = "cs2_video.txt", ConvarFile = "cs2_machine_convars.vcfg";
    public static readonly string[] VideoBlock = { "video.cfg" };
    public static readonly string[] ConvarBlock = { "config", "convars" };

    public const string KeyVendor = "VendorID", KeyDevice = "DeviceID", KeyReflex = "setting.r_low_latency", KeyVsync = "setting.mat_vsync";
    public const string CvFps = "fps_max", CvContrast = "r_player_visibility_mode", CvNetBuffer = "cl_net_buffer_ticks",
        CvTeleFps = "cl_hud_telemetry_frametime_show", CvTelePing = "cl_hud_telemetry_ping_show";

    /// <summary>fps_max without a line: gameinfo.gi of CS2 sets 400 over the code default of 120.</summary>
    public const int FpsDefault = 400;

    /// <summary>SteamID64 of account id 0 (individual accounts, public universe).</summary>
    public const ulong SteamId64Base = 76561197960265728UL;

    // ------------------------------------------------------------------ Valve KeyValues

    /// <summary>One key of a KeyValues text with where it sits, so a value can be changed in place.</summary>
    public sealed class KvNode
    {
        public string Key = "";
        /// <summary>The value of a "key" "value" pair; null for a block.</summary>
        public string? Value;
        public readonly List<KvNode> Kids = new();
        /// <summary>Where the key token starts and where it ends (exclusive).</summary>
        public int Start, KeyEnd;
        /// <summary>Pair: the value token (with its quotes) and the text inside the quotes.</summary>
        public int ValTokStart, ValStart, ValEnd;
        public bool Quoted;
        /// <summary>The end of the pair (after the value token) or of the block (after its closing brace).</summary>
        public int End;
        /// <summary>Block: index of the closing brace, -1 when the text ends before it.</summary>
        public int Close = -1;

        public bool IsBlock => Value == null;

        /// <summary>The first child block with that name (any case).</summary>
        public KvNode? Block(string name) => Kids.FirstOrDefault(k => k.IsBlock && k.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The first child pair with that name (any case).</summary>
        public KvNode? Pair(string name) => Kids.FirstOrDefault(k => !k.IsBlock && k.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The value of a child pair, or null.</summary>
        public string? Get(string name) => Pair(name)?.Value;
    }

    private enum TokKind { Str, Open, Close, End }

    private readonly record struct Tok(TokKind Kind, int Start, int End, int InStart, int InEnd, bool Quoted);

    /// <summary>White space as Valve's KeyValues reader sees it: ASCII only, so a byte 0x85 or 0xA0 of a file read as
    /// Latin-1 (<see cref="Decode"/>) never splits a token.</summary>
    private static bool Ws(char c) => c is ' ' or '\t' or '\r' or '\n' or '\v' or '\f';

    /// <summary>The next token: a quoted or bare string, a brace, or the end. Skips white space, a BOM and // comments.</summary>
    private static Tok Next(string s, ref int i)
    {
        while (true)
        {
            while (i < s.Length && (Ws(s[i]) || s[i] == '\uFEFF')) i++;
            if (i >= s.Length) return new Tok(TokKind.End, i, i, i, i, false);
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            break;
        }
        int st = i;
        if (s[i] == '{') { i++; return new Tok(TokKind.Open, st, i, st, i, false); }
        if (s[i] == '}') { i++; return new Tok(TokKind.Close, st, i, st, i, false); }
        if (s[i] == '"')
        {
            i++;
            int inS = i;
            while (i < s.Length && s[i] != '"') i += s[i] == '\\' && i + 1 < s.Length ? 2 : 1;
            int inE = Math.Min(i, s.Length);
            if (i < s.Length) i++;
            return new Tok(TokKind.Str, st, i, inS, inE, true);
        }
        while (i < s.Length && !Ws(s[i]) && s[i] is not ('"' or '{' or '}')) i++;
        return new Tok(TokKind.Str, st, i, st, i, false);
    }

    /// <summary>A bare [$WIN32]-style conditional, which KeyValues allows after a key or value.</summary>
    private static bool IsCondition(string s, Tok t) => !t.Quoted && t.End - t.Start >= 2 && s[t.Start] == '[' && s[t.End - 1] == ']';

    /// <summary>The text of a string token: \\ \" \n \t are turned back (Steam writes paths as "C:\\Steam").</summary>
    private static string Text(string s, Tok t)
    {
        var raw = s[t.InStart..t.InEnd];
        if (!t.Quoted || raw.IndexOf('\\') < 0) return raw;
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                char c = raw[i + 1];
                switch (c)
                {
                    case '\\': sb.Append('\\'); i++; continue;
                    case '"': sb.Append('"'); i++; continue;
                    case 'n': sb.Append('\n'); i++; continue;
                    case 't': sb.Append('\t'); i++; continue;
                }
            }
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    private static string Escape(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Reads a KeyValues text into a tree (the root has no key). Never throws: broken parts are left out.</summary>
    public static KvNode Parse(string? text)
    {
        var s = text ?? "";
        var root = new KvNode { Start = 0, End = s.Length };
        var stack = new Stack<KvNode>();
        stack.Push(root);
        int i = 0;
        while (true)
        {
            var t = Next(s, ref i);
            if (t.Kind == TokKind.End) break;
            if (t.Kind == TokKind.Close)
            {
                if (stack.Count > 1) { var b = stack.Pop(); b.Close = t.Start; b.End = t.End; }
                continue;
            }
            if (t.Kind == TokKind.Open)
            {
                // a brace without a key: keep its contents apart so they never count as keys of the outer block
                var anon = new KvNode { Start = t.Start, KeyEnd = t.Start };
                stack.Peek().Kids.Add(anon);
                stack.Push(anon);
                continue;
            }
            if (IsCondition(s, t)) continue;
            var node = new KvNode { Key = Text(s, t), Start = t.Start, KeyEnd = t.End };
            int back = i;
            var v = Next(s, ref i);
            while (v.Kind == TokKind.Str && IsCondition(s, v)) { back = i; v = Next(s, ref i); }
            if (v.Kind == TokKind.Open)
            {
                stack.Peek().Kids.Add(node);
                stack.Push(node);
            }
            else if (v.Kind == TokKind.Str)
            {
                node.Value = Text(s, v);
                node.ValTokStart = v.Start; node.ValStart = v.InStart; node.ValEnd = v.InEnd; node.Quoted = v.Quoted;
                node.End = v.End;
                stack.Peek().Kids.Add(node);
            }
            else i = back;   // a key right before '}' or the end: not a pair
        }
        return root;
    }

    /// <summary>The block at that path (names in any case), or null.</summary>
    public static KvNode? Find(KvNode root, IReadOnlyList<string> path)
    {
        var n = root;
        foreach (var p in path) { n = n.Block(p); if (n == null) return null; }
        return n;
    }

    /// <summary>
    /// The version of a convar line: 0 for the plain name, N for "name$N", -1 for another name. CS2 bumps a convar's
    /// version in gameinfo.gi when it resets its default and then reads only "name$N" (fps_max_ui$2, hud_scaling$3).
    /// </summary>
    public static int VersionOf(string key, string name)
    {
        if (key.Equals(name, StringComparison.OrdinalIgnoreCase)) return 0;
        if (key.Length > name.Length + 1 && key.StartsWith(name, StringComparison.OrdinalIgnoreCase) && key[name.Length] == '$'
            && int.TryParse(key.AsSpan(name.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return n;
        return -1;
    }

    /// <summary>The pair a key means in a block: the first match, or for a convar the highest "$N" version.</summary>
    private static KvNode? PairIn(KvNode block, string key, bool versioned)
    {
        if (!versioned) return block.Pair(key);
        KvNode? best = null;
        int bestN = -1;
        foreach (var k in block.Kids)
        {
            if (k.IsBlock) continue;
            int n = VersionOf(k.Key, key);
            if (n > bestN) { best = k; bestN = n; }
        }
        return best;
    }

    /// <summary>The value of a key in a block, or null when the block or the key is missing.</summary>
    public static string? Get(string? text, IReadOnlyList<string> block, string key, bool versioned)
        => Find(Parse(text), block) is { } b ? PairIn(b, key, versioned)?.Value : null;

    /// <summary>
    /// The text with the key set: its value replaced in place, else a new line before the block's closing brace with
    /// the block's indent plus one tab, the separator of the block's other lines and the text's own line ending.
    /// Null when the block is missing or not closed (a file in a form CS2 does not write).
    /// </summary>
    public static string? Set(string? text, IReadOnlyList<string> block, string key, string value, bool versioned)
    {
        var s = text ?? "";
        var b = Find(Parse(s), block);
        if (b == null || b.Close < 0) return null;
        var pair = PairIn(b, key, versioned);
        if (pair != null)
        {
            if (pair.Quoted) return s[..pair.ValStart] + Escape(value) + s[pair.ValEnd..];
            var bare = value.Length > 0 && value.All(c => !Ws(c) && c is not ('"' or '{' or '}' or '\\'));
            return s[..pair.ValTokStart] + (bare ? value : "\"" + Escape(value) + "\"") + s[pair.End..];
        }
        var nl = s.Contains("\r\n") ? "\r\n" : "\n";
        var sep = "\t\t";
        var last = b.Kids.LastOrDefault(k => !k.IsBlock);
        if (last != null)
        {
            var between = s[last.KeyEnd..last.ValTokStart];
            if (between.Length > 0 && between.All(c => c is ' ' or '\t')) sep = between;
        }
        var line = "\"" + Escape(key) + "\"" + sep + "\"" + Escape(value) + "\"";
        int close = b.Close, ls = LineStart(s, close);
        var before = s[ls..close];
        if (ls > 0 && before.All(c => c is ' ' or '\t'))
            return s[..ls] + before + "\t" + line + nl + s[ls..];
        return s[..close] + line + " " + s[close..];   // "{ … }" on one line
    }

    /// <summary>The text without the key (the whole line when it stands alone on it). Unchanged when it is not there.</summary>
    public static string Remove(string? text, IReadOnlyList<string> block, string key, bool versioned)
    {
        var s = text ?? "";
        if (Find(Parse(s), block) is not { } b || PairIn(b, key, versioned) is not { } p) return s;
        int ls = LineStart(s, p.Start), le = s.IndexOf('\n', p.End);
        if (le < 0) le = s.Length;
        if (s[ls..p.Start].All(c => c is ' ' or '\t') && s[p.End..le].All(c => c is ' ' or '\t' or '\r'))
            return s[..ls] + s[Math.Min(le + 1, s.Length)..];
        int from = p.Start;
        while (from > ls && s[from - 1] is ' ' or '\t') from--;
        return s[..from] + s[p.End..];
    }

    private static int LineStart(string s, int i)
    {
        int ls = i;
        while (ls > 0 && s[ls - 1] != '\n') ls--;
        return ls;
    }

    /// <summary>
    /// The text of a settings file and whether it starts with a UTF-8 BOM. Read as Latin-1, one char per byte, so
    /// <see cref="Encode"/> gives back every byte OrbitTweak did not change, also non-ASCII ones (a microphone name in
    /// voice_device_override) and bytes that are no valid UTF-8. Keys and the values OrbitTweak writes are ASCII.
    /// </summary>
    public static (string text, bool bom) Decode(byte[] b)
    {
        bool bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
        return (Encoding.Latin1.GetString(b, bom ? 3 : 0, b.Length - (bom ? 3 : 0)), bom);
    }

    public static byte[] Encode(string text, bool bom)
    {
        var data = Encoding.Latin1.GetBytes(text);
        if (!bom) return data;
        var all = new byte[data.Length + 3];
        all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
        data.CopyTo(all, 3);
        return all;
    }

    // ------------------------------------------------------------------ backups

    /// <summary>A backup: the raw value as it was, or "-" when the line was not there.</summary>
    public const string Missing = "-";

    public static string BackupOf(string? raw) => raw ?? Missing;

    /// <summary>The text with what a backup holds put back exactly: the old raw value, or no line at all.</summary>
    public static string Restore(string? text, IReadOnlyList<string> block, string key, bool versioned, string backup)
        => backup == Missing ? Remove(text, block, key, versioned) : Set(text, block, key, backup, versioned) ?? text ?? "";

    /// <summary>
    /// The text after setting a key whose value before OrbitTweak's first change is <paramref name="backup"/>. When the
    /// new value means the same as that one (also: a missing line and its default), the file goes back to exactly what it
    /// was and the backup is no longer needed (<c>drop</c>). <c>next</c> is null when the file has an unknown form.
    /// </summary>
    public static (string? next, bool drop) Apply(string? text, IReadOnlyList<string> block, string key, bool versioned, string value, string backup)
    {
        if (Canon(key, backup == Missing ? null : backup) == Canon(key, value))
        {
            if (Find(Parse(text), block) is not { Close: >= 0 }) return (null, false);
            return (Restore(text, block, key, versioned, backup), true);
        }
        return (Set(text, block, key, value, versioned), false);
    }

    /// <summary>What a raw value means for CS2, so "400.000000", "400" and a missing fps_max line compare equal.</summary>
    public static string Canon(string key, string? raw) => key switch
    {
        KeyReflex => Level(raw, 1),
        KeyVsync => On(raw, false) ? "1" : "0",
        CvFps => FpsMax(raw).ToString(CultureInfo.InvariantCulture),
        CvContrast => On(raw, true) ? "1" : "0",
        CvNetBuffer => Level(raw, 0),
        CvTeleFps or CvTelePing => Level(raw, 1),
        _ => raw ?? Missing,
    };

    /// <summary>
    /// The value now means something else than the backup (what was there before OrbitTweak's first change). "Changed" on
    /// the page and the count of "Zurücksetzen" use it, so a file CS2 saved again in another form ("400" instead of
    /// "400.000000", or a line it wrote with the default) does not count as changed.
    /// </summary>
    public static bool Differs(string key, string backup, string? raw) => Canon(key, backup == Missing ? null : backup) != Canon(key, raw);

    /// <summary>
    /// A backup that is no longer needed: the value in its file means what the backup holds again (CS2's own menu set it
    /// back, or CS2 reset the file). Such a backup changes nothing on Zurücksetzen but would keep the page on its account
    /// (<see cref="PickCfg"/>), so it goes before the folder is picked. A file that could not be read (null: locked, gone)
    /// or whose block is missing or not closed (CS2 is writing it right now, or a form CS2 does not write) keeps every backup:
    /// a key that is only missing from half a file must not count as "back to the default".
    /// </summary>
    public static bool Stale(string? text, IReadOnlyList<string> block, string key, bool versioned, string backup)
        => text != null && Find(Parse(text), block) is { Close: >= 0 } b && !Differs(key, backup, PairIn(b, key, versioned)?.Value);

    // ------------------------------------------------------------------ values

    /// <summary>A number of a settings file: "1", "400.000000", "true"/"false". Null when it is none.</summary>
    public static double? Num(string? raw)
    {
        var t = raw?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1;
        if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;
    }

    /// <summary>A switch: anything but 0 is on; a missing or unreadable value is the default.</summary>
    public static bool On(string? raw, bool dflt) => Num(raw) is { } n ? Math.Abs(n) >= 0.5 : dflt;

    /// <summary>A 0/1/2 setting as "0", "1" or "2"; a missing or unreadable value is the default.</summary>
    public static string Level(string? raw, int dflt)
    {
        var n = Num(raw) is { } d ? (int)Math.Clamp(Math.Round(d), 0, 2) : dflt;
        return n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The GPU vendor CS2 renders on, from "VendorID" (decimal as CS2 writes it, or 0x hex).</summary>
    public enum Gpu { Unknown, Nvidia, Amd, Intel }

    public static Gpu Vendor(string? raw)
        => PciId(raw) switch { 0x10DE => Gpu.Nvidia, 0x1002 => Gpu.Amd, 0x8086 => Gpu.Intel, _ => Gpu.Unknown };

    /// <summary>A PCI id of cs2_video.txt ("VendorID", "DeviceID"): decimal as CS2 writes it, or 0x hex. Null when it is none.</summary>
    public static uint? PciId(string? raw)
    {
        var t = raw?.Trim() ?? "";
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : null;
        return uint.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>The latency item exists with an NVIDIA card (Reflex) or an AMD card (Anti-Lag 2).</summary>
    public static bool HasLowLatency(Gpu g) => g is Gpu.Nvidia or Gpu.Amd;

    /// <summary>
    /// The card can use the latency item: Reflex needs a GeForce GTX 900 or newer, Anti-Lag 2 an RDNA chip (Radeon RX 5000
    /// or newer, or an RDNA APU); on older cards CS2 does not offer it and the setting does nothing. On purpose only clearly
    /// old families are ruled out, by the "DeviceID" CS2 writes next to "VendorID": NVIDIA below 0x1340 (Fermi and Kepler,
    /// e.g. GTX 780 0x1004, GTX 760 0x1187, GT 710 0x128B; Maxwell starts at 0x1340), AMD discrete GCN, Polaris and Vega
    /// (0x6600 to 0x6FFF, Fiji 0x7300 to 0x730F) and the Vega and older GCN APUs below. Any other or unknown id stays
    /// available, so a new card never loses the item.
    /// </summary>
    public static bool LowLatencySupported(Gpu g, string? deviceIdRaw)
    {
        if (!HasLowLatency(g)) return false;
        if (PciId(deviceIdRaw) is not { } id) return true;
        if (g == Gpu.Nvidia) return id >= 0x1340;
        return !(id is >= 0x6600 and <= 0x6FFF or >= 0x7300 and <= 0x730F || OldAmdApus.Contains(id));
    }

    /// <summary>Raven 0x15DD, Picasso/Dali 0x15D8, Renoir 0x1636, Cezanne 0x1638, Lucienne 0x164C, Barcelo 0x15E7,
    /// Stoney 0x98E4, Carrizo 0x9874: Vega or older GCN graphics, no Anti-Lag 2.</summary>
    private static readonly uint[] OldAmdApus = { 0x15DD, 0x15D8, 0x1636, 0x1638, 0x164C, 0x15E7, 0x98E4, 0x9874 };

    /// <summary>Why the latency item is not there on this card (one line, no full stop).</summary>
    public static string LowLatencyWhy(Gpu g) => g switch
    {
        Gpu.Nvidia => "Reflex gibt es erst ab GeForce GTX 900: auf dieser Grafikkarte bietet CS2 es nicht an",
        Gpu.Amd => "Anti-Lag 2 gibt es erst ab Radeon RX 5000 (RDNA): auf diesem Grafikchip bietet CS2 es nicht an",
        _ => "Nur mit NVIDIA- oder AMD-Grafikkarte",
    };

    /// <summary>The short line of the latency item, with what it needs (so a card the check lets through still says it).</summary>
    public static string ReflexSum(Gpu g) => "Senkt die Verzögerung zwischen Mausklick und Bild" + g switch
    {
        Gpu.Nvidia => ", ab GeForce GTX 900",
        Gpu.Amd => ", ab Radeon RX 5000",
        _ => ", ab NVIDIA GTX 900 oder AMD RX 5000",
    };

    public static string ReflexTitle(Gpu g) => g switch
    {
        Gpu.Nvidia => "NVIDIA Reflex",
        Gpu.Amd => "AMD Anti-Lag 2",
        _ => "NVIDIA Reflex / AMD Anti-Lag 2",
    };

    /// <summary>The choices of the latency item: Anti-Lag 2 has no boost.</summary>
    public static List<(string v, string t)> ReflexOptions(Gpu g)
    {
        var o = new List<(string, string)> { ("0", "Aus"), ("1", "An (Standard)") };
        if (g != Gpu.Amd) o.Add(("2", "An + Boost (nur NVIDIA)"));
        return o;
    }

    /// <summary>
    /// setting.r_low_latency as the page shows it: "0", "1" or "2"; a missing line is "1" (the default of r_low_latency).
    /// On AMD a "2" (left from an NVIDIA card) shows as "1", since Anti-Lag 2 knows only off and on.
    /// </summary>
    public static string Reflex(string? raw, Gpu g)
    {
        var v = Level(raw, 1);
        return g == Gpu.Amd && v == "2" ? "1" : v;
    }

    /// <summary>"Empfohlene anwenden" turns it on, but keeps a Boost the user chose.</summary>
    public static string ReflexRec(string shown) => shown == "2" ? "2" : "1";

    /// <summary>Why a latency value is refused, or null when it may be written. deviceId: "DeviceID" of cs2_video.txt.</summary>
    public static string? ReflexError(string? v, Gpu g, string? deviceId = null)
    {
        if (!LowLatencySupported(g, deviceId)) return LowLatencyWhy(g) + ".";
        if (v is not ("0" or "1" or "2")) return "Wähle Aus, An oder An + Boost.";
        if (v == "2" && g == Gpu.Amd) return "AMD Anti-Lag 2 hat keinen Boost. Wähle Aus oder An.";
        return null;
    }

    /// <summary>fps_max as a whole number; a missing line is 400.</summary>
    public static int FpsMax(string? raw) => Num(raw) is { } d ? (int)Math.Clamp(Math.Round(d), 0, 100_000) : FpsDefault;

    public const string FpsRange = "Wähle 0 (kein Limit) oder 64 bis 1000 FPS.";

    /// <summary>Why an FPS limit is refused (0 = no limit, else the slider range of the game), or null.</summary>
    public static string? FpsError(double v)
        => !double.IsFinite(v) || v != Math.Floor(v) || !(v == 0 || (v >= 64 && v <= 1000)) ? FpsRange : null;

    public static string FpsText(double v) => ((int)v).ToString(CultureInfo.InvariantCulture);

    public static List<(string v, string t)> NetBufferOptions() => new() { ("0", "Keiner (Standard)"), ("1", "1 Tick"), ("2", "2 Ticks") };

    public static List<(string v, string t)> TelemetryOptions() => new() { ("0", "Nie"), ("1", "Nur bei schlechten Werten (Standard)"), ("2", "Immer") };

    /// <summary>Why a 0/1/2 choice is refused, or null.</summary>
    public static string? LevelError(string? v, string message) => v is "0" or "1" or "2" ? null : message;

    public const string NetBufferRange = "Wähle Keiner, 1 Tick oder 2 Ticks.";
    public const string TelemetryRange = "Wähle Nie, Nur bei schlechten Werten oder Immer.";

    // ------------------------------------------------------------------ Steam

    /// <summary>The Steam folder from the registry ("c:/program files (x86)/steam" → "c:\program files (x86)\steam").</summary>
    public static string? SteamPath(string? reg)
    {
        var p = reg?.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        return string.IsNullOrEmpty(p) ? null : p;
    }

    /// <summary>
    /// The Steam libraries of libraryfolders.vdf, the ones whose "apps" hold CS2 (730) first. Also reads the old format
    /// from before 2021 ("LibraryFolders" { "1" "D:\\SteamLibrary" }).
    /// </summary>
    public static List<string> Libraries(string? vdf)
    {
        var root = Parse(vdf).Block("libraryfolders");
        var with = new List<string>();
        var rest = new List<string>();
        if (root == null) return with;
        foreach (var k in root.Kids)
        {
            if (k.IsBlock)
            {
                var p = k.Get("path");
                if (string.IsNullOrWhiteSpace(p)) continue;
                (k.Block("apps")?.Pair("730") != null ? with : rest).Add(p.Trim());
            }
            else if (k.Key.Length > 0 && k.Key.All(char.IsDigit) && !string.IsNullOrWhiteSpace(k.Value)) rest.Add(k.Value.Trim());
        }
        with.AddRange(rest.Where(r => !with.Contains(r, StringComparer.OrdinalIgnoreCase)));
        return with.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>appmanifest_730.acf: the folder name under steamapps\common and the state (4 = fully installed).</summary>
    public static (string? installDir, int stateFlags) Manifest(string? acf)
    {
        var a = Parse(acf).Block("AppState");
        if (a == null) return (null, 0);
        var dir = a.Get("installdir")?.Trim();
        int.TryParse(a.Get("StateFlags"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags);
        return (string.IsNullOrEmpty(dir) ? null : dir, flags);
    }

    /// <summary>The account id (the folder name under userdata) of a SteamID64: its low 32 bits. Null when it is none.</summary>
    public static uint? AccountId(string? steamId64)
    {
        if (!ulong.TryParse(steamId64?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < SteamId64Base) return null;
        var a = (uint)(id & 0xFFFFFFFF);
        return a == 0 ? null : a;
    }

    /// <summary>One account of loginusers.vdf.</summary>
    public sealed record SteamUser(uint Account, string? Persona, bool MostRecent, long Timestamp);

    public static List<SteamUser> LoginUsers(string? vdf)
    {
        var list = new List<SteamUser>();
        var users = Parse(vdf).Block("users");
        if (users == null) return list;
        foreach (var u in users.Kids)
        {
            if (!u.IsBlock || AccountId(u.Key) is not { } a) continue;
            long.TryParse(u.Get("Timestamp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts);
            var persona = u.Get("PersonaName")?.Trim();
            list.Add(new SteamUser(a, string.IsNullOrEmpty(persona) ? null : persona, u.Get("MostRecent")?.Trim() == "1", ts));
        }
        return list;
    }

    /// <summary>The account Steam signed in last: the one marked MostRecent, else the newest Timestamp.</summary>
    public static uint? MostRecent(IReadOnlyList<SteamUser> users)
        => (users.FirstOrDefault(u => u.MostRecent) ?? users.OrderByDescending(u => u.Timestamp).FirstOrDefault())?.Account;

    /// <summary>
    /// Whose CS2 settings to use. 1) The account Steam has signed in now (ActiveUser, 0 when none). 2) The most recent
    /// account of loginusers.vdf when it has CS2 settings. 3) The account whose cs2_video.txt was written last.
    /// 4) The most recent account even without settings (the page then says to start CS2 once).
    /// </summary>
    public static uint? PickAccount(uint activeUser, uint? mostRecent, IReadOnlyList<(uint account, DateTime written)> withSettings)
    {
        if (activeUser != 0) return activeUser;
        if (mostRecent is { } r && withSettings.Any(w => w.account == r)) return r;
        if (withSettings.Count > 0) return withSettings.OrderByDescending(w => w.written).First().account;
        return mostRecent;
    }

    /// <summary>
    /// The settings folder to work on: while OrbitTweak holds backups, the folder they belong to (so "Zurücksetzen" and
    /// further changes stay with that account after a switch), else the one found now. Backups that no longer change
    /// anything (<see cref="Stale"/>) are dropped before, so they never hold the page on an account.
    /// </summary>
    public static string? PickCfg(string? stored, bool anyBackup, string? detected)
        => anyBackup && !string.IsNullOrWhiteSpace(stored) ? stored : detected;

    /// <summary>The account id in a cfg path ...\userdata\&lt;id&gt;\730\local\cfg, or null (e.g. a USRLOCALCSGO folder).</summary>
    public static uint? AccountOfCfg(string? cfg)
    {
        var p = (cfg ?? "").Replace('/', '\\').TrimEnd('\\').Split('\\');
        int n = p.Length;
        if (n < 5 || !p[n - 1].Equals("cfg", StringComparison.OrdinalIgnoreCase) || !p[n - 2].Equals("local", StringComparison.OrdinalIgnoreCase)
            || p[n - 3] != "730" || !p[n - 5].Equals("userdata", StringComparison.OrdinalIgnoreCase)) return null;
        return uint.TryParse(p[n - 4], NumberStyles.None, CultureInfo.InvariantCulture, out var a) && a != 0 ? a : null;
    }

    // ------------------------------------------------------------------ the page

    public const string NotFound = "Counter-Strike 2 wurde auf diesem PC nicht gefunden.";
    public const string StartOnce = "Starte CS2 einmal und schließ es wieder, dann legt es seine Einstellungen an.";

    /// <summary>
    /// The page stays with an account OrbitTweak changed (heldOther), and a settings file of that account is missing now.
    /// "Starte CS2 einmal" would not help: CS2 starts with the account in use. Zurücksetzen brings back what OrbitTweak changed
    /// in the file that is still there and lets the page follow the account in use (Cs2Tweaks.DropStale has dropped the
    /// backups of the missing file, but only while CS2 is closed, hence the other line while it runs).
    /// </summary>
    public static string HeldMissing(string? who, bool running)
        => $"OrbitTweak hat die Einstellungen für {who ?? "ein anderes Steam-Konto"} geändert, und bei diesem Konto fehlt jetzt eine Einstellungsdatei. "
           + (running ? "Schließ CS2 und prüf dann neu."
                      : "Setz zurück, dann wechselt diese Seite zu deinem aktuellen Steam-Konto. Oder starte CS2 einmal mit diesem Konto, dann legt es die Datei wieder an.");

    /// <summary>Why the file items cannot be set while the settings files are not both there.</summary>
    public static string NoFiles(bool installed, bool running, bool heldOther, string? who)
        => !installed ? NotFound : heldOther ? HeldMissing(who, running) : StartOnce;
    public const string CloseFirst = "Schließ Counter-Strike 2 zuerst, sonst überschreibt das Spiel die Einstellung beim Beenden.";

    /// <summary>"dein Steam-Konto „Name“" or "das Steam-Konto 12345678".</summary>
    public static string Who(uint account, string? persona)
        => persona is { Length: > 0 } ? $"dein Steam-Konto „{persona}“" : $"das Steam-Konto {account.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The one line of the page about the state of CS2.</summary>
    /// <param name="who">From <see cref="Who"/>; null for a USRLOCALCSGO folder.</param>
    /// <param name="heldOther">The page stays with an account OrbitTweak changed, another one is signed in now.</param>
    public static string Note(bool installed, bool configFound, bool running, string? who, bool fromEnv, bool heldOther)
    {
        if (!installed && !configFound) return NotFound + " Installiere es über Steam und starte es einmal.";
        if (!configFound) return NoFiles(installed, running, heldOther, who);
        if (running) return "CS2 läuft gerade. Schließ es, bevor du hier etwas änderst, sonst überschreibt es die Werte beim Beenden.";
        string line;
        if (heldOther) line = $"OrbitTweak hat die Einstellungen für {who ?? "ein anderes Steam-Konto"} geändert. Bis du sie zurücksetzt, bleibt diese Seite bei diesem Konto.";
        else if (fromEnv || who == null) line = "CS2 nimmt seine Einstellungen aus dem Ordner in USRLOCALCSGO. Änderungen gelten ab dem nächsten Start.";
        else line = $"Gilt für {who}. CS2 übernimmt die Werte beim nächsten Start.";
        return installed ? line : line + " Den Spielordner hat OrbitTweak nicht gefunden, deshalb fehlt die Grafikkarten-Einstellung.";
    }
}
