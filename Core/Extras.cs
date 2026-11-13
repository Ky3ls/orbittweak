using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace OrbitTweak.Core;

// =====================================================================
//  Simple UTF-8 INI editor (keeps everything else in the file untouched)
// =====================================================================
internal static class Ini
{
    public static void Set(string path, string section, string key, string? value)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        int s = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (s < 0)
        {
            if (value == null) return;
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add($"[{section}]"); lines.Add($"{key}={value}");
        }
        else
        {
            int end = lines.FindIndex(s + 1, l => l.TrimStart().StartsWith("["));
            if (end < 0) end = lines.Count;
            int k = lines.FindIndex(s + 1, end - s - 1, l => l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            if (value == null) { if (k >= 0) lines.RemoveAt(k); }
            else if (k >= 0) lines[k] = $"{key}={value}";
            else
            {
                int ins = end;
                while (ins > s + 1 && lines[ins - 1].Trim().Length == 0) ins--;
                lines.Insert(ins, $"{key}={value}");
            }
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    public static string? Get(string path, string section, string key)
    {
        if (!File.Exists(path)) return null;
        bool inSec = false;
        foreach (var l in File.ReadAllLines(path))
        {
            var t = l.Trim();
            if (t.StartsWith("[")) { inSec = t.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase); continue; }
            if (inSec && t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return t[(key.Length + 1)..];
        }
        return null;
    }
}

// =====================================================================
//  RESHADE – official ReShade from reshade.me + SweetFX shaders, installed
//  into FiveM.app\plugins (the place FiveM loads graphics mods from)
// =====================================================================
internal static class ReShade
{
    public static string? Plugins => FiveM.AppDir is { } a ? Path.Combine(a, "plugins") : null;
    private static string Dll => Path.Combine(Plugins!, "dxgi.dll");
    private static string Config => Path.Combine(Plugins!, "ReShade.ini");
    private static string PresetDir => Path.Combine(Plugins!, "OrbitTweak-Presets");

    public static readonly (string id, string name, string desc, string ini)[] Presets =
    {
        ("vivid", "Lebendig", "Kräftige Farben, knackig scharf", """
            Techniques=Vibrance@Vibrance.fx,Curves@Curves.fx,LumaSharpen@LumaSharpen.fx
            TechniqueSorting=Vibrance@Vibrance.fx,Curves@Curves.fx,LumaSharpen@LumaSharpen.fx

            [Vibrance.fx]
            Vibrance=0.350000
            VibranceRGBBalance=1.000000,1.000000,1.000000
            Vibrance_Luma=0

            [Curves.fx]
            Mode=0
            Formula=4
            Contrast=0.200000

            [LumaSharpen.fx]
            sharp_strength=0.600000
            sharp_clamp=0.035000
            pattern=1
            offset_bias=1.000000
            show_sharpen=0
            """),
        ("pvp", "Klar (PvP)", "Maximale Sichtbarkeit, scharfe Kanten", """
            Techniques=Levels@Levels.fx,CAS@CAS.fx,Vibrance@Vibrance.fx
            TechniqueSorting=Levels@Levels.fx,CAS@CAS.fx,Vibrance@Vibrance.fx

            [Levels.fx]
            BlackPoint=10
            WhitePoint=242
            HighlightClipping=0

            [CAS.fx]
            Contrast=0.300000
            Sharpening=0.900000

            [Vibrance.fx]
            Vibrance=0.200000
            VibranceRGBBalance=1.000000,1.000000,1.000000
            Vibrance_Luma=0
            """),
        ("cine", "Cinematic", "Filmlook mit Vignette", """
            Techniques=Tonemap@Tonemap.fx,Curves@Curves.fx,Vignette@Vignette.fx,LumaSharpen@LumaSharpen.fx
            TechniqueSorting=Tonemap@Tonemap.fx,Curves@Curves.fx,Vignette@Vignette.fx,LumaSharpen@LumaSharpen.fx

            [Tonemap.fx]
            Gamma=0.950000
            Exposure=0.000000
            Saturation=0.120000
            Bleach=0.080000
            Defog=0.000000
            FogColor=0.000000,0.000000,1.000000

            [Curves.fx]
            Mode=0
            Formula=4
            Contrast=0.350000

            [Vignette.fx]
            Type=0
            Ratio=1.000000
            Radius=1.800000
            Amount=-0.450000
            Slope=2
            Center=0.500000,0.500000

            [LumaSharpen.fx]
            sharp_strength=0.400000
            sharp_clamp=0.035000
            pattern=1
            offset_bias=1.000000
            show_sharpen=0
            """),
        ("night", "Nachtsicht", "Dunkle Ecken sichtbar", """
            Techniques=Tonemap@Tonemap.fx,Levels@Levels.fx,CAS@CAS.fx
            TechniqueSorting=Tonemap@Tonemap.fx,Levels@Levels.fx,CAS@CAS.fx

            [Tonemap.fx]
            Gamma=1.250000
            Exposure=0.120000
            Saturation=0.050000
            Bleach=0.000000
            Defog=0.000000
            FogColor=0.000000,0.000000,1.000000

            [Levels.fx]
            BlackPoint=4
            WhitePoint=235
            HighlightClipping=0

            [CAS.fx]
            Contrast=0.200000
            Sharpening=0.700000
            """),
        ("techni", "Technicolor", "Satte Retro-Farben", """
            Techniques=Technicolor2@Technicolor2.fx,Vibrance@Vibrance.fx,LumaSharpen@LumaSharpen.fx
            TechniqueSorting=Technicolor2@Technicolor2.fx,Vibrance@Vibrance.fx,LumaSharpen@LumaSharpen.fx

            [Technicolor2.fx]
            ColorStrength=0.200000,0.200000,0.200000
            Brightness=1.000000
            Saturation=1.000000
            Strength=0.350000

            [Vibrance.fx]
            Vibrance=0.200000
            VibranceRGBBalance=1.000000,1.000000,1.000000
            Vibrance_Luma=0

            [LumaSharpen.fx]
            sharp_strength=0.500000
            sharp_clamp=0.035000
            pattern=1
            offset_bias=1.000000
            show_sharpen=0
            """),
    };

    public static bool Installed
    {
        get
        {
            try { return Plugins != null && File.Exists(Dll) && FileVersionInfo.GetVersionInfo(Dll).ProductName == "ReShade"; }
            catch { return false; }
        }
    }

    public static object Status()
    {
        if (Plugins == null) return new { found = false };
        string? version = null, preset = null;
        bool other = File.Exists(Dll) && !Installed;
        if (Installed) version = FileVersionInfo.GetVersionInfo(Dll).ProductVersion;
        if (File.Exists(Config) && Ini.Get(Config, "GENERAL", "PresetPath") is { } pp) preset = Path.GetFileNameWithoutExtension(pp);
        return new { found = true, installed = Installed, other, version, preset, presets = Presets.Select(p => new { p.id, p.name, p.desc }) };
    }

    /// <summary>FiveM blocks ReShade 5+ unless this line (hash of the PC name) is in CitizenFX.ini.</summary>
    private static string AckLine()
    {
        var name = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? "a";
        uint h = 0;
        foreach (var c in Encoding.Default.GetBytes(name))
        {
            var ch = c is >= (byte)'A' and <= (byte)'Z' ? (byte)(c + 32) : c;
            unchecked { h += (uint)(sbyte)ch; h += h << 10; h ^= h >> 6; }
        }
        unchecked { h += h << 3; h ^= h >> 11; h += h << 15; }
        return $"ID:{h:x8} acknowledged that ReShade 5.x has a bug that will lead to game crashes";
    }

    private static async Task<byte[]> Get(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) OrbitTweak");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        using var r = await http.SendAsync(req);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// The ReShade setup is an .exe with a zip inside (plus the signature behind it).
    /// Cut out exactly the zip part so .NET can read it.
    /// </summary>
    private static byte[] EmbeddedZip(byte[] b)
    {
        for (int e = b.Length - 22; e >= Math.Max(0, b.Length - 200_000); e--)
        {
            if (b[e] != 0x50 || b[e + 1] != 0x4B || b[e + 2] != 5 || b[e + 3] != 6) continue;
            int cdSize = BitConverter.ToInt32(b, e + 12), cdOff = BitConverter.ToInt32(b, e + 16), cl = BitConverter.ToUInt16(b, e + 20);
            int start = e - cdSize - cdOff, end = Math.Min(b.Length, e + 22 + cl);
            if (start >= 0 && b[start] == 0x50 && b[start + 1] == 0x4B) return b[start..end];
        }
        throw new InvalidOperationException("ReShade-Setup hat ein unbekanntes Format");
    }

    private static void ExtractShaders(byte[] zip, string root)
    {
        using var z = new ZipArchive(new MemoryStream(zip));
        foreach (var e in z.Entries)
        {
            if (e.Name.Length == 0) continue;
            var p = e.FullName.Replace('\\', '/');
            foreach (var part in new[] { "/Shaders/", "/Textures/" })
            {
                var i = p.IndexOf(part, StringComparison.Ordinal);
                if (i < 0) continue;
                var rel = p[(i + 1)..];
                if (rel.Contains("..")) break;
                var target = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, true);
                break;
            }
        }
    }

    public static async Task<string?> Install(string preset, Action<int, string> progress)
    {
        if (Plugins is not { } plugins) return "FiveM nicht gefunden";
        if (FiveM.IsRunning) return "Bitte FiveM zuerst schließen";
        if (File.Exists(Dll) && !Installed) return "In plugins liegt schon eine andere dxgi.dll (z. B. ENB). Bitte zuerst entfernen.";
        Directory.CreateDirectory(plugins);

        progress(5, "Suche neueste ReShade-Version…");
        var html = Encoding.UTF8.GetString(await Get("https://reshade.me/"));
        var m = Regex.Match(html, @"downloads/(ReShade_Setup_([0-9.]+)\.exe)");
        if (!m.Success) return "ReShade-Download nicht gefunden";
        progress(15, $"Lade ReShade {m.Groups[2].Value}…");
        var setup = await Get("https://reshade.me/" + m.Value);
        using (var z = new ZipArchive(new MemoryStream(EmbeddedZip(setup))))
        {
            var e = z.GetEntry("ReShade64.dll") ?? throw new InvalidOperationException("ReShade64.dll nicht im Setup gefunden");
            e.ExtractToFile(Dll, true);
        }
        if (!Installed) { File.Delete(Dll); return "Heruntergeladene Datei ist kein ReShade"; }

        progress(45, "Lade Shader (SweetFX)…");
        var shaders = Path.Combine(plugins, "reshade-shaders");
        ExtractShaders(await Get("https://github.com/crosire/reshade-shaders/archive/refs/heads/slim.zip"), shaders);
        progress(70, "Lade Shader (Basis)…");
        ExtractShaders(await Get("https://github.com/CeeJayDK/SweetFX/archive/refs/heads/master.zip"), shaders);

        progress(85, "Schreibe Presets…");
        Directory.CreateDirectory(PresetDir);
        foreach (var p in Presets) File.WriteAllText(Path.Combine(PresetDir, p.id + ".ini"), p.ini.Replace("\n", "\r\n"), new UTF8Encoding(false));
        Ini.Set(Config, "GENERAL", "EffectSearchPaths", Path.Combine(shaders, "Shaders") + @"\**");
        Ini.Set(Config, "GENERAL", "TextureSearchPaths", Path.Combine(shaders, "Textures") + @"\**");
        Ini.Set(Config, "GENERAL", "PerformanceMode", "1");
        Ini.Set(Config, "INPUT", "KeyOverlay", "36,0,0,0");
        Ini.Set(Config, "OVERLAY", "TutorialProgress", "4");
        SetPreset(preset);

        progress(95, "FiveM für ReShade freischalten…");
        if (FiveM.CitizenIni is { } ini) Ini.Set(ini, "Addons", "ReShade5", AckLine());
        progress(100, "Fertig");
        return null;
    }

    public static string? SetPreset(string id)
    {
        if (Plugins == null) return "FiveM nicht gefunden";
        var p = Presets.FirstOrDefault(x => x.id == id);
        if (p.id == null) return "Unbekanntes Preset";
        Directory.CreateDirectory(PresetDir);
        var file = Path.Combine(PresetDir, p.id + ".ini");
        if (!File.Exists(file)) File.WriteAllText(file, p.ini.Replace("\n", "\r\n"), new UTF8Encoding(false));
        Ini.Set(Config, "GENERAL", "PresetPath", file);
        return null;
    }

    public static string? Uninstall()
    {
        if (Plugins is not { } plugins) return "FiveM nicht gefunden";
        if (FiveM.IsRunning) return "Bitte FiveM zuerst schließen";
        if (Installed) File.Delete(Dll);
        foreach (var d in new[] { Path.Combine(plugins, "reshade-shaders"), PresetDir })
            if (Directory.Exists(d)) Directory.Delete(d, true);
        foreach (var f in new[] { Config, Path.Combine(plugins, "ReShade.log") })
            if (File.Exists(f)) File.Delete(f);
        if (FiveM.CitizenIni is { } ini) Ini.Set(ini, "Addons", "ReShade5", null);
        return null;
    }
}

// =====================================================================
//  WINDOWS SHELL THUMBNAILS – same previews as Explorer, no video decoding
// =====================================================================
internal static class Thumbs
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbm); }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

    private static readonly Dictionary<string, string> Cache = new();

    private static Task<T> Sta<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>();
        var t = new Thread(() => { try { tcs.SetResult(f()); } catch (Exception ex) { tcs.SetException(ex); } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    public static BitmapSource? Bitmap(string path, int w, int h)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var f);
        try
        {
            if (f.GetImage(new SIZE { cx = w, cy = h }, 0x1 | 0x8, out var hbm) != 0 || hbm == IntPtr.Zero) return null; // BIGGERSIZEOK | THUMBNAILONLY
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var fixedSrc = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgr32, null, 0); // drop bogus alpha
                fixedSrc.Freeze();
                return fixedSrc;
            }
            finally { DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(f); }
    }

    public static byte[] Jpeg(BitmapSource b, int quality = 82)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(b));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static async Task<string?> DataUrl(string path, int w = 400, int h = 225)
    {
        if (!File.Exists(path)) return null;
        var key = $"{path}|{w}|{File.GetLastWriteTimeUtc(path).Ticks}";
        lock (Cache) if (Cache.TryGetValue(key, out var c)) return c;
        var url = await Sta(() =>
        {
            try { var b = Bitmap(path, w, h); return b == null ? null : "data:image/jpeg;base64," + Convert.ToBase64String(Jpeg(b, 78)); }
            catch { return null; }
        });
        if (url != null) lock (Cache) Cache[key] = url;
        return url;
    }

    public static Task<byte[]?> LargeJpeg(string path, int w) => Sta(() =>
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            BitmapSource? b;
            if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp")
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.DecodePixelWidth = w; bi.EndInit(); bi.Freeze();
                b = new FormatConvertedBitmap(bi, System.Windows.Media.PixelFormats.Bgr32, null, 0);
            }
            else b = Bitmap(path, w, w * 9 / 16);
            return b == null ? null : Jpeg(b, 88);
        }
        catch { return null; }
    });
}

// =====================================================================
//  GTA BACKGROUNDS – the user's own GTA screenshots / clip frames
// =====================================================================
internal static class Backgrounds
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrbitTweak", "bg");
    public static string File_ => Path.Combine(Dir, "bg.jpg");
    private static readonly string[] Img = { ".jpg", ".jpeg", ".png" };
    private static readonly Regex Gta = new(@"grand theft auto|gta|fivem|los santos", RegexOptions.IgnoreCase);

    public static List<object> Candidates()
    {
        var found = new List<(string path, string source, DateTime date)>();
        void Scan(string dir, string source, bool needName, int depth = 2)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                var opts = new EnumerationOptions { RecurseSubdirectories = depth > 0, MaxRecursionDepth = depth, IgnoreInaccessible = true };
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", opts))
                {
                    if (!Img.Contains(f.Extension.ToLowerInvariant()) || f.Length < 50_000) continue;
                    if (needName && !Gta.IsMatch(f.FullName)) continue;
                    found.Add((f.FullName, source, f.LastWriteTime));
                }
            }
            catch { }
        }
        // Steam screenshots of GTA V (app 271590)
        if (Reg.Get(@"HKCU\Software\Valve\Steam", "SteamPath") is string steam)
        {
            var ud = Path.Combine(steam.Replace('/', '\\'), "userdata");
            if (Directory.Exists(ud))
                foreach (var u in Directory.GetDirectories(ud))
                    Scan(Path.Combine(u, "760", "remote", "271590", "screenshots"), "Steam", false, 0);
        }
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Scan(videos, "Videos", true, 2);                       // NVIDIA (Videos\Grand Theft Auto V) & Game Bar captures
        Scan(pics, "Bilder", true, 2);
        return found.OrderByDescending(x => x.date).Take(30)
            .Select(x => (object)new { path = x.path, name = Path.GetFileNameWithoutExtension(x.path), source = x.source, date = x.date }).ToList();
    }

    public static async Task<string?> Set(string path)
    {
        if (!File.Exists(path)) return "Datei nicht gefunden";
        var jpg = await Thumbs.LargeJpeg(path, 1920);
        if (jpg == null) return "Bild konnte nicht gelesen werden";
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(File_, jpg);
        return null;
    }
}

// =====================================================================
//  MOD DOWNLOADS – finds freshly downloaded mods and unpacks .rar/.7z
// =====================================================================
internal static class ModDownloads
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    private static readonly string[] Ext = { ".rpf", ".zip", ".rar", ".7z" };

    public static string Guess(string name)
    {
        var n = name.ToLowerInvariant();
        if (Regex.IsMatch(n, "minimap|radar|map|atlas|satellite|postal")) return "minimap";
        if (Regex.IsMatch(n, "kill|blood|hit|tracer|impact|effect|fx")) return "kill";
        if (Regex.IsMatch(n, "sound|audio|sfx|gunshot")) return "sound";
        if (Regex.IsMatch(n, "graphic|visual|nve|reshade|enb|weather|timecycle|light|preset")) return "gfx";
        return "other";
    }

    public static List<object> List()
    {
        var res = new List<object>();
        try
        {
            if (!Directory.Exists(Dir)) return res;
            foreach (var f in new DirectoryInfo(Dir).EnumerateFiles().Where(f => Ext.Contains(f.Extension.ToLowerInvariant()) && f.LastWriteTime > DateTime.Now.AddDays(-30))
                         .OrderByDescending(f => f.LastWriteTime).Take(40))
            {
                int rpfs = -1;
                if (f.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    try { using var z = ZipFile.OpenRead(f.FullName); rpfs = z.Entries.Count(e => e.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)); }
                    catch { continue; }
                    if (rpfs == 0) continue;
                }
                else if (f.Extension.Equals(".rpf", StringComparison.OrdinalIgnoreCase)) rpfs = 1;
                res.Add(new { name = f.Name, size = f.Length, date = f.LastWriteTime, rpfs, cat = Guess(f.Name) });
            }
        }
        catch { }
        return res;
    }

    /// <summary>Returns the files to hand to Mods.Install (unpacks .rar/.7z with Windows' tar).</summary>
    public static (List<string> files, string? temp, string? error) Prepare(string name) => PrepareFile(Path.Combine(Dir, Path.GetFileName(name)));

    public static (List<string> files, string? temp, string? error) PrepareFile(string path)
    {
        if (!File.Exists(path)) return (new(), null, "Datei nicht gefunden");
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".rpf" or ".zip") return (new() { path }, null, null);
        var temp = Path.Combine(Path.GetTempPath(), "OrbitTweak-mod-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
        var o = Proc.Run(tar, $"-xf \"{path}\" -C \"{temp}\"", 180_000);
        var rpfs = Directory.GetFiles(temp, "*.rpf", SearchOption.AllDirectories).ToList();
        if (rpfs.Count == 0)
            return (new(), temp, ext == ".rar" || ext == ".7z"
                ? "Archiv konnte nicht entpackt werden oder enthält keine .rpf. Unter älterem Windows bitte als .zip entpacken."
                : "Keine .rpf gefunden");
        return (rpfs, temp, null);
    }
}
