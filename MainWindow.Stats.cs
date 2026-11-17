using System.Text.Json;

namespace OrbitTweak;

/// <summary>Play time statistics (2.27). Stub: filled in by the stats feature.</summary>
public partial class MainWindow
{
    private Task<object?> StatsCmd(string cmd, JsonElement a) => throw new InvalidOperationException("Unbekannter Befehl: " + cmd);

    /// <summary>Called after every FiveM probe (every 2 s, on the UI thread). gamePid 0 = no game running.</summary>
    private void StatsTick(int gamePid, DateTime? gameStart) { }
}
