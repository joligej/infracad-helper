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

    public required string Description { get; set; }

    public IReadOnlyList<LegendSource> Sources { get; init; } = Array.Empty<LegendSource>();
    public string ComparisonKey { get; set; } = "";
    public string CapturedSampleBlock { get; set; } = "";
    public double CapturedSampleScale { get; set; } = 1000;
    public bool CanFollowSource { get; set; } = true;
    public string ApprovedRowId { get; set; } = "";
    public bool MissingSource { get; set; } = true;
    public ExampleMode ExampleMode { get; set; } = ExampleMode.FollowSource;
    public IReadOnlyList<ExampleDisplay> SourceDisplay { get; set; } = Array.Empty<ExampleDisplay>();
    public IReadOnlyList<ExampleDisplay> ExampleDisplay { get; set; } = Array.Empty<ExampleDisplay>();
    public string SourceAppearance { get; set; } = "";
    public string SourceNotice { get; set; } = "";
    public string VariantDetails { get; set; } = "";
    public bool ExcludedBySource { get; set; }
    public bool? SourceExcluded { get; set; }
    public string SampleBlock { get; init; } = "";
    public double SampleScale { get; init; } = 1000;
    public bool Retained { get; init; }

    public IReadOnlyList<string> Netbeheerders { get; init; } = Array.Empty<string>();

    public IReadOnlyList<KlicObjectData> KlicObjects { get; init; } = Array.Empty<KlicObjectData>();
    public bool IsKlic => ElementProperties.IsKlic(Discipline, Hoofdgroep)
        || SourceLayers.Any(s => ElementProperties.IsKlicSource(s.SourceIdentity))
        || KlicObjects.Any(o => o.HasData || ElementProperties.IsKlicSource(Path.GetFileNameWithoutExtension(o.Drawing)));
    public IReadOnlyList<string> SourceEntryKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExclusionKeys => SourceEntryKeys.Count > 0 ? SourceEntryKeys : new[] { LegendSettings.SelectionKey(this) };
    public IReadOnlyList<string> MergedValues { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AppearanceValue> AppearanceValues { get; init; } = Array.Empty<AppearanceValue>();
    public bool ShowMergedValues { get; init; } = true;
    public string KlicGroupKey { get; init; } = string.Empty;
    public IReadOnlyList<string> KlicDescriptionFields { get; init; } = Array.Empty<string>();

    public bool ShowProviderNames { get; set; } = true;
    public IReadOnlyDictionary<string, string> ProviderLabels { get; set; } = new Dictionary<string, string>();

    public string DisplayDescription
    {
        get
        {
            var description = Description;
            if (DescriptionSource is DescriptionSource.Catalogus or DescriptionSource.Laagnaam
                or DescriptionSource.Laagbeschrijving or DescriptionSource.Bloknaam)
                foreach (var provider in Netbeheerders)
                    description = ElementProperties.WithoutProviderSuffix(description, provider);
            var values = (ShowMergedValues ? MergedValues : Array.Empty<string>())
                .Concat(ShowProviderNames && (ShowMergedValues || Netbeheerders.Count <= 1) ? Netbeheerders.Select(p => ProviderLabels.GetValueOrDefault(p, p)) : Array.Empty<string>())
                .Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
            var text = values.Length == 0 ? description : description + " (" + string.Join(", ", values) + ")";
            var extra = KlicSummary.Description(KlicObjects, KlicDescriptionFields.Where(f => ShowProviderNames || f != "Netbeheerder"), ProviderLabels);
            if (Retained && MissingSource) text += " (bron ontbreekt of uitgeschakeld)";
            return extra.Length == 0 ? text : text + " · " + extra;
        }
    }

    public DescriptionSource DescriptionSource { get; init; } = DescriptionSource.Catalogus;

    public bool IsBlank => DescriptionSource == DescriptionSource.Blanco;

    public bool IsManual => DescriptionSource == DescriptionSource.Handmatig;

    public string? ManualHatchPattern { get; init; }

    public Dictionary<NlcsDrawType, string> LayersByType { get; init; } = new();

    public IReadOnlyList<string> SourceRenderLayers { get; init; } = Array.Empty<string>();

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
    public string FullRenderIdentity { get; init; } = string.Empty;
    public string SampleIdentity { get; set; } = string.Empty;

    public string SelectionIdentity { get; init; } = string.Empty;
    public string LegacySelectionIdentity { get; init; } = string.Empty;

    public string AppearanceLabel { get; init; } = string.Empty;

    public string ManualMergeId { get; init; } = string.Empty;

    public IReadOnlyList<string> MergedMembers { get; init; } = System.Array.Empty<string>();

    public QuantityKind QuantityType
    {
        get
        {
            if (IsArea && (Metric.Area > 0 || Metric.HasUnknownArea)) return QuantityKind.Area;
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
            QuantityKind.Area => (Metric.HasUnknownArea ? "?" : Metric.Area.ToString(fmt, nl)) + " " + unitArea,
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

    Blanco,

    Bloknaam
}
