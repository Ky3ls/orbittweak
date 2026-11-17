using System.Text.Json;

namespace OrbitTweak;

/// <summary>Autostart manager (2.27). Stub: filled in by the autostart feature.</summary>
public partial class MainWindow
{
    private Task<object?> AutostartCmd(string cmd, JsonElement a) => throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
}
