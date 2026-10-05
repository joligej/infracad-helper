namespace NlcsLegenda.Core;

public enum TemplateOrigin
{
    Gemeten,
    Afgeleid,
    Ontwerpkeuze
}

public sealed record TemplateValueOrigin(TemplateOrigin Origin, double Value, string Reason);

// Herkomst per canonieke templatewaarde. Gemeten = uit de referentielegenda (zie
// reference/template-contract.json); Afgeleid = berekend uit andere waarden; Ontwerpkeuze =
// bewust gekozen omdat de referentie hier geen maatgevende waarde geeft. TemplateProvenanceTests
// toetst dat elke const in TemplateDefaults hier staat met dezelfde waarde en een geldige herkomst.
public static class TemplateProvenance
{
    public static readonly IReadOnlyDictionary<string, TemplateValueOrigin> ByField =
        new Dictionary<string, TemplateValueOrigin>
        {
            [nameof(TemplateDefaults.SwatchWidthMm)] =
                new(TemplateOrigin.Afgeleid, TemplateDefaults.SwatchWidthMm,
                    "afgeleid van de gemeten lijnsample (22,4 mm), naar boven afgerond met een kleine marge"),
            [nameof(TemplateDefaults.SwatchHeightMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.SwatchHeightMm,
                    "twee keer de teksthoogte zodat het sample goed zichtbaar is naast de omschrijving"),
            [nameof(TemplateDefaults.RowPitchMm)] =
                new(TemplateOrigin.Gemeten, TemplateDefaults.RowPitchMm,
                    "baseline-afstand van de T25-tekst in de referentie (n=41)"),
            [nameof(TemplateDefaults.LineSpacingFactor)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.LineSpacingFactor,
                    "typografische regelafstand (1,35x) voor leesbare meerregelige omschrijvingen"),
            [nameof(TemplateDefaults.RemarksWidthMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.RemarksWidthMm,
                    "leesbare alineabreedte voor het opmerkingenblok (ca. 90 mm papier)"),
            [nameof(TemplateDefaults.TextGapMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.TextGapMm,
                    "ruimte tussen swatch en omschrijving zodat de tekst niet tegen het sample plakt"),
            [nameof(TemplateDefaults.TextHeightMm)] =
                new(TemplateOrigin.Gemeten, TemplateDefaults.TextHeightMm,
                    "NLCS-ISO T25 (0,5 m op 1:200) in de referentie"),
            [nameof(TemplateDefaults.HeaderTextHeightMm)] =
                new(TemplateOrigin.Gemeten, TemplateDefaults.HeaderTextHeightMm,
                    "NLCS-ISO T50 (1,0 m op 1:200) in de referentie"),
            [nameof(TemplateDefaults.TitleTextHeightMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.TitleTextHeightMm,
                    "titel kleiner dan de gemeten sample-titel (10 mm) voor een compacte kop"),
            [nameof(TemplateDefaults.HeaderSpacingMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.HeaderSpacingMm,
                    "witruimte boven een kopregel, ongeveer één regelhoogte"),
            [nameof(TemplateDefaults.ColumnWidthMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.ColumnWidthMm,
                    "breedte voor swatch plus een regel omschrijving zonder vroeg afbreken"),
            [nameof(TemplateDefaults.ColumnGapMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.ColumnGapMm,
                    "zichtbare scheiding tussen twee kolommen"),
            [nameof(TemplateDefaults.QuantityColumnWidthMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.QuantityColumnWidthMm,
                    "breedte voor een getal met eenheid in de hoeveelheidkolom"),
            [nameof(TemplateDefaults.BorderMarginMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.BorderMarginMm,
                    "kadermarge rond de legenda, gelijkgehouden aan de standaard viewportmarge"),
        };
}
