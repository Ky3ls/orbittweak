using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitTweak.Core;

/// <summary>
/// The pictures of a montage (the newest kill clips cut together with effects): title card, end card, the dark frame
/// that lies over the whole video, and the "KILL 3 / 8" note of every clip. Drawn here in the app; the helper process
/// that writes the video only gets the finished files.
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
