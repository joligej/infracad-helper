using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class DescriptionSourceTests
{
    private static NlcsLayerName Parse(string layer)
    {
        Assert.True(NlcsLayerParser.TryParse(layer, out var p));
        return p!;
    }

    [Fact]
    public void UnknownElement_FallsBackToLayerName()
    {
        var layer = Parse("N-WE-VH-VOLSTREKT_ONBEKEND_ELEMENT-G");
        var entries = LegendGrouping.Build(new[] { layer }, new LegendSettings(), _ => null);

        var entry = Assert.Single(entries);
        Assert.Equal(DescriptionSource.Laagnaam, entry.DescriptionSource);
    }

    [Fact]
    public void KnownElement_UsesCatalog()
    {
        var layer = Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A");
        var entries = LegendGrouping.Build(new[] { layer }, new LegendSettings(), _ => null);

        var entry = Assert.Single(entries);
        Assert.Equal(DescriptionSource.Catalogus, entry.DescriptionSource);
    }

    [Fact]
    public void TextOverride_TakesPrecedence()
    {
        var layer = Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A");
        var settings = new LegendSettings();
        settings.TextOverrides[layer.Element] = "Mijn eigen tekst";

        var entries = LegendGrouping.Build(new[] { layer }, settings, _ => null);

        var entry = Assert.Single(entries);
        Assert.Equal(DescriptionSource.EigenTekst, entry.DescriptionSource);
        Assert.Equal("Mijn eigen tekst", entry.Description);
    }

    [Fact]
    public void LayerDescription_IsUsedWhenNoOverride()
    {
        var layer = Parse("N-WE-VH-VOLSTREKT_ONBEKEND_ELEMENT-G");
        var entries = LegendGrouping.Build(
            new[] { layer }, new LegendSettings(), _ => "Beschrijving uit tekening");

        var entry = Assert.Single(entries);
        Assert.Equal(DescriptionSource.Laagbeschrijving, entry.DescriptionSource);
        Assert.Equal("Beschrijving uit tekening", entry.Description);
    }

    [Fact]
    public void ManualEntry_HasManualSource()
    {
        var manual = new ManualEntry
        {
            Layer = "N-WE-VH-EIGEN-G",
            Type = NlcsDrawType.Geometrie,
            Description = "Eigen regel"
        };

        var entry = manual.ToLegendEntry();
        Assert.Equal(DescriptionSource.Handmatig, entry.DescriptionSource);
    }
}
