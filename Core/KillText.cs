using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace OrbitTweak.Core;

/// <summary>
/// Kill detection by the kill message that PvP servers show right at the crosshair ("KILLED name", "+3 Streak",
/// "Eliminated …"). FiveM does not show GTA Online's red X on most servers, the message is what the player sees.
/// The strip around the middle of the picture is read with the Windows text recognition and searched for kill words.
/// This file has no dependencies on the rest of the app, so the same code can be tried on recorded clips.
/// </summary>
internal static class KillText
{
    /// <summary>Words that mean "you killed somebody" (the user can change the list).</summary>
    public const string DefaultWords = "KILLED, KILL, GETÖTET, ELIMINATED, ELIMINIERT, ERLEDIGT";
    /// <summary>Part of the game picture that is read, as fractions of the picture height around the centre (±324 × ±162 px at 1080p).</summary>
    public const double HalfW = 0.30, HalfH = 0.15;

    public enum Kind { None, Kill, Death }

    // "… by / von …" turns a kill word into the opposite; these also mark a death on their own
    private static readonly string[] DeathPhrases =
    {
        "KILLEDBY", "GETOTETVON", "ELIMINATEDBY", "ELIMINIERTVON", "ERLEDIGTVON", "YOUDIED", "YOUWEREKILLED", "YOUAREDEAD",
        "DUWURDEST", "DUBISTTOT", "DUBISTGESTORBEN", "WASTED"
    };

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

    /// <summary>Upper case, umlauts folded (the English recognizer has none), everything but letters and digits removed.</summary>
    public static string Fold(string s, bool keepSpaces = false)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s.ToUpperInvariant())
        {
            switch (ch)
            {
                case 'Ä': sb.Append('A'); break;
                case 'Ö': sb.Append('O'); break;
                case 'Ü': sb.Append('U'); break;
                case 'ẞ': case 'ß': sb.Append("SS"); break;
                default:
                    if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                    else if (keepSpaces && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                    break;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Text recognition is allowed one wrong letter in words of six or more letters ("KILLEO", "K1LLED").</summary>
    private static bool Has(string hay, string needle)
    {
        if (needle.Length == 0) return false;
        if (hay.Contains(needle, StringComparison.Ordinal)) return true;
        if (needle.Length < 6 || hay.Length < needle.Length) return false;
        for (int i = 0; i + needle.Length <= hay.Length; i++)
        {
            int diff = 0;
            for (int k = 0; k < needle.Length && diff <= 1; k++) if (hay[i + k] != needle[k]) diff++;
            if (diff <= 1) return true;
        }
        return false;
    }

    public static bool IsDeath(string line)
    {
        var t = Fold(line);
        foreach (var p in DeathPhrases) if (Has(t, p)) return true;
        return false;
    }

    /// <summary>
    /// Looks through recognised lines: a death phrase wins, otherwise the first line with one of the kill words.
    /// Returns the line as it was read and the kill word it matched.
    /// </summary>
    public static (Kind kind, string text, string word) Classify(IEnumerable<string> lines, string? words)
    {
        var list = (string.IsNullOrWhiteSpace(words) ? DefaultWords : words)
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Fold(x)).Where(x => x.Length >= 3).Distinct().ToList();
        (Kind, string, string) kill = (Kind.None, "", "");
        foreach (var line in lines)
        {
            if (IsDeath(line)) return (Kind.Death, line.Trim(), "");
            if (kill.Item1 != Kind.None) continue;
            var t = Fold(line); var tokens = Fold(line, true).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in list)
            {
                // long words may be glued to the name by the recognition ("KILLEDZM"), short ones have to stand alone
                bool hit = w.Length >= 6 ? Has(t, w) : tokens.Contains(w);
                if (hit) { kill = (Kind.Kill, line.Trim(), w); break; }
            }
        }
        return kill;
    }
}
