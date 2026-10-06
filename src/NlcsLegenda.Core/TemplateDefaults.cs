namespace NlcsLegenda.Core;

// Eén centrale bron voor de maatvoering van de legenda (papier-mm). De tekststijl en teksthoogtes
// volgen de NLCS-tekststijl NLCS-ISO: op schaal 1:200 geven T25 en T50 teksthoogtes van 2,5 en
// 5 mm. LegendSettings en "terugzetten naar template" verwijzen hiernaar, zodat er geen parallelle
// magische getallen ontstaan. LegendaOpmaakTests pinnen de belangrijkste waarden.
public static class TemplateDefaults
{
    // Breedte van het sample-vak; de sample-lijn en (indien getekend) het swatchkader delen deze
    // maat. 24 mm geeft genoeg ruimte voor het sample naast de omschrijving.
    public const double SwatchWidthMm = 24.0;
    public const double SwatchHeightMm = 5.0;
    public const double RowPitchMm = 6.3;
    public const double LineSpacingFactor = 1.35;
    public const double RemarksWidthMm = 90.0;
    public const double TextGapMm = 8.0;

    // Teksthoogtes conform NLCS-ISO (T25 = 2,5 mm, T50 = 5 mm op 1:200).
    public const double TextHeightMm = 2.5;
    public const double HeaderTextHeightMm = 5.0;
    public const double TitleTextHeightMm = 7.0;

    public const double HeaderSpacingMm = 6.0;
    public const double ColumnWidthMm = 67.0;
    public const double ColumnGapMm = 10.0;
    public const double QuantityColumnWidthMm = 18.0;
    public const double BorderMarginMm = 5.0;
}
