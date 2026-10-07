using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Pint de template-defaults op de gemeten referentiewaarden: op 1:200 is de omschrijving en de
// statuskop 0,5 m = 2,5 mm (T25) en de titel 1,0 m = 5 mm (T50). Zo kan de opmaak niet stilletjes
// afdrijven.
public class TemplateDefaultsTests
{
    [Fact]
    public void TextHeights_MatchMeasuredReference()
    {
        Assert.Equal(2.5, TemplateDefaults.TextHeightMm);
        Assert.Equal(2.5, TemplateDefaults.HeaderTextHeightMm);
        Assert.Equal(5.0, TemplateDefaults.TitleTextHeightMm);
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
    // de titel 5 mm = 1,0 m, precies wat in de referentie is gemeten.
    [Fact]
    public void ToModel_ScalesTextHeightLikeReference()
    {
        var s = new LegendSettings { Scale = 200 };
        Assert.Equal(0.5, s.ToModel(s.TextHeightMm), 3);
        Assert.Equal(1.0, s.ToModel(s.TitleTextHeightMm), 3);
    }

    // NLCS-ISO tekststijlen (font NLCS-ISO.ttf, breedtefactor 1,0): de T25/T50-codes geven op
    // 1:200 teksthoogtes van 2,5 en 5 mm. De omschrijving is T25, de titel T50.
    [Theory]
    [InlineData(200, 0.5, 1.0)]
    [InlineData(1000, 2.5, 5.0)]
    public void ToModel_MatchesMeasuredStyleHeights(double scale, double expT25, double expT50)
    {
        var s = new LegendSettings { Scale = scale };
        Assert.Equal(expT25, s.ToModel(TemplateDefaults.TextHeightMm), 3);
        Assert.Equal(expT50, s.ToModel(TemplateDefaults.TitleTextHeightMm), 3);
    }
}
