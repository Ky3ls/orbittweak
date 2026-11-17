using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

/// <summary>
/// The short line under each tweak (Sum in Core/Catalog.cs) and the browser preview's copy of the list (the Mock in
/// Web/app.js): every tweak and removed tweak has one, it is short, plain and calm and promises no FPS, and the preview
/// shows the same names and texts as the app. Read from the source files, because the catalog itself needs Windows.
/// </summary>
internal static class TweakTexts
{
    /// <summary>Abbreviations that may stand in capitals; any other word in capitals is shouting.</summary>
    private static readonly HashSet<string> Abbr = new() { "CPU", "GPU", "FPS", "USB", "LAN", "WLAN", "PC", "RAM", "SSD", "UI" };

    public static void Run(Action<bool, string> check, [CallerFilePath] string here = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
        var cat = File.ReadAllText(Path.Combine(root, "Core", "Catalog.cs"));
        var js = File.ReadAllText(Path.Combine(root, "Web", "app.js"));

        // the tweaks: Name, then Sum and Desc on the next two lines
        var tw = Regex.Matches(cat, @"Id = ""(\w+)"", Cat = ""(\w+)"".*Name = ""([^""]*)"",\r?\n\s*Sum = ""([^""]*)"",\r?\n\s*Desc = ""([^""]*)""")
            .ToDictionary(m => m.Groups[1].Value, m => (cat: m.Groups[2].Value, name: m.Groups[3].Value, sum: m.Groups[4].Value, desc: m.Groups[5].Value));
        int count = Regex.Matches(cat, @"new Tweak\r?\n\s*\{").Count;
        check(count > 15 && tw.Count == count, $"every tweak has a short line ({tw.Count} of {count})");

        // the removed tweaks: new("id", "Name", then Sum and Desc on the next two lines
        var old = Regex.Matches(cat, @"new\(""(\w+)"", ""([^""]*)"",\r?\n\s*""([^""]*)"",\r?\n\s*""([^""]*)"",")
            .ToDictionary(m => m.Groups[1].Value, m => (name: m.Groups[2].Value, sum: m.Groups[3].Value, desc: m.Groups[4].Value));
        int oldCount = Regex.Matches(cat, @"^\s*new\(""\w+"", ""[^""]*"",\r?$", RegexOptions.Multiline).Count;
        check(oldCount > 5 && old.Count == oldCount, $"every removed tweak has a short line ({old.Count} of {oldCount})");

        foreach (var (id, name, sum, desc) in tw.Select(x => (x.Key, x.Value.name, x.Value.sum, x.Value.desc)).Concat(old.Select(x => (x.Key, x.Value.name, x.Value.sum, x.Value.desc))))
        {
            check(sum.Length is >= 35 and <= 70, $"{id}: the short line has 35 to 70 characters ({sum.Length})");
            check(sum != name && sum != desc && desc.Length > sum.Length, $"{id}: the short line says more than the name and less than the description");
            check(!sum.Contains('!') && !sum.EndsWith('.') && char.IsUpper(sum[0]), $"{id}: the short line is calm (capital first, no exclamation mark, no full stop)");
            var caps = Regex.Matches(sum, @"\b[A-ZÄÖÜ]{2,}\b").Select(m => m.Value).Where(w => !Abbr.Contains(w)).ToList();
            check(caps.Count == 0, $"{id}: no words in capitals in the short line ({string.Join(", ", caps)})");
            check(!Regex.IsMatch(sum, @"\bFPS\b|mehr Leistung|schneller", RegexOptions.IgnoreCase), $"{id}: the short line promises no FPS (the description says where there are any)");
        }

        // the browser preview: ['id', 'cat', 'name', 'sum', 'desc', ...] and the removed ones as { id, name, sum, desc }
        var mock = Regex.Matches(js, @"^\s*\['(\w+)', '(\w+)', '([^']*)', '([^']*)', '([^']*)',", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => (cat: m.Groups[2].Value, name: m.Groups[3].Value, sum: m.Groups[4].Value, desc: m.Groups[5].Value));
        check(mock.Keys.OrderBy(k => k).SequenceEqual(tw.Keys.OrderBy(k => k)), "the preview lists the same tweaks as the app");
        foreach (var (id, m) in mock)
            check(tw.TryGetValue(id, out var t) && t == m, $"{id}: the preview shows the same category, name, short line and description as the app");
        var mockOld = Regex.Matches(js, @"\{ id: '(\w+)', name: '([^']*)', sum: '([^']*)', desc: '([^']*)'")
            .Select(m => (id: m.Groups[1].Value, v: (name: m.Groups[2].Value, sum: m.Groups[3].Value, desc: m.Groups[4].Value))).ToList();
        check(mockOld.Count > 0, "the preview has removed tweaks with a short line");
        foreach (var (id, v) in mockOld)
            check(old.TryGetValue(id, out var o) && o == v, $"{id}: the preview shows the removed tweak as the app does");
    }
}
