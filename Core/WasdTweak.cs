using System.Runtime.InteropServices;

namespace OrbitTweak.Core;

/// <summary>
/// The two parts of the "WASD tweak" as PvP players use it, both plain Windows settings:
///  - "Laggen": the Windows keyboard filter (Filter Keys / Anschlagverzögerung) switched on with a very fast key
///    repeat (after 200 ms every 15 ms). Held and quickly changed WASD keys then make the figure move jerkily.
///    Takes effect at once and also changes typing (held keys repeat much faster).
///  - the keyboard buffer of the keyboard driver (KeyboardDataQueueSize, Windows default 100). Read by the driver
///    when Windows starts, so a change needs a restart.
/// </summary>
public class WasdLagCfg
{
    public bool On { get; set; }
    public int Gap { get; set; } = 35;     // ms a key beside the held one is tapped
    public int Every { get; set; } = 80;   // ms from one tap to the next
    public int Key { get; set; }           // key that switches it on and off in the game, 0 = none
}

internal static class WasdTweak
{
    public const string KbdParams = @"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters";
    public const string Response = @"HKCU\Control Panel\Accessibility\Keyboard Response";
    public static readonly int[] Levels = { 50, 30, 25, 20, 16 };

    [StructLayout(LayoutKind.Sequential)]
    private struct FILTERKEYS { public uint cbSize, dwFlags, iWaitMSec, iDelayMSec, iRepeatMSec, iBounceMSec; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref FILTERKEYS f, uint winIni);
    private const uint SPI_GETFILTERKEYS = 0x0032, SPI_SETFILTERKEYS = 0x0033;
    private const uint FKF_ON = 0x1, FKF_AVAILABLE = 0x2, FKF_HOTKEYACTIVE = 0x4, FKF_CONFIRMHOTKEY = 0x8, FKF_HOTKEYSOUND = 0x10, FKF_INDICATOR = 0x20, FKF_CLICKON = 0x40;
    public const int LagDelay = 200, LagRepeat = 15;

    /// <summary>Size of the keyboard buffer in the registry; 0 = not set (Windows default 100).</summary>
    public static int Buffer => Reg.Get(KbdParams, "KeyboardDataQueueSize") is int v && v is > 0 and < 100 ? v : 0;

    public static void SetBuffer(int size)
    {
        if (size == 0) Reg.Del(KbdParams, "KeyboardDataQueueSize");
        else Reg.Set(KbdParams, "KeyboardDataQueueSize", size);
    }

    /// <summary>Whether the fast key repeat is on right now, and the timing Windows uses.</summary>
    public static (bool on, int delay, int repeat) Lag()
    {
        var f = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf<FILTERKEYS>() };
        if (!SystemParametersInfo(SPI_GETFILTERKEYS, f.cbSize, ref f, 0)) return (false, 0, 0);
        bool on = (f.dwFlags & FKF_ON) != 0 && f.iWaitMSec == 0 && f.iRepeatMSec is > 0 and <= 40;
        return (on, (int)f.iDelayMSec, (int)f.iRepeatMSec);
    }

    /// <summary>
    /// Switches the fast key repeat on or off for the running session and saves it for the next sign-in.
    /// The Windows shortcut for the keyboard filter (hold right Shift for 8 s) stays the way it was; the click sound
    /// on every key press that Windows offers with the filter is never switched on.
    /// </summary>
    public static bool SetLag(bool on)
    {
        var f = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf<FILTERKEYS>() };
        if (!SystemParametersInfo(SPI_GETFILTERKEYS, f.cbSize, ref f, 0)) return false;
        if (on)
        {
            f.dwFlags = FKF_ON | FKF_AVAILABLE | FKF_CONFIRMHOTKEY | FKF_HOTKEYSOUND | FKF_INDICATOR | (f.dwFlags & FKF_HOTKEYACTIVE);
            f.iWaitMSec = 0; f.iDelayMSec = LagDelay; f.iRepeatMSec = LagRepeat; f.iBounceMSec = 0;
        }
        else f.dwFlags &= ~FKF_ON;
        return SystemParametersInfo(SPI_SETFILTERKEYS, f.cbSize, ref f, 0x1 | 0x2); // save + tell running programs
    }

    /// <summary>
    /// The value for "Flags" of the keyboard filter when only its shortcut is switched off or on (tweak "Einrastfunktion-Popup aus"):
    /// a running "Laggen" must stay on, which the plain values 122 / 126 would end.
    /// </summary>
    public static string ResponseFlags(bool hotkey)
    {
        bool on = int.TryParse(Reg.Get(Response, "Flags")?.ToString(), out var fl) && (fl & (int)FKF_ON) != 0;
        uint v = on ? FKF_ON | FKF_AVAILABLE | FKF_CONFIRMHOTKEY | FKF_HOTKEYSOUND | FKF_INDICATOR
                    : FKF_AVAILABLE | FKF_CONFIRMHOTKEY | FKF_HOTKEYSOUND | FKF_INDICATOR | FKF_CLICKON;
        if (hotkey) v |= FKF_HOTKEYACTIVE;
        return v.ToString();
    }
}
