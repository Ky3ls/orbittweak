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

    /// <summary>A kill was noticed: how ("Kill-Meldung „KILLED“", "rotes X am Fadenkreuz") and when it happened (local time).</summary>
    public event Action<string, DateTime>? Kill;
    /// <summary>The player's own death was read at the crosshair ("KILLED BY Max", "WASTED"): the text, once per death.</summary>
    public event Action<string>? Death;
    /// <summary>Something worth a line in the clipper log (the red X turned out to be a hit marker here).</summary>
    public event Action<string>? Note;
    /// <summary>The player has to have fired shortly before. Off only for tests.</summary>
    public volatile bool NeedShot = true;
    /// <summary>Kill words the message strip is searched for (comma separated).</summary>
    public volatile string Words = KillText.DefaultWords;
    public DateTime LastAt { get; private set; }
    /// <summary>What the detector saw when it last fired (the red X enlarged, or the message strip), for the settings page.</summary>
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

    private readonly XMarks _x = new();
    private readonly KillMessages _msg = new();
    private readonly KillMerge _merge = new();
    /// <summary>The red X shows up on every hit where the player is: it is not taken as a kill there.</summary>
    public bool HitMarkers => _x.HitMarkers;

    private void Loop()
    {
        DateTime shot = DateTime.MinValue, read = DateTime.MinValue, msgSeen = DateTime.MinValue;
        int looks = 0; bool away = true, hitNoted = false, wasX = false; byte[]? lastX = null; int lastN = 0;
        while (_on)
        {
            var now = DateTime.UtcNow;
            // look closely (30 times a second) only around shots, otherwise a glance four times a second is enough
            bool hot = !NeedShot || (now - shot).TotalSeconds < 3;
            Thread.Sleep(hot ? 33 : 50);
            if (!AutoClip.GameInFront)
            {
                // an X or a message that is there right when the game comes back is not new
                away = true; _x.Reset(); _msg.Pause(DateTime.UtcNow); Thread.Sleep(200); continue;
            }
            now = DateTime.UtcNow;
            if (away) { away = false; _msg.Pause(now); }
            if (FireInput.Held()) shot = now;
            bool recent = !NeedShot || (now - shot).TotalSeconds < 3;
            // the server's kill message at the crosshair: read five times a second for a few seconds after shots
            if ((!NeedShot || (now - shot).TotalSeconds < TextWindow) && (now - read).TotalMilliseconds >= 200)
            {
                read = now;
                try
                {
                    // while there is no message the three ways of reading take turns, one reading each; while one is on
                    // screen a second way follows when the first misses it (no gaps). When a message counts as a new kill is
                    // up to KillMessages; a death message is passed on once (for "Kill + Tod" and the safety net).
                    var (kind, text, word, strip) = ReadMessage((now - msgSeen).TotalSeconds < 1.0);
                    if (kind == KillText.Kind.Kill) msgSeen = now;
                    if (_msg.Read(now, kind == KillText.Kind.Kill, kind == KillText.Kind.Death, text, word))
                        Report(now.AddSeconds(-KillLogic.LagMessage), "text", $"Kill-Meldung „{word}“ am Fadenkreuz", word, strip.px != null ? Png(strip.px, strip.w, strip.h, Math.Min(1, 240.0 / strip.w)) : null);
                    if (_msg.NewDeath) try { Death?.Invoke(text); } catch { }
                }
                catch { }
                now = DateTime.UtcNow;
            }
            if (!recent && looks++ % 5 != 0) continue;
            try
            {
                var (l, t, r, b) = GameFocus.Client();
                double scale = Math.Clamp((b - t) / 1080.0, 0.6, 3);
                int n = (int)Math.Round(26 * scale) * 2 + 1;
                var (cx, cy) = GameFocus.Center();
                var px = Grab(cx - n / 2, cy - n / 2, n, n);
                bool x = KillLogic.IsRedX(px, n);
                if (x && !wasX) { lastX = px; lastN = n; }   // what the newest X looked like, for the settings page
                wasX = x;
                // a kill mark is confirmed a moment after it is gone (hit markers come in rows); the time is when it appeared
                if (_x.Look(now, x, recent) is DateTime at)
                    Report(at.AddSeconds(-KillLogic.LagX), "x", "rotes X am Fadenkreuz", null, lastX != null ? Png(lastX, lastN, lastN, 3) : null);
                if (_x.HitMarkers && !hitNoted) { hitNoted = true; try { Note?.Invoke("Rotes X erscheint hier bei jedem Treffer: zählt nicht als Kill, die Kill-Meldung schon"); } catch { } }
            }
            catch { Thread.Sleep(500); }
        }
    }

    private void Report(DateTime atUtc, string source, string how, string? word, string? image)
    {
        if (!_merge.New(atUtc, source)) return;   // the red X and the message of the same kill
        LastAt = DateTime.Now; Count++; LastWord = word; LastImage = image;
        try { Kill?.Invoke(how, atUtc.ToLocalTime()); } catch { }
    }

    private readonly ReadTurns _turns = new();

    /// <summary>The strip around the middle of the game picture, as copied (null if the picture is too small).</summary>
    private static (byte[]? px, int w, int h, int H) Strip()
    {
        var (_, t, _, b) = GameFocus.Client();
        int H = b - t;
        if (H < 300) return (null, 0, 0, H);
        int w = (int)(H * KillText.HalfW) * 2, h = (int)(H * KillText.HalfH) * 2;
        var (cx, cy) = GameFocus.Center();
        return (Grab(cx - w / 2, cy - h / 2, w, h), w, h, H);
    }

    /// <summary>The enlarged strip prepared for one way of reading (see <see cref="ReadTurns"/>).</summary>
    private static byte[] Look(int way, byte[] big) => way switch { 1 => KillText.WhiteOnly(big), 2 => KillText.BrightOnly(big), _ => big };

    /// <summary>Names of the ways of reading, for the test on the settings page.</summary>
    private static readonly string[] WayNames = { "Bild wie es ist", "nur weiße Schrift", "nur helle und farbige Schrift" };

    /// <summary>
    /// Copies the strip around the middle of the game picture and reads it for a kill message. There are three ways of
    /// reading (the picture as it is, only its white text, only its bright white or coloured text); one per call in turns,
    /// and while a message is on screen ("both") a second one when the first finds nothing.
    /// </summary>
    private (KillText.Kind kind, string text, string word, (byte[]? px, int w, int h) strip) ReadMessage(bool both)
    {
        var (strip, w, h, H) = Strip();
        if (strip == null) return (KillText.Kind.None, "", "", default);
        var big = KillText.Scale(strip, w, h, KillText.ScaleFor(H), out int ow, out int oh);
        // On recorded fights two looks together found the message in 22 of 24 pictures, one alone in 17 to 19.
        // A message stays for 1.5 s and more, so even in turns it is seen several times.
        var (first, second) = _turns.Next(both);
        var found = KillText.Classify(KillText.Read(Look(first, big), ow, oh).GetAwaiter().GetResult(), Words);
        if (found.kind != KillText.Kind.None) _turns.Found(first);
        else if (second >= 0)
        {
            found = KillText.Classify(KillText.Read(Look(second, big), ow, oh).GetAwaiter().GetResult(), Words);
            if (found.kind != KillText.Kind.None) _turns.Found(second);
        }
        return (found.kind, found.text, found.word, (strip, w, h));
    }

    /// <summary>
    /// "Erkennung testen": copies the strip once and reads it all three ways. Returns every line that was read (once,
    /// each with what it would count as), what the detection makes of the whole, which way found it, and the strip as a picture.
    /// </summary>
    public (List<(string text, KillText.Kind hit)> lines, KillText.Kind kind, string text, string word, string how, string? image) Test()
    {
        if (KillText.Engine() == null)
            throw new InvalidOperationException("Windows-Texterkennung nicht verfügbar. Installiere unter Windows-Einstellungen → Zeit und Sprache → Sprache ein Sprachpaket mit „Optische Zeichenerkennung“.");
        var (strip, w, h, H) = Strip();
        if (strip == null) throw new InvalidOperationException("Das Spielbild ist zu klein (unter 300 Pixel hoch)");
        var big = KillText.Scale(strip, w, h, KillText.ScaleFor(H), out int ow, out int oh);
        var lines = new List<(string text, KillText.Kind hit)>();
        (KillText.Kind kind, string text, string word) found = (KillText.Kind.None, "", ""); string how = "";
        for (int way = 0; way < ReadTurns.Ways; way++)
        {
            var read = KillText.Read(Look(way, big), ow, oh).GetAwaiter().GetResult();
            foreach (var l in read.Select(x => x.Trim()).Where(x => x.Length > 0))
                if (!lines.Any(x => KillLogic.SameText(x.text, l))) lines.Add((l, KillText.Classify(new[] { l }, Words).kind));
            var f = KillText.Classify(read, Words);
            // like during a fight: a death wins, otherwise the first way that found a kill
            if (f.kind == KillText.Kind.Death && found.kind != KillText.Kind.Death || f.kind == KillText.Kind.Kill && found.kind == KillText.Kind.None) { found = f; how = WayNames[way]; }
        }
        return (lines, found.kind, found.text, found.word, how, Png(strip, w, h, Math.Min(1, 480.0 / w)));
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

    /// <summary>A copied part of the screen as a PNG for the settings page, made bigger or smaller by "scale".</summary>
    private static string Png(byte[] px, int w, int h, double scale)
    {
        var copy = (byte[])px.Clone();
        for (int i = 3; i < copy.Length; i += 4) copy[i] = 255;
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, copy, w * 4);
        var big = new TransformedBitmap(src, new ScaleTransform(scale, scale));
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(big));
        using var ms = new MemoryStream(); enc.Save(ms);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }
}
