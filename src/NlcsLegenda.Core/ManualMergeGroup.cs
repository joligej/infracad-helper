namespace NlcsLegenda.Core;

public sealed class ManualMergeGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MainKey { get; set; } = string.Empty;
    public List<string> Members { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Id) && Members is { Count: > 1 } && Members.Contains(MainKey);
    public ManualMergeGroup Clone() => new() { Id = Id, MainKey = MainKey, Members = new(Members) };

    public static ManualMergeGroup Create(IReadOnlyList<LegendEntry> members, LegendEntry main)
    {
        if (members.Count < 2 || !members.Contains(main))
            throw new ArgumentException("Selecteer minstens twee regels en kies daaruit het hoofdobject.");
        if (members.Any(e => e.IsBlank || e.ManualMergeId.Length > 0))
            throw new ArgumentException("Hef bestaande handmatige samenvoegingen eerst op. Blanco regels kunnen niet worden samengevoegd.");
        if (members.Select(e => e.QuantityType).Where(k => k != QuantityKind.None).Distinct().Count() > 1)
            throw new ArgumentException("Deze regels gebruiken verschillende eenheden. Houd aantallen, lengtes en oppervlakken apart.");
        return new() { MainKey = LegendOrder.Key(main), Members = members.Select(LegendOrder.Key).Distinct().ToList() };
    }

    public static List<LegendEntry> Apply(List<LegendEntry> entries, IEnumerable<ManualMergeGroup> groups, bool showValues = true)
    {
        var result = new List<LegendEntry>(entries);
        foreach (var group in groups.Where(g => g.IsValid))
        {
            var choices = group.Members.Distinct().ToDictionary(k => k, k => k);
            var aligned = LegendOrder.AlignChoices(choices, result);
            var members = result.Where(e => aligned.ContainsKey(LegendOrder.Key(e))).ToList();
            var main = members.FirstOrDefault(e => aligned[LegendOrder.Key(e)] == group.MainKey);
            // ontbrekend hoofdobject of gewijzigde eenheden: bronregels behouden
            if (main is null || members.Count < 2 ||
                members.Select(e => e.QuantityType).Where(k => k != QuantityKind.None).Distinct().Count() > 1) continue;
            int index = result.IndexOf(main);
            var label = MergedDescription.Create(members, main, Enum.GetValues<GroupDimension>());
            result[index] = LegendGrouping.Combine(members, main, group.Id, label.Text, label.Values, showValues);
            result.RemoveAll(e => members.Contains(e));
        }
        return result;
    }
}
