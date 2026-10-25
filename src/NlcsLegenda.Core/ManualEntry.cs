namespace NlcsLegenda.Core;

public sealed class ManualEntry
{
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
        Status = Status,
        CustomStatusName = customStatusName,
        Discipline = "XX",
        Hoofdgroep = string.IsNullOrWhiteSpace(Hoofdgroep) ? "HM" : Hoofdgroep.ToUpperInvariant(),
        Element = Slug(Description),
        Description = Description.Trim(),
        DescriptionSource = DescriptionSource.Handmatig,
        LayersByType = new Dictionary<NlcsDrawType, string> { [Type] = Layer.Trim() },
        SymbolBlockName = string.IsNullOrWhiteSpace(SymbolBlock) ? null : SymbolBlock!.Trim(),
        ManualHatchPattern = string.IsNullOrWhiteSpace(HatchPattern) ? "SOLID" : HatchPattern.Trim(),
        RenderIdentity = System.Text.Json.JsonSerializer.Serialize(new[] {
            Type.ToString(), Layer.Trim().ToUpperInvariant(),
            (SymbolBlock ?? "").Trim().ToUpperInvariant(), (HatchPattern ?? "").Trim().ToUpperInvariant() }),
        Metric = LayerMetric.Empty
    };

    private static string Slug(string text)
    {
        var chars = (text ?? string.Empty).Trim().ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var slug = new string(chars).Trim('_');
        return slug.Length == 0 ? "HANDMATIG" : slug;
    }
}
