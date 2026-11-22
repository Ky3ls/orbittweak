using System.Text.Json;
using OrbitTweak.Core;

namespace OrbitTweak;

/// <summary>
/// Monitor-Check und RAM-Check (2.31): the host side of the two Diagnose rows' dialogs (Web/hwcheck.js). The rows
/// themselves come with "health" (Core/Features.cs); the work is in Core/HwCheck.cs, the rules in Core/HwCheckLogic.cs.
///   hw.displays → { fivem, laptop, revertIn, displays: [{ device, name, conn, primary, w, h, hz, best, switchedFrom, pending }] }
///   hw.hz.set { device } → { ok, error?, device, from, to, revertIn }   (for now only, Windows stores nothing; back to
///                          from by itself after revertIn s: event "hwhz")
///   hw.hz.keep { device } → { ok, error?, device, from, to }   (stores the rate; refused other than "Zu spät…", nothing went back)
///   hw.hz.back { device } → { ok, error?, device, from, to }   (from = the rate it ran at, to = the rate it runs at now)
///   hw.ram → { ok, error?, laptop, channel, channels, xmp, configured, rated, gen, modules: [{ slot, bank, chan, gb, gen, speed, rated, part, maker }] }
/// A refused change comes back as ok = false with a German error, never as an exception.
/// </summary>
public partial class MainWindow
{
    private async Task<object?> HwCmd(string cmd, JsonElement a)
    {
        try
        {
            switch (cmd)
            {
                case "hw.displays":
                    return await Task.Run(HwCheck.DisplayInfo);

                case "hw.hz.set":
                {
                    var dev = Str(a, "device");
                    var r = await Task.Run(() => HwCheck.SetHz(dev, back =>
                    {
                        if (back.Ok) Log($"🖥 Monitor: nicht bestätigt, zurück auf {back.To} Hz");
                        Emit("hwhz", HzJson(back));
                    }));
                    if (r.Ok) Log($"🖥 Monitor: {r.From} → {r.To} Hz (wartet auf „Behalten“)");
                    return new { ok = r.Ok, error = r.Error, device = r.Device, from = r.From, to = r.To, revertIn = HwCheck.RevertSeconds };
                }

                case "hw.hz.keep":
                {
                    var r = await Task.Run(() => HwCheck.Keep(Str(a, "device")));
                    if (r.Ok) Log($"🖥 Monitor läuft jetzt mit {r.To} Hz");
                    return HzJson(r);
                }

                case "hw.hz.back":
                {
                    var r = await Task.Run(() => HwCheck.Back(Str(a, "device")));
                    if (r.Ok) Log($"↺ Monitor: zurück auf {r.To} Hz");
                    return HzJson(r);
                }

                case "hw.ram":
                    return await Task.Run(HwCheck.RamInfo);
            }
        }
        catch (Exception ex)
        {
            return new { ok = false, error = "Das ging gerade nicht: " + ex.Message };
        }
        throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
    }

    private static object HzJson(HwCheck.HzResult r) => new { ok = r.Ok, error = r.Error, device = r.Device, from = r.From, to = r.To };
}
