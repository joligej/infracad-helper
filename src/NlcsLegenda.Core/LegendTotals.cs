namespace NlcsLegenda.Core;

public sealed class GroupTotal
{
    public required string Hoofdgroep { get; init; }

    public int EntryCount { get; init; }

    public int Count { get; init; }

    public double Length { get; init; }

    public double Area { get; init; }

    public bool HasUnknownArea { get; init; }
}

public static class LegendTotals
{
    public static IReadOnlyList<GroupTotal> ByHoofdgroep(IEnumerable<LegendEntry> entries)
    {
        var groups = new Dictionary<string, (int entries, int count, double length, double area, bool unknownArea)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            var key = string.IsNullOrEmpty(e.Hoofdgroep) ? "??" : e.Hoofdgroep.ToUpperInvariant();
            groups.TryGetValue(key, out var acc);
            acc.entries++;
            switch (e.QuantityType)
            {
                case QuantityKind.Count: acc.count += e.Metric.Count; break;
                case QuantityKind.Length: acc.length += e.Metric.Length; break;
                case QuantityKind.Area: acc.area += e.Metric.Area; acc.unknownArea |= e.Metric.HasUnknownArea; break;
            }
            groups[key] = acc;
        }

        return groups
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new GroupTotal
            {
                Hoofdgroep = kv.Key,
                EntryCount = kv.Value.entries,
                Count = kv.Value.count,
                Length = kv.Value.length,
                Area = kv.Value.area,
                HasUnknownArea = kv.Value.unknownArea
            })
            .ToList();
    }
}
