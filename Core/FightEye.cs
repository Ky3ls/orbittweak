using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitTweak.Core;

/// <summary>
/// Keeps pictures of the last firefight for the AI check (<see cref="KillJudge"/>): while the player shoots and for a few
/// seconds after, the strip around the crosshair is copied from the screen a few times a second and the whole game
/// picture (made small) about once a second. Like a screenshot, nothing in the game is read. Pictures older than a
/// minute are dropped; nothing is written to disk.
/// </summary>
internal sealed class FightEye
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr sec, uint off);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    private sealed record Shot(DateTime At, bool Full, byte[] Jpeg);
    private readonly List<Shot> _ring = new();
    private volatile bool _on;
    private Thread? _t;

    public void Enable(bool on)
    {
        _on = on;
        if (!on) { lock (_ring) _ring.Clear(); return; }
        if (_t == null || !_t.IsAlive)
        {
            _t = new Thread(Loop) { IsBackground = true, Name = "OrbitFightEye", Priority = ThreadPriority.BelowNormal };
            _t.Start();
        }
    }

    private void Loop()
    {
        DateTime shot = DateTime.MinValue, crop = DateTime.MinValue, full = DateTime.MinValue;
        while (_on)
        {
            Thread.Sleep(50);
            if (!AutoClip.GameInFront) { Thread.Sleep(200); continue; }
            var now = DateTime.UtcNow;
            if (FireInput.Held()) shot = now;
            if ((now - shot).TotalSeconds > 4.5) continue;
            try
            {
                var (l, t, r, b) = GameFocus.Client();
                int W = r - l, H = b - t;
                if (H < 300) continue;
                if ((now - crop).TotalMilliseconds >= 400)
                {
                    crop = now;
                    // the strip the kill message appears in, at most 648 × 324 so the text stays readable but small to send
                    int w = (int)(H * KillText.HalfW) * 2, h = (int)(H * KillText.HalfH) * 2;
                    double s = Math.Min(1, 648.0 / w);
                    Add(new Shot(now, false, Grab(l + W / 2 - w / 2, t + H / 2 - h / 2, w, h, (int)(w * s), (int)(h * s), 82)));
                }
                if ((now - full).TotalMilliseconds >= 1200)
                {
                    full = now;
                    int ow = 768, oh = Math.Max(1, (int)Math.Round(768.0 * H / W));
                    Add(new Shot(now, true, Grab(l, t, W, H, ow, oh, 72)));
                }
            }
            catch { Thread.Sleep(500); }
        }
    }

    private void Add(Shot s)
    {
        lock (_ring)
        {
            _ring.Add(s);
            var old = DateTime.UtcNow.AddSeconds(-60);
            _ring.RemoveAll(x => x.At < old);
        }
    }

    /// <summary>
    /// The pictures of one firefight, in time order: crosshair strips spread evenly over it and a few whole pictures,
    /// the last one always among them (it shows a kill cam, if the player died). Their times are seconds after fromUtc
    /// (not after the first picture, which is only taken at the first shot), so a kill seen in one of them can be put
    /// at the right place in the clip.
    /// </summary>
    public List<KillJudge.Frame> Take(DateTime fromUtc, DateTime toUtc, int maxCrops = 14, int maxFulls = 4)
    {
        List<Shot> all;
        lock (_ring) all = _ring.Where(x => x.At >= fromUtc && x.At <= toUtc).ToList();
        if (all.Count == 0) return new();
        var t0 = fromUtc;
        var pick = Spread(all.Where(x => !x.Full).ToList(), maxCrops).Concat(Spread(all.Where(x => x.Full).ToList(), maxFulls));
        return pick.OrderBy(x => x.At).ThenBy(x => x.Full).Select(x => new KillJudge.Frame(Math.Round((x.At - t0).TotalSeconds, 1), x.Full, x.Jpeg)).ToList();
    }

    private static IEnumerable<Shot> Spread(List<Shot> list, int max)
    {
        if (list.Count <= max) return list;
        var res = new List<Shot>();
        for (int i = 0; i < max; i++) res.Add(list[(int)Math.Round(i * (list.Count - 1.0) / (max - 1))]);
        return res.Distinct();
    }

    /// <summary>Copies a part of the screen, made smaller if asked, as a JPEG.</summary>
    private static byte[] Grab(int x, int y, int w, int h, int ow, int oh, int quality)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = ow, biHeight = -oh, biPlanes = 1, biBitCount = 32 };
        var bmp = CreateDIBSection(mem, ref bi, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(mem, bmp);
        try
        {
            SetStretchBltMode(mem, ow == w ? 3 : 4);   // COLORONCOLOR for a plain copy, HALFTONE when it is made smaller
            StretchBlt(mem, 0, 0, ow, oh, screen, x, y, w, h, 0x00CC0020);
            var px = new byte[ow * oh * 4];
            Marshal.Copy(bits, px, 0, px.Length);
            return Jpeg(px, ow, oh, quality);
        }
        finally { SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
    }

    public static byte[] Jpeg(byte[] bgra, int w, int h, int quality)
    {
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgra, w * 4);
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream(); enc.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>The OpenRouter key for the AI check: kept in its own file, encrypted for this Windows user, never in the settings file.</summary>
internal static class AiKey
{
    private static string File => Path.Combine(AppDirs.Local, "ai.key");
    private static string? _key; private static bool _read;

    public static string? Get()
    {
        if (_read) return _key;
        _read = true;
        try
        {
            if (System.IO.File.Exists(File))
                _key = Encoding.UTF8.GetString(ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), null, DataProtectionScope.CurrentUser)).Trim();
        }
        catch { _key = null; }
        if (string.IsNullOrWhiteSpace(_key)) _key = null;
        return _key;
    }

    public static void Set(string? key)
    {
        key = key?.Trim();
        _read = true; _key = string.IsNullOrEmpty(key) ? null : key;
        try
        {
            if (_key == null) { if (System.IO.File.Exists(File)) System.IO.File.Delete(File); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllBytes(File, ProtectedData.Protect(Encoding.UTF8.GetBytes(_key), null, DataProtectionScope.CurrentUser));
        }
        catch { }
    }
}
