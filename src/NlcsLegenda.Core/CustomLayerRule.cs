namespace NlcsLegenda.Core;

public enum CustomSourceScope
{
    Local,
    AnySource,
    SpecificXref
}

public enum CustomQuantityMode
{
    Auto,
    Geen,
    Aantal,
    Lengte,
    Oppervlak
}

public sealed class CustomLayerRule
{
    public string Layer { get; set; } = string.Empty;

    public CustomSourceScope Scope { get; set; } = CustomSourceScope.Local;

    public string XrefName { get; set; } = string.Empty;

    // vaste elementidentiteit, onafhankelijk van laagnaam en omschrijving
    public string Element { get; set; } = string.Empty;

    public NlcsDrawType Type { get; set; } = NlcsDrawType.Geometrie;

    public NlcsStatus Status { get; set; } = NlcsStatus.Nieuw;

    // eigen bronlagen buiten de standaarduitsluitingen
    public string Discipline { get; set; } = "EI";

    public string Hoofdgroep { get; set; } = "EI";

    public string Description { get; set; } = string.Empty;

    public CustomQuantityMode QuantityMode { get; set; } = CustomQuantityMode.Auto;

    public string? BlockName { get; set; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Layer) && LayerNaming.IsValid(Layer)
        && !string.IsNullOrWhiteSpace(Element);

    public bool Matches(string databaseLayerName, string sourceXref) =>
        IsValid && LaagMatcht(databaseLayerName) && ScopeMatches(sourceXref);

    public bool LaagMatcht(string databaseLayerName) =>
        string.Equals(LayerNaming.LocalName(databaseLayerName), Layer, StringComparison.OrdinalIgnoreCase);

    public bool MatchtBlok(string? entityBlockName) =>
        string.IsNullOrEmpty(BlockName)
        || string.Equals(LayerNaming.LocalName(entityBlockName), LayerNaming.LocalName(BlockName), StringComparison.OrdinalIgnoreCase);

    public bool ScopeMatches(string sourceXref) => Scope switch
    {
        CustomSourceScope.Local => string.IsNullOrEmpty(sourceXref),
        CustomSourceScope.AnySource => true,
        CustomSourceScope.SpecificXref => string.Equals(sourceXref, XrefName, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public string CanonicalLayer =>
        Scope == CustomSourceScope.SpecificXref && !string.IsNullOrEmpty(XrefName)
            ? XrefName + "|" + Layer
            : Layer;

    public NlcsLayerName ToCanonical(string sourceXref, string databaseLayerName) => new()
    {
        Raw = string.IsNullOrEmpty(databaseLayerName) ? Layer : databaseLayerName,
        LocalName = CanonicalLayer,
        IsXref = !string.IsNullOrEmpty(sourceXref),
        XrefName = sourceXref,
        StatusCode = Status.Code(),
        Status = Status,
        Discipline = string.IsNullOrWhiteSpace(Discipline) ? "EI" : Discipline.Trim(),
        Hoofdgroep = string.IsNullOrWhiteSpace(Hoofdgroep) ? "EI" : Hoofdgroep.Trim(),
        Element = Element.Trim(),
        TypeSuffix = string.Empty,
        DrawType = Type,
        Scale = null
    };

    public CustomLayerRule Clone() => (CustomLayerRule)MemberwiseClone();
}
