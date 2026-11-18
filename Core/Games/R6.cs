using System.Text.Json;

namespace OrbitTweak.Core;

/// <summary>Rainbow Six Siege (2.30). Stub: filled in by the Rainbow Six Siege feature. Pure rules: Core/Games/R6Logic.cs.</summary>
internal sealed class R6Tweaks : IGameTweaks
{
    public string Id => "r6";
    public string Name => "Rainbow Six Siege";
    public GameInfo Info() => new() { Id = Id, Name = Name };
    public List<GameItem> Items() => new();
    public object? Set(string itemId, JsonElement value) => throw new InvalidOperationException("Unbekannte Einstellung.");
    public int Revert() => 0;
}
