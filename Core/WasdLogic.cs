namespace OrbitTweak.Core;

/// <summary>
/// What happens to the movement keys W, A, S, D while the game is in front. Pure logic without any Windows calls,
/// so it can be tried in a simulation: real key events and the time go in, the answer is whether the real event
/// is kept from the game and which key events have to be sent instead.
///
///  - Snap Tap: of two opposite keys (A/D, W/S) the one pressed last wins; the other is let go for the game and
///    given back when the winner is released.
///  - "Laggen": while a movement key is held, a key beside it is tapped for the game for a moment again and again,
///    in turns (W held: A, then D; every <see cref="Every"/> ms for <see cref="Gap"/> ms). The figure never stops,
///    it twitches to the sides while it runs on. Keys are added, never taken away, on purpose: the game reads the
///    keyboard directly, and a held key keeps reporting itself there (key repeat), so "letting it go" for the game
///    would be undone within milliseconds. A key only counts once it has been held for <see cref="First"/> ms, so
///    typing is not affected.
///
/// Rule that keeps keys from getting stuck: a key is only ever pressed for the game while it is physically held,
/// and a real release of a key the game still has down always reaches the game.
/// </summary>
internal sealed class WasdLogic
{
    public const uint W = 0x57, A = 0x41, S = 0x53, D = 0x44;
    private static uint Opp(uint vk) => vk switch { A => D, D => A, W => S, S => W, _ => 0 };
    public static bool Handles(uint vk) => vk is W or A or S or D;

    public bool Snap, Lag;
    /// <summary>Milliseconds a held key is let go, milliseconds from one let-go to the next, and how long a key has to be held before the first one.</summary>
    public int Gap = 30, Every = 90, First = 120;

    private readonly HashSet<uint> _down = new();      // physically held
    private readonly HashSet<uint> _muted = new();     // held, but let go for the game by Snap Tap
    private readonly HashSet<uint> _cut = new();       // not held, but pressed for the game for a moment by "Laggen" (the opposite of a held key)
    private readonly Dictionary<uint, long> _since = new();
    private long _nextCut, _back; private int _turn;

    /// <summary>Key events to send to the game (vk, true = release). Filled by <see cref="Key"/> and <see cref="Tick"/>; the caller sends and clears them.</summary>
    public readonly List<(uint vk, bool up)> Out = new();

    private bool _front, _snapWas;
    private static readonly (uint, uint)[] Pairs = { (A, D), (W, S) };

    /// <summary>
    /// Two moments in which both keys of an opposite pair can be held without Snap Tap having sorted them out:
    /// the game comes to the front while they are held (it does not know them as pressed yet), or Snap Tap is switched
    /// on while they are held (the game has both down). Either way only the key pressed later stays, the earlier one
    /// counts as let go.
    /// </summary>
    private void Sync(bool inFront)
    {
        bool gained = inFront && !_front, switchedOn = Snap && !_snapWas && inFront && _front;
        if (Snap && (gained || switchedOn))
            foreach (var (a, b) in Pairs)
                if (_down.Contains(a) && _down.Contains(b) && !_muted.Contains(a) && !_muted.Contains(b))
                {
                    uint older = _since[a] <= _since[b] ? a : b;
                    _muted.Add(older);
                    if (switchedOn) Out.Add((older, true));
                }
        _front = inFront; _snapWas = Snap;
    }

    /// <summary>A real key event of W, A, S or D. Returns true if the game must not get it.</summary>
    public bool Key(uint vk, bool down, bool inFront, long now)
    {
        uint opp = Opp(vk);
        Sync(inFront);
        if (!inFront)
        {
            // nothing is changed outside the game, only what is held is remembered
            if (!down) { _down.Remove(vk); _muted.Remove(vk); }
            else if (_down.Add(vk)) _since[vk] = now;
            return false;
        }
        if (down)
        {
            if (_muted.Contains(vk)) return true;                               // auto-repeat of a key the game has let go
            _cut.Remove(vk);                                                    // the player now really holds the key "Laggen" was pressing: it is his from here on
            if (_down.Add(vk))
            {
                _since[vk] = now;
                if (Snap && _down.Contains(opp) && _muted.Add(opp)) Out.Add((opp, true));
            }
            return false;
        }
        _down.Remove(vk);
        bool muted = _muted.Remove(vk);
        // a key is let go while "Laggen" is tapping another one: that tap ends at once, so nothing is pressed for the
        // game that the player does not want any more
        if (_cut.Count > 0) { foreach (var k in _cut) if (!_down.Contains(k)) Out.Add((k, true)); _cut.Clear(); }
        if (_down.Contains(opp) && _muted.Remove(opp)) Out.Add((opp, false));    // give the other direction back
        return muted;                                                            // the game has it up already
    }

    /// <summary>Called when the time from <see cref="Due"/> is over (and after every key event).</summary>
    public void Tick(bool inFront, long now)
    {
        Sync(inFront);
        if (!Lag || !inFront)
        {
            // switched off, or the game is not in front: every key pressed from here is let go again (never left stuck)
            if (_cut.Count > 0)
            {
                foreach (var k in _cut) if (!_down.Contains(k)) Out.Add((k, true));
                _cut.Clear();
            }
            return;
        }
        if (_cut.Count > 0)
        {
            if (now < _back) return;
            foreach (var k in _cut) if (!_down.Contains(k)) Out.Add((k, true));
            _cut.Clear();
        }
        if (now < _nextCut) return;
        // Which keys are held long enough, per axis (both keys of one axis held = that axis does not count)
        uint Held(uint a, uint b)
        {
            bool ha = _down.Contains(a) && !_muted.Contains(a) && now - _since[a] >= First, hb = _down.Contains(b) && !_muted.Contains(b) && now - _since[b] >= First;
            return ha == hb ? 0 : ha ? a : b;
        }
        uint v = Held(W, S), h = Held(A, D);
        // The figure keeps moving and twitches: one key is tapped for a moment, the next time another one, in turns.
        //  - one direction held (W): the two keys beside it (A, then D) – it jerks left and right while running on
        //  - two held (W+D, diagonal): the opposite of each in turn (S, then A) – it zigzags between the two directions
        uint k1 = 0, k2 = 0;
        if (v != 0 && h == 0) { k1 = A; k2 = D; }
        else if (h != 0 && v == 0) { k1 = W; k2 = S; }
        else if (v != 0 && h != 0) { k1 = Opp(v); k2 = Opp(h); }
        if (k1 == 0) { _nextCut = now + 40; return; }               // nothing to tap right now: look again shortly, never spin
        uint tap = (_turn++ & 1) == 0 ? k1 : k2;
        if (_down.Contains(tap)) tap = tap == k1 ? k2 : k1;        // the player holds that one himself: take the other
        if (_down.Contains(tap) || !_cut.Add(tap)) { _nextCut = now + 40; return; }
        Out.Add((tap, false));
        _back = now + Gap; _nextCut = now + Math.Max(Every, Gap + 10);
    }

    /// <summary>Milliseconds until <see cref="Tick"/> has something to do; -1 = nothing to wait for.</summary>
    public int Due(bool inFront, long now)
    {
        if (!Lag) return _cut.Count > 0 ? 0 : -1;
        if (!inFront) return _cut.Count > 0 ? 0 : 200;                           // look again whether the game came to the front
        if (_cut.Count > 0) return (int)Math.Max(0, _back - now);
        long at = long.MaxValue;
        foreach (var k in _down) if (!_muted.Contains(k)) at = Math.Min(at, Math.Max(_nextCut, _since[k] + First));
        return at == long.MaxValue ? 200 : (int)Math.Clamp(at - now, 0, 200);
    }

    public void Reset() { _down.Clear(); _muted.Clear(); _cut.Clear(); _since.Clear(); _nextCut = _back = 0; _front = _snapWas = false; Out.Clear(); }
}
