using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class PlaceholderTextTests
{
    [Theory]
    [InlineData("TYPE \\ LABEL \\ OMSCHRIJVING", true)]
    [InlineData("TYPE\\PLABEL\\POMSCHRIJVING", true)]
    [InlineData("TYPE LABEL OMSCHRIJVING", true)]
    [InlineData("  TYPE \\ LABEL \\ OMSCHRIJVING  ", true)]
    [InlineData("type / label", true)]
    [InlineData("Kolkleiding", false)]
    [InlineData("Distributieleiding", false)]
    [InlineData("Aansluitleiding", false)]
    [InlineData("Type betonstraatsteen 30x30", false)]
    [InlineData("Datakabel laagspanning", false)]
    [InlineData("Afsluiter \u00f8160", false)]
    [InlineData("TYPE", false)]          // één token is te weinig
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGeneric_DetectsOnlyPurePlaceholders(string? text, bool expected)
    {
        Assert.Equal(expected, PlaceholderText.IsGeneric(text));
    }

    [Fact]
    public void Grouping_SuppressesPlaceholderDescription_FallsBackToCatalog()
    {
        NlcsLayerParser.TryParse("N-WE-KL-DATA-S", out var layer);
        var s = new LegendSettings { SuppressKlicPlaceholders = true };
        s.ExcludedHoofdgroepen.Clear();

        // Laagbeschrijving is een lege placeholder -> moet worden genegeerd.
        var entry = Assert.Single(LegendGrouping.Build(
            new[] { layer! }, s, layerDescription: _ => "TYPE \\ LABEL \\ OMSCHRIJVING"));
        Assert.NotEqual("TYPE \\ LABEL \\ OMSCHRIJVING", entry.Description);
        Assert.NotEqual(DescriptionSource.Laagbeschrijving, entry.DescriptionSource);
    }

    [Fact]
    public void Grouping_KeepsRealDescription()
    {
        NlcsLayerParser.TryParse("N-WE-KL-DATA-S", out var layer);
        var s = new LegendSettings { SuppressKlicPlaceholders = true };
        s.ExcludedHoofdgroepen.Clear();

        var entry = Assert.Single(LegendGrouping.Build(
            new[] { layer! }, s, layerDescription: _ => "Glasvezelkabel KPN"));
        Assert.Equal("Glasvezelkabel KPN", entry.Description);
    }

    [Fact]
    public void Grouping_PlaceholderKept_WhenSuppressionOff()
    {
        NlcsLayerParser.TryParse("N-WE-KL-DATA-S", out var layer);
        var s = new LegendSettings { SuppressKlicPlaceholders = false };
        s.ExcludedHoofdgroepen.Clear();

        var entry = Assert.Single(LegendGrouping.Build(
            new[] { layer! }, s, layerDescription: _ => "TYPE \\ LABEL \\ OMSCHRIJVING"));
        Assert.Equal("TYPE \\ LABEL \\ OMSCHRIJVING", entry.Description);
    }
}
