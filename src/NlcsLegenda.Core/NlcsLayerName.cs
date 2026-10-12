namespace NlcsLegenda.Core;

public sealed record NlcsLayerSource(string LogicalLayerName, string SourceLayerName, string SourceIdentity);

public sealed class NlcsLayerName
{
    public required string Raw { get; init; }

    public required string LocalName { get; init; }

    public string LogicalLayerName => LocalName;

    public string SourceLayerName => Raw;

    public bool IsXref { get; init; }

    public string XrefName { get; init; } = string.Empty;

    public string SourceIdentity => IsXref ? XrefName : string.Empty;

    public IReadOnlyList<NlcsLayerSource> SourceLayers { get; init; } = Array.Empty<NlcsLayerSource>();

    public required string StatusCode { get; init; }

    public NlcsStatus Status { get; init; }

    public required string Discipline { get; init; }

    public required string Hoofdgroep { get; init; }

    public required string Element { get; init; }

    public required string TypeSuffix { get; init; }

    public NlcsDrawType DrawType { get; init; }

    public int? Scale { get; init; }

    public string RenderLayerName { get; init; } = string.Empty;

    public string EffectiveRenderLayerName => string.IsNullOrEmpty(RenderLayerName) ? LocalName : RenderLayerName;

    public string RenderIdentity { get; init; } = string.Empty;

    public NlcsLayerSource Source => new(LogicalLayerName, SourceLayerName, SourceIdentity);

    public string GroupKey => $"{StatusCode}|{Discipline}|{Hoofdgroep}|{Element}";

    public override string ToString() => LocalName;
}
