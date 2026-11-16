using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace OrbitTweak.Core;

/// <summary>
/// Kill detection by the kill message that PvP servers show right at the crosshair ("KILLED name", "+3 Streak",
/// "Eliminated …"). FiveM does not show GTA Online's red X on most servers, the message is what the player sees.
/// The strip around the middle of the picture is read with the Windows text recognition and searched for kill words.
/// This file has no dependencies on the rest of the app, so the same code can be tried on recorded clips; which
/// text counts as a kill or a death is decided in KillLogic.cs (no Windows there, so it is checked by tests/ClipLogic.Tests).
/// </summary>
internal static partial class KillText
{
    /// <summary>Part of the game picture that is read, as fractions of the picture height around the centre (±324 × ±162 px at 1080p).</summary>
    public const double HalfW = 0.30, HalfH = 0.15;

    private static OcrEngine? _ocr; private static bool _tried;
    public static OcrEngine? Engine()
    {
        if (_tried) return _ocr;
        _tried = true;
        try
        {
            _ocr = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
                ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("de-DE"));
        }
        catch { _ocr = null; }
        return _ocr;
    }

    /// <summary>How much the copied strip is enlarged before reading: small HUD text needs about 24 px letter height.</summary>
    public static double ScaleFor(int pictureHeight) => Math.Clamp(2160.0 / Math.Max(1, pictureHeight), 1, 3);

    /// <summary>Enlarges a BGRA picture (bilinear).</summary>
    public static byte[] Scale(byte[] src, int w, int h, double s, out int ow, out int oh)
    {
        ow = Math.Max(1, (int)Math.Round(w * s)); oh = Math.Max(1, (int)Math.Round(h * s));
        if (ow == w && oh == h) return src;
        var dst = new byte[ow * oh * 4];
        for (int y = 0; y < oh; y++)
        {
            double fy = (y + 0.5) / s - 0.5; int y0 = (int)Math.Floor(fy); double ty = fy - y0;
            int ya = Math.Clamp(y0, 0, h - 1), yb = Math.Clamp(y0 + 1, 0, h - 1);
            for (int x = 0; x < ow; x++)
            {
                double fx = (x + 0.5) / s - 0.5; int x0 = (int)Math.Floor(fx); double tx = fx - x0;
                int xa = Math.Clamp(x0, 0, w - 1), xb = Math.Clamp(x0 + 1, 0, w - 1);
                int i00 = (ya * w + xa) * 4, i01 = (ya * w + xb) * 4, i10 = (yb * w + xa) * 4, i11 = (yb * w + xb) * 4, o = (y * ow + x) * 4;
                for (int c = 0; c < 3; c++)
                    dst[o + c] = (byte)((src[i00 + c] * (1 - tx) + src[i01 + c] * tx) * (1 - ty) + (src[i10 + c] * (1 - tx) + src[i11 + c] * tx) * ty + 0.5);
                dst[o + 3] = 255;
            }
        }
        return dst;
    }

    /// <summary>
    /// Keeps only the (nearly) white parts of a picture, as dark letters on white. Kill messages are mostly white text
    /// laid over the game picture; without the busy picture behind it the text recognition finds the letters far more often.
    /// </summary>
    public static byte[] WhiteOnly(byte[] src, int min = 205)
    {
        var dst = new byte[src.Length];
        for (int i = 0; i < src.Length; i += 4)
        {
            int lo = Math.Min(src[i], Math.Min(src[i + 1], src[i + 2]));
            // soft edge: fully dark from "min" upwards, fading out over the 30 steps below it
            byte v = (byte)(255 - Math.Clamp((lo - (min - 30)) * 255 / 30, 0, 255));
            dst[i] = dst[i + 1] = dst[i + 2] = v; dst[i + 3] = 255;
        }
        return dst;
    }

    /// <summary>Reads the text lines of a BGRA picture.</summary>
    public static async Task<List<string>> Read(byte[] bgra, int w, int h)
    {
        var eng = Engine();
        if (eng == null) return new List<string>();
        using var sb = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        var res = await eng.RecognizeAsync(sb);
        return res.Lines.Select(l => l.Text).ToList();
    }
}
