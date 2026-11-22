using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>
/// 2.31, Server page: the pure rules of Platz-Alarm (watch a full or offline server), Freunde online (find friends in
/// the public player lists of the favourites) and Server auf den Desktop (a shortcut that connects in one click).
/// No Windows calls and no I/O: checked by tests/ServerWatchLogic.Tests. The Windows side is Core/ServerWatch.cs,
/// Core/ServerShortcut.cs and Core/Notify.cs, the host commands MainWindow.ServerWatch.cs.
/// </summary>
public static class ServerWatchLogic
{
    // =====================================================================
    //  limits
    // =====================================================================
    /// <summary>Platz-Alarme at the same time.</summary>
    public const int MaxWatches = 3;
    /// <summary>A watch ends by itself after this long.</summary>
    public static readonly TimeSpan WatchFor = TimeSpan.FromHours(2);
    /// <summary>Seconds between two looks at a watched server (with jitter, see <see cref="NextDelay"/>).</summary>
    public const int PollSec = 20;
    /// <summary>Longest wait after errors, and after "too many requests".</summary>
    public const int MaxErrorDelay = 300, MaxLimitedDelay = 600;
    public const int MaxFriends = 50;
    public const int MaxFriendLen = 40;
    /// <summary>Servers one friends round looks at; with more favourites the next round goes on with the rest.</summary>
    public const int ServersPerRound = 10;
    /// <summary>The page asks at most this often (and only while the Server page is shown).</summary>
    public static readonly TimeSpan FriendEvery = TimeSpan.FromSeconds(60);
    /// <summary>"Melden, wenn ein Freund online kommt": the look in the background, while the app runs and nobody plays.</summary>
    public static readonly TimeSpan FriendBackgroundEvery = TimeSpan.FromMinutes(3);
    /// <summary>A server's player list older than this no longer counts for "online".</summary>
    public static readonly TimeSpan FriendStale = TimeSpan.FromMinutes(10);

    // =====================================================================
    //  Platz-Alarm
    // =====================================================================
    public enum ProbeKind { Ok, NotFound, RateLimited, Error }

    /// <summary>One answer of the Cfx.re single endpoint: players and slots when Ok, NotFound = the server is not in the
    /// list (offline), RateLimited = HTTP 429 (RetryAfter in seconds when the server said), Error = anything else.</summary>
    public readonly record struct Probe(ProbeKind Kind, int Players = 0, int Max = 0, int? RetryAfter = null);

    public enum WatchKind { Slot, Online }

    public enum Step { Keep, Fire, Retry }

    /// <summary>Full means every slot taken. A server that reports no slot count (0) is never called full.</summary>
    public static bool IsFull(int players, int max) => max > 0 && players >= max;

    /// <summary>What a watch waits for: an offline server for being back, a full one for a free slot. Null when there is
    /// nothing to wait for (online with room).</summary>
    public static WatchKind? KindFor(bool online, int players, int max) =>
        !online ? WatchKind.Online : IsFull(players, max) ? WatchKind.Slot : null;

    /// <summary>What one answer means for a watch. An offline watch fires as soon as the server answers again (full or
    /// not); a slot watch fires when fewer players than slots are on. A server that went away during a slot watch is
    /// kept watching. Errors and 429 are retried later.</summary>
    public static Step Decide(WatchKind kind, Probe p) => p.Kind switch
    {
        ProbeKind.Ok when kind == WatchKind.Online => Step.Fire,
        ProbeKind.Ok => p.Max > 0 && !IsFull(p.Players, p.Max) ? Step.Fire : Step.Keep,
        ProbeKind.NotFound => Step.Keep,
        _ => Step.Retry,
    };

    /// <summary>Seconds until the next look. Normal: 20 s, jittered to 17–23 s so several watches never ask in step.
    /// After <paramref name="errors"/> failures in a row: 40, 80, 160, then 300 s at most. After HTTP 429: what the server
    /// asked for (Retry-After), but at least 2 minutes, doubling with every 429 in a row, 10 minutes at most, plus up to
    /// 15 s of jitter. <paramref name="jitter"/> is a random number from 0 to 1.</summary>
    public static double NextDelay(int errors, bool limited, int? retryAfter, double jitter)
    {
        jitter = double.IsNaN(jitter) ? .5 : Math.Clamp(jitter, 0, 1);
        if (limited)
        {
            double floor = 120 * Math.Pow(2, Math.Clamp(errors - 1, 0, 8));
            double wait = Math.Max(Math.Clamp(retryAfter ?? 0, 0, MaxLimitedDelay), floor);
            return Math.Min(MaxLimitedDelay, wait) + 15 * jitter;
        }
        double baseSec = errors <= 0 ? PollSec : Math.Min(MaxErrorDelay, PollSec * Math.Pow(2, Math.Min(errors, 8)));
        return baseSec * (0.85 + 0.3 * jitter);
    }

    /// <summary>Retry-After as seconds: a number of seconds or an HTTP date. Null when missing or unreadable; at most an hour.</summary>
    public static int? RetryAfter(string? header, DateTime nowUtc)
    {
        var h = header?.Trim();
        if (string.IsNullOrEmpty(h)) return null;
        if (int.TryParse(h, NumberStyles.None, CultureInfo.InvariantCulture, out var s)) return Math.Min(s, 3600);
        if (DateTime.TryParseExact(h, "r", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
            return (int)Math.Clamp(Math.Ceiling((at - nowUtc).TotalSeconds), 0, 3600);
        return null;
    }

    /// <summary>A watch ends by itself two hours after it started.</summary>
    public static bool Expired(DateTime started, DateTime now) => now - started >= WatchFor;

    /// <summary>When FiveM starts, a watch is over: the player chose a server themselves. When FiveM (the launcher) was
    /// already open as the watch began, only the game itself ends it (or a new start after FiveM was closed).</summary>
    public static bool EndsForFiveM(bool fivemAtStart, bool fivemNow, bool gameNow) => gameNow || (!fivemAtStart && fivemNow);

    /// <summary>"Automatisch verbinden" only starts FiveM when it is not running: a running FiveM is never touched.</summary>
    public static bool MayAutoConnect(bool auto, bool fivemRunning, bool authed) => auto && !fivemRunning && authed;

    /// <summary>Which watches count towards <see cref="MaxWatches"/>: the ones still looking.</summary>
    public static bool Active(string state) => state is "watching" or "retry";

    // =====================================================================
    //  names: servers, friends, players
    // =====================================================================
    // [0-9], not \d: in .NET \d also matches Arabic, Devanagari … digits, and FiveM only knows ^0 to ^9
    private static readonly Regex Colour = new(@"\^[0-9]", RegexOptions.CultureInvariant);
    private static readonly Regex GtaColour = new(@"~[a-zA-Z]~", RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>A name as people see it: FiveM colour codes (^0 to ^9) out and the outer whitespace trimmed.</summary>
    public static string Plain(string? s) => Colour.Replace(s ?? "", "").Trim();

    /// <summary>The form names are compared in: <see cref="Plain"/>, Unicode NFC (an é typed as e + accent equals é)
    /// and lower case (invariant, so a Turkish system does not change "I").</summary>
    public static string Key(string? s)
    {
        var p = Plain(s);
        try { p = p.Normalize(NormalizationForm.FormC); } catch (ArgumentException) { }   // broken surrogates: compare as they are
        return p.ToLowerInvariant();
    }

    /// <summary>A server name for notes and shortcuts: FiveM and GTA colour codes out, whitespace runs as one space,
    /// at most 64 characters. <paramref name="fallback"/> when nothing is left.</summary>
    public static string ServerName(string? raw, string fallback)
    {
        var s = Spaces.Replace(GtaColour.Replace(Colour.Replace(raw ?? "", ""), ""), " ").Trim();
        s = new string(s.Where(c => !char.IsControl(c)).ToArray());
        if (s.Length > 64) s = Cut(s, 64).TrimEnd() + "…";
        return s.Length > 0 ? s : fallback;
    }

    /// <summary>A friend's name as typed: colour codes and the outer whitespace out. Null when nothing is left, when it
    /// is longer than <see cref="MaxFriendLen"/> or holds control characters.</summary>
    public static string? CleanFriend(string? raw)
    {
        var s = Plain(raw);
        if (s.Length == 0 || s.Length > MaxFriendLen || s.Any(char.IsControl)) return null;
        return s;
    }

    /// <summary>Adds a friend to the list (in place). Returns a German error or null when it was added. The same name in
    /// another case or with colour codes counts as the same.</summary>
    public static string? AddFriend(List<string> names, string? raw)
    {
        var s = CleanFriend(raw);
        if (s == null) return Plain(raw).Length > MaxFriendLen ? $"Der Name ist zu lang (höchstens {MaxFriendLen} Zeichen)." : "Gib einen Namen ein.";
        var k = Key(s);
        if (names.Any(n => Key(n) == k)) return "Der Name steht schon in deiner Liste.";
        if (names.Count >= MaxFriends) return $"Höchstens {MaxFriends} Namen.";
        names.Add(s);
        return null;
    }

    /// <summary>Removes a friend (matched like <see cref="AddFriend"/>). True when one was removed.</summary>
    public static bool RemoveFriend(List<string> names, string? raw)
    {
        var k = Key(raw);
        return k.Length > 0 && names.RemoveAll(n => Key(n) == k) > 0;
    }

    private static readonly Regex Placeholder = new(@"^(?:Player|Anon[0-9]+)\z", RegexOptions.CultureInvariant);

    /// <summary>The stand-in names Cfx.re shows instead of the real ones on most servers ("Player", "Anon0", "Anon1" …).</summary>
    public static bool IsPlaceholder(string? name) => Placeholder.IsMatch(Plain(name));

    public enum Listing { Names, Anonymous, Hidden, Empty }

    /// <summary>What a server's public player list shows. Hidden: players are on but the list is empty. Anonymous: every
    /// name is a stand-in (or, from 5 players on, at least 90 %: a real player may be called "Player"). Checked live on
    /// 2026-11-22: 17 of 18 servers (all big ones) showed only "Player" or "AnonN" with ping 0.</summary>
    public static Listing Classify(int players, IReadOnlyCollection<string> names)
    {
        if (names.Count == 0) return players > 0 ? Listing.Hidden : Listing.Empty;
        int stand = names.Count(IsPlaceholder);
        if (stand == names.Count || (names.Count >= 5 && stand * 10 >= names.Count * 9)) return Listing.Anonymous;
        return Listing.Names;
    }

    /// <summary>Exact by default (after <see cref="Key"/>). Partial: the friend's name inside the player's, from three
    /// characters on (shorter ones would find half the server).</summary>
    public static bool Matches(string friendKey, string playerKey, bool partial) =>
        friendKey.Length > 0 && (playerKey == friendKey || (partial && friendKey.Length >= 3 && playerKey.Contains(friendKey, StringComparison.Ordinal)));

    /// <summary>The friends among one server's players: (friend as typed, player name as shown), each friend once, an
    /// exact hit before a partial one. Stand-in names never count, and a server that only shows stand-ins finds nobody.</summary>
    public static List<(string friend, string player)> Find(IEnumerable<string> friends, IReadOnlyCollection<string> players, bool partial)
    {
        var res = new List<(string, string)>();
        if (Classify(players.Count, players) != Listing.Names) return res;
        var keyed = players.Where(p => !IsPlaceholder(p)).Select(p => (shown: Plain(p), key: Key(p))).Where(p => p.key.Length > 0).ToList();
        var seen = new HashSet<string>();
        foreach (var f in friends)
        {
            var fk = Key(f);
            if (fk.Length == 0 || !seen.Add(fk)) continue;
            var hit = keyed.FirstOrDefault(p => p.key == fk);
            if (hit.key == null && partial) hit = keyed.FirstOrDefault(p => Matches(fk, p.key, true));
            if (hit.key != null) res.Add((Plain(f), hit.shown));
        }
        return res;
    }

    /// <summary>Which favourites one round looks at: at most <paramref name="per"/> of <paramref name="count"/>, going on
    /// where the last round stopped, so with more favourites every one comes up in turn. Returns the indices and where
    /// the next round starts.</summary>
    public static (int[] pick, int next) Rotate(int count, int per, int start)
    {
        if (count <= 0 || per <= 0) return (Array.Empty<int>(), 0);
        if (count <= per) return (Enumerable.Range(0, count).ToArray(), 0);
        start = ((start % count) + count) % count;
        var pick = Enumerable.Range(0, per).Select(i => (start + i) % count).ToArray();
        return (pick, (start + per) % count);
    }

    /// <summary>Where the user's ping to a server with a friend on it is measured: its first connect address, but only
    /// a plain public IPv4 address with a port ("1.2.3.4:30120"). Null for proxy addresses (most big servers list
    /// "https://…"), host names, IPv6 and every address that is not public (loopback, private networks, link-local,
    /// carrier-grade NAT, documentation and multicast ranges): that would not be the server's ping, and a server's list
    /// entry must never make OrbitTweak knock on devices in the user's own network.</summary>
    public static (string host, int port)? PingTarget(string? endpoint)
    {
        if (endpoint == null) return null;
        var m = Regex.Match(endpoint, @"^([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3}):([0-9]{1,5})\z", RegexOptions.CultureInvariant);
        if (!m.Success) return null;
        var b = new int[4];
        for (int i = 0; i < 4; i++) if ((b[i] = int.Parse(m.Groups[i + 1].Value, CultureInfo.InvariantCulture)) > 255) return null;
        int port = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
        if (port is < 1 or > 65535) return null;
        bool local = b[0] is 0 or 10 or 127 or >= 224
            || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168) || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2) || (b[0] == 198 && b[1] is 18 or 19)
            || (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113);
        return local ? null : ($"{b[0]}.{b[1]}.{b[2]}.{b[3]}", port);
    }

    /// <summary>Whether a new friends round may run: never twice within <paramref name="every"/>.</summary>
    public static bool MayRound(DateTime? last, DateTime now, TimeSpan every) => last == null || now - last.Value >= every || now < last.Value;

    /// <summary>
    /// Who came online, for "Melden, wenn ein Freund online kommt". The first round only learns who is on (no note for
    /// friends that were on before the switch). A friend counts as gone after two rounds in a row without them, so one
    /// server that did not answer once does not bring a second note.
    /// </summary>
    public sealed class FriendSeen
    {
        private readonly Dictionary<string, int> _miss = new();
        public bool Primed { get; private set; }

        /// <summary>One round with the keys (<see cref="Key"/>) of the friends seen now; returns the ones that are new.</summary>
        public List<string> Round(IEnumerable<string> seenKeys)
        {
            var now = new List<string>();
            foreach (var k in seenKeys) if (k.Length > 0 && !now.Contains(k)) now.Add(k);
            var fresh = new List<string>();
            foreach (var k in now)
            {
                if (Primed && !_miss.ContainsKey(k)) fresh.Add(k);
                _miss[k] = 0;
            }
            foreach (var k in _miss.Keys.ToList())
                if (!now.Contains(k) && ++_miss[k] >= 2) _miss.Remove(k);
            Primed = true;
            return fresh;
        }

        public void Reset() { _miss.Clear(); Primed = false; }
    }

    // =====================================================================
    //  Server auf den Desktop
    // =====================================================================
    /// <summary>A Cfx.re join code as OrbitTweak accepts it on the command line: 4 to 8 characters a–z and 0–9, nothing
    /// else (no upper case, no spaces, no line break, no path). Seen live: 6 and 7 characters.</summary>
    public static bool ValidCode(string? c)
    {
        if (c == null || c.Length < 4 || c.Length > 8) return false;
        foreach (var ch in c) if (!(ch is >= 'a' and <= 'z' or >= '0' and <= '9')) return false;
        return true;
    }

    /// <summary>"cfx:abc123" (a favourite's key) → "abc123"; null for IP servers and anything else.</summary>
    public static string? CodeOfKey(string? key) =>
        key != null && key.StartsWith("cfx:", StringComparison.Ordinal) && ValidCode(key[4..]) ? key[4..] : null;

    /// <summary>The code after "--connect" on the command line, null when there is none or it is not a valid code
    /// (then nothing happens at all). Only the first "--connect" counts.</summary>
    public static string? ConnectArg(IReadOnlyList<string>? args)
    {
        if (args == null) return null;
        for (int i = 0; i < args.Count; i++)
            if (string.Equals(args[i], "--connect", StringComparison.Ordinal))
                return i + 1 < args.Count && ValidCode(args[i + 1]) ? args[i + 1] : null;
        return null;
    }

    /// <summary>The arguments a desktop shortcut starts OrbitTweak with.</summary>
    public static string ShortcutArgs(string code) =>
        ValidCode(code) ? "--connect " + code : throw new ArgumentException("Kein gültiger Join-Code", nameof(code));

    /// <summary>The address FiveM connects to for a join code.</summary>
    public static string ConnectAddress(string code) => "cfx.re/join/" + code;

    private const string BadFileChars = "<>:\"/\\|?*";
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };
    public const int MaxFileName = 60;

    /// <summary>
    /// The shortcut's file name (without ".lnk") from the server name: FiveM and GTA colour codes out, characters
    /// Windows refuses in file names (&lt; &gt; : " / \ | ? * and control characters) out, invisible format characters out
    /// (a right-to-left mark could make "…exe" appear at the end), whitespace runs as one space, no dots or spaces at
    /// the end (Windows drops them), at most 60 characters. "FiveM-Server abc123" when nothing usable is left or the
    /// name is reserved by Windows (CON, NUL, COM1 …).
    /// </summary>
    public static string FileName(string? serverName, string code)
    {
        var fallback = "FiveM-Server " + code;
        var s = GtaColour.Replace(Colour.Replace(serverName ?? "", ""), "");
        var sb = new StringBuilder(s.Length);
        foreach (var r in s.EnumerateRunes())
        {
            if (r == Rune.ReplacementChar || r.Value < 32 || r.Value == 127) { sb.Append(' '); continue; }
            var cat = Rune.GetUnicodeCategory(r);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned) continue;
            if (r.IsBmp && BadFileChars.Contains((char)r.Value)) { sb.Append(' '); continue; }
            if (cat is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) { sb.Append(' '); continue; }
            sb.Append(r.ToString());
        }
        s = Spaces.Replace(sb.ToString(), " ").Trim();
        s = Cut(s, MaxFileName).TrimEnd(' ', '.');
        s = s.TrimStart(' ');
        if (s.Length == 0 || s.All(c => c == '.')) return fallback;
        var stem = s.Split('.')[0].TrimEnd(' ');
        if (Reserved.Contains(stem)) return fallback;
        return s;
    }

    /// <summary>The first <paramref name="max"/> characters without splitting a surrogate pair.</summary>
    private static string Cut(string s, int max)
    {
        if (s.Length <= max) return s;
        int n = max;
        if (char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n];
    }

    /// <summary>Another free name for a shortcut when one with the same name belongs to something else: "Name (2)", "Name (3)" …</summary>
    public static string Numbered(string name, int n) => n <= 1 ? name : $"{name} ({n})";

    /// <summary>Width and height from a PNG's IHDR, null when it is no PNG.</summary>
    public static (int w, int h)? PngSize(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> sig = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (png.Length < 24 || !png[..8].SequenceEqual(sig)) return null;
        if (png[12] != (byte)'I' || png[13] != (byte)'H' || png[14] != (byte)'D' || png[15] != (byte)'R') return null;
        int w = BinaryPrimitives.ReadInt32BigEndian(png[16..]), h = BinaryPrimitives.ReadInt32BigEndian(png[20..]);
        return w > 0 && h > 0 ? (w, h) : null;
    }

    /// <summary>
    /// An .ico file holding the given PNG images (Windows reads PNG entries since Vista): ICONDIR (reserved 0, type 1,
    /// count), one 16-byte ICONDIRENTRY per image (width and height, 0 meaning 256; no palette; 1 plane; 32 bits; size;
    /// offset), then the PNGs. Each image must be a PNG of 1 to 256 pixels per side, every size only once. Null otherwise.
    /// </summary>
    public static byte[]? Ico(IReadOnlyList<byte[]> pngs)
    {
        if (pngs.Count == 0 || pngs.Count > 16) return null;
        var sizes = new List<(int w, int h)>();
        foreach (var p in pngs)
        {
            if (p == null || PngSize(p) is not (int w, int h) || w > 256 || h > 256) return null;
            if (sizes.Contains((w, h))) return null;
            sizes.Add((w, h));
        }
        int head = 6 + 16 * pngs.Count;
        var ico = new byte[head + pngs.Sum(p => p.Length)];
        var span = ico.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)pngs.Count);
        int at = head;
        for (int i = 0; i < pngs.Count; i++)
        {
            var e = span[(6 + 16 * i)..];
            var (w, h) = sizes[i];
            e[0] = (byte)(w == 256 ? 0 : w);
            e[1] = (byte)(h == 256 ? 0 : h);
            BinaryPrimitives.WriteUInt16LittleEndian(e[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(e[6..], 32);
            BinaryPrimitives.WriteInt32LittleEndian(e[8..], pngs[i].Length);
            BinaryPrimitives.WriteInt32LittleEndian(e[12..], at);
            pngs[i].CopyTo(ico, at);
            at += pngs[i].Length;
        }
        return ico;
    }

    /// <summary>The server icon only comes from Cfx.re's own hosts over https (no request to an address a server owner
    /// chose: that would hand them the player's IP). Null for anything else.</summary>
    public static string? IconUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" || !u.IsDefaultPort || u.UserInfo.Length > 0) return null;
        var host = u.IdnHost.ToLowerInvariant();
        return host is "servers-live.fivem.net" or "cfx-server-metadata.cfx-services.net" ? u.AbsoluteUri : null;
    }

    // =====================================================================
    //  connect forwarding (a second start hands its --connect to the running app)
    // =====================================================================
    /// <summary>The tag of the WM_COPYDATA message that carries a join code ("OTCN").</summary>
    public const uint CopyTag = 0x4F54434E;

    /// <summary>The bytes sent: the code as UTF-16. Only valid codes are ever sent.</summary>
    public static byte[]? CopyBytes(string code) => ValidCode(code) ? Encoding.Unicode.GetBytes(code) : null;

    /// <summary>The code read back from a WM_COPYDATA message: the tag must match, the length be even and short, the
    /// text a valid code. Null for anything else (any program on the desktop can send this message).</summary>
    public static string? CodeFromCopy(uint tag, ReadOnlySpan<byte> data)
    {
        if (tag != CopyTag || data.Length < 8 || data.Length > 16 || data.Length % 2 != 0) return null;
        var s = Encoding.Unicode.GetString(data);
        return ValidCode(s) ? s : null;
    }
}
