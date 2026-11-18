using System.Text.Json;

namespace OrbitTweak.Core;

/// <summary>Counter-Strike 2 (2.30). Stub: filled in by the Counter-Strike 2 feature. Pure rules: Core/Games/Cs2Logic.cs.</summary>
internal sealed class Cs2Tweaks : IGameTweaks
{
    public string Id => "cs2";
    public string Name => "Counter-Strike 2";
    public GameInfo Info() => new() { Id = Id, Name = Name };
    public List<GameItem> Items() => new();
    public object? Set(string itemId, JsonElement value) => throw new InvalidOperationException("Unbekannte Einstellung.");
    public int Revert() => 0;
}
