namespace NlcsLegenda.Core;

public sealed record LegendSource(string Id, string Layer, NlcsDrawType Type, string Symbol, NlcsStatus Status)
{
    public string DrawingId { get; init; } = "";
    public string SourceHandle { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> IdentityKeys => DrawingId.Length > 0 && SourceHandle.Length > 0
        ? new[] { "DWG:" + DrawingId + ":" + SourceHandle } : new[] { Id };
    public bool SameObject(LegendSource other) => Status == other.Status && Type == other.Type
        && (DrawingId.Length == 0 || other.DrawingId.Length == 0 ? Id == other.Id
            : DrawingId == other.DrawingId && SourceHandle == other.SourceHandle);
    public bool SameLayer(LegendSource other) => LayerKey == other.LayerKey || DrawingId.Length > 0
        && DrawingId == other.DrawingId && LayerNaming.LocalName(Layer).Equals(LayerNaming.LocalName(other.Layer), StringComparison.OrdinalIgnoreCase);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Layer)
        && Symbol is not null && DrawingId is not null && SourceHandle is not null && Enum.IsDefined(Type) && Enum.IsDefined(Status);
    [System.Text.Json.Serialization.JsonIgnore]
    public string Category => $"{Layer}|{Type}|{Symbol}|{Status}".ToUpperInvariant();
    [System.Text.Json.Serialization.JsonIgnore]
    public string LayerKey => $"{Layer}|{Type}|{Status}".ToUpperInvariant();
}

public enum ChoiceScope { Variant, Layer, ObjectKind }

public sealed class RowChoice
{
    public List<LegendSource> Sources { get; set; } = new();
    public ChoiceScope Scope { get; set; }
    public string? Description { get; set; }
    public bool? Excluded { get; set; }
    public string? Example { get; set; }
    public bool AutomaticExample { get; set; }
    public string SavedRowId { get; set; } = "";
    public double ExampleScale { get; set; } = 1000;
    public string PreviousRowId { get; set; } = "";
    public RowChoice Clone() => new() { Sources = new(Sources), Scope = Scope,
        Description = Description, Excluded = Excluded, Example = Example, AutomaticExample = AutomaticExample, SavedRowId = SavedRowId,
        ExampleScale = ExampleScale, PreviousRowId = PreviousRowId };
    public bool Matches(LegendEntry entry) => (SavedRowId.Length > 0 && entry.Element == "BEWAARD_" + SavedRowId)
        || entry.Sources.Any(Matches);
    public bool Matches(LegendSource current) => Sources.Any(old =>
        current.Status == old.Status && current.Type == old.Type && (Scope switch
        {
            ChoiceScope.Layer => current.SameLayer(old),
            ChoiceScope.ObjectKind => current.SameLayer(old) && current.Symbol.Equals(old.Symbol, StringComparison.OrdinalIgnoreCase),
            _ => current.SameObject(old)
        }));
}

public sealed class SourceRow
{
    public string SelectionKey { get; set; } = "";
    public string OrderKey { get; set; } = "";
    public string Description { get; set; } = "";
    public List<LegendSource> Sources { get; set; } = new();
    public SourceRow Clone() => new() { SelectionKey = SelectionKey, OrderKey = OrderKey,
        Description = Description, Sources = new(Sources) };
    public static SourceRow From(LegendEntry entry) => new() { SelectionKey = LegendSettings.SelectionKey(entry),
        OrderKey = LegendOrder.Key(entry), Description = entry.DisplayDescription, Sources = entry.Sources.ToList() };
}
