using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrbitTweak.Core;

/// <summary>
/// The keyboard side of the WASD features, only active while the game is in front:
///  - Snap Tap (SOCD): when A and D (or W and S) are held together, the key pressed last wins and the other is
///    released for the game, like on "Snap Tap" keyboards.
///  - "Laggen": held movement keys are let go for a moment again and again, so the figure moves in a stop-and-go way.
///  - Windows key lock (tweak "winkey"): the Windows keys do nothing, so a slip does not open the start menu and throw
///    the player out of full screen (Microsoft's own advice for games: a low-level hook that eats VK_LWIN/VK_RWIN).
/// A low-level keyboard hook on its own thread feeds the real key events into <see cref="WasdLogic"/> (which decides,
/// and is tested in a simulation) and sends the key events it asks for.
/// </summary>
internal sealed class SnapTap
{
    private delegate IntPtr HookProc(int code, IntPtr wp, IntPtr lp);
    [StructLayout(LayoutKind.Sequential)] private struct KBD { public uint vk, scan, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG m, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint ms, uint wake, uint flags);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint tid, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] private static extern bool SetThreadPriority(IntPtr thread, int priority);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    // marks the key events sent from here, so the hook leaves them alone
    private static readonly IntPtr Marker = new(0x0B17_5A1A);
    private readonly HookProc _proc;
    private IntPtr _hook;
    private Thread? _thread;
    private uint _tid;
    private volatile bool _quit, _snap, _lag, _win;
    private int _winEaten;                                // bit 1 = left, bit 2 = right Windows key whose press was eaten (hook thread only)
    private volatile int _gap = 30, _every = 90;
    private readonly WasdLogic _logic = new();           // used on the hook thread only
    private readonly Stopwatch _clock = Stopwatch.StartNew();

#if DEBUG
    // Test aid, debug builds only: a test instance (one that runs with ORBIT_TEST_DATA, its own data folder) leaves the
    // real keyboard completely alone. Only key events marked with TestMarker count as presses, and F13 to F16 stand in
    // for W, A, S, D (in both directions). A test can so drive the hook while somebody is using the PC, without
    // touching what they type or play.
    private static readonly IntPtr TestMarker = new(0x0B17_7E57);
    private static readonly bool TestKeys = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORBIT_TEST_DATA"));
    private static uint FromTest(uint vk) => vk switch { 0x7C => WasdLogic.W, 0x7D => WasdLogic.A, 0x7E => WasdLogic.S, 0x7F => WasdLogic.D, _ => 0 };
    private static uint ToTest(uint vk) => vk switch { WasdLogic.W => 0x7C, WasdLogic.A => 0x7D, WasdLogic.S => 0x7E, WasdLogic.D => 0x7F, _ => vk };
#endif

    /// <summary>Set by the UI timer – the hook itself never does any slow work.</summary>
    public static volatile bool GameInFront;

    public SnapTap() { _proc = Hook; }

    /// <summary>
    /// Switches Snap Tap, "Laggen" and the Windows key lock on or off. gap = milliseconds a held key is let go, every =
    /// milliseconds from one let-go to the next. The hook only exists while one of them is on.
    /// </summary>
    public void Apply(bool snap, bool lag, int gap, int every, bool winKey = false)
    {
        _gap = Math.Clamp(gap, 10, 250); _every = Math.Clamp(every, _gap + 15, 600);
        _snap = snap; _lag = lag; _win = winKey;
        bool need = snap || lag || winKey;
        if (need && _thread == null)
        {
            // The hook runs on its own thread with its own message loop, so a busy UI can never delay keyboard input.
            _quit = false;
            _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "SnapTap" };
            _thread.Start();
        }
        else if (!need && _thread != null)
        {
            _quit = true;
            PostThreadMessage(_tid, 0x12, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
            _thread.Join(500);
            _thread = null;
        }
        else if (_thread != null) PostThreadMessage(_tid, 0, IntPtr.Zero, IntPtr.Zero); // wake the loop, the new values count at once
    }

    private void Run()
    {
        _tid = GetCurrentThreadId();
        // This thread sits in the path of every key press of the whole PC and does next to nothing per key. The game
        // runs with raised priority and keeps all cores busy; without the highest priority the hook would answer late
        // and Windows would hand keys on without asking it.
        SetThreadPriority(GetCurrentThread(), 15); // THREAD_PRIORITY_TIME_CRITICAL
        bool fine = false;
        _logic.Reset();
        _hook = SetWindowsHookEx(13, _proc, GetModuleHandle(null), 0);
        try
        {
            while (!_quit)
            {
                _logic.Snap = _snap; _logic.Lag = _lag; _logic.Gap = _gap; _logic.Every = _every;
                // keys are let go on a schedule of a few milliseconds: that needs the fine timer steps of Windows
                if (_lag != fine) { if (_lag) timeBeginPeriod(1); else timeEndPeriod(1); fine = _lag; }
                long now = _clock.ElapsedMilliseconds; bool front = GameInFront;
                _logic.Tick(front, now); Flush();
                int due = _logic.Due(front, now);
                MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, due < 0 ? 0xFFFFFFFF : (uint)due, 0x04FF, 0x0004); // any message, also ones already seen
                while (PeekMessage(out var m, IntPtr.Zero, 0, 0, 1)) if (m.message == 0x12) _quit = true;  // the hook is called from in here
            }
        }
        finally
        {
            // a key that is let go for a moment right now comes back if it is still held
            _logic.Lag = false; _logic.Tick(GameInFront, _clock.ElapsedMilliseconds); Flush();
            if (fine) timeEndPeriod(1);
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero; _logic.Reset(); _winEaten = 0;
        }
    }

    /// <summary>Sends the key events the logic asked for.</summary>
    private void Flush()
    {
        int n = _logic.Out.Count;
        if (n == 0) return;
        var keys = new INPUT[n];
        for (int i = 0; i < n; i++)
        {
            var (vk, up) = _logic.Out[i];
#if DEBUG
            if (TestKeys) vk = ToTest(vk);
#endif
            keys[i] = new INPUT { type = 1, ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = up ? 2u : 0u, dwExtraInfo = Marker } };
        }
        _logic.Out.Clear(); // first: sending calls the hook again (same thread) for these very events
        SendInput((uint)n, keys, Marshal.SizeOf<INPUT>());
    }

    private IntPtr Hook(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<KBD>(lp);
            bool test = false;
#if DEBUG
            test = TestKeys;   // a test instance leaves the real keyboard alone, the Windows keys too
#endif
            if (!test && k.extra != Marker && (k.vk == 0x5B || k.vk == 0x5C))   // VK_LWIN, VK_RWIN
            {
                int msg = wp.ToInt32();
                bool down = msg is 0x100 or 0x104, up = msg is 0x101 or 0x105;
                int bit = k.vk == 0x5B ? 1 : 2;
                if (down && _win && GameInFront) { _winEaten |= bit; return new IntPtr(1); }
                if (down) _winEaten &= ~bit;   // a press got through (game left the front while held): its release must get through too
                // the release of an eaten press is eaten too (a lone release would open the start menu); a release whose
                // press got through always gets through, so the key never sticks when the game comes to the front meanwhile
                if (up && (_winEaten & bit) != 0) { _winEaten &= ~bit; return new IntPtr(1); }
            }
            // only real presses of W, A, S, D: not what was sent from here and not what other programs send
            uint vk = k.vk; bool real = k.extra != Marker && (k.flags & 0x10) == 0;
#if DEBUG
            if (TestKeys) { real = k.extra == TestMarker && FromTest(k.vk) != 0; if (real) vk = FromTest(k.vk); }
#endif
            if (real && WasdLogic.Handles(vk))
            {
                int msg = wp.ToInt32();
                bool down = msg is 0x100 or 0x104, up = msg is 0x101 or 0x105;
                if (down || up)
                {
                    long now = _clock.ElapsedMilliseconds; bool front = GameInFront;
                    bool keep = _logic.Key(vk, down, front, now);
                    _logic.Tick(front, now);
                    Flush();
                    if (keep) return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(_hook, code, wp, lp);
    }
}
