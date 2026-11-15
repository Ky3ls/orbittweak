using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitTweak.Core;

/// <summary>
/// Kill detection without a kill feed, by what the game shows at the crosshair when the player kills someone:
/// the kill message of the server ("KILLED name", "+3 Streak" …, read by <see cref="KillText"/>) or GTA Online's small red X.
/// The middle of the game picture is copied from the screen (like a screenshot, nothing in the game is read):
/// the message strip five times a second for a few seconds after shots, the X square about 30 times a second around shots.
/// </summary>
internal sealed class KillMark
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr sec, uint off);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    /// <summary>A kill was noticed; the text says how ("Kill-Meldung „KILLED“", "rotes X am Fadenkreuz").</summary>
    public event Action<string>? Kill;
    /// <summary>The player has to have fired shortly before. Off only for tests.</summary>
    public volatile bool NeedShot = true;
    /// <summary>Kill words the message strip is searched for (comma separated).</summary>
    public volatile string Words = KillText.DefaultWords;
    public DateTime LastAt { get; private set; }
    /// <summary>What the detector saw when it last fired (enlarged), for the settings page.</summary>
    public string? LastImage { get; private set; }
    /// <summary>The kill word of the message that was read last (null if the last kill was a red X).</summary>
    public string? LastWord { get; private set; }
    public int Count { get; private set; }
    /// <summary>How long after a shot the message strip is still read. The message appears a moment after the kill and stays 1 to 3 s.</summary>
    private const double TextWindow = 5;
    private volatile bool _on;
    private Thread? _t;

    public void Enable(bool on)
    {
        _on = on;
        if (on && (_t == null || !_t.IsAlive))
        {
            _t = new Thread(Loop) { IsBackground = true, Name = "OrbitKillMark", Priority = ThreadPriority.BelowNormal };
            _t.Start();
        }
    }

    /// <summary>
    /// True if the square (BGRA, n × n pixels, crosshair in the middle) shows a red X of any size, from a few pixels
    /// up to the whole square: red on the four diagonals around the middle, hardly any straight above/below/beside it.
    /// A red dot (aiming at an enemy), a red "+", a red line, a red car or a red light do not pass.
    /// </summary>
    /// <summary>Clearly red (GTA's kill mark is about 224/50/50), not orange, pink or yellow.</summary>
    private static bool Red(int r, int g, int b) => r >= 150 && g * 2 <= r && b * 2 <= r;

    public static bool IsRedX(byte[] px, int n)
    {
        int c = n / 2, red = 0, inside = 0;
        double sx = 0, sy = 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = (y * n + x) * 4;
                if (!Red(px[i + 2], px[i + 1], px[i])) continue;
                red++;
                int dx = x - c, dy = y - c;
                if (dx * dx + dy * dy > c * c) continue;
                inside++; sx += dx; sy += dy;
            }
        if (inside < 5 || red > n * n * 0.3) return false;            // nothing there, or something big and red fills the middle
        // everything is measured from the middle of the red itself, so a mark that sits a pixel beside the exact
        // picture centre is still read correctly
        double mx = sx / inside, my = sy / inside;
        Span<int> hist = stackalloc int[2 * c + 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = (y * n + x) * 4;
                if (!Red(px[i + 2], px[i + 1], px[i])) continue;
                double dx = x - c - mx, dy = y - c - my;
                if ((x - c) * (x - c) + (y - c) * (y - c) > c * c) continue;
                hist[(int)Math.Sqrt(dx * dx + dy * dy)]++;
            }
        // how far the red reaches (90 % of it lies within this radius): the size of the mark
        int reach = 0, sum = 0;
        for (int k = 0; k < hist.Length; k++) { sum += hist[k]; reach = k; if (sum >= inside * 0.9) break; }
        if (reach < 2) return false;                                   // too small to tell an X from a dot
        // the mark sits on the crosshair
        double off = 2.0 + reach * 0.15;
        if (Math.Abs(mx) > off || Math.Abs(my) > off) return false;
        double rIn = Math.Max(1.0, reach * 0.35) - 1e-6, rOut = reach + 1.5;
        Span<int> sector = stackalloc int[8]; // 0 = right, then every 45° clockwise; odd numbers are the diagonals
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = (y * n + x) * 4;
                if (!Red(px[i + 2], px[i + 1], px[i])) continue;
                double dx = x - c - mx, dy = y - c - my, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < rIn || d > rOut) continue;
                double a = Math.Atan2(dy, dx) * 180 / Math.PI; if (a < 0) a += 360;
                sector[(int)Math.Floor((a + 22.5) / 45) % 8]++;
            }
        int diag = sector[1] + sector[3] + sector[5] + sector[7], axis = sector[0] + sector[2] + sector[4] + sector[6];
        int perArm = Math.Max(1, reach / 4), arms = 0;
        for (int k = 1; k < 8; k += 2) if (sector[k] >= perArm) arms++;
        return diag >= Math.Max(6, reach) && arms >= 3 && axis * 4 <= diag;
    }

    private void Loop()
    {
        var never = DateTime.MinValue;
        DateTime shot = never, fired = never, xStart = never, xSeen = never, xEnded = never, read = never, msgSeen = never, msgGone = never;
        bool wasX = false, done = false; int looks = 0;
        while (_on)
        {
            var now = DateTime.UtcNow;
            // look closely (30 times a second) only around shots, otherwise a glance four times a second is enough
            bool hot = !NeedShot || (now - shot).TotalSeconds < 3;
            Thread.Sleep(hot ? 33 : 50);
            if (!AutoClip.GameInFront) { wasX = false; xEnded = DateTime.UtcNow; msgSeen = DateTime.UtcNow; Thread.Sleep(200); continue; } // an X or a message that is there right when the game comes back is not new
            now = DateTime.UtcNow;
            if (FireInput.Held()) shot = now;
            // the server's kill message at the crosshair: read five times a second for a few seconds after shots
            if ((!NeedShot || (now - shot).TotalSeconds < TextWindow) && (now - read).TotalMilliseconds >= 200)
            {
                read = now;
                try
                {
                    // while a message is on screen both ways of reading are used (no gaps); while there is none, they take
                    // turns, which halves the work during a fight
                    var (kind, _, word) = ReadMessage((now - msgSeen).TotalSeconds < 1.0);
                    if (kind != KillText.Kind.Kill) msgGone = now;
                    else
                    {
                        // a kill: the message is new. One message stays for 1 to 3 s and single looks at it can fail, so
                        // a gap of up to 0.8 s does not make it a second kill (on recorded fights the longest gap inside one
                        // message was 0.6 s, the shortest between two kills 1.0 s). A text that has been there every time
                        // since reading began (some HUD label with a kill word in it) never counts.
                        bool fresh = (now - msgSeen).TotalSeconds > 0.8 && msgGone > msgSeen;
                        msgSeen = now;
                        if (fresh && (now - fired).TotalSeconds > 1.2)
                        {
                            fired = now; LastAt = DateTime.Now; Count++; LastWord = word; LastImage = null;
                            try { Kill?.Invoke($"Kill-Meldung „{word}“ am Fadenkreuz"); } catch { }
                        }
                    }
                }
                catch { }
                now = DateTime.UtcNow;
            }
            if (!hot && (now - shot).TotalSeconds >= 3 && looks++ % 5 != 0) continue;
            try
            {
                var (l, t, r, b) = GameFocus.Client();
                double scale = Math.Clamp((b - t) / 1080.0, 0.6, 3);
                int n = (int)Math.Round(26 * scale) * 2 + 1;
                var (cx, cy) = GameFocus.Center();
                var px = Grab(cx - n / 2, cy - n / 2, n, n);
                if (IsRedX(px, n))
                {
                    if (!wasX) { xStart = now; done = false; }
                    wasX = true; xSeen = now;
                    // a kill: the X is new (not there a moment ago, so not somebody's own X-shaped crosshair), has been
                    // there for two looks, is still fresh, and the player has just fired
                    double age = (now - xStart).TotalMilliseconds;
                    if (!done && age >= 25 && age < 1000 && (xStart - xEnded).TotalMilliseconds > 180
                        && (now - fired).TotalSeconds > 1.2 && (!NeedShot || (now - shot).TotalSeconds < 3))
                    {
                        done = true; fired = now; LastAt = DateTime.Now; Count++; LastWord = null;
                        try { LastImage = Png(px, n); } catch { }
                        try { Kill?.Invoke("rotes X am Fadenkreuz"); } catch { }
                    }
                }
                else { if (wasX) xEnded = xSeen; wasX = false; }
            }
            catch { Thread.Sleep(500); }
        }
    }

    private bool _whiteTurn;

    /// <summary>
    /// Copies the strip around the middle of the game picture and reads it for a kill message. There are two ways of
    /// reading (the picture as it is, and only its white text); "both" uses the second when the first finds nothing,
    /// otherwise one of them per call, in turns.
    /// </summary>
    private (KillText.Kind kind, string text, string word) ReadMessage(bool both)
    {
        var (_, t, _, b) = GameFocus.Client();
        int H = b - t;
        if (H < 300) return (KillText.Kind.None, "", "");
        int w = (int)(H * KillText.HalfW) * 2, h = (int)(H * KillText.HalfH) * 2;
        var (cx, cy) = GameFocus.Center();
        var strip = Grab(cx - w / 2, cy - h / 2, w, h);
        var big = KillText.Scale(strip, w, h, KillText.ScaleFor(H), out int ow, out int oh);
        // On recorded fights the two looks together found the message in 22 of 24 pictures, one alone in 17 to 19.
        // A message stays for 1.5 s and more, so even in turns it is seen several times.
        _whiteTurn = !_whiteTurn;
        bool whiteFirst = !both && _whiteTurn;
        var found = KillText.Classify(KillText.Read(whiteFirst ? KillText.WhiteOnly(big) : big, ow, oh).GetAwaiter().GetResult(), Words);
        if (found.kind == KillText.Kind.None && both)
            found = KillText.Classify(KillText.Read(KillText.WhiteOnly(big), ow, oh).GetAwaiter().GetResult(), Words);
        return found;
    }

    private static byte[] Grab(int x, int y, int w, int h)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bmp = CreateDIBSection(mem, ref bi, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(mem, bmp);
        try
        {
            BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020);
            var px = new byte[w * h * 4];
            Marshal.Copy(bits, px, 0, px.Length);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            return px;
        }
        finally { SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
    }

    private static string Png(byte[] px, int n)
    {
        var copy = (byte[])px.Clone();
        for (int i = 3; i < copy.Length; i += 4) copy[i] = 255;
        var src = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, copy, n * 4);
        var big = new TransformedBitmap(src, new ScaleTransform(3, 3));
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(big));
        using var ms = new MemoryStream(); enc.Save(ms);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }
}
