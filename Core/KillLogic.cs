namespace OrbitTweak.Core;

/// <summary>
/// The decisions of the kill detection, without the screen and without Windows, so they can be checked on their own
/// (tests/ClipLogic.Tests): is a picture a red X, when does a red X or a kill message count as a new kill, and is a
/// text the same as another one as far as text recognition can tell.
/// </summary>
internal static class KillLogic
{
    /// <summary>How long before it was noticed a kill happened, by the way it was noticed (for where the clip puts the kill).</summary>
    public const double LagX = 0.05, LagMessage = 0.45, LagFeed = 0.6;

    /// <summary>Clearly red (GTA's kill mark is about 224/50/50), not orange, pink or yellow.</summary>
    private static bool Red(int r, int g, int b) => r >= 150 && g * 2 <= r && b * 2 <= r;

    /// <summary>
    /// True if the square (BGRA, n × n pixels, crosshair in the middle) shows a red X of any size, from a few pixels
    /// up to the whole square: red on the four diagonals around the middle, hardly any straight above/below/beside it.
    /// A red dot (aiming at an enemy), a red "+", a red line, a red car or a red light do not pass.
    /// </summary>
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

    /// <summary>Upper-case letters and digits only (what text recognition reliably gives back).</summary>
    public static string Letters(string s) => new(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Edit distance (letters inserted, removed or changed).</summary>
    public static int Distance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var row = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) row[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int diag = row[0]; row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int up = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diag + (a[i - 1] == b[j - 1] ? 0 : 1));
                diag = up;
            }
        }
        return row[b.Length];
    }

    /// <summary>
    /// The same text as far as text recognition can tell: at most one wrong letter in six, a letter more or less at
    /// the start (an icon read as "I") included. Compares letters and digits only.
    /// </summary>
    public static bool SameText(string a, string b)
    {
        string x = Letters(a), y = Letters(b);
        if (x == y) return true;
        int len = Math.Max(x.Length, y.Length);
        if (len < 4) return false;
        return Distance(x, y) <= Math.Max(1, len / 6);
    }
}

/// <summary>
/// When a red X at the crosshair is a kill. Fed with every look at the crosshair (about 30 a second around shots).
/// A kill mark is new (not there at the first look after the game came back to the front), seen on two looks or more,
/// gone again within 1.5 s, and no other red X comes right before or after it: servers that show a red X on every hit
/// give a quick row of them, and those are hits. After three such rows the X is not used as a kill sign at all on this
/// server (HitMarkers). A look that misses an X for a moment (smoke, muzzle flash) does not split it in two.
/// </summary>
internal sealed class XMarks
{
    /// <summary>Quiet time around a kill mark: another red X within it makes both hit markers.</summary>
    public const double Quiet = 0.45;
    private const double MaxShown = 1.5, MinGap = 1.0, Blink = 0.1;
    private DateTime _start, _seen, _ended = DateTime.MinValue, _lastKill = DateTime.MinValue, _rowAt = DateTime.MinValue;
    private bool _was, _old, _shot, _inRow, _baseline = true;
    private DateTime? _pending;    // the start of an X that looks like a kill, waiting for the quiet time after it
    private int _rows;

    /// <summary>A red X shows up on every hit on this server: it says nothing about kills here.</summary>
    public bool HitMarkers => _rows >= 3;

    /// <summary>The game was not in front (or the detector just started): what is there at the next look is not new.</summary>
    public void Reset() { _was = false; _pending = null; _baseline = true; }

    /// <summary>One look. x = a red X is there; shot = the player fired within the last seconds. Returns the time of a kill, once.</summary>
    public DateTime? Look(DateTime now, bool x, bool shot)
    {
        if (x)
        {
            if (!_was)
            {
                bool row = (now - _ended).TotalSeconds < Quiet;
                if (row)
                {
                    // a new X right after the previous one: they are hit markers, the one before is no kill either
                    _pending = null;
                    if (!_inRow) { _rows++; _rowAt = now; }
                }
                _inRow = row;
                _old = _baseline || row;
                _start = now; _shot = shot;
            }
            _was = true; _seen = now;
            if ((now - _start).TotalSeconds > MaxShown) _old = true;   // stays: part of the picture, not a mark
        }
        else if (_was && (now - _seen).TotalSeconds >= Blink)
        {
            _was = false; _ended = _seen;
            // seen on two looks or more, after a shot, not right after the last kill
            if (!_old && (_seen - _start).TotalMilliseconds >= 25 && _shot && (_start - _lastKill).TotalSeconds > MinGap)
                _pending = _start;
        }
        _baseline = false;
        if (_rows > 0 && (now - _rowAt).TotalMinutes > 15) _rows = 0;   // another server, perhaps
        if (!_was && _pending is DateTime p && (now - _ended).TotalSeconds >= Quiet)
        {
            _pending = null;
            if (!HitMarkers) { _lastKill = p; return p; }
        }
        return null;
    }
}

/// <summary>
/// When a kill message at the crosshair ("KILLED name", "+1 Streak") is a new kill. Fed with every reading of the
/// message strip (five a second after shots). One message stays 1 to 3 s and single readings of it can fail, so a gap
/// of up to 0.8 s does not make it a second kill; with a name in it, the same text back within 2.5 s is still the same
/// message. A message with another name replacing the old one at once is a further kill. A text that has been there
/// every time (a HUD label with a kill word) never counts, and nothing counts for 3 s after the player's own death
/// was read (kill cams name the killer).
/// </summary>
internal sealed class KillMessages
{
    private DateTime _seen = DateTime.MinValue, _gone = DateTime.MinValue, _death = DateTime.MinValue, _counted = DateTime.MinValue;
    private string _text = "", _word = "";

    /// <summary>The game is not in front: a message that is there when it comes back is not new.</summary>
    public void Pause(DateTime now) { _seen = now; _text = ""; _word = ""; }

    /// <summary>One reading: a kill message (with its text and kill word), a death message, or neither. Returns true for a new kill.</summary>
    public bool Read(DateTime now, bool kill, bool death, string text = "", string word = "")
    {
        if (death) { _death = now; _gone = now; return false; }
        if (!kill) { _gone = now; return false; }
        double gap = (now - _seen).TotalSeconds;
        string name = Name(text, word);
        bool fresh = gap > 0.8 && _gone > _seen;
        // the same named message back after a longer gap in the reading
        if (fresh && name.Length >= 3 && gap < 2.5 && KillLogic.SameText(name, Name(_text, _word))) fresh = false;
        // another name right away: the next kill replaced the message
        bool replaced = !fresh && gap <= 0.8 && Other(name, Name(_text, _word));
        bool count = (fresh || replaced) && (now - _death).TotalSeconds > 3 && (now - _counted).TotalSeconds > 0.6;
        _seen = now;
        if (name.Length >= Name(_text, _word).Length || count || fresh) { _text = text; _word = word; }
        if (count) _counted = now;
        return count;
    }

    /// <summary>The rest of the message without its kill word (mostly the name of the one who was killed).</summary>
    private static string Name(string text, string word)
    {
        var t = KillLogic.Letters(text); var w = KillLogic.Letters(word);
        if (w.Length > 0) { int i = t.IndexOf(w, StringComparison.Ordinal); if (i >= 0) t = t.Remove(i, w.Length); }
        return t;
    }

    /// <summary>Clearly another name, not the same one read a little differently or only in part.</summary>
    private static bool Other(string a, string b)
    {
        if (a.Length < 3 || b.Length < 3) return false;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)) return false;
        return KillLogic.Distance(a, b) >= Math.Max(a.Length, b.Length) * 0.6;
    }
}

/// <summary>
/// One kill seen twice: the red X and the message after it are the same kill when their kill times lie close together.
/// </summary>
internal sealed class KillMerge
{
    private DateTime _last = DateTime.MinValue; private string _how = "";

    /// <summary>True if this kill (at its estimated time, noticed this way) is new.</summary>
    public bool New(DateTime at, string how)
    {
        bool same = how != _how && Math.Abs((at - _last).TotalSeconds) < 1.2;
        if (same) return false;
        _last = at; _how = how;
        return true;
    }
}
