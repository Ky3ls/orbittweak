namespace OrbitTweak.Core;

// =====================================================================
//  Which graphics settings file FiveM really reads. Plain logic without Windows calls, so it can be checked
//  anywhere (tests/ClipLogic.Tests).
//
//  citizenfx/fivem code/components/gta-core-five/src/VidBehavior.cpp: on a "new install" FiveM points GTA's settings
//  file to fxd:/gta5_settings.xml, and fxd:/ is %AppData%\CitizenFX\ (rage-device-five/src/CitizenMount.Shared.cpp).
//  When that file is missing on start, FiveM copies user:/settings.xml (Documents\Rockstar Games\GTA V) once and
//  then sets borderless and vsync off. Older installs keep reading the Documents settings.xml.
// =====================================================================
internal static class GfxFile
{
    public const string FiveMKind = "fivem", GtaKind = "gta";

    /// <summary>FILETIME (UTC) FiveM compares the crashes folder's creation time with: 2020-06-03 07:30:25 UTC.</summary>
    public const long NewInstallCutoff = 132356430250000000L;

    /// <summary>
    /// FiveM's IsNewInstall(): the marker FiveM.app\citizen\new_vid_behavior.txt, or FiveM.app\crashes created after
    /// the cutoff. Null when it cannot be told from here: no marker and no crashes folder (FiveM itself would say
    /// "old" then, but the folder may still appear before the game reads its settings).
    /// </summary>
    public static bool? IsNewInstall(bool marker, DateTime? crashesCreatedUtc)
    {
        if (marker) return true;
        if (crashesCreatedUtc is not { } c) return null;
        return c.ToFileTimeUtc() > NewInstallCutoff;
    }

    /// <summary>
    /// The file FiveM reads on its next start. <paramref name="newInstall"/> is <see cref="IsNewInstall"/>, or null
    /// when the FiveM folder was not found. Without a clear answer FiveM's own file wins when it exists (every install
    /// since June 2020 has one), the old Documents file only when it is the one there.
    /// </summary>
    public static string Pick(bool? newInstall, bool fivemFileExists, bool gtaFileExists) => newInstall switch
    {
        true => FiveMKind,
        false => GtaKind,
        null => fivemFileExists || !gtaFileExists ? FiveMKind : GtaKind,
    };

    /// <summary>Each file keeps its own backup right next to it.</summary>
    public static string BackupOf(string file) => file + ".orbit-backup";
}
