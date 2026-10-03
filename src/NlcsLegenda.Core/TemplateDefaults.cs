namespace NlcsLegenda.Core;

// Eén centrale bron voor de template-maatvoering van de legenda (papier-mm). De tekststijl en
// teksthoogtes volgen de NLCS-tekststijl NLCS-ISO; de maten zijn afgestemd op de TAUW-
// referentielegenda (relevante_bronnen/Templates/TAUW_LEGENDAS.dwt en
// 03 Voorbeeldtekening BE/ref/SIT-NW-LEGENDA.dwg). Meting op die referentie: INSUNITS=6 (meters),
// font NLCS-ISO.ttf met breedtefactor 1,0; tekststijlen NLCS-ISO-M200-T25 (0,5 m) en -T50 (1,0 m)
// en -M1000-T25 (2,5 m)/-T50 (5,0 m) geven teksthoogtes 2,5 en 5 mm. LegendSettings en
// reset-naar-template verwijzen hiernaar, zodat er geen parallelle magische getallen ontstaan.
// De herkomst per waarde (gemeten / afgeleid / ontwerpkeuze) staat in
// tests/.../reference/template-contract.json en wordt getoetst door TemplateContractTests.
public static class TemplateDefaults
{
    // Swatchbreedte = breedte van het sample-vak; de sample-lijn en (indien getekend) het
    // swatchkader delen deze maat. Gemeten lijnsample in de referentie: 22,4 mm (n=54).
    // 24 mm is een bewuste afronding met kleine marge (ontwerpkeuze); het losse swatchkader komt
    // in deze referentie nauwelijks voor (n=1) en is dus niet maatgevend.
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
