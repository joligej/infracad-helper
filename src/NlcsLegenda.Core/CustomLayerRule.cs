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

    // Matcht de regel een object op deze laag uit deze bron? databaseLayerName is de laagnaam
    // zoals AutoCAD die kent (voor xref-objecten source-gekwalificeerd: "xrefnaam|laag");
    // sourceXref is leeg voor de hoofdtekening of de xref waarin het object zit. Een echte
    // match vergt álle voorwaarden: geldige regel, gelijke lokale laag én passende bron. Het
    // optionele blokfilter wordt op entiteitsniveau met MatchtBlok gecontroleerd.
    public bool Matches(string databaseLayerName, string sourceXref) =>
        IsValid && LaagMatcht(databaseLayerName) && ScopeMatches(sourceXref);

    public bool LaagMatcht(string databaseLayerName) =>
        string.Equals(LayerNaming.LocalName(databaseLayerName), Layer, StringComparison.OrdinalIgnoreCase);

    // Blokfilter voor symboollagen met meerdere bloksoorten: zonder filter matcht elk blok,
    // met filter alleen het genoemde blok.
    public bool MatchtBlok(string? entityBlockName) =>
        string.IsNullOrEmpty(BlockName)
        || string.Equals(entityBlockName, BlockName, StringComparison.OrdinalIgnoreCase);

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

    // databaseLayerName is de echte laagnaam in de host-database (source-gekwalificeerd bij
    // xref); die gaat naar Raw zodat stijl- en beschrijvingslookups de juiste laagtabelrecord
    // vinden, ook voor xref-afhankelijke lagen. LocalName blijft de canonieke sleutel.
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
