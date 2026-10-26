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
        var aligned = Align(saved, entries.Select(Key).ToArray(), expandSplit: true);
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < aligned.Count; i++) positions.TryAdd(aligned[i], i);
        return entries.OrderBy(e => positions.GetValueOrDefault(Key(e), int.MaxValue)).ToList();
    }

    // oude positie bij dezelfde inhoud of gesplitste tekentypen
    private static List<string> Align(IReadOnlyList<string> saved, IReadOnlyList<string> current, bool expandSplit = false)
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
        return saved.SelectMany(key =>
        {
            var logical = Logical(key);
            if (oldGroups[logical].Length == 1 && newGroups.TryGetValue(logical, out var matches))
            {
                if (matches.Length == 1) return matches;
                if (expandSplit && IsTypeSplit(key, matches)) return matches;
            }
            return new[] { key };
        }).ToList();
    }

    private static bool IsTypeSplit(string previous, string[] current)
    {
        try
        {
            var before = JsonSerializer.Deserialize<string[]>(previous);
            var parts = current.Select(key => JsonSerializer.Deserialize<string[]>(key)).ToArray();
            if (before is not { Length: 4 } || parts.Any(p => p is not { Length: 4 })) return false;
            var styles = parts.Select(p => p![1]).ToArray();
            var types = styles.Select(s => s.Split('=')[0]).ToArray();
            if (types.Distinct().Count() != types.Length || types.Any(t => !Enum.TryParse<NlcsDrawType>(t, out _))) return false;
            return before[1] == string.Join(";", styles.OrderBy(s => Enum.Parse<NlcsDrawType>(s.Split('=')[0])));
        }
        catch (JsonException) { return false; }
    }

    public static Dictionary<string, string> AlignChoices(IReadOnlyDictionary<string, string> choices, IEnumerable<LegendEntry> entries)
    {
        var previous = choices.ToArray();
        var aligned = Align(previous.Select(p => p.Key).ToArray(), entries.Select(Key).ToArray());
        return previous.Select((pair, index) => (Key: aligned[index], pair.Value)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }

    public static List<string> Remember(IReadOnlyList<string> previous, IEnumerable<LegendEntry> ordered)
    {
        var current = ordered.Select(Key).Distinct(StringComparer.Ordinal).ToList();
        var visible = current.ToHashSet(StringComparer.Ordinal);
        var pending = new Queue<string>(current);
        var result = new List<string>();
        foreach (var key in Align(previous, current, expandSplit: true).Distinct(StringComparer.Ordinal))
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
