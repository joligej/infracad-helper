namespace NlcsLegenda.Core;

public sealed class SavedLegendRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Description { get; set; } = "";
    public NlcsStatus Status { get; set; }
    public string? CustomStatusName { get; set; }
    public string SampleBlock { get; set; } = "";
    public double Scale { get; set; } = 1000;
    public string OrderKey { get; set; } = "";
    public List<string> ExclusionKeys { get; set; } = new();
    public List<LegendSource> Sources { get; set; } = new();
    public string Appearance { get; set; } = "";
    public bool Manual { get; set; }
    public SavedLegendRow Clone() => new() { Id = Id, Description = Description, Status = Status,
        CustomStatusName = CustomStatusName, SampleBlock = SampleBlock, Scale = Scale, OrderKey = OrderKey,
        ExclusionKeys = new(ExclusionKeys), Sources = new(Sources), Appearance = Appearance, Manual = Manual };

    public LegendEntry RetainedEntry() => new()
    {
        Status = Status, CustomStatusName = CustomStatusName, Discipline = "XX", Hoofdgroep = "HM",
        Element = "BEWAARD_" + Id, Description = Description, DescriptionSource = DescriptionSource.Handmatig,
        SampleBlock = SampleBlock, SampleScale = Scale, Sources = Sources, Retained = true,
        SourceEntryKeys = ExclusionKeys, SourceNotice = "Bron ontbreekt of staat uit; vorig voorbeeld bewaard.",
        LayersByType = new() { [NlcsDrawType.Symbool] = "0" }, SymbolBlockName = SampleBlock,
        RenderIdentity = Appearance, AppearanceLabel = "bewaard voorbeeld"
    };
}

public sealed record LegendChange(string Change, string Description, string Details);

public static class LegendUpdatePlan
{
    public static IReadOnlyList<LegendChange> Compare(IReadOnlyList<SavedLegendRow> before, IReadOnlyList<LegendEntry> after)
    {
        var changes = new List<LegendChange>();
        foreach (var old in before)
        {
            var matches = after.Where(e => Matches(old, e)).ToArray();
            if (matches.Length == 0) changes.Add(new("Verdwenen", old.Description, "Bron ontbreekt, is uitgeschakeld of bewust uitgesloten."));
            else if (matches.Length > 1) changes.Add(new("Gesplitst", old.Description, string.Join("; ", matches.Select(Describe))));
            else if (matches[0].Retained) changes.Add(new("Bewaard", old.Description, matches[0].SourceNotice));
            else if (Appearance(matches[0]) != old.Appearance) changes.Add(new("Voorbeeld gewijzigd", matches[0].DisplayDescription, Describe(matches[0])));
        }
        foreach (var entry in after)
        {
            var matches = before.Where(o => Matches(o, entry)).ToArray();
            if (matches.Length == 0) changes.Add(new("Toegevoegd", entry.DisplayDescription, Describe(entry)));
            else if (matches.Length > 1) changes.Add(new("Samengevoegd", entry.DisplayDescription, string.Join("; ", matches.Select(o => o.Description))));
            if (entry.SourceNotice.Length > 0 && !entry.Retained) changes.Add(new("Controleren", entry.DisplayDescription, entry.SourceNotice));
            if (entry.Sources.Select(s => s.Status).Distinct().Count() > 1)
                changes.Add(new("Statussen gecombineerd", entry.DisplayDescription, string.Join(", ", entry.Sources.Select(s => s.Status).Distinct())));
        }
        return changes;
    }

    private static bool Matches(SavedLegendRow previous, LegendEntry current) =>
        (current.Retained && current.Element == "BEWAARD_" + previous.Id)
        || LegendContinuity.Related(previous.Sources, current.Sources) || previous.OrderKey == LegendOrder.Key(current);

    public static string Appearance(LegendEntry entry) => entry.SampleIdentity.Length > 0 ? entry.SampleIdentity : entry.RenderIdentity;

    public static string Describe(LegendEntry entry) => string.Join(" · ", new[]
    {
        entry.CustomStatusName ?? entry.Status.ToString(), string.Join(", ", entry.LayersByType.Keys), entry.AppearanceLabel,
        string.Join(", ", entry.Sources.Select(s => s.Symbol).Where(s => s.Length > 0).Distinct()),
        string.Join(", ", entry.SourceLayers.Select(s => s.SourceLayerName).Concat(entry.Sources.Select(s => s.Layer)).Distinct())
    }.Where(s => s.Length > 0));
}
