using OrbitTweak.Core;

/// <summary>Checks of the shared INI helper (Core/Games/IniText.cs).</summary>
internal static class IniTests
{
    public static void Run(Action<bool, string> Check)
    {
        const string ue = "[/Script/FortniteGame.FortGameUserSettings]\r\nFrameRateLimit=60.000000\r\nbShowFPS=False\r\n+Arr=1\r\n\r\n[ScalabilityGroups]\r\nsg.ShadowQuality=3\r\n";
        Check(IniText.Get(ue, "/Script/FortniteGame.FortGameUserSettings", "FrameRateLimit") == "60.000000", "read a value");
        Check(IniText.Get(ue, "/script/fortnitegame.fortgameusersettings", "framerATElimit") == "60.000000", "sections and keys without case");
        Check(IniText.Get(ue, "ScalabilityGroups", "FrameRateLimit") == null, "a key of another section is not there");
        Check(IniText.Get(ue, "/Script/FortniteGame.FortGameUserSettings", "Arr") == null, "Unreal array lines are no keys");
        var set = IniText.Set(ue, "/Script/FortniteGame.FortGameUserSettings", "FrameRateLimit", "240.000000");
        Check(set == ue.Replace("FrameRateLimit=60.000000", "FrameRateLimit=240.000000"), "a value is replaced in place, every other byte stays");
        var add = IniText.Set(ue, "/Script/FortniteGame.FortGameUserSettings", "bNew", "True");
        Check(add == ue.Replace("+Arr=1\r\n", "+Arr=1\r\nbNew=True\r\n"), "a new key goes to the end of its section, with CRLF");
        var sec = IniText.Set(ue, "New", "K", "V");
        Check(sec == ue + "[New]\r\nK=V\r\n", "a new section goes to the end");
        Check(IniText.Set("", "S", "K", "V") == "[S]\nK=V\n", "an empty text gets the section");
        Check(IniText.Set("[S]\nK = 1\n; c\n", "S", "K", "2") == "[S]\nK = 2\n; c\n", "spacing round '=' stays, comments stay");
        Check(IniText.Set("[S]\nA=1", "S", "B", "2") == "[S]\nA=1\nB=2", "no newline at the end: the new key follows the last line");
        Check(IniText.Remove(ue, "/Script/FortniteGame.FortGameUserSettings", "bShowFPS") == ue.Replace("bShowFPS=False\r\n", ""), "remove one key line");
        Check(IniText.Remove(ue, "Nope", "bShowFPS") == ue, "remove in a missing section changes nothing");
        Check(IniText.Get("[A]\nk=1\n[A]\nk=2\n", "A", "k") == "1", "the first matching key counts");
        Check(IniText.Set("﻿[S]\r\nK=1\r\n", "S", "K", "2") == "﻿[S]\r\nK=2\r\n", "a BOM stays");
    }
}
