using System.Text.Json;

namespace OrbitTweak.Core;

/// <summary>Fortnite (2.30). Stub: filled in by the Fortnite feature. Pure rules: Core/Games/FortniteLogic.cs.</summary>
internal sealed class FortniteTweaks : IGameTweaks
{
    public string Id => "fortnite";
    public string Name => "Fortnite";
    public GameInfo Info() => new() { Id = Id, Name = Name };
    public List<GameItem> Items() => new();
    public object? Set(string itemId, JsonElement value) => throw new InvalidOperationException("Unbekannte Einstellung.");
    public int Revert() => 0;
}
