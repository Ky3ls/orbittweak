using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace OrbitTweak.Core;

/// <summary>
/// Kill detection without touching the game: a small part of the screen (where the server shows its kill feed)
/// is copied like a screen recorder would do and read with the Windows text recognition (OCR).
/// A new feed line with the player's own name on the left = kill, on the right = death.
/// </summary>
internal sealed class KillFeed
{
    // ---------- GDI capture ----------
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

    /// <summary>Kill feed areas as fractions of the game picture (x, y, w, h).</summary>
    public static readonly Dictionary<string, (double x, double y, double w, double h)> Areas = new()
    {
        ["tr"] = (0.66, 0.02, 0.33, 0.30),  // top right (most PvP servers)
        ["tl"] = (0.01, 0.02, 0.33, 0.30),  // top left
        ["r"] = (0.66, 0.22, 0.33, 0.36),   // right middle
        ["tc"] = (0.30, 0.02, 0.40, 0.22),  // top center
    };

    public event Action<bool>? Hit; // true = kill, false = death
    public volatile bool Deaths;
    public string Name = "";
    public string Area = "tr";
    private volatile bool _on;
    private Task? _loop;
    private readonly Dictionary<string, DateTime> _seen = new();
    private OcrEngine? _ocr;

    public void Enable(bool on)
    {
        _on = on;
        if (on && (_loop == null || _loop.IsCompleted)) _loop = Task.Run(Loop);
    }

    private OcrEngine? Engine()
    {
        if (_ocr != null) return _ocr;
        try
        {
            _ocr = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
                ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("de-DE"));
        }
        catch { _ocr = null; }
        return _ocr;
    }

    private async Task Loop()
    {
        bool primed = false;
        while (_on)
        {
            await Task.Delay(300);
            if (!AutoClip.GameInFront || Name.Trim().Length < 3) { primed = false; continue; }
            try
            {
                var lines = await Read(false);
                var now = DateTime.UtcNow;
                foreach (var k in _seen.Where(p => (now - p.Value).TotalSeconds > 12).Select(p => p.Key).ToList()) _seen.Remove(k);
                foreach (var l in lines)
                {
                    var kind = Classify(l, Name);
                    if (kind == null) continue;
                    var key = Norm(l.text);
                    bool fresh = !_seen.Keys.Any(s => Similar(s, key));
                    _seen[key] = now;
                    // the first read after entering the game only learns what is already on screen
                    if (!fresh || !primed) continue;
                    if (kind == true || Deaths) Hit?.Invoke(kind.Value);
                }
                primed = true;
            }
            catch { await Task.Delay(1500); }
        }
    }

    public record Line(string text, double rel);

    /// <summary>Copies the kill-feed area and reads it. rel = horizontal centre of the name inside the line (0 = left, 1 = right).</summary>
    public async Task<List<Line>> Read(bool keepImage)
    {
        var eng = Engine() ?? throw new InvalidOperationException("Windows-Texterkennung nicht verfügbar. Installiere unter Windows-Einstellungen → Zeit und Sprache → Sprache ein Sprachpaket mit „Optische Zeichenerkennung“.");
        var (w, h, px) = Capture();
        LastImage = keepImage ? Png(px, w, h) : null;
        using var sb = SoftwareBitmap.CreateCopyFromBuffer(px.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        var res = await eng.RecognizeAsync(sb);
        var n = Norm(Name);
        var list = new List<Line>();
        foreach (var line in res.Lines)
        {
            double rel = -1;
            if (n.Length >= 3 && line.Words.Count > 0)
            {
                double minX = line.Words.Min(x => x.BoundingRect.X), maxX = line.Words.Max(x => x.BoundingRect.X + x.BoundingRect.Width);
                for (int i = 0; i < line.Words.Count && rel < 0; i++)
                {
                    var acc = "";
                    for (int j = i; j < Math.Min(line.Words.Count, i + 3); j++)
                    {
                        acc += Norm(line.Words[j].Text);
                        if (Matches(acc, n))
                        {
                            var a = line.Words[i].BoundingRect; var b = line.Words[j].BoundingRect;
                            rel = maxX - minX < 1 ? 0.5 : ((a.X + b.X + b.Width) / 2 - minX) / (maxX - minX);
                            break;
                        }
                    }
                }
            }
            list.Add(new Line(line.Text, rel));
        }
        return list;
    }

    public string? LastImage { get; private set; }

    /// <summary>true = kill, false = death, null = not about the player.</summary>
    public static bool? Classify(Line l, string name)
    {
        if (l.rel < 0) return null;
        var n = Norm(name); var t = Norm(l.text);
        if (t.Length < n.Length + 3) return null; // only the name (HUD, chat header) – no feed entry
        // text feeds like "X was killed by Y" / "X wurde von Y getötet" name the killer on the right
        bool inverted = t.Contains("killedby") || t.Contains("wurdevon") || t.Contains("getötetvon") || t.Contains("getoetetvon") || t.Contains("eliminatedby");
        return inverted ? l.rel >= 0.5 : l.rel < 0.5;
    }

    private (int w, int h, byte[] px) Capture()
    {
        var (cl, ct, cr, cb) = GameFocus.Client();
        var a = Areas.TryGetValue(Area, out var v) ? v : Areas["tr"];
        int sx = cl + (int)((cr - cl) * a.x), sy = ct + (int)((cb - ct) * a.y);
        int sw = Math.Max(40, (int)((cr - cl) * a.w)), sh = Math.Max(30, (int)((cb - ct) * a.h));
        double scale = sh < 500 ? 2 : 1;
        int w = (int)(sw * scale), h = (int)(sh * scale);
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bmp = CreateDIBSection(mem, ref bi, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(mem, bmp);
        try
        {
            SetStretchBltMode(mem, 4); // HALFTONE
            StretchBlt(mem, 0, 0, w, h, screen, sx, sy, sw, sh, 0x00CC0020); // SRCCOPY
            var px = new byte[w * h * 4];
            Marshal.Copy(bits, px, 0, px.Length);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            return (w, h, px);
        }
        finally
        {
            SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static string Png(byte[] px, int w, int h)
    {
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
        using var ms = new MemoryStream(); enc.Save(ms);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    public static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>OCR-tolerant match: exact, or one wrong letter for names with 6+ characters.</summary>
    private static bool Matches(string text, string n)
    {
        if (text.Contains(n)) return true;
        if (n.Length < 6 || text.Length < n.Length) return false;
        for (int i = 0; i + n.Length <= text.Length; i++)
        {
            int diff = 0;
            for (int k = 0; k < n.Length && diff <= 1; k++) if (text[i + k] != n[k]) diff++;
            if (diff <= 1) return true;
        }
        return false;
    }

    private static bool Similar(string a, string b)
    {
        if (a == b) return true;
        if (Math.Abs(a.Length - b.Length) > 2) return false;
        int same = 0, len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++) if (a[i] == b[i]) same++;
        return len > 0 && same >= len * 0.85;
    }
}
