using System.Collections.Concurrent;
using System.IO;
using System.Security;
using Microsoft.Win32;
using Windows.Management.Deployment;
using WinPackage = Windows.ApplicationModel.Package;

namespace OrbitTweak.Core;

/// <summary>Store apps (2.28): MSIX packages of the signed-in user that declare a startup task in their manifest (the
/// new Teams, WhatsApp, Spotify from the Store, Phone Link …). Their on/off state is the DWORD "State" under
/// SystemAppData\&lt;family&gt;\&lt;task id&gt;, the same value Settings › Apps › Autostart and the Task Manager switch.
/// Nothing is uninstalled or deleted. The pure rules (manifest, state, logo choice, ids) live in AutostartLogic.cs.</summary>
internal static partial class Autostart
{
    /// <summary>A package's startup tasks with what the page shows for it. Kept per package full name (an update is a new
    /// full name) for the whole run, so a second visit reads no manifests; packages without tasks are kept as Empty.</summary>
    private sealed record StoreApp(string Family, string Name, string Publisher, string Root, AutostartLogic.StoreTask[] Tasks, string? Icon);

    private static readonly StoreApp NoTasks = new("", "", "", "", Array.Empty<AutostartLogic.StoreTask>(), null);
    private static readonly ConcurrentDictionary<string, StoreApp> StoreApps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All startup tasks of the user's Store apps. Empty when the package list cannot be read (the AppX service
    /// is off, an old or stripped Windows): the rest of the page still works.</summary>
    private static List<AutostartItem> StoreList(bool icons)
    {
        var list = new List<AutostartItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in new PackageManager().FindPackagesForUser(string.Empty))
            {
                try
                {
                    var app = StoreAppOf(p, icons);
                    if (app == null) continue;
                    foreach (var t in app.Tasks)
                    {
                        var item = StoreItem(app, t, icons);
                        if (seen.Add(item.Id)) list.Add(item);   // two versions of one family during an update: one row
                    }
                }
                catch { }   // one package that cannot be read does not hide the others
            }
        }
        catch { }
        return list;
    }

    private static AutostartItem StoreItem(StoreApp app, AutostartLogic.StoreTask t, bool icons)
    {
        var entry = AutostartLogic.StoreEntry(app.Family, t.TaskId);
        int? state = StoreStateOf(app.Family, t.TaskId);
        bool locked = AutostartLogic.StoreLocked(state);
        // one app, one row: its name. A package with several tasks (rare) names the task too, when that is plain text
        var name = app.Name;
        if (app.Tasks.Length > 1 && AutostartLogic.PlainText(t.DisplayName) && !t.DisplayName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            name += " · " + t.DisplayName.Trim();
        var exe = t.Executable.Length > 0 && app.Root.Length > 0 ? SafeCombine(app.Root, t.Executable) : "";
        var advice = AutostartLogic.Recommend(entry, name, exe, app.Publisher, "", false);
        return new AutostartItem
        {
            Id = AutostartLogic.Id(AutostartLogic.Store, entry), Name = name, Entry = entry, Publisher = app.Publisher,
            Command = exe.Length > 0 ? exe : @"shell:AppsFolder\" + app.Family + (t.AppId.Length > 0 ? "!" + t.AppId : ""),
            Path = exe, Exists = true, Source = AutostartLogic.Store.Label, AllUsers = false, Folder = false, Store = true,
            On = AutostartLogic.StoreOn(state, t.Enabled), OffSince = null, Icon = icons ? app.Icon : null,
            // a policy decides: no advice to follow, the reason instead
            Lock = locked,
            Rec = locked ? null : advice?.Rec,
            Why = locked ? (state == 4 ? "Eine Richtlinie lässt es immer starten" : "Eine Richtlinie hält es aus") : advice?.Why ?? "",
        };
    }

    /// <summary>The package's tasks, name, publisher and logo, or null when it declares no startup task.</summary>
    private static StoreApp? StoreAppOf(WinPackage p, bool icons)
    {
        var full = p.Id.FullName;
        if (StoreApps.TryGetValue(full, out var known)) return known.Tasks.Length == 0 ? null : known;
        var app = NoTasks;
        try
        {
            var family = p.Id.FamilyName;
            if (!p.IsFramework && !p.IsResourcePackage && AutostartLogic.FamilyOk(family))
            {
                var root = RootOf(p);
                var manifest = root.Length > 0 ? System.IO.Path.Combine(root, "AppxManifest.xml") : "";
                if (manifest.Length > 0 && File.Exists(manifest) && new FileInfo(manifest).Length < 4 << 20)
                {
                    var xml = File.ReadAllText(manifest);
                    // most packages have no startup task: only those are parsed
                    var tasks = xml.Contains("windows.startupTask", StringComparison.OrdinalIgnoreCase) ? AutostartLogic.ParseManifest(xml) : new();
                    if (tasks.Count > 0)
                    {
                        string name = "", publisher = "";
                        try { name = (p.DisplayName ?? "").Trim(); } catch { }
                        if (!AutostartLogic.PlainText(name)) name = tasks.Select(t => t.DisplayName).FirstOrDefault(AutostartLogic.PlainText)?.Trim() ?? p.Id.Name;
                        try { publisher = AutostartLogic.Tidy(p.PublisherDisplayName); } catch { }
                        if (!AutostartLogic.PlainText(publisher)) publisher = "";
                        string? icon = null;
                        if (icons)
                        {
                            foreach (var rel in tasks.Select(t => t.Logo).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                                if ((icon = LogoOf(root, rel)) != null) break;
                            if (icon == null) try { var l = p.Logo; if (l != null && l.IsFile) icon = PngFile(l.LocalPath); } catch { }
                        }
                        app = new StoreApp(family, name, publisher, root, tasks.ToArray(), icon);
                    }
                }
            }
        }
        catch { app = NoTasks; }
        // without icons (only a check) nothing is kept: the next list with icons reads it properly
        if (icons || app.Tasks.Length == 0) StoreApps[full] = app;
        return app.Tasks.Length == 0 ? null : app;
    }

    /// <summary>The package's folder. InstalledPath is there from Windows 10 2004 on; before that InstalledLocation.</summary>
    private static string RootOf(WinPackage p)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            try { var r = p.InstalledPath; if (!string.IsNullOrEmpty(r)) return r; } catch { }
        try { return p.InstalledLocation?.Path ?? ""; } catch { return ""; }
    }

    /// <summary>A path inside the package folder, or "" when the relative part would leave it.</summary>
    private static string SafeCombine(string root, string rel)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, rel));
            var r = System.IO.Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return full.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? full : "";
        }
        catch { return ""; }
    }

    /// <summary>The manifest logo as PNG data URL: the best scale/target-size file next to the name the manifest gives.</summary>
    private static string? LogoOf(string root, string rel)
    {
        try
        {
            var path = SafeCombine(root, rel);
            if (path.Length == 0) return null;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (dir == null || !Directory.Exists(dir)) return null;
            var file = System.IO.Path.GetFileName(path);
            var stem = System.IO.Path.GetFileNameWithoutExtension(file);
            var names = Directory.EnumerateFiles(dir, stem + "*").Select(System.IO.Path.GetFileName).OfType<string>();
            var best = AutostartLogic.PickLogo(file, names);
            return best == null ? null : PngFile(System.IO.Path.Combine(dir, best));
        }
        catch { return null; }
    }

    /// <summary>A small PNG (or JPEG) file as data URL; null when it is missing, too big or no image.</summary>
    private static string? PngFile(string file)
    {
        try
        {
            var fi = new FileInfo(file);
            if (!fi.Exists || fi.Length == 0 || fi.Length > 256 * 1024) return null;
            var b = File.ReadAllBytes(file);
            string? mime = b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
                : b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg" : null;
            return mime == null ? null : $"data:{mime};base64,{Convert.ToBase64String(b)}";
        }
        catch { return null; }
    }

    // ---------- state ----------

    private static string StoreKey(string family, string taskId) => AutostartLogic.Store.ApprovedKey + "\\" + family + "\\" + taskId;

    /// <summary>The State DWORD, or null when Windows has none yet (then the manifest's default holds).</summary>
    private static int? StoreStateOf(string family, string taskId)
    {
        try
        {
            using var k = Open(StoreKey(family, taskId));
            return k?.GetValue("State") is int v ? v : null;
        }
        catch { return null; }
    }

    /// <summary>The task as the installed package declares it now, or null when the app or the task is gone.</summary>
    private static (StoreApp app, AutostartLogic.StoreTask task)? FindStoreTask(string family, string taskId)
    {
        try
        {
            foreach (var p in new PackageManager().FindPackagesForUser(string.Empty, family))
            {
                var app = StoreAppOf(p, false);
                var t = app?.Tasks.FirstOrDefault(x => x.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));
                if (app != null && t != null) return (app, t);
            }
        }
        catch { }
        return null;
    }

    /// <summary>Switches a Store app's startup task (State 2 or 1) and returns the state read back.</summary>
    private static bool SetStore(string entry, bool on)
    {
        var (family, taskId) = AutostartLogic.ParseStoreEntry(entry) ?? throw new InvalidOperationException("Unbekannter Autostart-Eintrag.");
        var found = FindStoreTask(family, taskId) ?? throw new InvalidOperationException("Die App gibt es nicht mehr. Lade die Liste neu.");
        var task = found.task;
        taskId = task.TaskId;   // the spelling of the manifest names the key
        var cur = StoreStateOf(family, taskId);
        if (AutostartLogic.StoreLocked(cur))
            throw new InvalidOperationException("Eine Richtlinie legt fest, ob diese App mit Windows startet. Das lässt sich hier nicht ändern.");
        if (AutostartLogic.StoreOn(cur, task.Enabled) != on)
        {
            try
            {
                using var k = Create(StoreKey(family, taskId));
                k.SetValue("State", AutostartLogic.StoreState(on), RegistryValueKind.DWord);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
            {
                throw new InvalidOperationException("Windows hat das Umschalten verweigert. Vermutlich schützt ein Virenschutz oder eine Richtlinie diesen Eintrag.");
            }
        }
        return AutostartLogic.StoreOn(StoreStateOf(family, taskId), task.Enabled);
    }
}
