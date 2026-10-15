using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LayerDescriptionTests
{
    private const string Layer = "N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G";

    [Theory]
    [InlineData("NLCS-basis")]
    [InlineData("NLCS-basis: N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G")]
    [InlineData("NLCS-basis - KANTOPSLUITING_TROTTOIRBAND")]
    [InlineData("nlcs basis / BRON|N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G")]
    [InlineData("N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G")]
    [InlineData("KANTOPSLUITING_TROTTOIRBAND")]
    public void TechnicalDescriptionUsesOrdinaryName(string description)
    {
        NlcsLayerParser.TryParse(Layer, out var layer);
        var result = Assert.Single(LegendGrouping.Build(new[] { layer! }, new LegendSettings { IncludeGeneralDescription = false }, _ => description));
        Assert.Equal("Trottoirband", result.Description);
        Assert.Equal(DescriptionSource.Catalogus, result.DescriptionSource);
    }

    [Theory]
    [InlineData("Trottoirband 180/200", "Trottoirband 180/200")]
    [InlineData("NLCS-basis: Trottoirband 180/200", "Trottoirband 180/200")]
    [InlineData("KLIC-kabel Enexis", "KLIC-kabel Enexis")]
    public void MeaningfulLayerDescriptionSurvives(string description, string expected)
    {
        NlcsLayerParser.TryParse(Layer, out var layer);
        var result = Assert.Single(LegendGrouping.Build(new[] { layer! }, new LegendSettings(), _ => description));
        Assert.Equal(expected, result.Description);
    }

    [Fact]
    public void ExplicitUserTextIsNeverFiltered()
    {
        NlcsLayerParser.TryParse(Layer, out var layer);
        var settings = new LegendSettings { IncludeGeneralDescription = false };
        settings.DescriptionOverrides.Elementen["VH|KANTOPSLUITING_TROTTOIRBAND"] = new() { Specifiek = "NLCS-basis: eigen tekst" };
        Assert.Equal("NLCS-basis: eigen tekst", Assert.Single(LegendGrouping.Build(new[] { layer! }, settings, _ => "KLIC")).Description);
        settings.DescriptionOverrides.Elementen.Clear();
        Assert.Equal("NLCS-basis: eigen tekst", Assert.Single(LegendGrouping.Build(new[] { layer! }, settings,
            _ => "NLCS-basis: eigen tekst", descriptionSourceOf: _ => DescriptionSource.EigenKoppeling)).Description);
    }

    [Fact]
    public void GenericKlicLabelDoesNotReplaceCableName()
    {
        NlcsLayerParser.TryParse("B-WE-KL-DATA-G", out var layer);
        var result = Assert.Single(LegendGrouping.Build(new[] { layer! }, new LegendSettings { IncludeGeneralDescription = false }, _ => "KLIC"));
        Assert.Equal("Datakabel", result.Description);
    }
}
