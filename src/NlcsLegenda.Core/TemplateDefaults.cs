namespace NlcsLegenda.Core;

// Eén centrale bron voor de template-maatvoering van de legenda (papier-mm). De tekststijl en
// teksthoogtes volgen de NLCS-tekststijl NLCS-ISO; de maten zijn afgestemd op de TAUW-
// referentielegenda (relevante_bronnen/Templates/TAUW_LEGENDAS.dwt en
// 03 Voorbeeldtekening BE/ref/SIT-NW-LEGENDA.dwg). Meting op die referentie op schaal 1:200:
// omschrijvingstekst 0,5 m = 2,5 mm (dominant), kopregel 1,0 m = 5 mm; swatchbreedte ~4,0-4,8 m
// = 20-24 mm. LegendSettings en reset-naar-template verwijzen hiernaar, zodat er geen parallelle
// magische getallen ontstaan.
public static class TemplateDefaults
{
    public const double SwatchWidthMm = 24.0;
    public const double SwatchHeightMm = 5.0;
    public const double RowPitchMm = 6.3;
    public const double LineSpacingFactor = 1.35;
    public const double RemarksWidthMm = 90.0;
    public const double TextGapMm = 8.0;

    // Teksthoogtes conform NLCS-ISO, bevestigd door meting van de referentielegenda.
    public const double TextHeightMm = 2.5;
    public const double HeaderTextHeightMm = 5.0;
    public const double TitleTextHeightMm = 7.0;

    public const double HeaderSpacingMm = 6.0;
    public const double ColumnWidthMm = 67.0;
    public const double ColumnGapMm = 10.0;
    public const double QuantityColumnWidthMm = 18.0;
    public const double BorderMarginMm = 5.0;
}
