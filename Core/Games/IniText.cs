namespace OrbitTweak.Core;

/// <summary>
/// Reads and changes one key of an INI text (Fortnite's GameUserSettings.ini, Rainbow Six Siege's GameSettings.ini …)
/// and keeps every other byte as it was: comments, order, spacing, line endings. Sections and keys compare without case;
/// the first matching key of a section counts (Unreal's "+Key=" array lines are never touched). Pure, so
/// tests/GameLogic.Tests checks it.
/// </summary>
public static class IniText
{
    /// <summary>The value of a key in a section, trimmed, or null when there is none.</summary>
    public static string? Get(string? text, string section, string key)
    {
        foreach (var (sec, k, v, _) in Entries(text ?? ""))
            if (sec.Equals(section, StringComparison.OrdinalIgnoreCase) && k.Equals(key, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }

    /// <summary>The text with the key set: its value replaced in place, else a new line at the end of the section, else a
    /// new section at the end. New lines use the text's own line ending.</summary>
    public static string Set(string? text, string section, string key, string value)
    {
        var t = text ?? "";
        var nl = t.Contains("\r\n") ? "\r\n" : "\n";
        var lines = Split(t);
        int inSection = -1, lastInSection = -1;
        string? cur = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = Clean(lines[i]);
            if (IsSection(line, out var s)) { cur = s; if (s.Equals(section, StringComparison.OrdinalIgnoreCase) && inSection < 0) { inSection = i; lastInSection = i; } continue; }
            if (cur == null || !cur.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Length > 0) lastInSection = i;
            if (KeyOf(line) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var raw = lines[i];
                bool cr = raw.EndsWith('\r');
                var body = cr ? raw[..^1] : raw;
                int eq = body.IndexOf('=');
                int start = eq + 1;
                while (start < body.Length && body[start] is ' ' or '\t') start++;
                lines[i] = body[..start] + value + (cr ? "\r" : "");
                return string.Join("\n", lines);
            }
        }
        var cr2 = nl == "\r\n" ? "\r" : "";
        if (inSection >= 0)
        {
            lines.Insert(lastInSection + 1, key + "=" + value + cr2);
            return string.Join("\n", lines);
        }
        var r = t;
        if (r.Length > 0 && !r.EndsWith('\n')) r += nl;
        return r + "[" + section + "]" + nl + key + "=" + value + nl;
    }

    /// <summary>The text without the key's first line in that section (for a revert to "was not there").</summary>
    public static string Remove(string? text, string section, string key)
    {
        var lines = Split(text ?? "");
        string? cur = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = Clean(lines[i]);
            if (IsSection(line, out var s)) { cur = s; continue; }
            if (cur != null && cur.Equals(section, StringComparison.OrdinalIgnoreCase) && KeyOf(line) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines.RemoveAt(i);
                return string.Join("\n", lines);
            }
        }
        return text ?? "";
    }

    /// <summary>Every key line: section, key, trimmed value, line index.</summary>
    public static IEnumerable<(string section, string key, string value, int line)> Entries(string text)
    {
        var lines = Split(text);
        string cur = "";
        for (int i = 0; i < lines.Count; i++)
        {
            var line = Clean(lines[i]);
            if (IsSection(line, out var s)) { cur = s; continue; }
            if (KeyOf(line) is { } k) yield return (cur, k, line[(line.IndexOf('=') + 1)..].Trim(), i);
        }
    }

    /// <summary>A line without its CR, spaces and a leading byte order mark.</summary>
    private static string Clean(string raw) => raw.TrimEnd('\r').Trim().TrimStart('\uFEFF').Trim();

    private static List<string> Split(string t) => t.Length == 0 ? new List<string>() : t.Split('\n').ToList();

    private static bool IsSection(string line, out string name)
    {
        name = "";
        if (line.Length < 2 || line[0] != '[' || line[^1] != ']') return false;
        name = line[1..^1].Trim();
        return true;
    }

    /// <summary>The key of a "key=value" line; null for comments, blank lines and Unreal's array lines (+Key=, -Key=, .Key=, !Key=).</summary>
    private static string? KeyOf(string line)
    {
        if (line.Length == 0 || line[0] is ';' or '#' or '+' or '-' or '.' or '!') return null;
        int eq = line.IndexOf('=');
        if (eq <= 0) return null;
        var k = line[..eq].Trim();
        return k.Length == 0 ? null : k;
    }
}
