namespace OrbitTweak.Core;

// =====================================================================
//  Tweak-Wächter (2.31): the pure rules, without Windows calls, so tests/TweakGuardLogic.Tests checks them anywhere.
//  OrbitTweak marks every tweak it switched on (Marks, HKLM\SOFTWARE\OrbitTweak\Applied). Windows updates, a new
//  driver, another tool or the user can switch one back; the guard looks now and then (never while a game runs) and
//  names a marked tweak that is no longer active, once two looks in a row (at least MinGap apart) agree. It never
//  claims to know who did it. The Windows part is Core/TweakGuard.cs, the host MainWindow.TweakGuard.cs.
// =====================================================================
internal static class TweakGuardLogic
{
    /// <summary>What one look found: on as OrbitTweak set it, off, or not readable this time (the check threw).</summary>
    public enum Read { On, Off, Unknown }

    /// <summary>
    /// One look at one thing the guard watches. Marked: OrbitTweak switched it on and it was not switched off since.
    /// Applies: it can still work on this PC (usable, and the condition it is meant for still holds, e.g. two GPUs).
    /// </summary>
    public readonly record struct Obs(string Id, bool Marked, bool Applies, Read State);

    /// <summary>The FPS limit of 2.29 (NVIDIA driver profile) is watched next to the tweaks under this id.</summary>
    public const string FpsId = "fpslimit";

    /// <summary>The first look, this long after the app is unlocked (the start has enough to do).</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(20);
    /// <summary>After a first "off": the second look comes this long after it.</summary>
    public static readonly TimeSpan ConfirmAfter = TimeSpan.FromSeconds(60);
    /// <summary>Two "off" looks count only this far apart: a look started by hand right after another one does not confirm.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(45);
    /// <summary>The usual rhythm while no game runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    /// <summary>While a game runs nothing is looked at; asked again this often whether it still runs.</summary>
    public static readonly TimeSpan GameRetry = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Switches whose "on" is a setting of OrbitTweak itself (it acts while FiveM runs): nothing outside the app can
    /// reset them, so they are not watched.
    /// </summary>
    public static readonly IReadOnlySet<string> AppHeld = new HashSet<string>(StringComparer.Ordinal) { "nothrottle", "bgbrake", "timer", "wifiscan", "winkey" };

    /// <summary>
    /// Whether a tweak is looked at: marked by OrbitTweak, not one of its own settings, usable on this PC and, when the
    /// tweak is meant for a condition (RecWhen: two GPUs, a desktop …), that condition still holds (cond null = none).
    /// </summary>
    public static bool Applies(string id, bool usable, bool? cond) => !AppHeld.Contains(id) && usable && cond != false;

    /// <summary>
    /// The condition a tweak is watched under (null = none): its RecWhen, but false when it is a FiveM tweak and FiveM is
    /// not found any more, or when the tweak's own requirement is gone (extra false: e.g. a new network driver without
    /// any of the power modes). "Wieder anwenden" could only fail then.
    /// </summary>
    public static bool? Condition(bool? recWhen, bool forFiveM, bool fivemFound, bool? extra = null)
        => (forFiveM && !fivemFound) || extra == false ? false : recWhen;

    /// <summary>What the guard knows between looks. Times are the first "off" look.</summary>
    public sealed class State
    {
        /// <summary>Seen off once, waiting for the second look.</summary>
        public Dictionary<string, DateTime> Suspect { get; } = new(StringComparer.Ordinal);
        /// <summary>Seen off twice in a row: shown with "Wieder anwenden" and "So lassen".</summary>
        public Dictionary<string, DateTime> Pending { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Confirmed: newly pending after this look. Cleared: pending before, not any more (on again, mark gone …).</summary>
    public sealed record StepResult(List<string> Confirmed, List<string> Cleared);

    /// <summary>
    /// One full look at everything watched. "Off" twice in a row (MinGap apart) makes a tweak pending; "on", a lost mark
    /// or a condition that no longer holds takes it out; a look that could not read it breaks a waiting streak but leaves
    /// a pending one as it is (a transient read neither nags nor makes the banner flicker). Anything not in this look
    /// (a tweak that is gone from the list, the FPS limit switched off) is no longer watched.
    /// </summary>
    public static StepResult Step(State s, IEnumerable<Obs> looks, DateTime now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var confirmed = new List<string>();
        var cleared = new List<string>();
        void Drop(string id) { s.Suspect.Remove(id); if (s.Pending.Remove(id)) cleared.Add(id); }
        foreach (var o in looks)
        {
            if (string.IsNullOrEmpty(o.Id) || !seen.Add(o.Id)) continue;
            if (!o.Marked || !o.Applies) { Drop(o.Id); continue; }
            switch (o.State)
            {
                case Read.On: Drop(o.Id); break;
                case Read.Unknown: s.Suspect.Remove(o.Id); break;
                case Read.Off:
                    if (s.Pending.ContainsKey(o.Id)) break;
                    if (!s.Suspect.TryGetValue(o.Id, out var first)) s.Suspect[o.Id] = now;
                    else if (now - first >= MinGap) { s.Suspect.Remove(o.Id); s.Pending[o.Id] = first; confirmed.Add(o.Id); }
                    // too soon after the first look: the first time stays, the planned second look decides
                    break;
            }
        }
        foreach (var id in s.Suspect.Keys.Where(k => !seen.Contains(k)).ToList()) s.Suspect.Remove(id);
        foreach (var id in s.Pending.Keys.Where(k => !seen.Contains(k)).ToList()) { s.Pending.Remove(id); cleared.Add(id); }
        return new(confirmed, cleared);
    }

    /// <summary>
    /// A quick look at the pending ones only (when the page asks): one that is on again, lost its mark or no longer
    /// applies goes; "off" and unreadable stay. Things that are not pending are not touched. Returns the cleared ids.
    /// </summary>
    public static List<string> Recheck(State s, IEnumerable<Obs> looks)
    {
        var cleared = new List<string>();
        foreach (var o in looks)
        {
            if (!s.Pending.ContainsKey(o.Id)) continue;
            if (!o.Marked || !o.Applies || o.State == Read.On) { s.Pending.Remove(o.Id); cleared.Add(o.Id); }
        }
        return cleared;
    }

    /// <summary>When to look next: the second look for a first "off" (never in the past), else the usual rhythm.</summary>
    public static DateTime Next(State s, DateTime now)
    {
        if (s.Suspect.Count == 0) return now + Interval;
        var due = s.Suspect.Values.Min() + ConfirmAfter;
        var soonest = now + TimeSpan.FromSeconds(5);
        return due < soonest ? soonest : due;
    }

    /// <summary>
    /// What "Wieder anwenden" or "So lassen" acts on: exactly the asked ids that are pending, each once, in the order of
    /// the tweak list (the power plan before USB power saving, which lives in the plan); the FPS limit last.
    /// </summary>
    public static List<string> Pick(State s, IEnumerable<string>? asked, IReadOnlyList<string> order)
    {
        var want = new HashSet<string>((asked ?? Array.Empty<string>()).Where(id => id != null && s.Pending.ContainsKey(id)), StringComparer.Ordinal);
        var list = order.Where(id => id != FpsId && want.Contains(id)).Distinct().ToList();
        // pending but not in the list order: after the rest, by id; the FPS limit always last
        list.AddRange(want.Where(id => id != FpsId && !list.Contains(id)).OrderBy(id => id, StringComparer.Ordinal));
        if (want.Contains(FpsId)) list.Add(FpsId);
        return list;
    }

    // ---------- the game ----------
    /// <summary>A game process by name: anything of FiveM (FiveM.exe, FiveM_b3095_GTAProcess, FiveM_ChromeBrowser …) or one of the given exe names.</summary>
    public static bool IsGameProcess(string? name, IEnumerable<string> others)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase)) return true;
        return others.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- the FPS limit (Core/FpsTex.cs) ----------
    /// <summary>
    /// The FPS limit as the card on the Grafik page sees it (fps.get): reset when OrbitTweak keeps a limit, the profile
    /// of the newest build is known (not pending: a limit still to come with the next start of FiveM is put there by
    /// FiveM's own upkeep, a driver profile that is gone included) and that profile now has another value than the one
    /// OrbitTweak wrote. No limit kept or no driver: nothing to watch (the caller passes Marked/Applies false).
    /// </summary>
    public static Read FpsRead(bool ours, bool pending, int written, int limit)
        => !ours || pending || written <= 0 ? Read.On : limit != written ? Read.Off : Read.On;

    /// <summary>The line under the FPS limit in the banner: what the driver profile has now instead of OrbitTweak's value.</summary>
    public static string FpsDetail(int limit, int written)
        => limit > 0 ? $"Im Treiberprofil stehen jetzt {limit} statt {written} FPS" : $"Im Treiberprofil steht jetzt kein Limit statt {written} FPS";

    /// <summary>
    /// "So lassen" for the FPS limit: there is no mark to clear (the limit is kept as a value), so the kept limit at that
    /// moment is remembered. It stays quiet while that limit is kept and the profile still differs; a new limit set on the
    /// card or the limit in force again (on) makes it watched again. Returns the value to keep (null = none).
    /// </summary>
    public static int? FpsQuietNext(int? quiet, int kept, Read state)
        => quiet is int q && q == kept && kept > 0 && state != Read.On ? q : null;

    // ---------- words ----------
    /// <summary>"A", "A und B", "A, B und C".</summary>
    public static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " und " + names[^1],
    };

    /// <summary>The banner's title: "1 Tweak wurde zurückgesetzt", "2 Tweaks wurden zurückgesetzt".</summary>
    public static string Title(int n) => n == 1 ? "1 Tweak wurde zurückgesetzt" : $"{n} Tweaks wurden zurückgesetzt";

    /// <summary>The line in "Letzte Aktivität" when tweaks were found reset (it does not guess why).</summary>
    public static string Announce(IReadOnlyList<string> names)
        => names.Count == 0 ? "" : $"🛡 {(names.Count == 1 ? "Nicht mehr aktiv" : $"{names.Count} Tweaks nicht mehr aktiv")}: {Join(names)} · unter Tweaks wieder anwenden oder so lassen";

    /// <summary>The line in "Letzte Aktivität" after "So lassen".</summary>
    public static string KeptLine(IReadOnlyList<string> names)
        => names.Count == 0 ? "" : $"🛡 So gelassen: {Join(names)} · der Tweak-Wächter fragt dort nicht mehr nach";
}
