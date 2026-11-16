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

    /// <summary>
    /// Which crosshair pictures of a fight go to the AI check (indices, in time order), from the share of bright text
    /// pixels in each (<see cref="KillText.TextShare"/>): first where that share stands out or changes (a message comes
    /// or goes), then the gaps are filled so quiet stretches and the end are covered too. Neighbours that look alike are
    /// one moment and are taken once, so a message of 1.5 s (four pictures) does not use up all places.
    /// </summary>
    public static List<int> PickFrames(IReadOnlyList<double> share, int max)
    {
        int n = share.Count;
        if (n <= max) return Enumerable.Range(0, n).ToList();
        var pick = new SortedSet<int> { 0, n - 1 };
        // stands out against the usual level of the fight (a bright sky is there all the time), or changed since the last picture
        double usual = share.OrderBy(x => x).ElementAt(n / 2);
        var score = new double[n];
        for (int i = 0; i < n; i++) score[i] = Math.Max(0, share[i] - usual) + (i > 0 ? Math.Abs(share[i] - share[i - 1]) : 0);
        bool Alike(int a, int b) => b >= 0 && b < n && pick.Contains(b) && Math.Abs(share[a] - share[b]) < Math.Max(0.002, 0.2 * Math.Max(share[a], share[b]));
        int room = max - Math.Max(2, max / 4);   // a quarter of the places stays for the gaps
        foreach (var i in Enumerable.Range(0, n).Where(i => score[i] > 0.001).OrderByDescending(i => score[i]))
        {
            if (pick.Count >= room) break;
            if (!pick.Contains(i) && !Alike(i, i - 1) && !Alike(i, i + 1)) pick.Add(i);
        }
        // the rest: always into the middle of the longest gap
        while (pick.Count < max)
        {
            int best = -1, gap = 0, prev = -1;
            foreach (var p in pick) { if (prev >= 0 && p - prev > gap) { gap = p - prev; best = prev + gap / 2; } prev = p; }
            if (gap < 2) break;
            pick.Add(best);
        }
        return pick.ToList();
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

    /// <summary>The last reading was the player's own death, and a new one (no death message in the 4 s before it).</summary>
    public bool NewDeath { get; private set; }

    /// <summary>One reading: a kill message (with its text and kill word), a death message, or neither. Returns true for a new kill.</summary>
    public bool Read(DateTime now, bool kill, bool death, string text = "", string word = "")
    {
        NewDeath = death && (now - _death).TotalSeconds > 4;
        if (death) { _death = now; _gone = now; return false; }
        if (!kill) { _gone = now; return false; }
        double gap = (now - _seen).TotalSeconds;
        string name = Name(text, word);
        bool fresh = gap > 0.8 && _gone > _seen;
        // the same named message back after a longer gap in the reading
        if (fresh && name.Length >= 3 && gap < 2.5 && KillLogic.SameText(name, Name(_text, _word))) fresh = false;
        // another name right away: the next kill replaced the message
        bool replaced = !fresh && gap <= 0.8 && Other(name, Name(_text, _word));
        bool count = (fresh || replaced) && (now - _death).TotalSeconds > 3 && (now - _counted).TotalSeconds > 0.5;
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

/// <summary>
/// Which way the message strip is read (0 = the picture as it is, 1 = only its white text, 2 = only its bright white or
/// coloured text). One way per reading, in turns, so the work stays that of a single reading. While a message is on
/// screen ("both"), the way that found the last one goes first and one other way follows only when it finds nothing.
/// </summary>
internal sealed class ReadTurns
{
    public const int Ways = 3;
    private int _turn = -1, _good = 1;

    /// <summary>The way(s) for the next reading; second is -1 when there is none.</summary>
    public (int first, int second) Next(bool both)
    {
        _turn = (_turn + 1) % Ways;
        if (!both) return (_turn, -1);
        return (_good, _turn == _good ? (_turn + 1) % Ways : _turn);
    }

    /// <summary>This way found a message.</summary>
    public void Found(int way) { if (way is >= 0 and < Ways) _good = way; }
}

/// <summary>
/// Which recognised text is a kill message and which one a death message (the part of KillText without Windows).
/// A kill word counts only at the start of a word: "KILLED Max" and "KILLEDMAX" (glued by the recognition) are kills,
/// "Skilled_Sniper" is not. Long words may have one letter misread ("KILLEO", "K1LLED"), but words that are one letter
/// away from a kill word without being one ("KILLER", "FILLED", "BILLED") never count. Short words have to stand
/// alone ("+1 KILL" yes, "KILLS 12" no).
/// </summary>
internal static partial class KillText
{
    /// <summary>Words that mean "you killed somebody" (the user can change the list).</summary>
    public const string DefaultWords = "KILLED, KILL, GETÖTET, ELIMINATED, ELIMINIERT, ERLEDIGT";

    public enum Kind { None, Kill, Death }

    // "… by / von …" turns a kill word into the opposite; these also mark a death on their own. A phrase counts only
    // where a word ends right after it: "KILLED BY Max" is a death, "ELIMINATED Byron" and "GETÖTET Vonda" are kills.
    private static readonly string[] DeathPhrases =
    {
        "KILLEDBY", "GETOTETVON", "ELIMINATEDBY", "ELIMINIERTVON", "ERLEDIGTVON", "YOUDIED", "YOUWEREKILLED", "YOUAREDEAD",
        "DUWURDEST", "DUBISTTOT", "DUBISTGESTORBEN", "WASTED", "BEENKILLED", "BEENELIMINATED", "GOTKILLED",
        "DICHGETOTET", "DICHELIMINIERT", "DICHERLEDIGT"
    };
    // … these too, but a misread letter in their closing "YOU" never counts ("Max killed you" is a death, "You killed Young_Bob" is not)
    private static readonly string[] DeathAtEnd = { "KILLEDYOU", "ELIMINATEDYOU" };
    // words that look like a kill word to the recognition but are none
    private static readonly string[] NotKill = { "KILLER", "SKILL", "FILLED", "BILLED", "MILLED", "PILLED", "TILLED", "WILLED" };
    // the usual long kill words: a death phrase behind one of them is part of the name ("KILLED Wasted Max", "KILLED Max_Wasted")
    private static readonly string[] NameAfter = DefaultWords.Split(',').Select(x => Fold(x)).Where(x => x.Length >= 6).ToArray();

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
                    // "_" joins the parts of a name into one word ("Wasted_Max")
                    else if (keepSpaces && ch != '_' && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                    break;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// A line folded without spaces, and where its words start and end in it. A word that begins with digits ("+1KILL")
    /// also starts after them.
    /// </summary>
    private static (string t, bool[] start, bool[] end) Words(string line)
    {
        var spaced = Fold(line, true);
        var t = new System.Text.StringBuilder(spaced.Length);
        var start = new bool[spaced.Length + 1]; var end = new bool[spaced.Length + 1];
        bool inWord = false, digits = false;
        foreach (var ch in spaced)
        {
            if (ch == ' ') { if (inWord) end[t.Length] = true; inWord = false; continue; }
            if (!inWord) { start[t.Length] = true; inWord = true; digits = char.IsDigit(ch); }
            else if (digits && !char.IsDigit(ch)) { start[t.Length] = true; digits = false; }
            else if (!char.IsDigit(ch)) digits = false;
            t.Append(ch);
        }
        if (inWord) end[t.Length] = true;
        return (t.ToString(), start[..(t.Length + 1)], end[..(t.Length + 1)]);
    }

    /// <summary>The word stands at position i of t: exactly, or with one wrong letter in words of six or more letters.</summary>
    private static bool At(string t, int i, string w)
    {
        if (w.Length == 0 || i + w.Length > t.Length) return false;
        int diff = 0, allowed = w.Length >= 6 ? 1 : 0;
        for (int k = 0; k < w.Length && diff <= allowed; k++) if (t[i + k] != w[k]) diff++;
        return diff <= allowed;
    }

    /// <summary>A death message ("KILLED BY …", "You have been eliminated", "Max killed you", "WASTED").</summary>
    public static bool IsDeath(string line)
    {
        var l = Words(line);
        for (int i = 0; i < l.t.Length; i++)
        {
            bool hit = false;
            foreach (var p in DeathPhrases) if (Phrase(l, i, p, false)) { hit = true; break; }
            if (!hit) foreach (var p in DeathAtEnd) if (Phrase(l, i, p, true)) { hit = true; break; }
            if (hit && !AfterKillWord(l, i)) return true;
        }
        return false;
    }

    /// <summary>
    /// A death phrase at position i, followed by the end of a word ("ELIMINATED Byron", "GETÖTET Vonda", "KILLED Youssef"
    /// are no "… BY / VON / YOU"). Read exactly (anywhere), or with one wrong letter only where a word starts
    /// ("HEADSHOT KILLED" is no "GOT KILLED") and not in its closing "BY", "VON" or "YOU" unless that stands as a word
    /// of its own ("KILLED Bob", "KILLED Kyle" are kills, "KILLED 8Y Max" is a death). atEnd: never a misread "YOU".
    /// </summary>
    private static bool Phrase((string t, bool[] start, bool[] end) l, int i, string p, bool atEnd)
    {
        var (t, start, end) = l;
        if (i + p.Length > t.Length || !end[i + p.Length]) return false;
        if (string.CompareOrdinal(t, i, p, 0, p.Length) == 0) return true;
        if (!start[i] || !At(t, i, p)) return false;
        int tail = p.EndsWith("BY", StringComparison.Ordinal) ? 2 : p.EndsWith("VON", StringComparison.Ordinal) || p.EndsWith("YOU", StringComparison.Ordinal) ? 3 : 0;
        if (string.CompareOrdinal(t, i + p.Length - tail, p, p.Length - tail, tail) == 0) return true;
        return !atEnd && start[i + p.Length - tail];
    }

    /// <summary>A long kill word stands before position i: what follows is the name of the one who was killed.</summary>
    private static bool AfterKillWord((string t, bool[] start, bool[] end) l, int i)
    {
        for (int j = 0; j < i; j++)
            foreach (var w in NameAfter)
                if (j + w.Length <= i && KillAt(l, j, w)) return true;
        return false;
    }

    /// <summary>The kill word w (folded) is in the line, at the start of a word and not as part of a look-alike word.</summary>
    public static bool HasKillWord(string line, string w) => HasKillWord(Words(line), w);

    private static bool HasKillWord((string t, bool[] start, bool[] end) l, string w)
    {
        if (w.Length == 0) return false;
        for (int i = 0; i + w.Length <= l.t.Length; i++) if (KillAt(l, i, w)) return true;
        return false;
    }

    /// <summary>The kill word w stands at position i: where a word starts, short words alone, no look-alike word.</summary>
    private static bool KillAt((string t, bool[] start, bool[] end) l, int i, string w)
    {
        var (t, start, end) = l;
        if (!start[i] || !At(t, i, w)) return false;
        // short words have to stand alone; long ones may be glued to the name
        if (w.Length < 6 && !end[i + w.Length]) return false;
        return !NotKill.Any(n => !w.StartsWith(n, StringComparison.Ordinal) && string.CompareOrdinal(t, i, n, 0, n.Length) == 0);
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
            var l = Words(line);
            foreach (var w in list)
                if (HasKillWord(l, w)) { kill = (Kind.Kill, line.Trim(), w); break; }
        }
        return kill;
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

    /// <summary>Brightest channel from which text counts as bright, and how far the channels must lie apart for a colour.</summary>
    private const int BrightMin = 180, ColourMin = 100;

    /// <summary>How much a pixel looks like bright text, 0 to 255: white, or bright and clearly coloured (red, yellow, green, cyan …).</summary>
    private static int Vivid(int b, int g, int r)
    {
        int hi = Math.Max(r, Math.Max(g, b)), lo = Math.Min(r, Math.Min(g, b));
        int bright = Math.Clamp((hi - (BrightMin - 30)) * 255 / 30, 0, 255);
        int kind = Math.Clamp(Math.Max(hi - lo - (ColourMin - 30), lo - 175) * 255 / 30, 0, 255);
        return bright * kind / 255;
    }

    /// <summary>
    /// Keeps the bright text of a picture whatever its colour (white, red, yellow, green …), as dark letters on white.
    /// For servers whose kill message is coloured: <see cref="WhiteOnly"/> drops it. Pale sky, grey walls and the dark
    /// scenery drop out here as well.
    /// </summary>
    public static byte[] BrightOnly(byte[] src)
    {
        var dst = new byte[src.Length];
        for (int i = 0; i < src.Length; i += 4)
        {
            byte v = (byte)(255 - Vivid(src[i], src[i + 1], src[i + 2]));
            dst[i] = dst[i + 1] = dst[i + 2] = v; dst[i + 3] = 255;
        }
        return dst;
    }

    /// <summary>Share of a BGRA picture (every step-th pixel each way) that looks like bright text, 0 to 1.</summary>
    public static double TextShare(byte[] bgra, int w, int h, int step = 2)
    {
        int all = 0, hit = 0;
        for (int y = 0; y < h; y += step)
            for (int x = 0, i = y * w * 4; x < w; x += step, i += step * 4)
            {
                all++;
                if (Vivid(bgra[i], bgra[i + 1], bgra[i + 2]) >= 128) hit++;
            }
        return all == 0 ? 0 : (double)hit / all;
    }
}
