namespace OrbitTweak.Core;

// =====================================================================
//  Which graphics settings file FiveM really reads. Plain logic without Windows calls, so it can be checked
//  anywhere (tests/ClipLogic.Tests).
//
//  citizenfx/fivem code/components/gta-core-five/src/VidBehavior.cpp: on a "new install" FiveM points GTA's settings
//  file to fxd:/gta5_settings.xml, and fxd:/ is %AppData%\CitizenFX\ (rage-device-five/src/CitizenMount.Shared.cpp).
//  When that file is missing on start, FiveM copies user:/settings.xml once and then sets borderless and vsync off.
//  Older installs keep reading user:/settings.xml. Since 2023 FiveM redirects everything under Documents\Rockstar Games
//  to %LocalAppData%\Rockstar Games (client/citicore/FileMapping.Win32.cpp), so for FiveM that file is
//  %LocalAppData%\Rockstar Games\GTA V\settings.xml; the Documents one is only GTA V's own (story mode, GTA Online).
// =====================================================================
internal static class GfxFile
{
    public const string FiveMKind = "fivem", GtaKind = "gta";

    /// <summary>FILETIME (UTC) FiveM compares the crashes folder's creation time with: 2020-06-03 07:30:25 UTC.</summary>
    public const long NewInstallCutoff = 132356430250000000L;

    /// <summary>
    /// FiveM's IsNewInstall() as it answers on the next start: the marker FiveM.app\citizen\new_vid_behavior.txt, or
    /// FiveM.app\crashes created after the cutoff. A missing crashes folder (<paramref name="crashesCreatedUtc"/> null)
    /// also means new: every FiveM process makes that folder early on start (client/launcher/MiniDump.Shared.cpp,
    /// InitializeExceptionHandler), before the game reads its settings, so it is created after the cutoff.
    /// </summary>
    public static bool IsNewInstall(bool marker, DateTime? crashesCreatedUtc) =>
        marker || crashesCreatedUtc is not { } c || c.ToFileTimeUtc() > NewInstallCutoff;

    /// <summary>
    /// The file FiveM reads on its next start. <paramref name="newInstall"/> is <see cref="IsNewInstall"/>, or null
    /// when the FiveM.app folder was not found. Then FiveM's own file wins when it exists (every install since June
    /// 2020 has one), the old settings.xml only when it is the one there.
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
