using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OrbitTweak.Core;

// =====================================================================
//  MOD CLASSIFIER – tells what an .rpf is and where FiveM can load it
//  Based on FiveM's own loader (citizen-mod-loader-five, ModPackage.cpp / ModVFSDevice.cpp):
//  - FiveM.app\mods only loads OPEN/CFXP archives that hold an assembly.xml ("mod package").
//  - Sound archives (WEAPONS_PLAYER.rpf …) go to FiveM.app\citizen\platform\audio\sfx and must stay
//    NG-encrypted with their exact name; an OPEN archive there stops FiveM with "Detected modified game files".
//  - Raw streaming archives (weapons.rpf) and loose .ytd/.ydr files are wrapped into a package.
// =====================================================================

internal enum RpfKind { Package, DlcPack, AudioArchive, StreamArchive, GameArchive, Unknown }

/// <summary>One &lt;add&gt; of an assembly.xml: the archive chain it sits in, its file under content/ and its target.</summary>
internal sealed record PkgAdd(List<string> Archives, string Source, string Target);

/// <param name="Cat">Category read from the content, null when the content does not tell.</param>
/// <param name="Problem">Why FiveM would not load it (or crash on it); null = fine.</param>
/// <param name="Note">A hint that does not stop the install.</param>
internal sealed record RpfInfo(RpfKind Kind, uint Encryption, string Canon, string? Cat, string? Title, List<PkgAdd> Adds, string? Problem, string? Note = null)
{
    public bool Encrypted => Encryption is RpfToc.NG or RpfToc.AES;
}

/// <summary>German texts shown in the app.</summary>
internal static class ModText
{
    public const string OpenSound = "Dieses Pack ist nicht FiveM-fertig (unverschlüsseltes OPEN-Archiv). FiveM würde beim Start mit „Detected modified game files“ abbrechen. Lade die FiveM-Version oder verschlüssle es mit ArchiveFix.";
    public const string Encrypted = "Verschlüsseltes Archiv: im mods-Ordner lädt FiveM nur unverschlüsselte Pakete. Nur die FiveM-Version dieses Mods ist installierbar.";
    public const string GameArchive = "Ganze Spiel-Archive (update.rpf, x64*.rpf, common.rpf) werden nicht unterstützt.";
    public const string Unknown = "Unbekannter Inhalt: OrbitTweak weiß nicht, wo FiveM diese Datei laden würde.";
    public const string Wrapped = "Rohes Archiv erkannt – als FiveM-Paket verpackt.";
    public const string Broken = "Wirkt nicht: FiveM ignoriert diese Datei im mods-Ordner.";
    public const string LooseSound = "Lose Sound-Dateien (.awc) kann FiveM nicht laden. Du brauchst die FiveM-Version des Packs (WEAPONS_PLAYER.rpf / RESIDENT.rpf).";
    public const string PackageSound = "Das Paket ersetzt Sound-Dateien. Die lädt FiveM aus dem mods-Ordner wahrscheinlich nicht – nimm lieber die FiveM-Version (WEAPONS_PLAYER.rpf / RESIDENT.rpf).";
    public static string Sound(IEnumerable<string> packs, bool gta) => $"Sound-Pack erkannt ({string.Join(", ", packs)}). " + (gta
        ? "OrbitTweak ersetzt die Dateien im GTA-V-Ordner, die Originale sind gesichert. Wirkt auch auf Servern mit Pure-Mode 1. Steam/Rockstar „Dateien überprüfen“ setzt sie zurück."
        : "FiveM lädt Sounds nicht aus dem mods-Ordner – OrbitTweak legt sie in den citizen-Ordner. Wirkt auf Servern ohne Pure-Mode.");
}

internal static class ModClassifier
{
    /// <summary>Base-game wave packs FiveM knows (citizenfx/fivem PatchAudioWavePackOverlay.cpp, g_basePacks).</summary>
    public static readonly HashSet<string> SfxPacks = new(StringComparer.OrdinalIgnoreCase)
    {
        "ANIMALS","ANIMALS_FAR","ANIMALS_NEAR","CUTSCENE_MASTERED_ONLY","DLC_GTAO","INTERACTIVE_MUSIC","ONESHOT_AMBIENCE","PAIN","POLICE_SCANNER","PROLOGUE",
        "RADIO_01_CLASS_ROCK","RADIO_02_POP","RADIO_03_HIPHOP_NEW","RADIO_04_PUNK","RADIO_05_TALK_01","RADIO_06_COUNTRY","RADIO_07_DANCE_01","RADIO_08_MEXICAN",
        "RADIO_09_HIPHOP_OLD","RADIO_11_TALK_02","RADIO_12_REGGAE","RADIO_13_JAZZ","RADIO_14_DANCE_02","RADIO_15_MOTOWN","RADIO_16_SILVERLAKE","RADIO_17_FUNK",
        "RADIO_18_90S_ROCK","RADIO_ADVERTS","RADIO_NEWS","RESIDENT","SCRIPT","SS_AC","SS_DE","SS_FF","SS_GM","SS_NP","SS_QR","SS_ST","SS_UZ","STREAMED_AMBIENCE",
        "STREAMED_VEHICLES","STREAMED_VEHICLES_GRANULAR","STREAMED_VEHICLES_GRANULAR_NPC","STREAMED_VEHICLES_LOW_LATENCY","STREAMS","S_FULL_AMB_F","S_FULL_AMB_M",
        "S_FULL_GAN","S_FULL_SER","S_MINI_AMB","S_MINI_GAN","S_MINI_SER","S_MISC","WEAPONS_PLAYER",
    };
    /// <summary>Files the game streams by name.</summary>
    public static readonly HashSet<string> StreamExt = new(StringComparer.OrdinalIgnoreCase) { ".ytd", ".ydr", ".ydd", ".yft", ".ypt", ".ycd", ".ybn", ".ymap", ".ytyp", ".gfx", ".ymt" };

    /// <summary>
    /// The game's own name of a sound archive. An NG key is made from the file name, so a renamed copy
    /// ("WEAPONS_PLAYER (2).rpf", "RESIDENT - FiveM.rpf") has to get its real name back. Null = no sound archive name.
    /// </summary>
    public static string? SoundPack(string stem)
    {
        if (SfxPacks.TryGetValue(stem, out var exact)) return exact;
        // only long names: "RESIDENT - FiveM", "WEAPONS_PLAYER_v2"; short ones (PAIN, SCRIPT) would match ordinary words
        foreach (var p in SfxPacks.Where(p => p.Length >= 8).OrderByDescending(p => p.Length))
            if (Regex.IsMatch(stem, "^" + Regex.Escape(p) + @"(?=$|[\s_\-\.\(\[])", RegexOptions.IgnoreCase)) return p;
        return null;
    }

    private static bool GameArchiveName(string stem) => Regex.IsMatch(stem, @"^(update\d*|common|x64[a-w]?|dlc)$", RegexOptions.IgnoreCase);

    public static RpfInfo Classify(string path, string originalName)
    {
        using var s = File.OpenRead(path);
        var canon = Mods.CleanName(originalName);
        var stem = Path.GetFileNameWithoutExtension(canon);
        var toc = RpfToc.Read(s);
        if (!toc.Readable)
        {
            // NG/AES: the TOC can not be read without the game's keys, only the name tells what it is
            if (SoundPack(stem) is { } pack) return new(RpfKind.AudioArchive, toc.Encryption, pack + ".rpf", "sound", null, new(), null);
            if (GameArchiveName(stem)) return new(RpfKind.GameArchive, toc.Encryption, canon, null, null, new(), ModText.GameArchive);
            return new(RpfKind.Unknown, toc.Encryption, canon, null, null, new(), ModText.Encrypted);
        }
        var root = toc.Root.Kids;
        var files = AllPaths(toc).ToList();
        RpfToc.E? Top(string n) => root.FirstOrDefault(k => !k.Dir && k.Name.Equals(n, StringComparison.OrdinalIgnoreCase));

        if (Top("assembly.xml") is { } asm)
        {
            var xml = RpfToc.ReadSmall(s, asm);
            if (xml == null) return new(RpfKind.Package, toc.Encryption, canon, null, null, new(), "assembly.xml ist gepackt oder zu groß");
            var (title, adds, problem) = ParseAssembly(xml);
            string? note = null;
            if (problem == null)
            {
                // a top-level <add> is mounted as DLC: FiveM reads its setup2.xml without a check and crashes when it is missing
                var content = root.FirstOrDefault(k => k.Dir && k.Name.Equals("content", StringComparison.OrdinalIgnoreCase));
                foreach (var a in adds.Where(a => a.Archives.Count == 0))
                {
                    var src = content == null ? null : Find(content, a.Source);
                    if (src?.Nested is { } dlc && !dlc.Root.Kids.Any(k => k.Name.Equals("setup2.xml", StringComparison.OrdinalIgnoreCase)))
                    { problem = $"assembly.xml: {a.Source} hat keine setup2.xml, FiveM würde abstürzen"; break; }
                }
                if (adds.Any(a => Regex.IsMatch(string.Join("/", a.Archives.Append(a.Target)).Replace('\\', '/'), @"(^|/)audio/sfx/|\.awc$", RegexOptions.IgnoreCase)))
                    note = ModText.PackageSound;
            }
            var targets = adds.Select(a => string.Join("/", a.Archives.Append(a.Target)).Replace('\\', '/')).ToList();
            return new(RpfKind.Package, toc.Encryption, canon, GuessByTargets(targets, files), title, adds, problem, note);
        }
        if (Top("setup2.xml") != null && Top("content.xml") != null)
            return new(RpfKind.DlcPack, toc.Encryption, canon, GuessByTargets(Array.Empty<string>(), files), null, new(), null);
        int awc = root.Count(k => !k.Dir && k.Name.EndsWith(".awc", StringComparison.OrdinalIgnoreCase));
        bool stream = root.Any(k => !k.Dir && StreamExt.Contains(Path.GetExtension(k.Name)));
        if ((awc > 0 && awc * 2 >= root.Count) || (!stream && SoundPack(stem) != null))
            return new(RpfKind.AudioArchive, toc.Encryption, SoundPack(stem) is { } p ? p + ".rpf" : canon, "sound", null, new(), null);
        if (stream)
            return new(RpfKind.StreamArchive, toc.Encryption, canon, GuessByTargets(Array.Empty<string>(), files), null, new(), null);
        if (GameArchiveName(stem) || root.Any(k => k.Dir && k.Name is "common" or "x64"))
            return new(RpfKind.GameArchive, toc.Encryption, canon, GuessByTargets(Array.Empty<string>(), files), null, new(), ModText.GameArchive);
        return new(RpfKind.Unknown, toc.Encryption, canon, GuessByTargets(Array.Empty<string>(), files), null, new(), ModText.Unknown);
    }

    /// <summary>All file paths, including those inside nested archives ("content/weapons.rpf/w_pi_pistol.ytd").</summary>
    private static IEnumerable<string> AllPaths(RpfToc t, string prefix = "")
    {
        foreach (var (p, e) in t.Files())
        {
            yield return prefix + p;
            if (e.Nested != null) foreach (var x in AllPaths(e.Nested, prefix + p + "/")) yield return x;
        }
    }

    private static RpfToc.E? Find(RpfToc.E dir, string rel)
    {
        var e = dir;
        foreach (var part in rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            e = e.Kids.FirstOrDefault(k => k.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (e == null) return null;
        }
        return e;
    }

    /// <summary>The same checks FiveM's ModPackage.cpp needs: a missing or empty metadata element is a null dereference there.</summary>
    public static (string? title, List<PkgAdd> adds, string? problem) ParseAssembly(byte[] xml)
    {
        try
        {
            var d = XDocument.Parse(Encoding.UTF8.GetString(xml).TrimStart('\uFEFF'));
            var p = d.Root!;
            var adds = new List<PkgAdd>();
            bool noSource = false;
            void Walk(XElement el, List<string> roots)
            {
                foreach (var c in el.Elements())
                    if (c.Name.LocalName == "archive") Walk(c, roots.Append((string?)c.Attribute("path") ?? "").ToList());
                    else if (c.Name.LocalName == "add")
                    {
                        if (c.Attribute("source") == null) noSource = true;
                        adds.Add(new PkgAdd(roots, (string?)c.Attribute("source") ?? "", c.Value.Trim()));
                    }
            }
            if (p.Element("content") is { } content) Walk(content, new());
            static bool Text(XElement? e) => e != null && !string.IsNullOrWhiteSpace(e.Value);
            var m = p.Element("metadata");
            string? problem = null;
            if (p.Name != "package" || (string?)p.Attribute("target") != "Five") problem = "assembly.xml: <package target=\"Five\"> fehlt";
            else if (!((string?)p.Attribute("id") ?? "").StartsWith('{')) problem = "assembly.xml: id={GUID} fehlt";
            else if (m == null || !Text(m.Element("name")) || m.Element("version")?.Element("major") == null || m.Element("version")?.Element("minor") == null
                     || !Text(m.Element("author")?.Element("displayName")) || !Text(m.Element("description")))
                problem = "assembly.xml: Angaben unvollständig, FiveM kann daran abstürzen";
            else if (noSource) problem = "assembly.xml: <add> ohne source, FiveM kann daran abstürzen";
            else if (adds.Count == 0) problem = "assembly.xml: kein <add>-Eintrag, FiveM lädt nichts";
            return (((string?)m?.Element("name"))?.Trim(), adds, problem);
        }
        catch (Exception ex) { return (null, new(), "assembly.xml unlesbar: " + ex.Message); }
    }

    /// <summary>Category from what the pack replaces. Null when the content does not tell.</summary>
    public static string? GuessByTargets(IEnumerable<string> targets, IEnumerable<string> files)
    {
        var all = string.Join("\n", targets.Concat(files)).Replace('\\', '/').ToLowerInvariant();
        if (all.Length == 0) return null;
        if (Regex.IsMatch(all, @"(^|/)audio/|\.awc\b|(^|/)(weapons_player|resident)\.rpf", RegexOptions.Multiline)) return "sound";
        if (Regex.IsMatch(all, @"minimap_(sea_)?\d+_\d+\.ytd|minimap_lod|(^|/)minimap\.gfx|(^|/)radar_|postal", RegexOptions.Multiline)) return "minimap";
        if (Regex.IsMatch(all, @"\.ypt\b|bloodfx|(^|/)blood_|/effects/|hud_reticle|big_message|killfx", RegexOptions.Multiline)) return "kill";
        if (Regex.IsMatch(all, @"(^|/)w_[a-z]{2}_[a-z0-9_]+(\+hi)?\.(ytd|ydr|yft)|(^|/)weapons\.rpf|(^|/)weapons/", RegexOptions.Multiline)) return "skin";
        if (Regex.IsMatch(all, @"timecycle|visualsettings\.dat|(^|/)w_[a-z]+\.xml|water\.xml|skydome|cloudhat|lensflare|/weather/", RegexOptions.Multiline)) return "gfx";
        return null;
    }

    /// <summary>Category from the file name, strongest words first: "sfx", "killsound", "hitsound" must win over "fx", "kill", "hit".</summary>
    public static string GuessByName(string name)
    {
        var n = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
        if (Regex.IsMatch(n, @"sound|sfx|audio|\bawc\b|gunshot|weapons_player|resident|explosion|siren|\bsnd\b")) return "sound";
        if (Regex.IsMatch(n, @"minimap|radar|postal|atlas|satellite|\bmap\b")) return "minimap";
        if (Regex.IsMatch(n, @"blood|kill|tracer|impact|muzzle|hitmarker|hit[\s_-]?effect|\bfx\b|effect")) return "kill";
        if (Regex.IsMatch(n, @"skin|camo|\bw_[a-z]{2}_|weapon|gun|pistol|rifle|carbine|vintage|deagle|ak-?47|\bm4")) return "skin";
        if (Regex.IsMatch(n, @"graphic|visual|nve|quantv|timecycle|\btc\b|weather|sky|cloud|water|light|preset|reshade|enb")) return "gfx";
        return "other";
    }
}

/// <summary>Builds FiveM mod packages (OPEN RPF with an assembly.xml) from raw files. Data is streamed, never held in memory.</summary>
internal static class PackageBuilder
{
    public sealed class Items
    {
        /// <summary>A: raw streaming archives (weapons.rpf); FiveM mounts an &lt;add&gt; ending in .rpf as "faux streaming" pack.</summary>
        public List<(string path, string inner)> StreamArchives = new();
        /// <summary>B: loose .ytd/.ydr/.gfx: two archive levels and a target without '/' make them streaming files.</summary>
        public List<(string path, string name)> Loose = new();
        /// <summary>C: data files at their game path (common/data/…, x64/data/…), laid over update.rpf.</summary>
        public List<(string path, string gamePath)> Data = new();
        /// <summary>D: add-on DLC packs (setup2.xml + content.xml).</summary>
        public List<(string path, string name)> Dlcs = new();
        public bool Any => StreamArchives.Count + Loose.Count + Data.Count + Dlcs.Count > 0;
    }

    public static void Build(Items it, string outPath, string title, Guid id)
    {
        var w = new StreamRpfWriter();
        var upd = new StringBuilder(); var loose = new StringBuilder(); var top = new StringBuilder();
        static string X(string s) => SecurityElement.Escape(s) ?? "";
        foreach (var (p, inner) in it.StreamArchives)
        {
            w.AddFile("content", inner, p);
            upd.Append($"<add source=\"{X(inner)}\">x64\\orbit\\{X(inner)}</add>");
        }
        foreach (var (p, gp) in it.Data)
        {
            var rel = gp.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
            var slash = rel.LastIndexOf('/');
            w.AddAuto("content/" + (slash > 0 ? rel[..slash] : ""), rel[(slash + 1)..], p);
            upd.Append($"<add source=\"{X(rel)}\">{X(rel.Replace('/', '\\'))}</add>");
        }
        foreach (var (p, n) in it.Loose)
        {
            w.AddAuto("content/stream", n, p);
            loose.Append($"<add source=\"stream/{X(n)}\">{X(n)}</add>");
        }
        if (loose.Length > 0) upd.Append($"<archive path=\"x64\\orbit.rpf\">{loose}</archive>");
        foreach (var (p, n) in it.Dlcs)
        {
            w.AddFile("content", n + ".rpf", p);
            top.Append($"<add source=\"{X(n)}.rpf\">update\\x64\\dlcpacks\\{X(n)}\\dlc.rpf</add>");
        }
        var content = (upd.Length > 0 ? $"<archive path=\"update\\update.rpf\">{upd}</archive>" : "") + top;
        w.AddBytes("", "assembly.xml", Assembly(id, title, content));
        w.Save(outPath);
    }

    /// <summary>An unpacked OpenIV package (.oiv = zip with assembly.xml + content/) packed into an RPF for FiveM's mods folder.</summary>
    public static void RepackOiv(string dir, string outPath)
    {
        var w = new StreamRpfWriter();
        var asm = Directory.EnumerateFiles(dir).First(f => Path.GetFileName(f).Equals("assembly.xml", StringComparison.OrdinalIgnoreCase));
        w.AddFile("", "assembly.xml", asm);
        var content = Directory.EnumerateDirectories(dir).FirstOrDefault(d => Path.GetFileName(d).Equals("content", StringComparison.OrdinalIgnoreCase));
        if (content != null)
            foreach (var f in Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(content, Path.GetDirectoryName(f)!).Replace('\\', '/');
                w.AddAuto("content" + (rel == "." ? "" : "/" + rel), Path.GetFileName(f), f);
            }
        w.Save(outPath);
    }

    public static byte[] Assembly(Guid id, string title, string content) => new UTF8Encoding(false).GetBytes($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <package version="2.1" id="{id.ToString("B").ToUpperInvariant()}" target="Five">
        <metadata><name>{SecurityElement.Escape(string.IsNullOrWhiteSpace(title) ? "Mod" : title)}</name><version><major>1</major><minor>0</minor></version>
        <author><displayName>OrbitTweak</displayName></author><description>Installed with OrbitTweak</description></metadata>
        <content>{content}</content>
        </package>
        """.Replace("\r\n", "\n").Replace("\n", "\r\n"));
}
