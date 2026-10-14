namespace NlcsLegenda.Core;

// Papiermaten uit de legenda-referentie; meetpunten en toleranties staan in
// docs/template-maatvoering.md. Hulplijnen in de template zijn niet plotbaar.
public static class TemplateDefaults
{
    public const double SwatchWidthMm = 24.0;
    public const double SwatchHeightMm = 5.0;
    public const double RowPitchMm = 6.3099;
    public const double TextGapMm = 5.7188;
    public const double TextHeightMm = 2.5;
    public const double HeaderTextHeightMm = 2.5;
    public const double TitleTextHeightMm = 5.0;
    // Afstand tussen de basislijnen van titel en eerste statuskop.
    public const double HeaderSpacingMm = 6.9257;
    // De basislijn van de omschrijving ligt 0,7097 mm onder het sample-midden.
    public const double TextBaselineOffsetRatio = 0.28388973;
    public const double ColumnWidthMm = 143.0;
    public const double ColumnGapMm = 10.0;
    public const double LineSpacingFactor = 1.35;
    public const double RemarksWidthMm = 128.8894;
    public const double QuantityColumnWidthMm = 18.0;
    public const double BorderMarginMm = 5.0;
}
