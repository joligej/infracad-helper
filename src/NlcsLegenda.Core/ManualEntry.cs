namespace NlcsLegenda.Core;

public sealed class ManualEntry
{
    public LegendObjectLink? Link { get; set; }
    public ExampleMode ExampleMode { get; set; } = ExampleMode.Fixed;
    public List<LegendSource> LinkedSources { get; set; } = new();
    public string ComparisonKey { get; set; } = "";
    public string SampleBlock { get; set; } = "";
    public double SampleScale { get; set; } = 1000;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    public string Layer { get; set; } = string.Empty;

    public NlcsDrawType Type { get; set; } = NlcsDrawType.Geometrie;

    public string Description { get; set; } = string.Empty;

    public NlcsStatus Status { get; set; } = NlcsStatus.Nieuw;

    public string Hoofdgroep { get; set; } = "HM";

    public string? SymbolBlock { get; set; }

    public string? HatchPattern { get; set; }

    public bool IsValid =>
        LayerNaming.IsValidReference(Layer) && !string.IsNullOrWhiteSpace(Description);

    public ManualEntry Clone() => new()
    {
        Link = Link?.Clone(), ExampleMode = ExampleMode, LinkedSources = new(LinkedSources), ComparisonKey = ComparisonKey,
        Id = Id, SampleBlock = SampleBlock, SampleScale = SampleScale,
        Layer = Layer,
        Type = Type,
        Description = Description,
        Status = Status,
        Hoofdgroep = Hoofdgroep,
        SymbolBlock = SymbolBlock,
        HatchPattern = HatchPattern
    };

    public LegendEntry ToLegendEntry(string? customStatusName = null) => new()
    {
        ComparisonKey = ComparisonKey, SampleBlock = SampleBlock, SampleScale = SampleScale, Sources = LinkedSources, ExampleMode = ExampleMode, CanFollowSource = Link is not null || LinkedSources.Count > 0,
        Status = Status,
        CustomStatusName = customStatusName,
        Discipline = "XX",
        Hoofdgroep = string.IsNullOrWhiteSpace(Hoofdgroep) ? "HM" : Hoofdgroep.ToUpperInvariant(),
        Element = !string.IsNullOrEmpty(Id) ? "EIGEN_" + Id : Slug(Description),
        Description = Description.Trim(),
        DescriptionSource = DescriptionSource.Handmatig,
        LayersByType = new Dictionary<NlcsDrawType, string> { [Type] = Layer.Trim() },
        SymbolBlockName = string.IsNullOrWhiteSpace(SymbolBlock) ? null : SymbolBlock!.Trim(),
        ManualHatchPattern = string.IsNullOrWhiteSpace(HatchPattern) ? "SOLID" : HatchPattern.Trim(),
        RenderIdentity = string.IsNullOrEmpty(Id) ? LegacyIdentity() : "eigen:" + Id,
        LegacySelectionIdentity = LegacyIdentity(),
        Metric = LayerMetric.Empty
    };

    private string LegacyIdentity() => System.Text.Json.JsonSerializer.Serialize(new[] {
        Type.ToString(), Layer.Trim().ToUpperInvariant(), (SymbolBlock ?? "").Trim().ToUpperInvariant(), (HatchPattern ?? "").Trim().ToUpperInvariant() });

    private static string Slug(string text)
    {
        var chars = (text ?? string.Empty).Trim().ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var slug = new string(chars).Trim('_');
        return slug.Length == 0 ? "HANDMATIG" : slug;
    }
}
