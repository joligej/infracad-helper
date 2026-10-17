using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class SymbolDescriptionTests
{
    [Theory]
    [InlineData("SIE-MEUBILAIR_PAAL_LICHTMAST-SO", "Lichtmast")]
    [InlineData("BRON|SIE-MEUBILAIR_PUT_KOLK-SO", "Kolk")]
    [InlineData("SIE-WEG_PICKNICKTAFEL-SO", "Picknicktafel")]
    [InlineData("SIE-WATER_MEERPAAL-SO", "Meerpaal")]
    public void SharedLayerGetsSpecificBlockName(string block, string expected)
    {
        NlcsLayerParser.TryParse("B-WE-IE-MEUBILAIR-S", out var layer);
        var entry = Assert.Single(LegendGrouping.Build(new[] { layer! }, new(), _ => "Meubilair", symbolBlock: _ => block));
        Assert.Equal(expected, entry.Description);
        Assert.Equal(DescriptionSource.Bloknaam, entry.DescriptionSource);
    }

    [Fact]
    public void SourceAndUserDescriptionsTakePrecedence()
    {
        NlcsLayerParser.TryParse("B-WE-IE-MEUBILAIR-S", out var layer);
        var settings = new LegendSettings();
        string Describe(string text) => Assert.Single(LegendGrouping.Build(new[] { layer! }, settings,
            _ => text, symbolBlock: _ => "SIE-MEUBILAIR_PAAL_LICHTMAST-SO")).Description;
        Assert.Equal("Lichtmast 6 m", Describe("Lichtmast 6 m"));
        settings.DescriptionOverrides.Elementen["IE|MEUBILAIR"] = new() { Specifiek = "Eigen naam" };
        Assert.Equal("Eigen naam", Describe("Lichtmast 6 m"));
        Assert.Null(SymbolDescription.Describe("eigen_onbekend_blok", layer!));
    }
}
