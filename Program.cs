using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrbitTweak;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);

    private static Mutex? _single;

    /// <summary>
    /// Normal start: the app. With "--recorder" the same exe runs as the small helper process that records the game
    /// for the clipper (see Core/RecorderHost.cs), with "--join" as the one that joins clips into a highlight video
    /// (Core/Highlight.cs); the helpers have no window.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--recorder") return Core.RecorderHost.RunChild(args);
        if (args.Length > 1 && args[0] == "--join") return Core.Highlight.RunChild(args);
        if (!OnlyInstance(args)) return 0;
        App.Main();
        return 0;
    }

    /// <summary>
    /// The app has to run once only. Two instances both hook the keyboard for Snap Tap; the hook that comes first
    /// swallows key events the other one then never sees, so the second one presses keys "back" that are not held
    /// any more (W, A, S or D seems to press itself). Two instances would also both record and save every clip twice.
    /// A second start brings the running window to the front and ends. After an update the new instance waits for
    /// the old one to close.
    /// </summary>
    private static bool OnlyInstance(string[] args)
    {
        string name = @"Local\OrbitTweak.App.SingleInstance";
#if DEBUG
        // test runs work in their own data folder next to the real app; ORBIT_TEST_SINGLE=1 switches the guard on for them
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORBIT_TEST_DATA")))
        {
            if (Environment.GetEnvironmentVariable("ORBIT_TEST_SINGLE") != "1") return true;
            name += ".Test";
        }
#endif
        try
        {
            _single = new Mutex(false, name);
            bool mine;
            try { mine = _single.WaitOne(args.Contains("--updated") ? 15000 : 2500); }
            catch (AbandonedMutexException) { mine = true; } // the previous instance ended without releasing it
            if (mine) return true;
        }
        catch { return true; } // the guard itself must never keep the app from starting
        try
        {
            int me = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
            {
                if (p.Id == me) continue;
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue; // the recorder helper has no window
                if (IsIconic(h)) ShowWindow(h, 9);
                SetForegroundWindow(h);
                Core.ServerShortcut.Forward(h, args);   // 2.31: "--connect <code>" from a desktop shortcut goes to the running app
                break;
            }
        }
        catch { }
        return false;
    }
}
