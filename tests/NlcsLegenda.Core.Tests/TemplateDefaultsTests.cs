using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Pint de template-defaults op de gemeten/formele referentiewaarden. De teksthoogtes zijn
// gemeten op de referentielegenda SIT-NW-LEGENDA.dwg (schaal 1:200): omschrijving 0,5 m =
// 2,5 mm, kopregel 1,0 m = 5 mm. Zo kan output niet stilletjes van de referentie afdrijven.
public class TemplateDefaultsTests
{
    [Fact]
    public void TextHeights_MatchMeasuredReference()
    {
        Assert.Equal(2.5, TemplateDefaults.TextHeightMm);
        Assert.Equal(5.0, TemplateDefaults.HeaderTextHeightMm);
        Assert.Equal(7.0, TemplateDefaults.TitleTextHeightMm);
    }

    [Fact]
    public void SwatchAndColumns_MatchTemplate()
    {
        Assert.Equal(24.0, TemplateDefaults.SwatchWidthMm);
        Assert.Equal(5.0, TemplateDefaults.SwatchHeightMm);
        Assert.Equal(6.3, TemplateDefaults.RowPitchMm);
        Assert.Equal(67.0, TemplateDefaults.ColumnWidthMm);
    }

    [Fact]
    public void Settings_DefaultsComeFromCentralTemplate()
    {
        var s = new LegendSettings();
        Assert.Equal(TemplateDefaults.SwatchWidthMm, s.SwatchWidthMm);
        Assert.Equal(TemplateDefaults.TextHeightMm, s.TextHeightMm);
        Assert.Equal(TemplateDefaults.HeaderTextHeightMm, s.HeaderTextHeightMm);
        Assert.Equal(TemplateDefaults.TitleTextHeightMm, s.TitleTextHeightMm);
        Assert.Equal(TemplateDefaults.ColumnWidthMm, s.ColumnWidthMm);
    }

    [Fact]
    public void ResetToTemplate_RestoresCentralValues()
    {
        var s = new LegendSettings { SwatchWidthMm = 99, TextHeightMm = 99, ColumnWidthMm = 99 };
        s.ResetFormattingToTemplate();
        Assert.Equal(TemplateDefaults.SwatchWidthMm, s.SwatchWidthMm);
        Assert.Equal(TemplateDefaults.TextHeightMm, s.TextHeightMm);
        Assert.Equal(TemplateDefaults.ColumnWidthMm, s.ColumnWidthMm);
    }

    // Teksthoogtes converteren schaal-afhankelijk naar modeleenheden: 2,5 mm op 1:200 = 0,5 m,
    // precies wat in de referentie is gemeten.
    [Fact]
    public void ToModel_ScalesTextHeightLikeReference()
    {
        var s = new LegendSettings { Scale = 200 };
        Assert.Equal(0.5, s.ToModel(s.TextHeightMm), 3);
        Assert.Equal(1.0, s.ToModel(s.HeaderTextHeightMm), 3);
    }

    // Gemeten tekststijlen in SIT-NW-LEGENDA.dwg (INSUNITS=6 = meters, font NLCS-ISO.ttf, wf 1,0):
    // NLCS-ISO-M200-T25 fixedH 0,500; -M200-T50 fixedH 1,000; -M1000-T25 fixedH 2,500;
    // -M1000-T50 fixedH 5,000. De T25/T50-codes zijn dus 2,5 en 5 mm.
    [Theory]
    [InlineData(200, 0.5, 1.0)]
    [InlineData(1000, 2.5, 5.0)]
    public void ToModel_MatchesMeasuredStyleHeights(double scale, double expT25, double expT50)
    {
        var s = new LegendSettings { Scale = scale };
        Assert.Equal(expT25, s.ToModel(TemplateDefaults.TextHeightMm), 3);
        Assert.Equal(expT50, s.ToModel(TemplateDefaults.HeaderTextHeightMm), 3);
    }
}
