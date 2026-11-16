using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitTweak.Core;

/// <summary>
/// The pictures of a montage, drawn here in the app; whatever cuts the video only gets the finished files.
/// Card, Vignette and Note belong to the montage without ffmpeg (Highlight.MakeMontage); Title, Shade, Chip and Tag to
/// the one cut by ffmpeg (FfMontage / MontageCut), drawn at the size of that video.
/// </summary>
internal static class MontageArt
{
    public const int W = 1920, H = 1080;

    private static Color Accent(string? hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? "#FF7A3D" : hex); }
        catch { return Color.FromRgb(0xFF, 0x7A, 0x3D); }
    }

    private static FormattedText Text(string s, double size, Brush b, FontWeight w, string family = "Segoe UI")
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily(family), FontStyles.Normal, w, FontStretches.Normal), size, b, 1.0);

    private static void Save(DrawingVisual dv, int w, int h, string file)
    {
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    /// <summary>A full card: dark, a glow in the accent colour, a big line and a small one below it.</summary>
    public static void Card(string file, string big, string small, string? accentHex)
    {
        var a = Accent(accentHex);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 8, 10)), null, new Rect(0, 0, W, H));
            var glow = new RadialGradientBrush(Color.FromArgb(120, a.R, a.G, a.B), Color.FromArgb(0, a.R, a.G, a.B)) { RadiusX = 0.55, RadiusY = 0.7, Center = new Point(0.5, 0.52), GradientOrigin = new Point(0.5, 0.52) };
            dc.DrawRectangle(glow, null, new Rect(0, 0, W, H));
            // two orbit rings, like the app's mark
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(70, a.R, a.G, a.B)), 2), new Point(W / 2.0, H / 2.0), 520, 520);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.5) { DashStyle = new DashStyle(new double[] { 6, 10 }, 0) }, new Point(W / 2.0, H / 2.0), 700, 700);
            var t = Text(big, 150, Brushes.White, FontWeights.Black);
            dc.DrawText(t, new Point((W - t.Width) / 2, H / 2.0 - t.Height / 2 - 40));
            dc.DrawRoundedRectangle(new SolidColorBrush(a), null, new Rect(W / 2.0 - 90, H / 2.0 + 62, 180, 8), 4, 4);
            var s = Text(small, 44, new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8)), FontWeights.SemiBold);
            dc.DrawText(s, new Point((W - s.Width) / 2, H / 2.0 + 100));
        }
        Save(dv, W, H, file);
    }

    /// <summary>The frame over the whole video: dark corners and thin cinema bars, clear in the middle.</summary>
    public static void Vignette(string file)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var v = new RadialGradientBrush { RadiusX = 0.78, RadiusY = 0.82, Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
            v.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
            v.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 1));
            dc.DrawRectangle(v, null, new Rect(0, 0, W, H));
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, W, 54));
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, H - 54, W, 54));
        }
        Save(dv, W, H, file);
    }

    // ---------- the montage cut by ffmpeg (MontageCut): drawn at the size of the video, transparent around ----------

    private static readonly FontFamily Ui = new("Segoe UI");
    private static Typeface Face(FontWeight w) => new(Ui, FontStyles.Normal, w, FontStretches.Normal);
    private static FormattedText T(string s, double size, Brush b, FontWeight w) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face(w), size, b, 1.0);
    private static Brush B(byte a, Color c) { var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze(); return b; }
    private static readonly Color Ink = Color.FromRgb(10, 10, 12), Mist = Color.FromRgb(0xA1, 0xA1, 0xAA);

    /// <summary>Width of a text drawn letter by letter with extra space between the letters.</summary>
    private static double Tracked(DrawingContext? dc, string s, double size, Brush b, FontWeight w, double tracking, double x, double y)
    {
        double at = x;
        foreach (var ch in s)
        {
            var t = T(ch.ToString(), size, b, w);
            dc?.DrawText(t, new Point(at, y));
            at += t.WidthIncludingTrailingWhitespace + tracking;
        }
        return Math.Max(0, at - x - tracking);
    }

    private static void SaveSized(DrawingVisual dv, int w, int h, string file) => Save(dv, Math.Max(2, w), Math.Max(2, h), file);

    /// <summary>
    /// The title of the intro or outro, laid over the blurred clip: a small line in the accent colour, the big
    /// title, an accent bar and a line below. Transparent apart from a soft dark glow that keeps the text readable.
    /// </summary>
    public static void Title(string file, int w, int h, string kicker, string big, string small, string? accentHex)
    {
        var a = Accent(accentHex); double u = h / 1080.0;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var shade = new RadialGradientBrush { RadiusX = 0.42, RadiusY = 0.5, Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
            dc.DrawRectangle(shade, null, new Rect(0, 0, w, h));
            // the big line, made smaller until it fits
            double size = 150 * u; FormattedText t;
            while ((t = T(big, size, Brushes.White, FontWeights.Black)).Width > w * 0.84 && size > 40 * u) size *= 0.92;
            double ty = h / 2.0 - t.Height / 2 - 18 * u;
            if (kicker.Length > 0)
            {
                double ks = 30 * u, kw = Tracked(null, kicker, ks, Brushes.White, FontWeights.Bold, 7 * u, 0, 0);
                Tracked(dc, kicker, ks, B(255, a), FontWeights.Bold, 7 * u, (w - kw) / 2, ty - 34 * u);
            }
            dc.DrawText(t, new Point((w - t.Width) / 2, ty));
            double by = ty + t.Height + 6 * u;
            dc.DrawRoundedRectangle(B(255, a), null, new Rect(w / 2.0 - 60 * u, by, 120 * u, 7 * u), 3.5 * u, 3.5 * u);
            if (small.Length > 0)
            {
                var s = T(small, 40 * u, B(255, Color.FromRgb(0xE4, 0xE4, 0xE7)), FontWeights.SemiBold);
                dc.DrawText(s, new Point((w - s.Width) / 2, by + 26 * u));
            }
        }
        Save(dv, w, h, file);
    }

    /// <summary>The dark corners laid over every clip of the montage: clear in the middle, darker towards the edges.</summary>
    public static void Shade(string file, int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var v = new RadialGradientBrush { RadiusX = 0.71, RadiusY = 0.71, Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
            v.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
            v.GradientStops.Add(new GradientStop(Color.FromArgb(55, 0, 0, 0), 0.8));
            v.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 1));
            dc.DrawRectangle(v, null, new Rect(0, 0, w, h));
        }
        Save(dv, w, h, file);
    }

    /// <summary>
    /// The kill counter like a kill feed entry: a dark chip with an accent edge, a crosshair, "KILL 3" and "/ 8".
    /// h = height of the video (the chip is sized from it).
    /// </summary>
    public static void Chip(string file, int h, int number, int total, string? accentHex)
    {
        var a = Accent(accentHex); double u = h / 1080.0;
        var kill = T("KILL", 25 * u, B(255, a), FontWeights.Black);
        var num = T(number.ToString(CultureInfo.InvariantCulture), 31 * u, Brushes.White, FontWeights.Black);
        var of = T("/ " + total.ToString(CultureInfo.InvariantCulture), 21 * u, B(255, Mist), FontWeights.SemiBold);
        double ch = 58 * u, icon = 26 * u, x0 = 22 * u;
        double cw = x0 + icon + 14 * u + kill.Width + 10 * u + num.Width + 9 * u + of.Width + 22 * u;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var box = new Rect(0.5, 0.5, cw - 1, ch - 1); double r = 10 * u;
            dc.DrawRoundedRectangle(B(205, Ink), new Pen(B(26, Colors.White), Math.Max(1, u)), box, r, r);
            dc.PushClip(new RectangleGeometry(box, r, r));
            dc.DrawRectangle(B(255, a), null, new Rect(0, 0, 5 * u, ch));
            dc.Pop();
            // crosshair
            var pen = new Pen(B(255, a), 2.6 * u) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var c = new Point(x0 + icon / 2, ch / 2); double rr = icon * 0.36;
            dc.DrawEllipse(null, pen, c, rr, rr);
            dc.DrawLine(pen, new Point(c.X, c.Y - icon / 2), new Point(c.X, c.Y - rr * 0.45));
            dc.DrawLine(pen, new Point(c.X, c.Y + rr * 0.45), new Point(c.X, c.Y + icon / 2));
            dc.DrawLine(pen, new Point(c.X - icon / 2, c.Y), new Point(c.X - rr * 0.45, c.Y));
            dc.DrawLine(pen, new Point(c.X + rr * 0.45, c.Y), new Point(c.X + icon / 2, c.Y));
            double x = x0 + icon + 14 * u, base0 = ch / 2 + num.Baseline - num.Height / 2;
            dc.DrawText(kill, new Point(x, base0 - kill.Baseline)); x += kill.Width + 10 * u;
            dc.DrawText(num, new Point(x, base0 - num.Baseline)); x += num.Width + 9 * u;
            dc.DrawText(of, new Point(x, base0 - of.Baseline));
        }
        SaveSized(dv, (int)Math.Ceiling(cw), (int)Math.Ceiling(ch), file);
    }

    /// <summary>"DOUBLE KILL" and the like: a pill in the accent colour, wide letters.</summary>
    public static void Tag(string file, int h, string text, string? accentHex)
    {
        var a = Accent(accentHex); double u = h / 1080.0;
        // dark letters on a light accent, white ones on a dark accent
        bool light = (0.2126 * a.R + 0.7152 * a.G + 0.0722 * a.B) / 255 > 0.55;
        var ink = light ? B(255, Ink) : Brushes.White;
        double size = 32 * u, tr = 4 * u, tw = Tracked(null, text, size, ink, FontWeights.Black, tr, 0, 0);
        var probe = T(text, size, ink, FontWeights.Black);
        double ph = 60 * u, pw = tw + 2 * 34 * u;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRoundedRectangle(B(242, a), null, new Rect(0, 0, pw, ph), ph / 2, ph / 2);
            Tracked(dc, text, size, ink, FontWeights.Black, tr, 34 * u, (ph - probe.Height) / 2);
        }
        SaveSized(dv, (int)Math.Ceiling(pw), (int)Math.Ceiling(ph), file);
    }

    public const int NoteW = 420, NoteH = 96;
    /// <summary>The note of one clip: "KILL 3 / 8".</summary>
    public static void Note(string file, int index, int count, string? accentHex)
    {
        var a = Accent(accentHex);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(225, 10, 10, 12)), null, new Rect(0, 0, NoteW, NoteH), 22, 22);
            dc.DrawRoundedRectangle(new SolidColorBrush(a), null, new Rect(0, 20, 8, NoteH - 40), 4, 4);
            var k = Text("KILL", 50, new SolidColorBrush(a), FontWeights.Black);
            dc.DrawText(k, new Point(34, (NoteH - k.Height) / 2));
            var n = Text(index.ToString(CultureInfo.InvariantCulture), 50, Brushes.White, FontWeights.Black);
            dc.DrawText(n, new Point(34 + k.Width + 18, (NoteH - n.Height) / 2));
            var of = Text("/ " + count.ToString(CultureInfo.InvariantCulture), 30, new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)), FontWeights.SemiBold);
            dc.DrawText(of, new Point(34 + k.Width + 18 + n.Width + 14, (NoteH - of.Height) / 2 + 6));
        }
        Save(dv, NoteW, NoteH, file);
    }
}
