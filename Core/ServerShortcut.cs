using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static OrbitTweak.Core.ServerWatchLogic;

namespace OrbitTweak.Core;

/// <summary>
/// 2.31, Server auf den Desktop: a real .lnk on the desktop that starts "OrbitTweak.exe --connect &lt;code&gt;", so the
/// pre-launch boost runs before FiveM connects. A .lnk and not a .url: only a shortcut to the exe carries the
/// arguments, and the shell shows its own icon (the server's, as a PNG-in-ICO under %AppData%\OrbitTweak\icons).
/// Also the hand-over when OrbitTweak is already running: the second start sends the code to the running window
/// (WM_COPYDATA, checked strictly on both sides) and ends.
/// </summary>
internal static class ServerShortcut
{
    public static string IconDir => Path.Combine(AppDirs.Roaming, "icons");

    public sealed record Result(bool Ok, string? Path, string? File, bool ServerIcon, bool Replaced, string? Error);

    // ---------- the shortcut ----------

    /// <summary>Writes (or renews) the desktop shortcut for a join code. Never throws.</summary>
    public static async Task<Result> Create(string code, string name, string? iconUrl)
    {
        try
        {
            if (!ValidCode(code)) return Fail("Kein gültiger Join-Code.");
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !System.IO.File.Exists(exe)) return Fail("Der Pfad von OrbitTweak.exe ist unbekannt.");
            var ico = await IconFile(code, iconUrl);
            return await FileIcon.Sta(() => Write(code, name, exe, ico), "OrbitTweak Verknüpfung");
        }
        catch (Exception ex) { return Fail("Verknüpfung nicht erstellt: " + ex.Message); }
    }

    private static Result Fail(string e) => new(false, null, null, false, false, e);

    private static Result Write(string code, string name, string exe, string? ico)
    {
        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk)) return Fail("Den Desktop-Ordner gibt es nicht.");
        var args = ShortcutArgs(code);
        var baseName = FileName(name, code);
        string? path = null; bool replaced = false;
        for (int n = 1; n <= 20 && path == null; n++)
        {
            var p = Path.Combine(desk, Numbered(baseName, n) + ".lnk");
            if (!System.IO.File.Exists(p)) { path = p; break; }
            // our own shortcut for the same server is renewed (new icon, OrbitTweak moved); anything else stays untouched
            if (ReadLink(p) is { } old && string.Equals(Path.GetFileName(old.target), Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase) && old.args.Trim() == args)
            { path = p; replaced = true; }
        }
        if (path == null) return Fail("Auf dem Desktop gibt es schon zu viele Verknüpfungen mit diesem Namen.");

        object? o = null;
        try
        {
            o = new ShellLink();
            var l = (IShellLinkW)o;
            l.SetPath(exe);
            l.SetArguments(args);
            l.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            var shown = ServerName(name, "FiveM-Server " + code);
            l.SetDescription($"Mit {shown} verbinden (OrbitTweak startet FiveM mit deinem Pre-Launch-Boost)");
            l.SetIconLocation(ico ?? exe, 0);
            l.SetShowCmd(1);
            ((IPersistFile)o).Save(path, true);
        }
        finally { if (o != null) try { Marshal.FinalReleaseComObject(o); } catch { } }
        if (!System.IO.File.Exists(path)) return Fail("Windows hat die Verknüpfung nicht gespeichert.");
        return new(true, path, Path.GetFileName(path), ico != null, replaced, null);
    }

    // ---------- the server icon ----------

    /// <summary>The server's icon as .ico, made from its picture on Cfx.re (PNG or WebP; WebP needs the Windows WebP
    /// codec, which current Windows has). Null when there is none or it cannot be read: the shortcut then shows the
    /// app's own icon. An icon made earlier is used when the download fails.</summary>
    private static async Task<string?> IconFile(string code, string? url)
    {
        var file = Path.Combine(IconDir, code + ".ico");
        if (IconUrl(url) is { } u)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var r = await Net.Http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                const int max = 2 * 1024 * 1024;
                if (r.IsSuccessStatusCode && !(r.Content.Headers.ContentLength > max))
                {
                    using var s = await r.Content.ReadAsStreamAsync(cts.Token);
                    using var ms = new MemoryStream();
                    var buf = new byte[65536]; int n; bool big = false;
                    while ((n = await s.ReadAsync(buf, cts.Token)) > 0) { if (ms.Length + n > max) { big = true; break; } ms.Write(buf, 0, n); }
                    if (!big)
                    {
                        var data = ms.ToArray();
                        var ico = await FileIcon.Sta(() => MakeIco(data), "OrbitTweak Servericon");
                        if (ico != null)
                        {
                            Directory.CreateDirectory(IconDir);
                            var tmp = file + ".tmp";
                            await System.IO.File.WriteAllBytesAsync(tmp, ico);
                            System.IO.File.Move(tmp, file, true);
                            return file;
                        }
                    }
                }
            }
            catch { }
        }
        return System.IO.File.Exists(file) ? file : null;
    }

    /// <summary>256, 48, 32 and 16 px, the picture fitted into the square (centred, transparent around it).</summary>
    private static byte[]? MakeIco(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (dec.Frames.Count == 0) return null;
            var src = dec.Frames[0];
            if (src.PixelWidth < 8 || src.PixelHeight < 8 || src.PixelWidth > 4096 || src.PixelHeight > 4096) return null;
            var pngs = new[] { 256, 48, 32, 16 }.Select(px => Render(src, px)).ToList();
            return Ico(pngs);
        }
        catch { return null; }
    }

    private static byte[] Render(BitmapSource src, int px)
    {
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        double k = Math.Min((double)px / src.PixelWidth, (double)px / src.PixelHeight);
        double w = src.PixelWidth * k, h = src.PixelHeight * k;
        using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect((px - w) / 2, (px - h) / 2, w, h));
        var rt = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rt.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rt));
        using var o = new MemoryStream();
        enc.Save(o);
        return o.ToArray();
    }

    // ---------- hand-over to the running app ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam, uint flags, uint timeout, out IntPtr result);

    public const int WM_COPYDATA = 0x004A;

    /// <summary>A second start with "--connect &lt;code&gt;" hands the code to the running window (Program.OnlyInstance).
    /// Nothing is sent for anything but a valid join code.</summary>
    public static void Forward(IntPtr hwnd, string[] args)
    {
        if (hwnd == IntPtr.Zero || ConnectArg(args) is not { } code || CopyBytes(code) is not { } bytes) return;
        var mem = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, mem, bytes.Length);
            var cds = new COPYDATASTRUCT { dwData = (IntPtr)CopyTag, cbData = bytes.Length, lpData = mem };
            SendMessageTimeout(hwnd, WM_COPYDATA, IntPtr.Zero, ref cds, 0x0002 /* SMTO_ABORTIFHUNG */, 3000, out _);
        }
        catch { }
        finally { Marshal.FreeHGlobal(mem); }
    }

    /// <summary>The join code in a WM_COPYDATA message, or null (any program can send this message to the window).</summary>
    public static string? ReadCopy(IntPtr lParam)
    {
        try
        {
            if (lParam == IntPtr.Zero) return null;
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if ((ulong)cds.dwData.ToInt64() != CopyTag || cds.cbData < 8 || cds.cbData > 16 || cds.lpData == IntPtr.Zero) return null;
            var data = new byte[cds.cbData];
            Marshal.Copy(cds.lpData, data, 0, cds.cbData);
            return CodeFromCopy(CopyTag, data);
        }
        catch { return null; }
    }

    // ---------- the shell's link object (no WSH needed) ----------

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    private static (string target, string args)? ReadLink(string file)
    {
        object? o = null;
        try
        {
            o = new ShellLink();
            ((IPersistFile)o).Load(file, 0);   // STGM_READ
            var l = (IShellLinkW)o;
            var path = new StringBuilder(1024); l.GetPath(path, path.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */);
            var args = new StringBuilder(1024); l.GetArguments(args, args.Capacity);
            return (path.ToString(), args.ToString());
        }
        catch { return null; }
        finally { if (o != null) try { Marshal.FinalReleaseComObject(o); } catch { } }
    }
}
