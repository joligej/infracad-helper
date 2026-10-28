namespace NlcsLegenda.Core;

public sealed class RowChoiceIndex
{
    private readonly Dictionary<string, List<(RowChoice Choice, int Order)>> _items = new(StringComparer.OrdinalIgnoreCase);
    public RowChoiceIndex(IEnumerable<RowChoice> choices)
    {
        int order = 0;
        foreach (var choice in choices)
        {
            foreach (var key in choice.Sources.SelectMany(s => Keys(s, choice.Scope)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_items.TryGetValue(key, out var values)) _items[key] = values = new();
                values.Add((choice, order));
            }
            order++;
        }
    }

    private static IEnumerable<string> Keys(LegendSource source, ChoiceScope scope, bool lookup = false)
    {
        string suffix = $"|{source.Type}|{source.Status}";
        if (scope == ChoiceScope.Variant)
            return (lookup ? source.IdentityKeys.Append(source.Id) : source.IdentityKeys).Select(id => "V:" + id + suffix);
        string symbol = scope == ChoiceScope.ObjectKind ? "|" + source.Symbol : "";
        string prefix = scope == ChoiceScope.Layer ? "L:" : "O:";
        var keys = new List<string> { prefix + source.Layer + suffix + symbol };
        if (source.DrawingId.Length > 0) keys.Add(prefix + source.DrawingId + ":" + LayerNaming.LocalName(source.Layer) + suffix + symbol);
        return keys;
    }

    public RowChoice[] Find(IEnumerable<LegendSource> sources) => sources.SelectMany(source =>
            Enum.GetValues<ChoiceScope>().SelectMany(scope => Keys(source, scope, lookup: true)))
        .Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(key => _items.GetValueOrDefault(key) ?? new())
        .Distinct().OrderBy(p => p.Choice.Scope == ChoiceScope.Layer ? 0 : p.Choice.Scope == ChoiceScope.ObjectKind ? 1 : 2)
        .ThenBy(p => p.Order).Select(p => p.Choice).ToArray();

    public bool Includes(LegendSource source) => Find(new[] { source }).LastOrDefault(c => c.Excluded.HasValue)?.Excluded == false;
}
