namespace NlcsLegenda.Core;

public sealed class NlcsLayerName
{
    public required string Raw { get; init; }

    public required string LocalName { get; init; }

    public bool IsXref { get; init; }

    public string XrefName { get; init; } = string.Empty;

    public required string StatusCode { get; init; }

    public NlcsStatus Status { get; init; }

    public required string Discipline { get; init; }

    public required string Hoofdgroep { get; init; }

    public required string Element { get; init; }

    public required string TypeSuffix { get; init; }

    public NlcsDrawType DrawType { get; init; }

    public int? Scale { get; init; }

    // Onderscheidt regels met dezelfde logische identiteit maar zichtbaar andere weergave
    // (bijv. dezelfde NLCS-laag uit twee bronnen met verschillende kleur). Leeg = geen splitsing;
    // de analyse vult dit per render-partitie zodat zulke regels niet onterecht samenvallen.
    public string RenderGroup { get; init; } = string.Empty;

    public string GroupKey => $"{StatusCode}|{Discipline}|{Hoofdgroep}|{Element}";

    public override string ToString() => LocalName;
}
