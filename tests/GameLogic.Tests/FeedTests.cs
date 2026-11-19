using OrbitTweak.Core;

/// <summary>Checks of GameFeedLogic (the lines of "Andere Spiele" in the activity feed). Called from Program.cs.</summary>
internal static class FeedTests
{
    public static void Run(Action<bool, string> Check)
    {
        static (string, string)[] Opts(params (string V, string T)[] o) => o;

        // choices say their label, not the code in the file
        var mode = R6Logic.Find("windowmode")!.Options!;
        Check(GameFeedLogic.Value("choice", mode, null, "2") == "Rahmenlos" && GameFeedLogic.Value("choice", mode, null, "1") == "Fenster", "feed: Siege display mode 2 is Rahmenlos, 1 is Fenster");
        Check(GameFeedLogic.Value("choice", FortniteLogic.RenderOptions, null, "perf") == "Leistung", "feed: Fortnite render mode perf is Leistung");
        Check(GameFeedLogic.Value("choice", FortniteLogic.ShadowOptions, null, "0") == "Aus", "feed: Fortnite shadows 0 is Aus");
        Check(GameFeedLogic.Value("choice", Cs2Logic.TelemetryOptions(), null, "2") == "Immer", "feed: CS2 telemetry 2 is Immer");
        Check(GameFeedLogic.Value("choice", Opts(("0", "Aus")), null, "7") == "7" && GameFeedLogic.Value("choice", null, null, "7") == "7", "feed: an unknown choice falls back to its value");

        // numbers with their unit, German decimal comma
        Check(GameFeedLogic.Value("number", null, "FPS", 237) == "237 FPS" && GameFeedLogic.Value("number", null, "FPS", 0) == "0 FPS", "feed: CS2 fps_max says its unit");
        Check(GameFeedLogic.Value("number", null, "ms", 2.5) == "2,5 ms" && GameFeedLogic.Value("number", null, null, 141) == "141", "feed: a decimal comma, no unit when there is none");

        // switches and nothing
        Check(GameFeedLogic.Value("switch", null, null, true) == "an" && GameFeedLogic.Value("switch", null, null, false) == "aus", "feed: switches are an/aus");
        Check(GameFeedLogic.Value(null, null, null, true) == "an", "feed: a bool is an/aus whatever the kind");
        Check(GameFeedLogic.Value("choice", mode, null, null) == "–" && GameFeedLogic.Value("number", null, "FPS", null) == "–", "feed: no value is a dash");

        // one or many
        Check(GameFeedLogic.Applied(1) == "1 empfohlene Einstellung gesetzt" && GameFeedLogic.Applied(3) == "3 empfohlene Einstellungen gesetzt", "feed: applied, singular and plural");
        Check(GameFeedLogic.Reverted(1) == "1 Einstellung zurückgesetzt" && GameFeedLogic.Reverted(2) == "2 Einstellungen zurückgesetzt", "feed: reverted, singular and plural");
    }
}
