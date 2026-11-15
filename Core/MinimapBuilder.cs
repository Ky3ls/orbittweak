using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitTweak.Core;

// =====================================================================
//  CUSTOM MINIMAP FROM A PICTURE
//  Turns any image into a real FiveM minimap mod: the picture is cut into the six map tiles
//  GTA uses (2 wide, 3 high), saved as game textures and packed into a mod package (.rpf)
//  for FiveM's "mods" folder, the same way downloaded minimap packs are built.
// =====================================================================

public class MinimapModCfg
{
    public string? Path { get; set; }             // the picture
    public string Mode { get; set; } = "tile";    // tile = picture repeats (always visible), map = picture is the whole map
    public int Repeat { get; set; } = 24;         // tile mode: how often the picture fits across the map width
    public int Res { get; set; } = 2048;          // pixels per map tile: 2048 | 3072
    public string Fit { get; set; } = "stretch";  // map mode: stretch | cover | contain
    public int Dim { get; set; }                  // darken 0..80 %
}

/// <summary>Writes RPF7 archives without encryption ("OPEN"), the format FiveM reads from its mods folder.</summary>
internal sealed class RpfWriter
{
    private sealed class Node
    {
        public string Name = "";
        public List<Node>? Children;                // directory
        public byte[]? Data;                        // file content
        public bool Resource; public uint Sys, Gfx; // game resource (texture, model): data starts with the RSC7 header
        public int First, Block;
    }

    private readonly Node _root = new() { Children = new() };

    private Node Dir(string path)
    {
        var n = _root;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var c = n.Children!.FirstOrDefault(x => x.Name == part && x.Children != null);
            if (c == null) { c = new Node { Name = part, Children = new() }; n.Children!.Add(c); }
            n = c;
        }
        return n;
    }

    public void AddFile(string dir, string name, byte[] data) => Dir(dir).Children!.Add(new Node { Name = name, Data = data });
    public void AddResource(string dir, string name, byte[] rsc7, uint sys, uint gfx) => Dir(dir).Children!.Add(new Node { Name = name, Data = rsc7, Resource = true, Sys = sys, Gfx = gfx });

    public byte[] Build()
    {
        // entries: every folder's children sit next to each other, sorted by name (the game searches them by name)
        var list = new List<Node> { _root };
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Children is not { } ch) continue;
            ch.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            list[i].First = list.Count;
            list.AddRange(ch);
        }
        var names = new MemoryStream();
        var nameOff = new Dictionary<Node, int>();
        foreach (var n in list) { nameOff[n] = (int)names.Length; names.Write(Encoding.ASCII.GetBytes(n.Name)); names.WriteByte(0); }
        while (names.Length % 16 != 0) names.WriteByte(0);
        if (names.Length > 0xFFFF) throw new InvalidOperationException("Zu viele Dateien für ein Archiv");

        int header = 16 + list.Count * 16 + (int)names.Length;
        int block = (header + 511) / 512;
        foreach (var n in list.Where(n => n.Data != null)) { n.Block = block; block += (n.Data!.Length + 511) / 512; }

        var o = new byte[block * 512L];
        void U32(int at, uint v) => BitConverter.TryWriteBytes(o.AsSpan(at), v);
        U32(0, 0x52504637); U32(4, (uint)list.Count); U32(8, (uint)names.Length); U32(12, 0x4E45504F); // "RPF7", "OPEN"
        for (int i = 0; i < list.Count; i++)
        {
            var n = list[i]; int e = 16 + i * 16;
            if (n.Children != null)
            {
                U32(e, (uint)nameOff[n]); U32(e + 4, 0x7FFFFF00); U32(e + 8, (uint)n.First); U32(e + 12, (uint)n.Children.Count);
            }
            else if (n.Resource)
            {
                if (n.Data!.Length >= 0xFFFFFF) throw new InvalidOperationException("Textur ist zu groß");
                o[e] = (byte)nameOff[n]; o[e + 1] = (byte)(nameOff[n] >> 8);
                o[e + 2] = (byte)n.Data.Length; o[e + 3] = (byte)(n.Data.Length >> 8); o[e + 4] = (byte)(n.Data.Length >> 16);
                o[e + 5] = (byte)n.Block; o[e + 6] = (byte)(n.Block >> 8); o[e + 7] = (byte)((n.Block >> 16) | 0x80);
                U32(e + 8, n.Sys); U32(e + 12, n.Gfx);
            }
            else
            {
                // stored without compression: size field 0, real size in the next field
                ulong v = (uint)nameOff[n] | ((ulong)(uint)n.Block << 40);
                BitConverter.TryWriteBytes(o.AsSpan(e), v);
                U32(e + 8, (uint)n.Data!.Length); U32(e + 12, 0);
            }
        }
        names.ToArray().CopyTo(o, 16 + list.Count * 16);
        foreach (var n in list.Where(n => n.Data != null)) n.Data!.CopyTo(o, n.Block * 512L);
        return o;
    }
}

internal static class MinimapBuilder
{
    public const string FileName = "OrbitTweak-Minimap.rpf";
    private const string Pack = "orbit_minimap";

    // GTA's detailed map models for the zoomed-in radar. They are swapped for an empty model, otherwise
    // the original streets would be drawn on top of the picture.
    private static readonly int[][] DetailCells =
    {
        new[] { 2, 6 }, new[] { 1, 8 }, new[] { 0, 8 }, new[] { 0, 8 }, new[] { 0, 8 }, new[] { 0, 8 }, new[] { 0, 8 }, new[] { 0, 6 }
    };
    private const string EmptyModel =
        "UlNDN6UAAAAAAAKgAAAAUKsQDXdgZAACNoYABiTACKUVoOKMUBEDND7jAoi6AAZU/TAQu1jBjWEU0A08YESNnw+MqPEy41UCOL41mNDiFcpXYEKo/w9PBcDIhYp/8ws5mrD1wGEGBoeDIDbDBf+jDAxLwHwQe0Z6ogMjFnd9YMGePmCggBW/vCgQswMxNwMzirgAlD7AjF8/DGQIhqO4rwDNXVX8QUc3RG0C+2eqiIUTsn8ZGRrq/59H+BfEv8BIwF4LGTdkzPgfl0IhMAkzL4CNOP8gh79LePz/c24BKHyQvMLjpXA+OM6g8rb+of+XXfE/KlB3AM4/haQfxAfpF4Dqh/EXFB1A4U+YgMpPWIDgM4H4WxH8D+Ko6WMDC2o6RAccaPwDaPHlAC13vn7usIWWWw0KQKH8uDOvwHy08P6xGWI/errhxpGOHIiNByJBxEaI/aKw6GOlrvmgEGAC5hEWBlYgZgOT7ECaHUhzACELAycQsjBwYejCDqbd/6CEko+ZUN0rCAlLpmmZM0OBoDw3My8zN7Eg3iDeTE85JWW0QB4Fo2AUjIJRMApGwSgYBaNgFIyCUTAKRsEoGAWjYBSMglEwCkbBKKAzAAA=";

    // ---------- textures ----------

    /// <summary>Name hash the game uses to find textures (Jenkins one-at-a-time, lower case).</summary>
    public static uint Hash(string s)
    {
        uint h = 0;
        foreach (var ch in s.ToLowerInvariant()) { h += (byte)ch; h += h << 10; h ^= h >> 6; }
        h += h << 3; h ^= h >> 11; h += h << 15;
        return h;
    }

    /// <summary>A texture dictionary (.ytd) with one texture, laid out like the game's own minimap tiles.</summary>
    public static (byte[] rsc, uint sys, uint gfx) Ytd(string name, int w, int h, bool dxt5, byte[] pixels)
    {
        if (name.Length > 15) throw new ArgumentException("name too long");
        var sys = new byte[0x2000];
        Array.Fill(sys, (byte)0xCD);
        void Z(int at, int n) => Array.Clear(sys, at, n);
        void U16(int at, int v) => BitConverter.TryWriteBytes(sys.AsSpan(at), (ushort)v);
        void U32(int at, uint v) => BitConverter.TryWriteBytes(sys.AsSpan(at), v);
        void Ptr(int at, uint v) => BitConverter.TryWriteBytes(sys.AsSpan(at), (ulong)v);
        const uint V = 0x50000000, G = 0x60000000; // start of the system / graphics part

        // dictionary header
        Z(0, 0x40);
        U32(0x00, 0x40570FD0); U32(0x04, 1); Ptr(0x08, V + 0xD0);
        U32(0x18, 1);
        Ptr(0x20, V + 0x100); U16(0x28, 1); U16(0x2A, 1);   // name hashes
        Ptr(0x30, V + 0x110); U16(0x38, 1); U16(0x3A, 1);   // texture pointers
        // texture
        const int T = 0x40;
        Z(T, 0x90);
        U32(T, 0xB6F0EE97);
        Ptr(T + 0x28, V + 0xF0);
        U16(T + 0x30, 1); U16(T + 0x32, 0x80);
        U32(T + 0x40, (uint)((pixels.Length + 0xFFF) & ~0xFFF));
        U16(T + 0x50, w); U16(T + 0x52, h); U16(T + 0x54, 1); U16(T + 0x56, dxt5 ? w : w / 2);
        U32(T + 0x58, dxt5 ? 0x35545844u : 0x31545844u);    // "DXT5" / "DXT1"
        sys[T + 0x5C] = 0; sys[T + 0x5D] = 1;               // one mip level: stays sharp on every texture setting
        U16(T + 0x5E, 0);
        Ptr(T + 0x70, G);
        // page map, name, hash, pointer list
        Z(0xD0, 0x10); sys[0xD8] = 1; sys[0xD9] = 1;
        Z(0xF0, 0x10); Encoding.ASCII.GetBytes(name).CopyTo(sys, 0xF0);
        U32(0x100, Hash(name));
        Ptr(0x110, V + T);

        // one graphics page, big enough for the pixels
        int page = 0x2000, shift = 0;
        while (page < pixels.Length) { page <<= 1; shift++; }
        uint sysFlags = 0x00020000, gfxFlags = 0xD0020000 | (uint)shift;

        using var ms = new MemoryStream();
        var hdr = new byte[16];
        BitConverter.TryWriteBytes(hdr.AsSpan(0), 0x37435352u); BitConverter.TryWriteBytes(hdr.AsSpan(4), 13u);
        BitConverter.TryWriteBytes(hdr.AsSpan(8), sysFlags); BitConverter.TryWriteBytes(hdr.AsSpan(12), gfxFlags);
        ms.Write(hdr);
        using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true))
        {
            z.Write(sys);
            z.Write(pixels);
            var pad = new byte[Math.Min(page - pixels.Length, 1 << 16)];
            for (int left = page - pixels.Length; left > 0; left -= pad.Length) z.Write(pad, 0, Math.Min(pad.Length, left));
        }
        return (ms.ToArray(), sysFlags, gfxFlags);
    }

    // ---------- DXT compression ----------

    private static int To565(int r, int g, int b) => ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255);
    private static void From565(int c, out int r, out int g, out int b)
    {
        r = (c >> 11) & 31; g = (c >> 5) & 63; b = c & 31;
        r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); b = (b << 3) | (b >> 2);
    }

    /// <summary>
    /// Colour part of a block: two end colours (16 bit) and 2 bits per pixel choosing a mix of them.
    /// punch = DXT1 with see-through pixels (uses the 3-colour mode, index 3 = transparent).
    /// </summary>
    private static void ColorBlock(ReadOnlySpan<byte> px, Span<byte> dst, bool punch)
    {
        // px: 16 pixels, BGRA
        Span<int> use = stackalloc int[16]; int n = 0;
        for (int i = 0; i < 16; i++) if (px[i * 4 + 3] >= 128) use[n++] = i;
        if (n == 0)
        {
            if (punch) { dst[0] = dst[1] = dst[2] = dst[3] = 0; dst[4] = dst[5] = dst[6] = dst[7] = 0xFF; return; }
            for (int i = 0; i < 16; i++) use[i] = i; n = 16; // DXT5: alpha is stored separately, colour of invisible pixels does not matter
        }
        // mean and the direction the colours spread along
        double mr = 0, mg = 0, mb = 0;
        for (int k = 0; k < n; k++) { int i = use[k] * 4; mb += px[i]; mg += px[i + 1]; mr += px[i + 2]; }
        mr /= n; mg /= n; mb /= n;
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        for (int k = 0; k < n; k++)
        {
            int i = use[k] * 4; double r = px[i + 2] - mr, g = px[i + 1] - mg, b = px[i] - mb;
            xx += r * r; xy += r * g; xz += r * b; yy += g * g; yz += g * b; zz += b * b;
        }
        double ax = 1, ay = 1, az = 1;
        for (int it = 0; it < 6; it++)
        {
            double nx = xx * ax + xy * ay + xz * az, ny = xy * ax + yy * ay + yz * az, nz = xz * ax + yz * ay + zz * az;
            double m = Math.Max(Math.Abs(nx), Math.Max(Math.Abs(ny), Math.Abs(nz)));
            if (m < 1e-9) break;
            ax = nx / m; ay = ny / m; az = nz / m;
        }
        double len = Math.Sqrt(ax * ax + ay * ay + az * az);
        if (len < 1e-9) { ax = ay = az = 0.577; } else { ax /= len; ay /= len; az /= len; }
        double lo = double.MaxValue, hi = double.MinValue;
        for (int k = 0; k < n; k++)
        {
            int i = use[k] * 4; double t = (px[i + 2] - mr) * ax + (px[i + 1] - mg) * ay + (px[i] - mb) * az;
            if (t < lo) lo = t; if (t > hi) hi = t;
        }
        int C(double v) => (int)Math.Clamp(Math.Round(v), 0, 255);
        int c0 = To565(C(mr + ax * hi), C(mg + ay * hi), C(mb + az * hi));
        int c1 = To565(C(mr + ax * lo), C(mg + ay * lo), C(mb + az * lo));

        Span<int> pr = stackalloc int[4], pg = stackalloc int[4], pb = stackalloc int[4];
        uint best = 0; long bestErr = long.MaxValue; int b0 = c0, b1 = c1;
        // second pass: fit the end colours to the chosen indices (least squares), keep it if it is better
        for (int pass = 0; pass < 2; pass++)
        {
            bool three = punch && n < 16;
            if (three) { if (c0 > c1) (c0, c1) = (c1, c0); }
            else if (c0 < c1) (c0, c1) = (c1, c0);
            From565(c0, out pr[0], out pg[0], out pb[0]); From565(c1, out pr[1], out pg[1], out pb[1]);
            int levels;
            if (three)
            {
                pr[2] = (pr[0] + pr[1]) / 2; pg[2] = (pg[0] + pg[1]) / 2; pb[2] = (pb[0] + pb[1]) / 2; levels = 3;
            }
            else if (c0 == c1) levels = 1; // equal end colours would switch on the 3-colour mode: only use index 0
            else
            {
                pr[2] = (2 * pr[0] + pr[1]) / 3; pg[2] = (2 * pg[0] + pg[1]) / 3; pb[2] = (2 * pb[0] + pb[1]) / 3;
                pr[3] = (pr[0] + 2 * pr[1]) / 3; pg[3] = (pg[0] + 2 * pg[1]) / 3; pb[3] = (pb[0] + 2 * pb[1]) / 3; levels = 4;
            }
            uint idx = 0; long err = 0;
            for (int i = 0; i < 16; i++)
            {
                if (px[i * 4 + 3] < 128 && three) { idx |= 3u << (i * 2); continue; }
                int r = px[i * 4 + 2], g = px[i * 4 + 1], b = px[i * 4], bi = 0, be = int.MaxValue;
                for (int j = 0; j < levels; j++)
                {
                    int dr = r - pr[j], dg = g - pg[j], db = b - pb[j], e = dr * dr + dg * dg + db * db;
                    if (e < be) { be = e; bi = j; }
                }
                idx |= (uint)bi << (i * 2);
                if (px[i * 4 + 3] >= 128 || !punch) err += be;
            }
            if (err < bestErr) { bestErr = err; best = idx; b0 = c0; b1 = c1; }
            if (pass == 1 || levels < 3 || err == 0) break;

            // least squares: colour = w * end0 + (1 - w) * end1
            double a00 = 0, a01 = 0, a11 = 0, r0 = 0, g0 = 0, bb0 = 0, r1 = 0, g1 = 0, bb1 = 0;
            for (int i = 0; i < 16; i++)
            {
                int j = (int)(idx >> (i * 2)) & 3;
                if (three && j == 3) continue;
                double w = three ? (j == 0 ? 1 : j == 1 ? 0 : 0.5) : (j == 0 ? 1 : j == 1 ? 0 : j == 2 ? 2 / 3.0 : 1 / 3.0), v = 1 - w;
                a00 += w * w; a01 += w * v; a11 += v * v;
                r0 += w * px[i * 4 + 2]; g0 += w * px[i * 4 + 1]; bb0 += w * px[i * 4];
                r1 += v * px[i * 4 + 2]; g1 += v * px[i * 4 + 1]; bb1 += v * px[i * 4];
            }
            double det = a00 * a11 - a01 * a01;
            if (Math.Abs(det) < 1e-6) break;
            c0 = To565(C((r0 * a11 - r1 * a01) / det), C((g0 * a11 - g1 * a01) / det), C((bb0 * a11 - bb1 * a01) / det));
            c1 = To565(C((r1 * a00 - r0 * a01) / det), C((g1 * a00 - g0 * a01) / det), C((bb1 * a00 - bb0 * a01) / det));
        }
        dst[0] = (byte)b0; dst[1] = (byte)(b0 >> 8); dst[2] = (byte)b1; dst[3] = (byte)(b1 >> 8);
        BitConverter.TryWriteBytes(dst[4..], best);
    }

    /// <summary>Transparency part of a DXT5 block: two end values and 3 bits per pixel.</summary>
    private static void AlphaBlock(ReadOnlySpan<byte> px, Span<byte> dst)
    {
        int lo = 255, hi = 0;
        for (int i = 0; i < 16; i++) { int a = px[i * 4 + 3]; if (a < lo) lo = a; if (a > hi) hi = a; }
        dst[0] = (byte)hi; dst[1] = (byte)lo;
        ulong bits = 0;
        if (hi > lo)
        {
            Span<int> pal = stackalloc int[8];
            pal[0] = hi; pal[1] = lo;
            for (int k = 1; k < 7; k++) pal[k + 1] = ((7 - k) * hi + k * lo) / 7;
            for (int i = 0; i < 16; i++)
            {
                int a = px[i * 4 + 3], bi = 0, be = int.MaxValue;
                for (int j = 0; j < 8; j++) { int e = Math.Abs(a - pal[j]); if (e < be) { be = e; bi = j; } }
                bits |= (ulong)bi << (i * 3);
            }
        }
        for (int k = 0; k < 6; k++) dst[2 + k] = (byte)(bits >> (8 * k));
    }

    /// <summary>Compresses BGRA pixels. DXT1: 8 bytes per 4×4 block (see-through = on/off), DXT5: 16 bytes with smooth transparency.</summary>
    public static byte[] Compress(byte[] bgra, int w, int h, bool dxt5)
    {
        int bw = (w + 3) / 4, bh = (h + 3) / 4, bs = dxt5 ? 16 : 8;
        var o = new byte[bw * bh * bs];
        Parallel.For(0, bh, by =>
        {
            Span<byte> px = stackalloc byte[64];
            for (int bx = 0; bx < bw; bx++)
            {
                for (int y = 0; y < 4; y++)
                {
                    int sy = Math.Min(h - 1, by * 4 + y);
                    for (int x = 0; x < 4; x++)
                    {
                        int sx = Math.Min(w - 1, bx * 4 + x);
                        bgra.AsSpan((sy * w + sx) * 4, 4).CopyTo(px[((y * 4 + x) * 4)..]);
                    }
                }
                var d = o.AsSpan((by * bw + bx) * bs, bs);
                if (dxt5) { AlphaBlock(px, d); ColorBlock(px, d[8..], false); }
                else ColorBlock(px, d, true);
            }
        });
        return o;
    }

    // ---------- picture → tiles ----------

    private static T Sta<T>(Func<T> f)
    {
        T res = default!; Exception? err = null;
        var t = new Thread(() => { try { res = f(); } catch (Exception ex) { err = ex; } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err != null) throw err;
        return res;
    }

    private static BitmapSource Load(string path, int maxWidth)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        if (bi.PixelWidth > maxWidth)
        {
            // decode again at the size that is really needed (saves a lot of memory for huge pictures)
            bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.DecodePixelWidth = maxWidth;
            bi.UriSource = new Uri(path);
            bi.EndInit();
        }
        bi.Freeze();
        return bi;
    }

    /// <summary>Picture info and a small preview for the app.</summary>
    public static object Info(string path)
    {
        if (!File.Exists(path)) return new { error = "Datei nicht gefunden" };
        try
        {
            return Sta<object>(() =>
            {
                using var fs = File.OpenRead(path);
                var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
                int w = frame.PixelWidth, h = frame.PixelHeight;
                string Png(BitmapSource b)
                {
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(b));
                    using var ms = new MemoryStream(); enc.Save(ms);
                    return "data:image/png;base64," + System.Convert.ToBase64String(ms.ToArray());
                }
                // the middle of the picture in full resolution: lets the preview show how sharp the radar really gets
                int dw = Math.Clamp((int)(w * 0.12), Math.Min(16, w), w), dh = Math.Clamp((int)(h * 0.12), Math.Min(16, h), h);
                BitmapSource mid = new CroppedBitmap(frame, new Int32Rect((w - dw) / 2, (h - dh) / 2, dw, dh));
                if (dw > 1024) mid = new TransformedBitmap(mid, new ScaleTransform(1024.0 / dw, 1024.0 / dw));
                return new { path, name = System.IO.Path.GetFileName(path), w, h, preview = Png(Load(path, 640)), detail = Png(mid) };
            });
        }
        catch (Exception ex) { return new { error = "Bild konnte nicht gelesen werden: " + ex.Message }; }
    }

    /// <summary>
    /// Draws one part of the whole map picture. The map is 2 × 3 tiles; "window" is the part to draw in tile-pixel
    /// units (0..2·res wide, 0..3·res high), the result has outW × outH pixels (BGRA, not premultiplied).
    /// </summary>
    private static byte[] Render(BitmapSource src, MinimapModCfg c, int res, Rect window, int outW, int outH)
    {
        double mapW = 2.0 * res, mapH = 3.0 * res;
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(outW / window.Width, outH / window.Height));
            dc.PushTransform(new TranslateTransform(-window.X, -window.Y));
            if (c.Mode == "tile")
            {
                double pw = mapW / Math.Clamp(c.Repeat, 1, 200), ph = pw * src.PixelHeight / src.PixelWidth;
                var brush = new ImageBrush(src) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, pw, ph), Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                dc.DrawRectangle(brush, null, new Rect(0, 0, mapW, mapH));
            }
            else
            {
                var r = new Rect(0, 0, mapW, mapH);
                if (c.Fit is "cover" or "contain")
                {
                    double s = c.Fit == "cover" ? Math.Max(mapW / src.PixelWidth, mapH / src.PixelHeight) : Math.Min(mapW / src.PixelWidth, mapH / src.PixelHeight);
                    double w = src.PixelWidth * s, h = src.PixelHeight * s;
                    r = new Rect((mapW - w) / 2, (mapH - h) / 2, w, h);
                }
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, mapW, mapH)));
                dc.DrawImage(src, r);
                dc.Pop();
            }
            dc.Pop(); dc.Pop();
        }
        var rtb = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[outW * outH * 4];
        rtb.CopyPixels(px, outW * 4, 0);
        // undo the premultiplied alpha; "darken" only touches the picture, see-through parts stay see-through
        int keep = 100 - Math.Clamp(c.Dim, 0, 90);
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a == 0) continue;
            if (a != 255) { px[i] = (byte)Math.Min(255, px[i] * 255 / a); px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / a); px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / a); }
            if (keep != 100) { px[i] = (byte)(px[i] * keep / 100); px[i + 1] = (byte)(px[i + 1] * keep / 100); px[i + 2] = (byte)(px[i + 2] * keep / 100); }
        }
        return px;
    }

    /// <summary>Builds the mod package. Returns the finished .rpf bytes.</summary>
    public static byte[] Build(MinimapModCfg c, Action<int, string>? progress = null)
    {
        if (string.IsNullOrEmpty(c.Path) || !File.Exists(c.Path)) throw new InvalidOperationException("Bild nicht gefunden");
        int res = c.Res >= 3072 ? 3072 : 2048;
        progress?.Invoke(3, "Lese Bild…");
        var src = Sta(() => Load(c.Path, c.Mode == "tile" ? 2048 : 2 * res));
        var inner = new RpfWriter();
        int done = 0;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 2; col++)
            {
                int r = row, cl = col;
                progress?.Invoke(6 + done * 14, $"Kachel {done + 1} von 6…");
                var px = Sta(() => Render(src, c, res, new Rect(cl * res, r * res, res, res), res, res));
                // land layer: see-through is on/off, sea layer below it: smooth transparency (both like the original tiles)
                var land = Ytd($"minimap_{r}_{cl}", res, res, false, Compress(px, res, res, false));
                var sea = Ytd($"minimap_sea_{r}_{cl}", res, res, true, Compress(px, res, res, true));
                inner.AddResource("", $"minimap_{r}_{cl}.ytd", land.rsc, land.sys, land.gfx);
                inner.AddResource("", $"minimap_sea_{r}_{cl}.ytd", sea.rsc, sea.sys, sea.gfx);
                done++;
            }
        progress?.Invoke(90, "Übersichtskarte…");
        {
            // small picture of the whole map for the zoomed-out pause menu
            var px = Sta(() => Render(src, c, res, new Rect(0, 0, 2 * res, 3 * res), 128, 128));
            var lod = Ytd("minimap_lod_128", 128, 128, true, Compress(px, 128, 128, true));
            inner.AddResource("", "minimap_lod_128.ytd", lod.rsc, lod.sys, lod.gfx);
        }
        var empty = System.Convert.FromBase64String(EmptyModel);
        uint es = BitConverter.ToUInt32(empty, 8), eg = BitConverter.ToUInt32(empty, 12);
        for (int row = 0; row < DetailCells.Length; row++)
            for (int col = DetailCells[row][0]; col <= DetailCells[row][1]; col++)
                inner.AddResource("", $"minimap_{row}_{col}.ydd", empty, es, eg);

        progress?.Invoke(94, "Packe Mod…");
        // same layout as other FiveM mod packages: package description + a small add-on pack that holds the files
        var dlc = new RpfWriter();
        dlc.AddFile("", "content.xml", Utf8($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <CDataFileMgr__ContentsOfDataFileXml>
                <dataFiles>
                    <Item>
                        <filename>{Pack}:/%PLATFORM%/{Pack}.rpf</filename>
                        <fileType>RPF_FILE</fileType>
                    </Item>
                </dataFiles>
            </CDataFileMgr__ContentsOfDataFileXml>
            """));
        dlc.AddFile("", "setup2.xml", Utf8($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <SSetupData>
                <deviceName>{Pack}</deviceName>
            </SSetupData>
            """));
        dlc.AddFile("x64", Pack + ".rpf", inner.Build());

        var outer = new RpfWriter();
        outer.AddFile("", "assembly.xml", Utf8($$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package version="2.1" id="{0B17A7E0-4D4D-4B1E-9C5E-6D696E696D70}" target="Five">
            <metadata>
            <name>OrbitTweak Minimap</name>
            <version>
            <major>1</major>
            <minor>0</minor>
            </version>
            <author>
            <displayName>OrbitTweak</displayName>
            </author>
            <description>Custom minimap made from a picture with OrbitTweak.</description>
            </metadata>
            <content>
            <add source="{{Pack}}.rpf">update\x64\dlcpacks\{{Pack}}\dlc.rpf</add>
            </content>
            </package>
            """));
        outer.AddFile("content", Pack + ".rpf", dlc.Build());
        var bytes = outer.Build();
        progress?.Invoke(100, "Fertig");
        return bytes;
    }

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s.Replace("\r\n", "\n").Replace("\n", "\r\n"));

    /// <summary>Builds the mod and puts it into FiveM's mods folder.</summary>
    public static (long size, string? error) Install(MinimapModCfg c, Dictionary<string, string> cats, Action<int, string>? progress = null)
    {
        if (Mods.Dir is not { } dir) return (0, "FiveM nicht gefunden");
        if (FiveM.IsRunning) return (0, "Bitte FiveM zuerst schließen");
        byte[] rpf;
        try { rpf = Build(c, progress); }
        catch (Exception ex) { return (0, "Minimap konnte nicht erstellt werden: " + ex.Message); }
        Directory.CreateDirectory(dir);
        var target = System.IO.Path.Combine(dir, FileName);
        var tmp = target + ".tmp";
        File.WriteAllBytes(tmp, rpf);
        File.Move(tmp, target, true);
        // an older, switched-off copy would only confuse the mod list
        if (FiveM.AppDir is { } app) { var off = System.IO.Path.Combine(app, "mods_orbit_off", FileName); try { if (File.Exists(off)) File.Delete(off); } catch { } }
        cats[FileName] = "minimap";
        return (rpf.LongLength, null);
    }

    public static object Status()
    {
        if (Mods.Dir is not { } dir || FiveM.AppDir is not { } app) return new { found = false, installed = false };
        var on = System.IO.Path.Combine(dir, FileName); var off = System.IO.Path.Combine(app, "mods_orbit_off", FileName);
        var f = File.Exists(on) ? new FileInfo(on) : File.Exists(off) ? new FileInfo(off) : null;
        return new { found = true, installed = f != null, on = f != null && f.FullName == on, size = f?.Length ?? 0, date = f?.LastWriteTime };
    }
}
