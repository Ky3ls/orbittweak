using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OrbitTweak.Core;

/// <summary>
/// The icon inside a file as a PNG data URL, read the way Explorer reads it: the shell loads the file's icon resources as
/// data, nothing is written and no process is touched. Shared by Autostart (48 px, on its own STA thread) and Andere
/// Spiele (<see cref="Game"/>, 96 px). WPF's bitmaps want an STA thread; <see cref="Sta"/> gives one that closes its
/// dispatcher when done.
/// </summary>
internal static class FileIcon
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIconW(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>The icon at <paramref name="index"/> of a file (exe, dll, ico; negative = resource id), drawn at
    /// <paramref name="px"/> × <paramref name="px"/>. False when the file has no such icon; true with a null
    /// <paramref name="png"/> when it has one that could not be turned into a PNG. Call on an STA thread.</summary>
    public static bool TryExtract(string file, int index, int px, out string? png)
    {
        png = null;
        IntPtr big = IntPtr.Zero, small = IntPtr.Zero;
        try
        {
            int hr = SHDefExtractIconW(file, index, 0, out big, out small, (uint)px | (16u << 16));
            if (hr == 0 && big != IntPtr.Zero) { png = Png(big); return true; }
        }
        catch { }
        finally
        {
            if (big != IntPtr.Zero) DestroyIcon(big);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
        return false;
    }

    /// <summary>An HICON as PNG data URL, with its alpha (WPF reads the icon, no System.Drawing).</summary>
    public static string? Png(IntPtr hIcon)
    {
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }

    /// <summary>Runs f on a thread of its own in a single-threaded apartment (the shell's icon calls want an initialized
    /// COM apartment, WPF's bitmaps a thread of their own instead of a pool thread).</summary>
    public static Task<T> Sta<T>(Func<T> f, string name)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { tcs.SetResult(f()); }
            catch (Exception ex) { tcs.SetException(ex); }
            finally
            {
                // WPF's bitmaps gave this thread a dispatcher (and a hidden window): close it with the thread
                try { Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown(); } catch { }
            }
        }) { IsBackground = true, Name = name };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    // ---------- Andere Spiele ----------

    /// <summary>Drawn at 52 px at most on the page: sharp up to about 185 % scaling.</summary>
    private const int GamePx = 96;

    /// <summary>The icons read so far by path, with the file's write time they were read at: an updated game shows its
    /// new icon, and og.games (polled while a game runs) never reads the same exe twice.</summary>
    private static readonly ConcurrentDictionary<string, (DateTime at, string? png)> Games = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The icon of a game's own exe for the page "Andere Spiele": the first of the files that has one, null
    /// when none is there or none can be read. Never throws. Blocks the calling (pool) thread while a new icon is read
    /// on an STA thread; the hosts call it inside Task.Run like the rest of og.*.</summary>
    public static string? Game(params string?[] exes)
    {
        foreach (var exe in exes)
        {
            try
            {
                if (string.IsNullOrEmpty(exe)) continue;
                var at = File.GetLastWriteTimeUtc(exe);
                if (at.Year < 1700) continue;   // not there: Windows reports 1601-01-01
                if (Games.TryGetValue(exe, out var hit) && hit.at == at)
                {
                    if (hit.png != null) return hit.png;
                    continue;
                }
                // a file the shell cannot read in time (a slow drive) counts as one without an icon until it changes
                var read = Sta(() => TryExtract(exe, 0, GamePx, out var p) ? p : null, "OrbitTweak game icon");
                var png = read.Wait(TimeSpan.FromSeconds(4)) ? read.Result : null;
                Games[exe] = (at, png);
                if (png != null) return png;
            }
            catch { }
        }
        return null;
    }
}
