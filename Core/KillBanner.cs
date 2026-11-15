using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitTweak.Core;

/// <summary>
/// The small note shown at the top of the game picture when a kill was noticed: "KILL" with the number of kills in
/// this round. Drawn here (not by the page), so it is there at once, whatever the window of the app is doing.
/// </summary>
internal static class KillBanner
{
    public const int W = 270, H = 62;

    /// <summary>The small counter that stays in a corner of the game picture: a red dot while the recording runs, and the kills of the round.</summary>
    public static byte[] Hud(int kills, bool rec, string? accentHex) => Hud(kills, rec, accentHex, Array.Empty<string>());

    /// <summary>The counter with further small notes behind the kills (time of day, playing time, "LAG" …).</summary>
    public static byte[] Hud(int kills, bool rec, string? accentHex, IReadOnlyList<string> extra)
    {
        var basePng = HudCore(kills, rec, accentHex, out int bw);
        if (extra.Count == 0) return basePng;
        var plain = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var texts = extra.Select(s => new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, plain, 11.5, new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8)), 1.0)).ToList();
        double w = bw + texts.Sum(t => 6 + 14 + t.Width + 14); int iw = (int)Math.Ceiling(w), ih = 28;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(basePng); bi.EndInit();
            dc.DrawImage(bi, new Rect(0, 0, bw, ih));
            double x = bw;
            foreach (var t in texts)
            {
                x += 6;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(205, 10, 10, 12)), null, new Rect(x, 0, t.Width + 28, ih), 14, 14);
                dc.DrawText(t, new Point(x + 14, (ih - t.Height) / 2));
                x += t.Width + 28;
            }
        }
        var rtb = new RenderTargetBitmap(iw, ih, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] HudCore(int kills, bool rec, string? accentHex, out int width)
    {
        Color accent;
        try { accent = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(accentHex) ? "#FF7A3D" : accentHex); }
        catch { accent = Color.FromRgb(0xFF, 0x7A, 0x3D); }
        var heavy = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        FormattedText Text(string s, double size, Brush b) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, heavy, size, b, 1.0);
        var recText = Text(rec ? "REC" : "AUS", 11, new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8)));
        var num = Text(kills.ToString(CultureInfo.InvariantCulture), 14, Brushes.White);
        var label = Text(kills == 1 ? "KILL" : "KILLS", 11, new SolidColorBrush(accent));
        double w = 12 + 8 + 6 + recText.Width + 10 + 1 + 10 + num.Width + 5 + label.Width + 12; int iw = (int)Math.Ceiling(w), ih = 28;
        width = iw;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(205, 10, 10, 12)), null, new Rect(0, 0, iw, ih), 14, 14);
            double x = 12;
            dc.DrawEllipse(new SolidColorBrush(rec ? Color.FromRgb(0xFF, 0x3B, 0x3B) : Color.FromRgb(0x71, 0x71, 0x7A)), null, new Point(x + 4, ih / 2.0), 4, 4);
            x += 8 + 6;
            dc.DrawText(recText, new Point(x, (ih - recText.Height) / 2)); x += recText.Width + 10;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), null, new Rect(x, 7, 1, ih - 14)); x += 1 + 10;
            dc.DrawText(num, new Point(x, (ih - num.Height) / 2)); x += num.Width + 5;
            dc.DrawText(label, new Point(x, (ih - label.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(iw, ih, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>kills = kills in this round; inClip = how many of them go into the clip that is being saved.</summary>
    public static byte[] Png(int kills, int inClip, string? accentHex)
    {
        Color accent;
        try { accent = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(accentHex) ? "#FF7A3D" : accentHex); }
        catch { accent = Color.FromRgb(0xFF, 0x7A, 0x3D); }
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(232, 10, 10, 12)), null, new Rect(0, 0, W, H), 13, 13);
            dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(0, 11, 4, H - 22), 2, 2);
            var heavy = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
            var plain = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            FormattedText Text(string s, Typeface tf, double size, Brush b) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, b, 1.0);
            var kill = Text("KILL", heavy, 25, new SolidColorBrush(accent));
            dc.DrawText(kill, new Point(20, 5));
            dc.DrawText(Text(kills.ToString(CultureInfo.InvariantCulture), heavy, 25, Brushes.White), new Point(20 + kill.Width + 10, 5));
            var sub = inClip > 1 ? $"{inClip} Kills in diesem Clip" : "Clip wird gespeichert";
            dc.DrawText(Text(sub, plain, 11.5, new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA))), new Point(21, 38));
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
