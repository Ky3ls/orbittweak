namespace OrbitTweak.Core;

/// <summary>
/// The parts of the Diagnose check (<see cref="Health"/>) that are plain logic, without Windows calls, so they can be
/// checked anywhere (tests/ClipLogic.Tests).
/// </summary>
internal static class HealthLogic
{
    /// <summary>
    /// Score 0 to 100 over the rows: "ok" 100, "warn" 50, "bad" 0. An "info" row is a note on a trade-off, not a fault,
    /// and does not count at all. No rated row is 100.
    /// </summary>
    public static int Score(IEnumerable<string> states)
    {
        int n = 0, sum = 0;
        foreach (var st in states)
        {
            if (st == "info") continue;
            n++;
            sum += st == "ok" ? 100 : st == "warn" ? 50 : 0;
        }
        return n == 0 ? 100 : (int)Math.Round(sum / (double)n);
    }

    // ---------- memory integrity (HVCI) ----------
    public enum Hvci { Unknown, On, Pending, Off, Unsupported }

    /// <summary>
    /// PowerShell that prints the fields of Win32_DeviceGuard (root\Microsoft\Windows\DeviceGuard) <see cref="ParseHvci"/>
    /// reads, one "name=values" line each. Microsoft documents this query for an elevated session, which OrbitTweak is.
    /// </summary>
    public const string HvciQuery =
        "$g = Get-CimInstance -Namespace root\\Microsoft\\Windows\\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop; "
        + "'running=' + ($g.SecurityServicesRunning -join ','); 'configured=' + ($g.SecurityServicesConfigured -join ','); "
        + "'props=' + ($g.AvailableSecurityProperties -join ',')";

    /// <summary>
    /// The state from <see cref="HvciQuery"/>'s output. SecurityServicesRunning has 2 = memory integrity runs;
    /// SecurityServicesConfigured has 2 = it is switched on but does not run yet (usually a restart is missing);
    /// AvailableSecurityProperties without 1 = no hypervisor support, so it cannot run on this PC.
    /// Anything that is not that output (an error, "Timeout", no instance) is <see cref="Hvci.Unknown"/>.
    /// </summary>
    public static Hvci ParseHvci(string? output)
    {
        int[]? running = null, configured = null, props = null;
        foreach (var raw in (output ?? "").Split('\n'))
        {
            var line = raw.Trim();
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            var nums = Nums(line[(i + 1)..]);
            switch (line[..i])
            {
                case "running": running = nums; break;
                case "configured": configured = nums; break;
                case "props": props = nums; break;
            }
        }
        if (running is not { Length: > 0 }) return Hvci.Unknown;
        if (running.Contains(2)) return Hvci.On;
        if (props is { Length: > 0 } && !props.Contains(1)) return Hvci.Unsupported;
        if (configured != null && configured.Contains(2)) return Hvci.Pending;
        return Hvci.Off;
    }

    /// <summary>"1,2,3" → the numbers; null when a piece is not a number.</summary>
    private static int[]? Nums(string s)
    {
        if (s.Trim().Length == 0) return Array.Empty<int>();
        var p = s.Split(',');
        var r = new int[p.Length];
        for (int k = 0; k < p.Length; k++) if (!int.TryParse(p[k].Trim(), out r[k])) return null;
        return r;
    }

    private const string HvciTradeOff = "schützt den Windows-Kern vor eingeschleustem Code und unsicheren Treibern, kostet in Spielen "
        + "meist bis 5 % FPS, in einzelnen bis rund 10 %; manche Anti-Cheats wie FACEIT können sie verlangen";

    /// <summary>
    /// The Diagnose row: never a fault. On is an "info" row with a button to Windows Security; so is switched on but not
    /// running yet (what a Windows update that turns it on leaves until the restart): the trade-off is the same, and
    /// whether it then really runs is not certain (drivers, hardware), so the row says what is, not what will be.
    /// null = not shown.
    /// </summary>
    public static (string value, string st, string? fix)? HvciRow(Hvci h) => h switch
    {
        Hvci.On => ("An – " + HvciTradeOff, "info", "coreisolation"),
        Hvci.Pending => ("Eingeschaltet, wartet auf einen Neustart – " + HvciTradeOff, "info", "coreisolation"),
        Hvci.Off => ("Aus", "ok", null),
        Hvci.Unsupported => ("Nicht verfügbar", "ok", null),
        _ => null,
    };
}
