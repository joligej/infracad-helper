using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class DescriptionPriorityTests
{
    private static List<NlcsLayerName> Parse(params string[] layers)
    {
        var list = new List<NlcsLayerName>();
        foreach (var l in layers)
            if (NlcsLayerParser.TryParse(l, out var p))
                list.Add(p!);
        return list;
    }

    [Fact]
    public void TextOverride_WinsOverEverything()
    {
        var settings = new LegendSettings();
        settings.TextOverrides["OPENVERHARDING_BETONSTRAATSTEEN"] = "Mijn klinkers";

        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), settings,
            _ => "Beschrijving uit laag");

        Assert.Equal("Mijn klinkers", entries[0].Description);
    }

    [Fact]
    public void LayerDescription_WinsOverCuratedText()
    {
        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), new LegendSettings(),
            _ => "Betonstraatsteen keperverband");

        Assert.Equal("Betonstraatsteen keperverband", entries[0].Description);
    }

    [Fact]
    public void CuratedText_UsedWhenNoDescription()
    {
        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), new LegendSettings(),
            _ => null);

        Assert.Equal("Betonstraatsteen", entries[0].Description);
    }

    [Fact]
    public void HumanizedFallback_ForUnknownElement()
    {
        var entries = LegendGrouping.Build(
            Parse("N-WE-GR-BEPLANTING_SIERGRAS-G"), new LegendSettings(),
            _ => null);

        Assert.Equal("Beplanting Siergras", entries[0].Description);
    }

    [Fact]
    public void GeneralPrefix_ShownWhenEnabled()
    {
        var settings = new LegendSettings { IncludeGeneralDescription = true };
        var entries = LegendGrouping.Build(
            Parse("N-WE-GR-BEPLANTING_SIERGRAS-G"), settings, _ => null);

        Assert.Equal("Groen - Beplanting Siergras", entries[0].Description);
    }
}
