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
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.SwatchWidthMm,
                    "afronding van de gemeten lijnsample (22,4 mm) met een kleine marge"),
            [nameof(TemplateDefaults.SwatchHeightMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.SwatchHeightMm,
                    "vaste swatchhoogte voor een zichtbaar sample"),
            [nameof(TemplateDefaults.RowPitchMm)] =
                new(TemplateOrigin.Gemeten, TemplateDefaults.RowPitchMm,
                    "baseline-afstand van de T25-tekst in de referentie (n=41)"),
            [nameof(TemplateDefaults.LineSpacingFactor)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.LineSpacingFactor,
                    "regelafstand binnen meerregelige omschrijvingen"),
            [nameof(TemplateDefaults.RemarksWidthMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.RemarksWidthMm,
                    "breedte van het opmerkingenblok"),
            [nameof(TemplateDefaults.TextGapMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.TextGapMm,
                    "ruimte tussen de swatch en de omschrijving"),
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
                    "witruimte boven een kopregel, ongeveer één regel"),
            [nameof(TemplateDefaults.ColumnWidthMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.ColumnWidthMm,
                    "kolombreedte voor swatch plus omschrijving"),
            [nameof(TemplateDefaults.ColumnGapMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.ColumnGapMm,
                    "tussenruimte tussen twee kolommen"),
            [nameof(TemplateDefaults.QuantityColumnWidthMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.QuantityColumnWidthMm,
                    "breedte van de hoeveelheidkolom"),
            [nameof(TemplateDefaults.BorderMarginMm)] =
                new(TemplateOrigin.Ontwerpkeuze, TemplateDefaults.BorderMarginMm,
                    "kadermarge rond de legenda, gelijk aan de viewportmarge"),
        };
}
