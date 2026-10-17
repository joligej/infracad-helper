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

    [Theory]
    [InlineData("N-WE-GR-BOOM-V", "GR-BOOM;", "Boom")]
    [InlineData("N-WE-GR-GRAS-V", "NLCS 5.0: GR-GRAS;", "Gras")]
    [InlineData("N-WE-GR-HAAG-G", "GR-HAAG;", "Haag")]
    [InlineData("N-WE-IE-KUNSTOBJECT_WATERELEMENT-V", "IE-KUNSTOBJECT; WATERELEMENT", "Kunstobject Waterelement")]
    [InlineData("N-WE-OG-BEBOUWING_ONTWERP-V", "NLCS 5.0: OG-BEBOUWING. Projectaanvulling: ONTWERP", "Bebouwing Ontwerp")]
    [InlineData("N-WE-VH-VERHARDING_OVERIG_VOETPAD_ROOD-V", "VH-VERHARDING_OVERIG; VOETPAD_ROOD", "Verharding Overig Voetpad Rood")]
    public void GeneratedNlcsMetadataUsesOrdinaryName(string name, string description, string expected)
    {
        NlcsLayerParser.TryParse(name, out var layer);
        var entry = Assert.Single(LegendGrouping.Build(new[] { layer! },
            new LegendSettings { IncludeGeneralDescription = false }, _ => description));
        Assert.Equal(expected, entry.Description);
    }

    [Fact]
    public void GenericKlicLabelDoesNotReplaceCableName()
    {
        NlcsLayerParser.TryParse("B-WE-KL-DATA-G", out var layer);
        var result = Assert.Single(LegendGrouping.Build(new[] { layer! }, new LegendSettings { IncludeGeneralDescription = false }, _ => "KLIC"));
        Assert.Equal("Datakabel", result.Description);
    }
}
