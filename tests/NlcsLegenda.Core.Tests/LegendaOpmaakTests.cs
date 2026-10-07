using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Pint de legenda-opmaakwaarden exact vast zodat ze niet per ongeluk veranderen. De waarden zijn
// gemeten aan het legenda-referentiemateriaal: titel op de T50-laag (5 mm), statuskoppen en
// omschrijvingen op de T25-laag (2,5 mm), swatch 24 x 5 mm, rijafstand 6,3 mm en een tussenruimte
// van 5,7 mm tussen swatch en tekst.
public class LegendaOpmaakTests
{
    [Fact]
    public void Teksthoogtes_volgen_NLCS_ISO()
    {
        Assert.Equal(2.5, TemplateDefaults.TextHeightMm);
        Assert.Equal(2.5, TemplateDefaults.HeaderTextHeightMm);
        Assert.Equal(5.0, TemplateDefaults.TitleTextHeightMm);
    }

    [Fact]
    public void Swatch_en_rijmaten_zijn_vast()
    {
        Assert.Equal(24.0, TemplateDefaults.SwatchWidthMm);
        Assert.Equal(5.0, TemplateDefaults.SwatchHeightMm);
        Assert.Equal(6.3, TemplateDefaults.RowPitchMm);
        Assert.Equal(1.35, TemplateDefaults.LineSpacingFactor);
    }

    [Fact]
    public void Kolom_en_marge_maten_zijn_vast()
    {
        Assert.Equal(5.7, TemplateDefaults.TextGapMm);
        Assert.Equal(6.0, TemplateDefaults.HeaderSpacingMm);
        Assert.Equal(67.0, TemplateDefaults.ColumnWidthMm);
        Assert.Equal(10.0, TemplateDefaults.ColumnGapMm);
        Assert.Equal(18.0, TemplateDefaults.QuantityColumnWidthMm);
        Assert.Equal(5.0, TemplateDefaults.BorderMarginMm);
        Assert.Equal(90.0, TemplateDefaults.RemarksWidthMm);
    }

    [Fact]
    public void Symbool_insertschaal_is_schaal_gedeeld_door_1000()
    {
        // Symbolen worden op legendaschaal geplaatst: op 1:200 is dat 0,2 (schaal/1000).
        Assert.Equal(0.2, new LegendSettings { Scale = 200 }.ModelUnitsPerPaperMm, 3);
        Assert.Equal(0.1, new LegendSettings { Scale = 100 }.ModelUnitsPerPaperMm, 3);
    }
}
