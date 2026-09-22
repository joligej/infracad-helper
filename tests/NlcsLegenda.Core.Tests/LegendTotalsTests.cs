using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendTotalsTests
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
    public void ByHoofdgroep_SumsPerUnitAndType()
    {
        var lijn = Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G")[0];
        var vlak = Parse("N-WE-VH-GESLOTENVERHARDING_ASFALT-V")[0];

        var metrics = new Dictionary<string, LayerMetric>
        {
            [lijn.LocalName] = new LayerMetric(3, 120.0, 0),
            [vlak.LocalName] = new LayerMetric(0, 0, 500.0)
        };

        var entries = LegendGrouping.Build(new[] { lijn, vlak }, new LegendSettings(), _ => null, metrics);
        var totals = LegendTotals.ByHoofdgroep(entries);

        var ri = Assert.Single(totals, t => t.Hoofdgroep == "RI");
        Assert.Equal(120.0, ri.Length, 3);
        Assert.Equal(0, ri.Area);

        var vh = Assert.Single(totals, t => t.Hoofdgroep == "VH");
        Assert.Equal(500.0, vh.Area, 3);
        Assert.Equal(0, vh.Length);
    }

    [Fact]
    public void ByHoofdgroep_SortedByCode()
    {
        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A", "N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G"),
            new LegendSettings(), _ => null);

        var totals = LegendTotals.ByHoofdgroep(entries);
        Assert.Equal(new[] { "RI", "VH" }, totals.Select(t => t.Hoofdgroep).ToArray());
    }
}
