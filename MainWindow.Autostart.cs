using System.Diagnostics;
using System.Text.Json;
using OrbitTweak.Core;

namespace OrbitTweak;

/// <summary>Autostart manager (2.27): what starts with Windows, switched on and off like the Task Manager does it
/// (Explorer\StartupApproved flags, nothing is deleted). The work is in Core/Autostart.cs, the rules in Core/AutostartLogic.cs.</summary>
public partial class MainWindow
{
    private async Task<object?> AutostartCmd(string cmd, JsonElement a)
    {
        switch (cmd)
        {
            // -> { items: [{ id, name, entry, publisher, command, path, exists, source, allUsers, folder, on, offSince, icon, rec, why }] }
            case "autostart.list":
                return new { items = await Autostart.ListAsync() };

            // { id, on, name? } -> { on } (the state read back after writing); a refused write comes back as a German error.
            // name is only for the activity feed (the page's display name reads better than "com.squirrel.Teams.Teams")
            case "autostart.set":
            {
                var id = Str(a, "id"); bool on = Bool(a, "on");
                bool now = await Task.Run(() => Autostart.Set(id, on));
                if (now == on)
                {
                    var name = Str(a, "name").Trim();
                    if (name.Length == 0 || name.Length > 80) name = AutostartLogic.ParseId(id)?.entry ?? id;
                    Log($"Autostart: „{name}“ {(on ? "startet wieder mit Windows" : "startet nicht mehr mit Windows")}");
                }
                return new { on = now };
            }

            // { id } -> { ok, error? }: shows the link or the program in Explorer
            case "autostart.reveal":
            {
                var id = Str(a, "id");
                var file = await Task.Run(() => Autostart.Locate(id));
                if (file == null) return new { ok = false, error = "Die Datei ist nicht mehr da." };
                Process.Start("explorer.exe", $"/select,\"{file}\"")?.Dispose();
                return new { ok = true };
            }
        }
        throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
    }
}
