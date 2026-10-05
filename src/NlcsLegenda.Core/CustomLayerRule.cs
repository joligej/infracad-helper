namespace NlcsLegenda.Core;

// Bron waarop een eigen-laagregel matcht. Local = alleen de laag in de hoofdtekening,
// AnySource = de laag in elke meegenomen bron, SpecificXref = alleen in één genoemde xref.
public enum CustomSourceScope
{
    Local,
    AnySource,
    SpecificXref
}

// Hoeveelheidsmodus voor een eigen laag. Auto leidt de soort af uit de geometrie (zoals bij
// NLCS); de andere waarden forceren tellen, lengte, oppervlak of niets.
public enum CustomQuantityMode
{
    Auto,
    Geen,
    Aantal,
    Lengte,
    Oppervlak
}

// Koppelt een zelfgekozen (niet-NLCS) laag expliciet als bron voor de legenda. Een regel
// beschrijft wat de laag voorstelt (type, status, omschrijving) en hoe de hoeveelheid telt.
// Meerdere regels met dezelfde Element-sleutel vormen samen één legenda-item (bijv. een eigen
// lijn + arcering), net als G+A bij NLCS.
public sealed class CustomLayerRule
{
    // Exacte lokale laagnaam (zonder xref-prefix) waarop de regel matcht.
    public string Layer { get; set; } = string.Empty;

    public CustomSourceScope Scope { get; set; } = CustomSourceScope.Local;

    // Alleen gebruikt bij Scope = SpecificXref.
    public string XrefName { get; set; } = string.Empty;

    // Logische elementidentiteit; stabiel over tekeningen en bepaalt welke regels samen één
    // legenda-item vormen. Niet de omschrijving, laagnaam of een handle.
    public string Element { get; set; } = string.Empty;

    public NlcsDrawType Type { get; set; } = NlcsDrawType.Geometrie;

    public NlcsStatus Status { get; set; } = NlcsStatus.Nieuw;

    // Eigen discipline/hoofdgroep voor sorteren en groeperen. Bewust niet XX/AL, want die
    // staan standaard in de uitsluitingslijsten en zouden de regel anders wegfilteren.
    public string Discipline { get; set; } = "EI";

    public string Hoofdgroep { get; set; } = "EI";

    public string Description { get; set; } = string.Empty;

    public CustomQuantityMode QuantityMode { get; set; } = CustomQuantityMode.Auto;

    // Optioneel blokfilter voor symboollagen met meerdere bloksoorten.
    public string? BlockName { get; set; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Layer) && LayerNaming.IsValid(Layer)
        && !string.IsNullOrWhiteSpace(Element);

    // Matcht de regel een object op deze laag uit deze bron? sourceXref is leeg voor de
    // hoofdtekening of de naam van de xref waarin het object zit.
    public bool Matches(string layerName, string sourceXref)
    {
        if (!string.Equals(layerName, Layer, StringComparison.OrdinalIgnoreCase))
            return false;
        if (BlockName is { Length: > 0 })
            return true; // blokfilter wordt op entiteitsniveau gecontroleerd
        return ScopeMatches(sourceXref);
    }

    public bool ScopeMatches(string sourceXref) => Scope switch
    {
        CustomSourceScope.Local => string.IsNullOrEmpty(sourceXref),
        CustomSourceScope.AnySource => true,
        CustomSourceScope.SpecificXref => string.Equals(sourceXref, XrefName, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    // Stabiele sleutel voor metriek én swatch-rendering. Bij een specifieke xref wordt de naam
    // meegenomen zodat dezelfde lokale laagnaam uit verschillende bronnen niet botst.
    public string CanonicalLayer =>
        Scope == CustomSourceScope.SpecificXref && !string.IsNullOrEmpty(XrefName)
            ? XrefName + "|" + Layer
            : Layer;

    public NlcsLayerName ToCanonical(string sourceXref) => new()
    {
        Raw = Layer,
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
