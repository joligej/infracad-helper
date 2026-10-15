using System.Text.Json;

namespace NlcsLegenda.Core;

public static class LegendOrder
{
    public static string Key(LegendEntry entry) => JsonSerializer.Serialize(new[]
    {
        LegendSettings.EntryKey(entry).ToUpperInvariant(),
        entry.RenderIdentity,
        string.Join(";", entry.MergedMembers.OrderBy(m => m, StringComparer.OrdinalIgnoreCase)),
        entry.IsBlank ? "blank" : "entry"
    });

    public static List<LegendEntry> Apply(IEnumerable<LegendEntry> automatic, IReadOnlyList<string> saved)
    {
        var entries = automatic.ToList();
        var aligned = Align(saved, entries.Select(Key).ToArray());
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < aligned.Count; i++) positions.TryAdd(aligned[i], i);
        return entries.OrderBy(e => positions.GetValueOrDefault(Key(e), int.MaxValue)).ToList();
    }

    // oude positie alleen hergebruiken bij een unieke inhoudelijke regel
    private static List<string> Align(IReadOnlyList<string> saved, IReadOnlyList<string> current)
    {
        string Logical(string key)
        {
            try
            {
                var parts = JsonSerializer.Deserialize<string[]>(key);
                return parts is { Length: 4 } ? JsonSerializer.Serialize(new[] { parts[0], parts[2], parts[3] }) : key;
            }
            catch (JsonException) { return key; }
        }
        var oldGroups = saved.Distinct().GroupBy(Logical).ToDictionary(g => g.Key, g => g.ToArray());
        var newGroups = current.Distinct().GroupBy(Logical).ToDictionary(g => g.Key, g => g.ToArray());
        return saved.Select(key =>
        {
            var logical = Logical(key);
            return oldGroups[logical].Length == 1 && newGroups.TryGetValue(logical, out var matches) && matches.Length == 1
                ? matches[0] : key;
        }).ToList();
    }

    public static List<string> Remember(IReadOnlyList<string> previous, IEnumerable<LegendEntry> ordered)
    {
        var current = ordered.Select(Key).Distinct(StringComparer.Ordinal).ToList();
        var visible = current.ToHashSet(StringComparer.Ordinal);
        var pending = new Queue<string>(current);
        var result = new List<string>();
        foreach (var key in Align(previous, current).Distinct(StringComparer.Ordinal))
        {
            if (visible.Contains(key))
            {
                if (pending.Count > 0) result.Add(pending.Dequeue());
            }
            else result.Add(key);
        }
        result.AddRange(pending);
        return result;
    }
}
