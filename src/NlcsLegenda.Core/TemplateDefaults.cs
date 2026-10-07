namespace NlcsLegenda.Core;

// Eén centrale bron voor de maatvoering van de legenda (papier-mm). De tekststijl en teksthoogtes
// volgen de NLCS-tekststijl NLCS-ISO. De titel staat op de T50-laag (5 mm), de statuskoppen en
// omschrijvingen op de T25-laag (2,5 mm), zoals in het legenda-referentiemateriaal. LegendSettings en
// "terugzetten naar template" verwijzen hiernaar, zodat er geen parallelle magische getallen
// ontstaan. LegendaOpmaakTests pinnen de belangrijkste waarden.
public static class TemplateDefaults
{
    // Breedte van het sample-vak; de sample-lijn en (indien getekend) het swatchkader delen deze
    // maat. 24 mm geeft genoeg ruimte voor het sample naast de omschrijving.
    public const double SwatchWidthMm = 24.0;
    public const double SwatchHeightMm = 5.0;
    public const double RowPitchMm = 6.3;
    public const double LineSpacingFactor = 1.35;
    public const double RemarksWidthMm = 90.0;

    // Ruimte tussen het sample-vak en de omschrijving.
    public const double TextGapMm = 5.7;

    // Teksthoogtes conform NLCS-ISO. Omschrijving en statuskop T25 = 2,5 mm, de titel T50 = 5 mm.
    public const double TextHeightMm = 2.5;
    public const double HeaderTextHeightMm = 2.5;
    public const double TitleTextHeightMm = 5.0;

    public const double HeaderSpacingMm = 6.0;
    public const double ColumnWidthMm = 67.0;
    public const double ColumnGapMm = 10.0;
    public const double QuantityColumnWidthMm = 18.0;
    public const double BorderMarginMm = 5.0;
}
