using System.Globalization;

namespace OrbitTweak.Core;

/// <summary>
/// The lines "Andere Spiele" writes to the activity feed of the home page ("Letzte Aktivität", MainWindow.OtherGames.cs).
/// Values are said the way the page says them (og.js ogShown), never as the codes the settings files hold.
/// tests/GameLogic.Tests/FeedTests.cs checks them.
/// </summary>
public static class GameFeedLogic
{
    /// <summary>A value read back after a change: an/aus for a switch, the option's label for a choice ("2" of the Siege
    /// display mode is "Rahmenlos"), the number with its unit ("240 FPS"); the plain value when nothing fits.</summary>
    /// <param name="options">The item's choices (value, label), null for switches and numbers.</param>
    public static string Value(string? kind, IEnumerable<(string v, string t)>? options, string? unit, object? value)
    {
        switch (value)
        {
            case null: return "–";
            case bool b: return b ? "an" : "aus";
        }
        var raw = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (kind == "choice" && options != null)
            foreach (var (v, t) in options)
                if (v == raw) return t;
        if (kind == "number" && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return raw.Replace('.', ',') + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
        return raw;
    }

    /// <summary>"Empfohlene anwenden" set n items.</summary>
    public static string Applied(int n) => n == 1 ? "1 empfohlene Einstellung gesetzt" : $"{n} empfohlene Einstellungen gesetzt";

    /// <summary>"Zurücksetzen" brought n items back.</summary>
    public static string Reverted(int n) => n == 1 ? "1 Einstellung zurückgesetzt" : $"{n} Einstellungen zurückgesetzt";
}
