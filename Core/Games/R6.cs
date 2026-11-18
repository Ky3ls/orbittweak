using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OrbitTweak.Core;

/// <summary>
/// Rainbow Six Siege (2.30): the Windows part (finding the game and its GameSettings.ini, writing, backups, Revert).
/// Pure rules and the items: Core/Games/R6Logic.cs.
/// Anti-cheat (BattlEye, Ubisoft's kernel anti-cheat): only GameSettings.ini (the file the game's own menu writes) and the
/// Windows graphics setting of RainbowSix.exe change. The running game is only seen by its process name, never opened.
/// </summary>
internal sealed class R6Tweaks : IGameTweaks
{
    public string Id => "r6";
    public string Name => R6Logic.Name;

    private const string GpuField = "GpuPreference";
    private static string GpuBkKey => $@"{Bk.User}\{R6Logic.BkGpu}";

    // =============================================================== page
    public GameInfo Info()
    {
        var dir = InstallDir();
        var t = Pick();
        bool running = Running();
        return new GameInfo
        {
            Id = Id,
            Name = Name,
            // a found settings file counts too: an install the registry does not know (Xbox app …) still gets the INI items
            Installed = dir != null || t.File != null,
            Path = dir,
            Running = running,
            ConfigFound = t.File != null,
            ConfigPath = t.File,
            Note = R6Logic.Note(dir != null, t.File != null, running, t.Profiles, t.Older),
            AntiCheat = R6Logic.AntiCheat,
        };
    }

    public List<GameItem> Items()
    {
        var t = Pick();
        string? text = null, unreadable = null;
        if (t.File != null) try { text = Read(t.File).text; } catch (InvalidOperationException e) { unreadable = e.Message.TrimEnd('.'); }
        var exe = ExePath();
        bool two = Hw.SwitchableGpus;
        var list = new List<GameItem>();
        foreach (var s in R6Logic.Specs)
        {
            if (s.IsGpu)
            {
                var why = exe == null ? R6Logic.NoExe : !two ? R6Logic.OneGpu : "";
                list.Add(Make(s, exe != null && GpuPrefs.Get(exe, GpuField) == "2", why, GpuChanged()));
            }
            else
            {
                var raw = text == null ? null : IniText.Get(text, s.Section!, s.Key!);
                // the backups belong to one file: only that one shows "changed"
                bool changed = t.Backed && R6Logic.IsChanged(Reg.Get(Bk.User, R6Logic.BkKey(s)) as string, raw);
                // a file that is there but cannot be read says why, not "start the game once"
                list.Add(Make(s, R6Logic.ToPage(s, raw), unreadable ?? R6Logic.WhyNot(s, text, two), changed));
            }
        }
        return list;
    }

    private static GameItem Make(R6Spec s, object? value, string why, bool changed) => new()
    {
        Id = s.Id,
        Title = s.Title,
        Sum = s.Sum,
        Desc = s.Desc,
        Kind = s.Kind,
        Value = value,
        Options = s.Options?.Select(o => new GameOption(o.V, o.T)).ToList(),
        Min = s.Min,
        Max = s.Max,
        Step = s.Step,
        Unit = s.Unit,
        Rec = s.Rec,
        RecValue = s.RecValue,
        Effect = s.Effect,
        Source = s.Source,
        NeedsClosed = s.NeedsClosed,
        Available = why == "",
        Why = why,
        Changed = changed,
    };

    // =============================================================== set
    public object? Set(string itemId, JsonElement value)
    {
        var s = R6Logic.Find(itemId) ?? throw new InvalidOperationException("Unbekannte Einstellung.");
        return s.IsGpu ? SetGpu(R6Logic.ToBool(value, s.Title)) : SetIni(s, value);
    }

    private static object? SetIni(R6Spec s, JsonElement value)
    {
        var target = R6Logic.ToIni(s, value);
        DropStale();
        var t = Pick();
        if (t.File == null) throw Err(R6Logic.NoConfig);
        var (text, bom) = Read(t.File);
        var why = R6Logic.WhyNot(s, text, Hw.SwitchableGpus);
        if (why != "") throw Err(why);
        var cur = IniText.Get(text, s.Section!, s.Key!);
        if (R6Logic.Same(cur, target)) return R6Logic.ToPage(s, cur);
        if (Running()) throw Err(R6Logic.RunningError);

        // backup before the first write: the old value ("-" = not there) and which profile's file it belongs to
        var key = R6Logic.BkKey(s);
        bool newFile = Reg.Get(Bk.User, R6Logic.BkFile) == null, newKey = Reg.Get(Bk.User, key) == null;
        if (newFile) Reg.SetStr(Bk.User, R6Logic.BkFile, t.File);
        if (newKey) Reg.SetStr(Bk.User, key, cur ?? "-");
        try { Write(t.File, IniText.Set(text, s.Section!, s.Key!, target), bom); }
        catch
        {
            // a refused write leaves no backup behind
            if (newKey) Reg.Del(Bk.User, key);
            if (newFile) Reg.Del(Bk.User, R6Logic.BkFile);
            throw;
        }
        return R6Logic.ToPage(s, IniText.Get(Read(t.File).text, s.Section!, s.Key!));
    }

    /// <summary>Windows reads the field when the game starts, so the game does not need to be closed.</summary>
    private static bool SetGpu(bool on)
    {
        var exe = ExePath();
        if (exe == null) throw Err(R6Logic.NoExe);
        var cur = GpuPrefs.Get(exe, GpuField);
        switch (R6Logic.GpuPlan(on, cur, Reg.Get(GpuBkKey, exe) as string))
        {
            case R6Logic.GpuStep.High:
                if (!Hw.SwitchableGpus) throw Err(R6Logic.OneGpu);
                GpuPrefs.SetField(exe, GpuField, "2", R6Logic.BkGpu);
                break;
            case R6Logic.GpuStep.Default:
                GpuPrefs.SetField(exe, GpuField, "0", R6Logic.BkGpu);
                break;
            case R6Logic.GpuStep.Restore:
                GpuPrefs.RestoreField(exe, GpuField, R6Logic.BkGpu);
                break;
        }
        return GpuPrefs.Get(exe, GpuField) == "2";
    }

    // =============================================================== revert
    public int Revert()
    {
        var keys = R6Logic.Specs.Where(s => !s.IsGpu && Reg.Get(Bk.User, R6Logic.BkKey(s)) is string).ToList();
        var file = Reg.Get(Bk.User, R6Logic.BkFile) as string;
        bool ini = keys.Count > 0 && file != null && File.Exists(file);
        if (ini && Running()) throw Err(R6Logic.RunningError);

        int n = 0;
        if (ini)
        {
            var (text, bom) = Read(file!);
            var next = text;
            foreach (var s in keys)
            {
                var b = Reg.Get(Bk.User, R6Logic.BkKey(s)) as string;
                if (R6Logic.IsChanged(b, IniText.Get(next, s.Section!, s.Key!))) n++;
                next = R6Logic.Restore(next, s, b);
            }
            if (next != text) Write(file!, next, bom);   // throws (locked, read-only): the backups stay for the next try
        }
        // the file is back (or its profile is gone, then there is nothing to restore): the backups are done
        DropIni();

        bool gpu = false;
        foreach (var exe in GpuPrefs.Backed(R6Logic.BkGpu))
        {
            if (R6Logic.IsChanged(Reg.Get(GpuBkKey, exe) as string, GpuPrefs.Get(exe, GpuField))) gpu = true;
            GpuPrefs.RestoreField(exe, GpuField, R6Logic.BkGpu);
        }
        return gpu ? n + 1 : n;
    }

    private static bool GpuChanged()
        => GpuPrefs.Backed(R6Logic.BkGpu).Any(exe => R6Logic.IsChanged(Reg.Get(GpuBkKey, exe) as string, GpuPrefs.Get(exe, GpuField)));

    private static bool HasIniBackups() => R6Logic.Specs.Any(s => !s.IsGpu && Reg.Get(Bk.User, R6Logic.BkKey(s)) != null);

    private static void DropIni()
    {
        foreach (var s in R6Logic.Specs) if (!s.IsGpu) Reg.Del(Bk.User, R6Logic.BkKey(s));
        Reg.Del(Bk.User, R6Logic.BkFile);
    }

    /// <summary>Backups of a profile whose file is gone (account folder deleted) can never be restored: they must not
    /// count for the file that is written now.</summary>
    private static void DropStale()
    {
        var file = Reg.Get(Bk.User, R6Logic.BkFile) as string;
        bool keys = HasIniBackups();
        if ((keys || file != null) && (file == null || !keys || !File.Exists(file))) DropIni();
    }

    // =============================================================== the settings file
    /// <summary>File: the GameSettings.ini to read and write. Backed: it is the file the backups belong to. Older: that
    /// file is not the one played last (another account played since).</summary>
    private sealed record Target(string? File, int Profiles, bool Backed, bool Older);

    /// <summary>While OrbitTweak has backups it stays with that profile's file, so Zurücksetzen goes back to the same one;
    /// otherwise the GameSettings.ini written last (the account that played last).</summary>
    private static Target Pick()
    {
        var all = Profiles();
        var newest = R6Logic.Newest(all.Select(f => (f.FullName, f.LastWriteTimeUtc)));
        if (HasIniBackups() && Reg.Get(Bk.User, R6Logic.BkFile) is string pin && SafeExists(pin))
            return new Target(pin, Math.Max(all.Count, 1), true, newest != null && !newest.Equals(pin, StringComparison.OrdinalIgnoreCase));
        return new Target(newest, all.Count, false, false);
    }

    /// <summary>Every Documents\My Games\Rainbow Six - Siege\&lt;profile GUID&gt;\GameSettings.ini. Documents comes from
    /// Windows (it can be moved to OneDrive); the classic folder in the user profile is looked at too.</summary>
    private static List<FileInfo> Profiles()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<FileInfo>();
        foreach (var r in roots)
        {
            if (string.IsNullOrEmpty(r)) continue;
            try
            {
                var dir = Path.Combine(r, "My Games", "Rainbow Six - Siege");
                if (!Directory.Exists(dir)) continue;
                foreach (var d in Directory.GetDirectories(dir))
                {
                    if (!R6Logic.IsProfileFolder(Path.GetFileName(d))) continue;
                    var f = new FileInfo(Path.Combine(d, R6Logic.SettingsFile));
                    if (f.Exists && seen.Add(f.FullName)) list.Add(f);
                }
            }
            catch { }
        }
        return list;
    }

    /// <summary>The file's text; a file that cannot be read comes back as a German error, not as the system's message.</summary>
    private static (string text, bool bom) Read(string file)
    {
        try { return R6Logic.Decode(File.ReadAllBytes(file)); }
        catch (IOException e) when (e is FileNotFoundException or DirectoryNotFoundException) { throw Err(R6Logic.NoConfig); }
        catch (UnauthorizedAccessException) { throw Err(R6Logic.NoAccess); }
        catch (IOException) { throw Err(R6Logic.Locked); }
    }

    /// <summary>All at once: the new text goes to a temp file next to it, which then replaces the file (CRLF, encoding and
    /// every other byte stay). A read-only file is not unlocked: the user set that on purpose.</summary>
    private static void Write(string file, string text, bool bom)
    {
        if (new FileInfo(file).IsReadOnly) throw Err(R6Logic.ReadOnly);
        var tmp = file + ".orbittweak.tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(R6Logic.Encode(text, bom));
                fs.Flush(true);
            }
            File.Replace(tmp, file, null);
        }
        catch (IOException e) when (e is FileNotFoundException or DirectoryNotFoundException) { throw Err(R6Logic.NoConfig); }
        // access denied while the file is not read-only: Windows' ransomware protection or the permissions, not a lock
        catch (UnauthorizedAccessException) { throw Err(R6Logic.NoAccess); }
        catch (IOException) { throw Err(R6Logic.Locked); }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    // =============================================================== the game
    /// <summary>By process name only: BattlEye and Ubisoft's anti-cheat protect the game, so it is never opened (no
    /// handle, no MainModule, no path).</summary>
    internal static bool Running()
    {
        foreach (var n in R6Logic.ProcessNames)
        {
            var ps = Process.GetProcessesByName(n);
            bool any = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            if (any) return true;
        }
        return false;
    }

    /// <summary>The full path of RainbowSix.exe (the DirectX 12 game, not the BattlEye launcher), null when not found.</summary>
    private static string? ExePath()
    {
        var d = InstallDir();
        if (d == null) return null;
        try { return Path.GetFullPath(Path.Combine(d, R6Logic.Exe)); } catch { return null; }
    }

    /// <summary>The first install folder that has RainbowSix.exe: Ubisoft Connect, then Steam, then Epic.</summary>
    internal static string? InstallDir()
    {
        foreach (var c in Candidates())
        {
            var d = R6Logic.NormalizeDir(c);
            if (d != null && SafeExists(Path.Combine(d, R6Logic.Exe))) return d;
        }
        return null;
    }

    private static IEnumerable<string?> Candidates()
    {
        const string ubi32 = @"HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
        const string ubi64 = @"HKLM\SOFTWARE\Ubisoft\Launcher\Installs";
        yield return Reg.Get($@"{ubi32}\{R6Logic.UbiProduct}", "InstallDir") as string;
        yield return Reg.Get($@"{ubi64}\{R6Logic.UbiProduct}", "InstallDir") as string;
        // installs linked to another store can sit under another product id
        foreach (var root in new[] { ubi32, ubi64 })
            foreach (var sub in Reg.SubKeys(root))
                if (sub != R6Logic.UbiProduct && Reg.Get($@"{root}\{sub}", "InstallDir") is string d && R6Logic.IsSiegeName(d)) yield return d;
        yield return Reg.Get($@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install {R6Logic.UbiProduct}", "InstallLocation") as string;
        foreach (var d in SteamDirs()) yield return d;
        foreach (var d in EpicDirs()) yield return d;
    }

    /// <summary>steamapps\common\&lt;installdir&gt; of app 359550 in every Steam library.</summary>
    private static List<string> SteamDirs()
    {
        var list = new List<string>();
        try
        {
            var steam = R6Logic.NormalizeDir(Reg.Get(@"HKCU\Software\Valve\Steam", "SteamPath") as string)
                        ?? R6Logic.NormalizeDir(Reg.Get(@"HKLM\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") as string);
            if (steam == null) return list;
            var libs = new List<string> { steam };
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) libs.AddRange(R6Logic.SteamLibraries(File.ReadAllText(vdf)));
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var acf = Path.Combine(lib, "steamapps", $"appmanifest_{R6Logic.SteamApp}.acf");
                    if (File.Exists(acf) && R6Logic.AcfInstallDir(File.ReadAllText(acf)) is { } dir)
                        list.Add(Path.Combine(lib, "steamapps", "common", dir));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    /// <summary>Epic's manifests (%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item).</summary>
    private static List<string> EpicDirs()
    {
        var list = new List<string>();
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.item"))
            {
                try { if (R6Logic.EpicInstall(File.ReadAllText(f)) is { } d) list.Add(d); }
                catch { }
            }
        }
        catch { }
        return list;
    }

    private static bool SafeExists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }

    private static InvalidOperationException Err(string msg) => new(msg.EndsWith('.') ? msg : msg + ".");
}
