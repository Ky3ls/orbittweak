using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace OrbitTweak.Core;

/// <summary>Where an autostart entry comes from: one of the three Run keys or one of the two startup folders, and the
/// key under Explorer\StartupApproved where Windows keeps its on/off flag (the Task Manager and Settings › Apps ›
/// Autostart read and write the same flags, so all three stay in sync).</summary>
public sealed record AutostartSource(string Key, bool AllUsers, bool Folder, string RunKey, string ApprovedKey, string Label);

/// <summary>The pure part of the autostart manager: the StartupApproved flag, reading a command line, the display name
/// and the recommendation per program. No Windows calls here, so tests/AutostartLogic.Tests checks it anywhere.</summary>
public static class AutostartLogic
{
    private const string Cv = @"Software\Microsoft\Windows\CurrentVersion";
    private const string Approved = Cv + @"\Explorer\StartupApproved";

    public static readonly AutostartSource[] Sources =
    {
        new("hkcu", false, false, @"HKCU\" + Cv + @"\Run", @"HKCU\" + Approved + @"\Run", "Nur für dich · Registrierung"),
        new("hklm", true, false, @"HKLM\" + Cv + @"\Run", @"HKLM\" + Approved + @"\Run", "Alle Nutzer · Registrierung"),
        new("hklm32", true, false, @"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKLM\" + Approved + @"\Run32", "Alle Nutzer · Registrierung (32 Bit)"),
        new("user", false, true, "", @"HKCU\" + Approved + @"\StartupFolder", "Nur für dich · Autostart-Ordner"),
        new("common", true, true, "", @"HKLM\" + Approved + @"\StartupFolder", "Alle Nutzer · Autostart-Ordner"),
    };

    /// <summary>Stable id of an entry: its source and the Run value name or the file name in the folder.</summary>
    public static string Id(AutostartSource s, string entry) => s.Key + "|" + entry;

    public static (AutostartSource src, string entry)? ParseId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        int i = id.IndexOf('|');
        if (i <= 0 || i == id.Length - 1) return null;
        var key = id[..i];
        var s = Sources.FirstOrDefault(x => x.Key == key);
        return s == null ? null : (s, id[(i + 1)..]);
    }

    /// <summary>A file name in a startup folder must stay a plain name (no way out of the folder).</summary>
    public static bool PlainFileName(string name)
        => name.Length > 0 && name.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) < 0 && name != "." && name != "..";

    // ---------- the StartupApproved flag ----------

    /// <summary>A missing value means on. The first byte decides: even is on, odd is off (the Task Manager writes 02 and
    /// 03, Settings › Apps › Autostart also 06 and 07).</summary>
    public static bool IsOn(byte[]? v) => v == null || v.Length == 0 || (v[0] & 1) == 0;

    /// <summary>The 12 bytes the Task Manager writes: on = 02 and zeros; off = 03, three zeros and the moment it was
    /// switched off as FILETIME (UTC) at offset 4.</summary>
    public static byte[] Flag(bool on, DateTime utc)
    {
        var b = new byte[12];
        b[0] = on ? (byte)2 : (byte)3;
        if (!on) BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(4), utc.ToUniversalTime().ToFileTimeUtc());
        return b;
    }

    /// <summary>When an entry was switched off (the FILETIME after an odd flag), or null when that is not known.</summary>
    public static DateTime? OffSince(byte[]? v)
    {
        if (v == null || v.Length < 12 || (v[0] & 1) == 0) return null;
        long ft = BinaryPrimitives.ReadInt64LittleEndian(v.AsSpan(4, 8));
        if (ft <= 0) return null;
        try
        {
            var t = DateTime.FromFileTimeUtc(ft);
            return t.Year is >= 2000 and < 2200 ? t : null;
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    // ---------- command lines ----------

    private static readonly Regex ExeEnd = new(@"\.(exe|com|bat|cmd|scr)(?=[\s""]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The file name of a Windows path, also on Linux (where '\' is no separator).</summary>
    public static string FileName(string p)
    {
        int i = p.LastIndexOfAny(new[] { '\\', '/' });
        return i < 0 ? p : p[(i + 1)..];
    }

    /// <summary>The folder of a Windows path ("" when there is none), also on Linux.</summary>
    public static string Folder(string p)
    {
        int i = p.LastIndexOfAny(new[] { '\\', '/' });
        return i <= 0 ? "" : p[..i];
    }

    /// <summary>Splits a Run command into program and arguments: "C:\a b\x.exe" -min, C:\a b\x.exe -min (unquoted, as
    /// many installers write it), x.exe /tray.</summary>
    public static (string exe, string args) SplitCommand(string? cmd)
    {
        var s = (cmd ?? "").Trim();
        if (s.Length == 0) return ("", "");
        if (s[0] == '"')
        {
            int q = s.IndexOf('"', 1);
            return q < 0 ? (s[1..].Trim(), "") : (s[1..q].Trim(), s[(q + 1)..].Trim());
        }
        var m = ExeEnd.Match(s);
        if (m.Success)
        {
            int e = m.Index + m.Length;
            return (s[..e].Trim(), s[e..].Trim());
        }
        int sp = s.IndexOf(' ');
        return sp < 0 ? (s, "") : (s[..sp], s[(sp + 1)..].Trim());
    }

    /// <summary>rundll32 starts a DLL: that DLL is the file that has to exist (left-overs of removed programs often point
    /// at one that is gone and show an error at every start).</summary>
    public static string? Rundll32Target(string exe, string args)
    {
        var f = FileName(exe);
        if (!f.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) && !f.Equals("rundll32", StringComparison.OrdinalIgnoreCase)) return null;
        var a = args.Trim();
        if (a.Length == 0) return null;
        string dll;
        if (a[0] == '"') { int q = a.IndexOf('"', 1); dll = q < 0 ? a[1..] : a[1..q]; }
        else { int c = a.IndexOf(','); if (c < 0) c = a.IndexOf(' '); dll = c < 0 ? a : a[..c]; }
        dll = dll.Trim();
        return dll.Length == 0 ? null : dll;
    }

    private static readonly Regex Squirrel = new(@"--processStart(?:AndWait)?\s+(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Squirrel installs (Discord, the old Teams, Slack …) start "Update.exe --processStart App.exe": the app's own
    /// exe carries the icon and the publisher. Returns its file name, or null.</summary>
    public static string? SquirrelApp(string exe, string args)
    {
        if (!FileName(exe).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) return null;
        var m = Squirrel.Match(args);
        if (!m.Success) return null;
        var app = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
        return PlainFileName(app) ? app : null;
    }

    // ---------- names ----------

    private static readonly Regex Guid8 = new("[0-9a-f]{8}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    { "update", "updater", "launcher", "application", "app", "setup", "bootstrapper", "host", "service", "helper", "tray", "agent", "client" };

    /// <summary>Strips ®, ™ and doubled spaces from a version-info text.</summary>
    public static string Tidy(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Replace("®", "").Replace("™", "").Replace("(R)", "").Replace("(TM)", "").Replace("(r)", "").Replace("(tm)", "");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    /// <summary>A name that reads like a name: several words ("Logitech G HUB"), or one plain word ("Steam", "Discord").
    /// Internal value names ("RtkAudUService", "SecurityHealth", "com.squirrel.Teams.Teams", a GUID) are not.</summary>
    public static bool Readable(string name)
    {
        var n = name.Trim();
        if (n.Length < 3 || n.Length > 64) return false;
        if (n.Contains('{') || Guid8.IsMatch(n)) return false;
        if (n.Contains(' ')) return true;
        return char.IsUpper(n[0]) && n.All(char.IsLetter) && n.Count(char.IsUpper) == 1;
    }

    /// <summary>The name to show: the entry's own name when it is readable, otherwise the program's description or
    /// product name from its version info (as long as that says more than the file name).</summary>
    public static string DisplayName(string entry, string exe, string? description, string? product)
    {
        var e = entry.Trim();
        if (Readable(e)) return e;
        var file = FileName(exe);
        var stem = file.Contains('.') ? file[..file.LastIndexOf('.')] : file;
        bool Good(string s) => s.Length >= 2 && s.Length <= 64 && s.Any(char.IsLetter) && !Generic.Contains(s)
            && !s.Equals(e, StringComparison.OrdinalIgnoreCase) && !s.Equals(file, StringComparison.OrdinalIgnoreCase) && !s.Equals(stem, StringComparison.OrdinalIgnoreCase);
        var d = Tidy(description);
        if (Good(d)) return d;
        var p = Tidy(product);
        if (Good(p) && !p.Contains("Operating System", StringComparison.OrdinalIgnoreCase) && !p.Contains("Betriebssystem", StringComparison.OrdinalIgnoreCase)) return p;
        return e.Length > 0 ? e : stem;
    }

    // ---------- recommendation ----------

    /// <summary>"off": only costs boot time (launchers, updaters, apps you open yourself) or cannot start anyway.
    /// "on": audio, graphics, input devices and protection need it. null: no advice (Discord for voice, OrbitTweak,
    /// cloud sync and everything not on the list); a neutral entry can still carry a note in Why.</summary>
    public sealed record Advice(string? Rec, string Why);

    private const string WhyLauncher = "Launcher, öffnet sich beim Spielstart von selbst";
    private const string WhyApp = "App, die du bei Bedarf selbst öffnest";
    private const string WhyUpdater = "Updater, das Programm prüft beim Öffnen selbst";
    private const string WhyHelper = "Hilfsprogramm, für den Start nicht nötig";
    private const string WhyBrowser = "Lädt den Browser vor und kostet nur Startzeit";
    private const string WhyMissing = "Datei fehlt, startet ohnehin nicht";
    private const string WhySecurity = "Schutz deines PCs, bitte an lassen";
    private const string WhyAudio = "Ton und Audio-Einstellungen hängen daran";
    private const string WhyDriver = "Treiber-Software, Einstellungen und Profile hängen daran";
    private const string WhyInput = "Maus, Tastatur und Headset brauchen es für Profile und Tasten";

    private static Regex R(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // first match wins: the exceptions stand before the vendors, the vendors before the general lists
    private static readonly (Regex re, string? rec, string why)[] Rules =
    {
        (R(@"orbittweak"), null, ""),
        (R(@"discord"), null, "Für Sprachchat oft gewollt, deine Entscheidung"),
        (R(@"onedrive|dropbox|google ?drive|googledrivesync|icloud ?drive|\bnextcloud\b|\bmega ?sync\b"), null, "Synchronisiert deine Dateien nur, solange es läuft"),
        // exceptions inside vendors that otherwise should stay on
        (R(@"razer cortex|\bcortex\b"), "off", WhyHelper),
        (R(@"driver & support assistant|driver and support assistant|\bdsatray\b"), "off", WhyUpdater),
        // should stay on
        (R(@"securityhealth|windows security|windows defender|windows-sicherheit|\bavast|\bavg ?(ui|antivirus)|\bavgui\b|\bavira\b|bitdefender|\bbdagent\b|kaspersky|\bavpui\b|\beset\b|\begui\b|\bnorton\b|mcafee|malwarebytes|\bmbamtray\b|\bsophos\b|f-secure|\bg ?data\b|trend micro|\bwebroot\b|emsisoft|\bpanda (dome|security)\b"), "on", WhySecurity),
        (R(@"realtek|\brtkaud|\bravcpl|\brthdvcpl|nahimic|\bdolby\b|maxxaudio|\bwavessvc|sound blaster|voicemeeter|sonic studio|conexant|smartaudio|\bdts (audio|sound)\b"), "on", WhyAudio),
        (R(@"nvidia|geforce|\bnvbackend\b|\bnvcpl|advanced micro devices|\bradeon|\bamd (software|adrenalin|noise)|\bamdrs|\bcncmd\b|\bintel\b|\bigfx|graphics command center|arc control"), "on", WhyDriver),
        (R(@"logitech|\blghub\b|\blcore\b|\blogi (options|bolt)|razer|synapse|steelseries|corsair|\bicue\b|hyperx|ngenuity|roccat|wooting|glorious core"), "on", WhyInput),
        // only cost boot time
        (R(@"\bsteam\b|epic ?games ?launcher|\beadesktop\b|\bealauncher\b|\bea (app|desktop)\b|\beadm\b|\borigin\b|ubisoft|\bupc\b|\buplay\b|battle\.net|rockstar|gog galaxy|galaxyclient|riot ?client|amazon games|bethesda\.net|wargaming"), "off", WhyLauncher),
        (R(@"autolaunch|browser ?assistant|browser_assistant|--auto-launch|--no-startup-window"), "off", WhyBrowser),
        (R(@"adobe ?arm\b|acrobat update|adobe updater|creative cloud|\bccxprocess\b|\badobe ?gc|\bacrotray\b|\bjusched\b|java update|google update|\bccleaner\b"), "off", WhyUpdater),
        (R(@"spotify|\bteams\b|ms-teams|\bskype\b|itunes|whatsapp|\bzoom\b|amazon music|\bdeezer\b|\btidal\b|overwolf"), "off", WhyApp),
    };

    /// <summary>The advice for one entry, from its names, exe, publisher and arguments.</summary>
    public static Advice? Recommend(string entry, string name, string exe, string publisher, string args, bool missing)
    {
        var hay = string.Join(" | ", entry, name, FileName(exe), publisher, args);
        if (Rules[0].re.IsMatch(hay)) return null;   // OrbitTweak itself
        if (missing) return new("off", WhyMissing);
        foreach (var (re, rec, why) in Rules)
            if (re.IsMatch(hay)) return rec == null && why.Length == 0 ? null : new(rec, why);
        return null;
    }
}
