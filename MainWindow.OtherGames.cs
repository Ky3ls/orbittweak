using System.Text.Json;
using OrbitTweak.Core;

namespace OrbitTweak;

/// <summary>
/// Andere Spiele (2.30): the host side of the page "Andere Spiele" (Web/og.js). The games are in Core/Games, the shared
/// models and the list in Core/OtherGames.cs.
///   og.games → { games: [GameInfo] }
///   og.items { game } → { game: GameInfo, items: [GameItem] }
///   og.set { game, id, value } → { ok, value }   (a refused change comes back as a German error)
///   og.apply { game } → { ok, applied, failed: [{ id, title, error }] }   ("Empfohlene anwenden": every Rec item to RecValue)
///   og.revert { game } → { ok, reverted }
/// Each change goes to the activity feed in the page's words (Core/Games/GameFeedLogic.cs).
/// </summary>
public partial class MainWindow
{
    private async Task<object?> OgCmd(string cmd, JsonElement a)
    {
        switch (cmd)
        {
            case "og.games":
                return new { games = await Task.Run(() => OtherGames.All.Select(SafeInfo).ToList()) };

            case "og.items":
            {
                var g = OtherGames.Get(Str(a, "game"));
                return await Task.Run(() => new { game = SafeInfo(g), items = g.Items() });
            }

            case "og.set":
            {
                var g = OtherGames.Get(Str(a, "game"));
                var id = Str(a, "id");
                var v = a.TryGetProperty("value", out var p) ? p.Clone() : default;
                var (now, it) = await Task.Run(() =>
                {
                    var item = g.Items().FirstOrDefault(i => i.Id == id);
                    return (g.Set(id, v), item);
                });
                // the feed says it like the page: the option's label, the number with its unit, an/aus
                var shown = GameFeedLogic.Value(it?.Kind, it?.Options?.Select(o => (o.V, o.T)), it?.Unit, now);
                Log($"🎮 {g.Name}: {it?.Title ?? id} → {shown}");
                return new { ok = true, value = now };
            }

            case "og.apply":
            {
                var g = OtherGames.Get(Str(a, "game"));
                var (applied, failed) = await Task.Run(() =>
                {
                    int n = 0; var bad = new List<object>();
                    foreach (var i in g.Items().Where(i => i.Rec && i.Available))
                    {
                        if (JsonSerializer.Serialize(i.Value) == JsonSerializer.Serialize(i.RecValue)) continue;
                        try { g.Set(i.Id, JsonSerializer.SerializeToElement(i.RecValue)); n++; }
                        catch (Exception ex) { bad.Add(new { id = i.Id, title = i.Title, error = ex.Message }); }
                    }
                    return (n, bad);
                });
                if (applied > 0) Log($"🎮 {g.Name}: {GameFeedLogic.Applied(applied)}");
                return new { ok = failed.Count == 0, applied, failed };
            }

            case "og.revert":
            {
                var g = OtherGames.Get(Str(a, "game"));
                int n = await Task.Run(g.Revert);
                if (n > 0) Log($"↺ {g.Name}: {GameFeedLogic.Reverted(n)}");
                return new { ok = true, reverted = n };
            }
        }
        throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
    }

    /// <summary>A game whose detection fails still shows up, as not installed.</summary>
    private static GameInfo SafeInfo(IGameTweaks g)
    {
        try { return g.Info(); }
        catch { return new GameInfo { Id = g.Id, Name = g.Name }; }
    }
}
