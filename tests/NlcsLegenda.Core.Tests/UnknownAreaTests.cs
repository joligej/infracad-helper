using NlcsLegenda.Core;
using System.Text.Json;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class UnknownAreaTests
{
    [Fact]
    public void IncompleteAreaStaysUnknownAfterMergingAndExport()
    {
        var measured = new LayerMetric(1, 0, 25);
        var unknown = new LayerMetric(1, 0, 0) { AreaIssue = "Arcering ABC: open contour" };
        var merged = (measured + unknown).Add(1, 0, 4);
        Assert.Equal(29, merged.Area);
        Assert.True(merged.HasUnknownArea);
        var entry = new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH", Element = "VLAK", Description = "Vlak",
            LayersByType = new() { [NlcsDrawType.Arcering] = "N-WE-VH-VLAK-A" }, Metric = merged };
        Assert.Equal("? m²", entry.QuantityText());
        Assert.True(Assert.Single(LegendTotals.ByHoofdgroep(new[] { entry })).HasUnknownArea);
        using var json = JsonDocument.Parse(LegendExport.ToJson(new[] { entry }));
        Assert.Equal(JsonValueKind.Null, json.RootElement[0].GetProperty("oppervlakM2").ValueKind);
        Assert.Equal("?", json.RootElement[0].GetProperty("hoeveelheid").GetString());
        Assert.Contains("open contour", json.RootElement[0].GetProperty("oppervlakBroncontrole").GetString());
        var csv = LegendExport.ToCsv(new[] { entry });
        Assert.Contains("Oppervlak_broncontrole", csv);
        Assert.Contains(";;?;m²;", csv);
        using var combined = JsonDocument.Parse(LegendExport.ToCombinedJson(new[] { ("bron", (IReadOnlyList<LegendEntry>)new[] { entry }) }));
        Assert.Equal(JsonValueKind.Null, combined.RootElement[0].GetProperty("oppervlakM2").ValueKind);
    }
}
