namespace NlcsLegenda.Core;

using System.Globalization;

public sealed class LegendEntry
{
    public required NlcsStatus Status { get; init; }

    public string? CustomStatusName { get; init; }

    public string StatusGroupId => CustomStatusName ?? Status.ToString();

    public required string Discipline { get; init; }

    public required string Hoofdgroep { get; init; }

    public required string Element { get; init; }

    public required string Description { get; init; }

    public IReadOnlyList<string> Netbeheerders { get; init; } = Array.Empty<string>();

    public IReadOnlyList<KlicObjectData> KlicObjects { get; init; } = Array.Empty<KlicObjectData>();
    public string KlicGroupKey { get; init; } = string.Empty;
    public IReadOnlyList<string> KlicDescriptionFields { get; init; } = Array.Empty<string>();

    public string DisplayDescription
    {
        get
        {
            var text = Netbeheerders.Count == 0 ? Description : Description + " (" + string.Join(", ", Netbeheerders) + ")";
            var extra = KlicSummary.Description(KlicObjects, KlicDescriptionFields);
            return extra.Length == 0 ? text : text + " · " + extra;
        }
    }

    public DescriptionSource DescriptionSource { get; init; } = DescriptionSource.Catalogus;

    public bool IsBlank => DescriptionSource == DescriptionSource.Blanco;

    public bool IsManual => DescriptionSource == DescriptionSource.Handmatig;

    public string? ManualHatchPattern { get; init; }

    public Dictionary<NlcsDrawType, string> LayersByType { get; init; } = new();

    public IReadOnlyList<NlcsLayerSource> SourceLayers { get; init; } = System.Array.Empty<NlcsLayerSource>();

    // lijngeometrie apart van vlakcontouren
    public string? GeometryLayer =>
        LayersByType.TryGetValue(NlcsDrawType.Geometrie, out var g) ? g : null;

    public string? VlakLayer => LayersByType.TryGetValue(NlcsDrawType.Vlak, out var v) ? v : null;

    public string? HatchLayer => LayersByType.TryGetValue(NlcsDrawType.Arcering, out var a) ? a : null;

    public string? FillLayer => LayersByType.TryGetValue(NlcsDrawType.Vlakvulling, out var v) ? v : null;

    public string? HatchOrFillLayer => HatchLayer ?? FillLayer;

    public string? SymbolLayer => LayersByType.TryGetValue(NlcsDrawType.Symbool, out var s) ? s : null;

    public bool HasHatch => HatchOrFillLayer is not null;

    public bool IsArea => HasHatch || LayersByType.ContainsKey(NlcsDrawType.Vlak);

    public string PrimaryLayer =>
        GeometryLayer ?? VlakLayer ?? HatchOrFillLayer ?? SymbolLayer
        ?? LayersByType.Values.FirstOrDefault() ?? "0";

    public LayerMetric Metric { get; init; } = LayerMetric.Empty;

    public string? SymbolBlockName { get; init; }

    public string RenderIdentity { get; init; } = string.Empty;

    public IReadOnlyList<string> MergedMembers { get; init; } = System.Array.Empty<string>();

    public QuantityKind QuantityType
    {
        get
        {
            if (IsArea && Metric.Area > 0) return QuantityKind.Area;
            // geen lijnlengte uit de interne geometrie van een symbool
            if (GeometryLayer is not null && Metric.Length > 0) return QuantityKind.Length;
            if (Metric.Count > 0) return QuantityKind.Count;
            if (Metric.Length > 0) return QuantityKind.Length;
            if (Metric.Area > 0) return QuantityKind.Area;
            return QuantityKind.None;
        }
    }

    public string QuantityText(int decimals = 0, string unitArea = "m\u00B2",
        string unitLength = "m", string unitCount = "st")
    {
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        string fmt = "N" + Math.Max(0, decimals);
        return QuantityType switch
        {
            QuantityKind.Area => Metric.Area.ToString(fmt, nl) + " " + unitArea,
            QuantityKind.Length => Metric.Length.ToString(fmt, nl) + " " + unitLength,
            QuantityKind.Count => Metric.Count.ToString(nl) + " " + unitCount,
            _ => string.Empty
        };
    }

    public double QuantitySortValue() => QuantityType switch
    {
        QuantityKind.Area => Metric.Area,
        QuantityKind.Length => Metric.Length,
        QuantityKind.Count => Metric.Count,
        _ => 0.0
    };
}

public enum QuantityKind
{
    None,

    Area,

    Length,

    Count
}

public enum DescriptionSource
{
    Laagbeschrijving,

    Catalogus,

    Handmatig,

    Laagnaam,

    EigenKoppeling,

    Blanco
}
