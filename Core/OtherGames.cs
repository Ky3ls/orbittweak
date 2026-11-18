using System.Text.Json;

namespace OrbitTweak.Core;

// =====================================================================
//  Andere Spiele (2.30): tweaks for Fortnite, Counter-Strike 2 and Rainbow Six Siege.
//  Each game lives in its own file under Core/Games (the Windows part) with its pure rules in Core/Games/<Game>Logic.cs
//  (checked by tests/GameLogic.Tests). Host commands: MainWindow.OtherGames.cs. Page: Web/og.js, mocks Web/og-<game>.js.
//  Anti-cheat (Easy Anti-Cheat, VAC, BattlEye): only the game's own settings files, Windows and driver settings are
//  changed, never the game's files or anything of a running game.
// =====================================================================

/// <summary>What the page knows about a game.</summary>
public sealed class GameInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Installed { get; init; }
    /// <summary>The install folder, when known.</summary>
    public string? Path { get; init; }
    public bool Running { get; init; }
    /// <summary>The settings file the tweaks write was found (most games make it on their first start).</summary>
    public bool ConfigFound { get; init; }
    public string? ConfigPath { get; init; }
    /// <summary>One honest line for the page, e.g. "Starte Fortnite einmal, dann legt es seine Einstellungen an".</summary>
    public string Note { get; init; } = "";
    /// <summary>The game's anti-cheat, named on the page with why the tweaks are safe with it.</summary>
    public string AntiCheat { get; init; } = "";
}

/// <summary>A choice of a "choice" item: v = value, t = German label.</summary>
public sealed record GameOption(string V, string T);

/// <summary>One setting of a game as the page shows it.</summary>
public sealed class GameItem
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>One short line under the title.</summary>
    public string Sum { get; init; } = "";
    /// <summary>The full, honest explanation behind "Details" (what it changes, what it brings, the trade-off).</summary>
    public string Desc { get; init; } = "";
    /// <summary>"switch" (Value bool), "choice" (Value string, Options), "number" (Value number, Min/Max/Step/Unit).</summary>
    public string Kind { get; init; } = "switch";
    public object? Value { get; init; }
    public List<GameOption>? Options { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    public string? Unit { get; init; }
    /// <summary>Part of "Empfohlene anwenden" for this game, which sets RecValue.</summary>
    public bool Rec { get; init; }
    public object? RecValue { get; init; }
    /// <summary>"fps", "latency", "comfort" or "stability": what it is good for.</summary>
    public string Effect { get; init; } = "";
    /// <summary>Short note where the effect is documented (shown in the details).</summary>
    public string Source { get; init; } = "";
    /// <summary>The game rewrites its settings file on exit: it has to be closed for this item.</summary>
    public bool NeedsClosed { get; init; }
    public bool Available { get; init; } = true;
    /// <summary>Why it is not available (no NVIDIA card, settings file missing …).</summary>
    public string Why { get; init; } = "";
    /// <summary>OrbitTweak changed it: "Zurücksetzen" brings back what was there before.</summary>
    public bool Changed { get; init; }
}

/// <summary>The tweaks of one game.</summary>
internal interface IGameTweaks
{
    string Id { get; }
    string Name { get; }
    GameInfo Info();
    List<GameItem> Items();
    /// <summary>Sets one item and returns the value read back. Throws InvalidOperationException with a German message
    /// (game running, file missing, value out of range …). Backs up what was there before the first change.</summary>
    object? Set(string itemId, JsonElement value);
    /// <summary>Restores everything OrbitTweak changed for this game; returns how many items came back.</summary>
    int Revert();
}

internal static class OtherGames
{
    public static readonly IGameTweaks[] All = { new FortniteTweaks(), new Cs2Tweaks(), new R6Tweaks() };

    public static IGameTweaks Get(string? id)
        => All.FirstOrDefault(g => g.Id == id) ?? throw new InvalidOperationException("Unbekanntes Spiel.");
}
