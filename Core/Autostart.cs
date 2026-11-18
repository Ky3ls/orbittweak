using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OrbitTweak.Core;

/// <summary>One program that starts with Windows (what the page shows).</summary>
public sealed class AutostartItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The Run value name or the file name in the startup folder.</summary>
    public string Entry { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Command { get; init; } = "";
    /// <summary>The file that is started ("" when it could not be found out, e.g. a web link).</summary>
    public string Path { get; init; } = "";
    /// <summary>False only when a known path was looked at and nothing is there.</summary>
    public bool Exists { get; init; } = true;
    public string Source { get; init; } = "";
    public bool AllUsers { get; init; }
    public bool Folder { get; init; }
    /// <summary>A Store app's startup task (2.28): switched by its State value, nothing to show in Explorer.</summary>
    public bool Store { get; init; }
    /// <summary>A group policy decides (Store apps, State 3 or 4): the switch is locked.</summary>
    public bool Lock { get; init; }
    public bool On { get; init; }
    public DateTime? OffSince { get; init; }
    /// <summary>The program's icon as PNG data URL, or null (the page draws its own).</summary>
    public string? Icon { get; init; }
    /// <summary>"off" (only costs boot time), "on" (audio, graphics, input, protection) or null.</summary>
    public string? Rec { get; init; }
    public string Why { get; init; } = "";
}

/// <summary>The autostart manager: lists the three Run keys, the two startup folders and the Store apps' startup tasks
/// and switches entries on and off exactly like the Task Manager (the binary flags under Explorer\StartupApproved, the
/// State value of a Store app in AutostartStore.cs). Nothing is ever deleted. The pure rules live in AutostartLogic.cs.</summary>
internal static partial class Autostart
{
    private const int IconPx = 48;   // drawn at 32 px on the page: sharp up to 150 % scaling

    // ---------- reading ----------

    /// <summary>Everything that starts with Windows. Runs on its own STA thread: the shell calls for links and icons want
    /// an initialized COM apartment, and WPF's bitmaps get a thread of their own instead of a pool thread.
    /// The Store apps are read meanwhile on a pool thread (the package list needs no apartment, and their logos are files).</summary>
    public static async Task<List<AutostartItem>> ListAsync()
    {
        var store = Task.Run(() => StoreList(true));
        var list = await Sta(() => List(true));
        list.AddRange(await store);
        return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static List<AutostartItem> List(bool icons)
    {
        var list = new List<AutostartItem>();
        foreach (var s in AutostartLogic.Sources)
        {
            try
            {
                var flags = Flags(s.ApprovedKey);
                if (s.Folder) ReadFolder(s, flags, list, icons);
                else ReadRun(s, flags, list, icons);
            }
            catch { }   // one source that cannot be read does not hide the others
        }
        return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ReadRun(AutostartSource s, Dictionary<string, byte[]> flags, List<AutostartItem> list, bool icons)
    {
        using var k = Open(s.RunKey);
        if (k == null) return;
        foreach (var name in k.GetValueNames())
        {
            if (name.Length == 0) continue;   // the (Default) value starts nothing
            if (k.GetValue(name) is not string cmd || cmd.Trim().Length == 0) continue;
            try { list.Add(FromCommand(s, name, name, cmd, flags, icons)); } catch { }
        }
    }

    private static void ReadFolder(AutostartSource s, Dictionary<string, byte[]> flags, List<AutostartItem> list, bool icons)
    {
        var dir = FolderOf(s);
        if (dir.Length == 0 || !Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            try
            {
                var fi = new FileInfo(file);
                // Windows opens every file in the folder at sign-in (a .vbs as much as a .lnk, hidden or not): all of them
                // are listed, only desktop.ini is the folder's own
                if (!AutostartLogic.StartupFile(fi.Name)) continue;
                var ext = fi.Extension.ToLowerInvariant();
                var entry = fi.Name;
                var stem = System.IO.Path.GetFileNameWithoutExtension(entry);
                if (ext == ".lnk") list.Add(FromLink(s, entry, stem, file, flags, icons));
                else if (ext == ".url") list.Add(FromUrl(s, entry, stem, file, flags, icons));
                else list.Add(FromCommand(s, entry, stem, "\"" + file + "\"", flags, icons));
            }
            catch { }
        }
    }

    /// <summary>A Run value, an .exe/.bat in the folder, or a link's target with its arguments.</summary>
    private static AutostartItem FromCommand(AutostartSource s, string entry, string shown, string cmd, Dictionary<string, byte[]> flags, bool icons,
        string? iconFile = null, int iconIndex = 0, string? linkFile = null)
    {
        var expanded = Environment.ExpandEnvironmentVariables(cmd.Trim());
        var (exe, args) = AutostartLogic.SplitCommand(expanded);
        var target = Resolve(exe);
        // rundll32 starts a DLL: that is the file that has to be there
        var dll = AutostartLogic.Rundll32Target(exe, args);
        if (dll != null) target = Resolve(dll);
        // a link in the startup folder may open a folder: that is there, too
        bool exists = target.Length == 0 || File.Exists(target) || Directory.Exists(target);
        // the file whose name, publisher and icon count: Update.exe of a Squirrel install starts the app next to it
        var info = target;
        var sq = AutostartLogic.SquirrelApp(exe, args);
        if (sq != null && exists && target.Length > 0) info = SquirrelExe(target, sq) ?? target;

        // cmd, PowerShell, wscript … only run a script: their "Windows PowerShell · Microsoft Corporation" would make
        // any script look like a part of Windows, so such an entry keeps its own name and shows no publisher (a DLL
        // started by rundll32 is the program itself and does count)
        string desc = "", product = "", company = "";
        if (exists && info.Length > 0 && (dll != null || !AutostartLogic.ScriptHost(exe)) && File.Exists(info))
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(info);
                desc = v.FileDescription ?? ""; product = v.ProductName ?? ""; company = v.CompanyName ?? "";
            }
            catch { }
        }
        var name = AutostartLogic.DisplayName(shown, info.Length > 0 ? info : exe, desc, product);
        var publisher = AutostartLogic.Tidy(company);
        var advice = AutostartLogic.Recommend(entry, name, info.Length > 0 ? info : exe, publisher, args, !exists);
        flags.TryGetValue(entry, out var flag);

        string? icon = null;
        if (icons)
        {
            if (!string.IsNullOrEmpty(iconFile)) icon = IconOf(iconFile, iconIndex);
            // a DLL started by rundll32 rarely has an icon of its own: then the page's own mark reads better than a DLL symbol
            if (icon == null && exists && info.Length > 0) icon = IconOf(info, 0, dll == null);
            if (icon == null && linkFile != null && exists) icon = ShellIconOf(linkFile);
        }
        return new AutostartItem
        {
            Id = AutostartLogic.Id(s, entry), Name = name, Entry = entry, Publisher = publisher,
            Command = expanded, Path = target, Exists = exists, Source = s.Label, AllUsers = s.AllUsers, Folder = s.Folder,
            On = AutostartLogic.IsOn(flag), OffSince = AutostartLogic.OffSince(flag), Icon = icon,
            Rec = advice?.Rec, Why = advice?.Why ?? "",
        };
    }

    private static AutostartItem FromLink(AutostartSource s, string entry, string stem, string file, Dictionary<string, byte[]> flags, bool icons)
    {
        var l = ReadLink(file);
        if (l is { } link && link.target.Length > 0)
        {
            var cmd = "\"" + link.target + "\"" + (link.args.Length > 0 ? " " + link.args : "");
            var iconFile = link.icon.Length > 0 ? Environment.ExpandEnvironmentVariables(link.icon) : null;
            return FromCommand(s, entry, stem, cmd, flags, icons, iconFile, link.iconIndex, file);
        }
        // a link the shell cannot read (or one to something that is no file): the link itself is the entry
        flags.TryGetValue(entry, out var flag);
        var advice = AutostartLogic.Recommend(entry, stem, file, "", "", false);
        return new AutostartItem
        {
            Id = AutostartLogic.Id(s, entry), Name = stem, Entry = entry, Command = file, Path = file, Exists = true,
            Source = s.Label, AllUsers = s.AllUsers, Folder = true, On = AutostartLogic.IsOn(flag), OffSince = AutostartLogic.OffSince(flag),
            Icon = icons ? ShellIconOf(file) : null, Rec = advice?.Rec, Why = advice?.Why ?? "",
        };
    }

    /// <summary>An internet shortcut: [InternetShortcut] URL=…, IconFile=…, IconIndex=….</summary>
    private static AutostartItem FromUrl(AutostartSource s, string entry, string stem, string file, Dictionary<string, byte[]> flags, bool icons)
    {
        string url = "", iconFile = ""; int iconIndex = 0;
        foreach (var line in File.ReadLines(file).Take(64))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line[..eq].Trim(); var v = line[(eq + 1)..].Trim();
            if (k.Equals("URL", StringComparison.OrdinalIgnoreCase)) url = v;
            else if (k.Equals("IconFile", StringComparison.OrdinalIgnoreCase)) iconFile = Environment.ExpandEnvironmentVariables(v);
            else if (k.Equals("IconIndex", StringComparison.OrdinalIgnoreCase)) int.TryParse(v, out iconIndex);
        }
        flags.TryGetValue(entry, out var flag);
        var advice = AutostartLogic.Recommend(entry, stem, file, "", url, false);
        string? icon = null;
        if (icons) icon = (iconFile.Length > 0 ? IconOf(iconFile, iconIndex) : null) ?? ShellIconOf(file);
        return new AutostartItem
        {
            Id = AutostartLogic.Id(s, entry), Name = stem, Entry = entry, Command = url.Length > 0 ? url : file, Path = "", Exists = true,
            Source = s.Label, AllUsers = s.AllUsers, Folder = true, On = AutostartLogic.IsOn(flag), OffSince = AutostartLogic.OffSince(flag),
            Icon = icon, Rec = advice?.Rec, Why = advice?.Why ?? "",
        };
    }

    /// <summary>A full path for a program name: as it is when rooted, else looked up like Windows does (System32, the
    /// Windows folder, PATH). "" when it is nowhere, then the entry gets no "file missing" warning (we do not know).</summary>
    private static string Resolve(string file)
    {
        if (file.Length == 0) return "";
        try
        {
            if (System.IO.Path.IsPathRooted(file)) return System.IO.Path.GetFullPath(file);
            var names = System.IO.Path.HasExtension(file) ? new[] { file } : new[] { file + ".exe", file };
            var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            foreach (var d in dirs)
                foreach (var n in names)
                {
                    try { var p = System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(d), n); if (File.Exists(p)) return p; } catch { }
                }
        }
        catch { }
        return "";
    }

    /// <summary>The newest app-x.y.z\App.exe next to a Squirrel Update.exe.</summary>
    private static string? SquirrelExe(string update, string app)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(update);
            if (dir == null) return null;
            return Directory.EnumerateDirectories(dir, "app-*")
                .Select(d => System.IO.Path.Combine(d, app)).Where(File.Exists)
                .OrderByDescending(p => File.GetLastWriteTimeUtc(p)).FirstOrDefault();
        }
        catch { return null; }
    }

    private static string FolderOf(AutostartSource s)
        => Environment.GetFolderPath(s.AllUsers ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);

    // ---------- switching ----------

    /// <summary>Switches one entry on or off (only its StartupApproved flag) and returns the state read back.</summary>
    public static bool Set(string id, bool on)
    {
        var p = AutostartLogic.ParseId(id) ?? throw new InvalidOperationException("Unbekannter Autostart-Eintrag.");
        var (s, entry) = p;
        if (s == AutostartLogic.Store) return SetStore(entry, on);
        if (!Present(s, entry)) throw new InvalidOperationException("Den Eintrag gibt es nicht mehr. Lade die Liste neu.");
        var cur = Flag(s.ApprovedKey, entry);
        // on with no flag at all is already on: nothing to write (Windows itself only writes the flag on a change)
        bool write = on ? cur != null && !AutostartLogic.IsOn(cur) : AutostartLogic.IsOn(cur);
        if (write)
        {
            try
            {
                using var k = Create(s.ApprovedKey);
                k.SetValue(entry, AutostartLogic.Flag(on, DateTime.UtcNow), RegistryValueKind.Binary);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
            {
                // OrbitTweak normally runs as admin: then it is not about rights but about something guarding the key
                bool admin; try { admin = Proc.IsAdmin(); } catch { admin = true; }
                throw new InvalidOperationException(s.AllUsers && !admin
                    ? "Windows hat das Umschalten verweigert. Für Programme aller Nutzer braucht OrbitTweak Administratorrechte."
                    : "Windows hat das Umschalten verweigert. Vermutlich schützt ein Virenschutz oder eine Richtlinie diesen Eintrag.");
            }
        }
        return AutostartLogic.IsOn(Flag(s.ApprovedKey, entry));
    }

    /// <summary>The file to show in Explorer for an entry: the link or file in the startup folder, or the program a Run
    /// value starts. Null when there is nothing on disk.</summary>
    public static string? Locate(string id)
    {
        var p = AutostartLogic.ParseId(id);
        if (p == null) return null;
        var (s, entry) = p.Value;
        if (s == AutostartLogic.Store) return null;   // a Store app's folder (WindowsApps) cannot be opened in Explorer
        if (s.Folder)
        {
            if (!AutostartLogic.PlainFileName(entry)) return null;
            var f = System.IO.Path.Combine(FolderOf(s), entry);
            return File.Exists(f) ? f : null;
        }
        using var k = Open(s.RunKey);
        if (k?.GetValue(entry) is not string cmd) return null;
        var (exe, args) = AutostartLogic.SplitCommand(Environment.ExpandEnvironmentVariables(cmd));
        var t = Resolve(AutostartLogic.Rundll32Target(exe, args) ?? exe);
        return t.Length > 0 && File.Exists(t) ? t : null;
    }

    private static bool Present(AutostartSource s, string entry)
    {
        if (s.Folder) return AutostartLogic.PlainFileName(entry) && File.Exists(System.IO.Path.Combine(FolderOf(s), entry));
        using var k = Open(s.RunKey);
        return k?.GetValue(entry) != null;
    }

    // ---------- registry ----------

    private static RegistryKey? Open(string path)
    {
        var (hive, sub) = Split(path);
        using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return b.OpenSubKey(sub);
    }

    private static RegistryKey Create(string path)
    {
        var (hive, sub) = Split(path);
        using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return b.CreateSubKey(sub, true);
    }

    private static (RegistryHive, string) Split(string path)
    {
        int i = path.IndexOf('\\');
        return (path[..i] == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, path[(i + 1)..]);
    }

    private static Dictionary<string, byte[]> Flags(string approvedKey)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var k = Open(approvedKey);
            if (k == null) return d;
            foreach (var n in k.GetValueNames())
                if (k.GetValue(n) is byte[] b) d[n] = b;
        }
        catch { }
        return d;
    }

    private static byte[]? Flag(string approvedKey, string entry)
    {
        try { using var k = Open(approvedKey); return k?.GetValue(entry) as byte[]; }
        catch { return null; }
    }

    // ---------- shortcuts ----------

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

    /// <summary>Target, arguments and icon of a .lnk, read with the shell's own ShellLink object (no WSH needed).
    /// Null when the shell cannot read it.</summary>
    private static (string target, string args, string icon, int iconIndex)? ReadLink(string file)
    {
        object? o = null;
        try
        {
            o = new ShellLink();
            ((IPersistFile)o).Load(file, 0);   // STGM_READ
            var l = (IShellLinkW)o;
            var path = new StringBuilder(1024); l.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            var args = new StringBuilder(4096); l.GetArguments(args, args.Capacity);
            var icon = new StringBuilder(1024); l.GetIconLocation(icon, icon.Capacity, out int idx);
            return (path.ToString(), args.ToString(), icon.ToString(), idx);
        }
        catch { return null; }
        finally { if (o != null) try { Marshal.FinalReleaseComObject(o); } catch { } }
    }

    // ---------- icons ----------

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIconW(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;

    /// <summary>PNG data URLs by "path,index", for the whole run of the app (icons do not change while it runs; a
    /// program updated meanwhile shows its new icon after the next start).</summary>
    private static readonly ConcurrentDictionary<string, string?> Icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The icon in a file (exe, dll, ico) at the given index (negative = resource id), else the shell's icon for
    /// the file. Null when there is none or the file is gone.</summary>
    private static string? IconOf(string file, int index, bool shellFallback = true)
    {
        if (file.Length == 0) return null;
        try { if (!File.Exists(file)) return null; } catch { return null; }
        return Icons.GetOrAdd(file + "," + index + (shellFallback ? "" : ",own"), _ =>
        {
            IntPtr big = IntPtr.Zero, small = IntPtr.Zero;
            try
            {
                int hr = SHDefExtractIconW(file, index, 0, out big, out small, IconPx | (16u << 16));
                if (hr == 0 && big != IntPtr.Zero) return Png(big);
            }
            catch { }
            finally
            {
                if (big != IntPtr.Zero) DestroyIcon(big);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
            return shellFallback ? ShellIcon(file) : null;   // a file without icons of its own: the icon Explorer shows for it
        });
    }

    private static string? ShellIconOf(string file) => Icons.GetOrAdd(file + ",shell", _ => ShellIcon(file));

    private static string? ShellIcon(string file)
    {
        var fi = new SHFILEINFOW();
        try
        {
            var r = SHGetFileInfoW(file, 0, ref fi, (uint)Marshal.SizeOf<SHFILEINFOW>(), SHGFI_ICON | SHGFI_LARGEICON);
            return r != IntPtr.Zero && fi.hIcon != IntPtr.Zero ? Png(fi.hIcon) : null;
        }
        catch { return null; }
        finally { if (fi.hIcon != IntPtr.Zero) DestroyIcon(fi.hIcon); }
    }

    /// <summary>An HICON as PNG data URL, with its alpha (WPF reads the icon, no System.Drawing).</summary>
    private static string? Png(IntPtr hIcon)
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

    // ---------- thread ----------

    private static Task<T> Sta<T>(Func<T> f)
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
        }) { IsBackground = true, Name = "OrbitTweak autostart" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }
}
