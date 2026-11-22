using System.Diagnostics;
using System.Text.Json;
using static OrbitTweak.Core.TweakGuardLogic;

namespace OrbitTweak.Core;

/// <summary>
/// Tweak-Wächter (2.31), the Windows part: reads, for each tweak OrbitTweak switched on (Marks), whether it is still
/// active, with the same Check the switch on the Tweaks page uses, and the FPS limit of 2.29 the way its card reads it.
/// It changes nothing in Windows (the one value it writes is its own note for "So lassen" of the FPS limit); the rules
/// are in TweakGuardLogic.cs, the schedule and the commands in MainWindow.TweakGuard.cs.
/// </summary>
internal static class TweakGuard
{
    /// <summary>"So lassen" for the FPS limit: the kept limit it was said for (TweakGuardLogic.FpsQuietNext). Machine-wide like the limit.</summary>
    private const string OWN = @"HKLM\SOFTWARE\OrbitTweak";
    private const string FPS_QUIET = "GuardFpsQuiet";
    private static readonly JsonSerializerOptions J = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Exe names of the other games OrbitTweak knows (FiveM is matched by its prefix).</summary>
    private static readonly string[] OtherGames = FortniteLogic.ProcessNames.Concat(R6Logic.ProcessNames).Append("cs2").ToArray();

    /// <summary>FiveM or another game runs: then nothing is looked at (by process name only, no handle is opened).</summary>
    public static bool GameRunning()
    {
        Process[] all;
        try { all = Process.GetProcesses(); } catch { return true; }   // unknown: better wait
        bool hit = false;
        foreach (var p in all)
        {
            try { if (!hit && IsGameProcess(p.ProcessName, OtherGames)) hit = true; } catch { }
            p.Dispose();
        }
        return hit;
    }

    /// <summary>
    /// One look at every tweak of the list (TweakGuardLogic.Observe): only the marked ones are read, with the Check the
    /// switch uses; usability and the condition only for those. A condition or check that throws counts as not readable
    /// this time, never as "off" or "gone" (Tweak.IsRecommended would turn a RecWhen that throws into "gone").
    /// </summary>
    public static List<Obs> Look(IReadOnlyList<Tweak> tweaks, Func<Tweak, bool>? only = null)
    {
        var list = new List<Obs>(tweaks.Count);
        bool? fivem = null;
        foreach (var t in tweaks)
        {
            if (only != null && !only(t)) continue;
            // FiveM's own tweaks need FiveM (uninstalled: nothing to put back), the rest what the tweak itself asks for
            bool forFiveM = t.Cat == "fivem";
            list.Add(Observe(t.Id, Marks.Has(t.Id), () => t.IsUsable, () =>
            {
                if (forFiveM) fivem ??= FiveM.Root != null;
                // gone for sure first (FiveM, the adapter), then RecWhen, which may throw: unreadable this time
                return Condition(null, forFiveM, fivem ?? true, Extra(t.Id)) == false ? false : t.RecWhen?.Invoke();
            }, t.Check));
        }
        return list;
    }

    /// <summary>A tweak's own requirement beyond Usable and RecWhen (null = none): the network adapter tweak needs an
    /// adapter that still has one of its power modes (Usable stays true while a backup exists, so it can be switched off).</summary>
    private static bool? Extra(string id)
    {
        try { return id == "nic" ? Catalog.NicAny() : null; } catch { return null; }
    }

    /// <summary>
    /// The FPS limit as its card reads it (FpsLimit.Get, one driver session): null when OrbitTweak keeps none, else the
    /// look and, when it differs, the line for the banner. A limit the user let be ("So lassen") is not watched until a
    /// new limit is set or it is in force again. Only called when a limit is kept, so a PC without NVIDIA pays nothing.
    /// </summary>
    public static (Obs look, string? detail)? LookFps()
    {
        int kept = FpsLimit.Stored;
        int? quiet = Reg.Get(OWN, FPS_QUIET) is int q ? q : null;
        if (kept <= 0)
        {
            if (quiet != null) Reg.Del(OWN, FPS_QUIET);
            return null;
        }
        if (!FpsLimit.Available) return (new Obs(FpsId, true, false, Read.Unknown), null);
        Read st; string? detail = null;
        try
        {
            // the card's own state, so the guard and the card always agree on "changed outside OrbitTweak"
            var j = JsonSerializer.SerializeToElement(FpsLimit.Get(), J);
            if (!j.GetProperty("available").GetBoolean()) return (new Obs(FpsId, true, false, Read.Unknown), null);
            int limit = j.GetProperty("limit").GetInt32(), written = j.GetProperty("written").GetInt32();
            st = FpsRead(j.GetProperty("ours").GetBoolean(), j.GetProperty("pending").GetBoolean(), written, limit);
            if (st == Read.Off) detail = FpsDetail(limit, written);
        }
        catch { st = Read.Unknown; }
        var next = FpsQuietNext(quiet, kept, st);
        if (next != quiet) try { if (next is int n) Reg.Set(OWN, FPS_QUIET, n); else Reg.Del(OWN, FPS_QUIET); } catch { }
        return (new Obs(FpsId, next == null, true, st), detail);
    }

    /// <summary>"So lassen" for the FPS limit: quiet while this limit is kept and the profile still differs.</summary>
    public static void QuietFps()
    {
        int kept = FpsLimit.Stored;
        if (kept > 0) Reg.Set(OWN, FPS_QUIET, kept);
    }
}
