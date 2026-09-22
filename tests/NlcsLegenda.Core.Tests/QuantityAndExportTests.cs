using System.Text.Json;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class QuantityAndExportTests
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
    public void Metrics_AreAggregatedAcrossLayersOfGroup()
    {
        var metrics = new Dictionary<string, LayerMetric>(StringComparer.OrdinalIgnoreCase)
        {
            ["N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G"] = new(2, 10, 0),
            ["N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"] = new(1, 0, 50)
        };

        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G",
                  "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"),
            new LegendSettings(), null, metrics);

        var entry = Assert.Single(entries);
        Assert.Equal(3, entry.Metric.Count);
        Assert.Equal(10, entry.Metric.Length);
        Assert.Equal(50, entry.Metric.Area);
    }

    [Fact]
    public void QuantityText_PicksAreaForAreaElements()
    {
        var metrics = new Dictionary<string, LayerMetric>(StringComparer.OrdinalIgnoreCase)
        {
            ["N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"] = new(1, 0, 1234)
        };
        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"),
            new LegendSettings(), null, metrics);

        Assert.EndsWith("m\u00B2", entries[0].QuantityText());
    }

    [Fact]
    public void QuantityText_PicksLengthForLineElements()
    {
        var metrics = new Dictionary<string, LayerMetric>(StringComparer.OrdinalIgnoreCase)
        {
            ["N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G"] = new(3, 245, 0)
        };
        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G"),
            new LegendSettings(), null, metrics);

        Assert.EndsWith(" m", entries[0].QuantityText());
    }

    [Fact]
    public void QuantityText_PicksCountForSymbolElements()
    {
        var metrics = new Dictionary<string, LayerMetric>(StringComparer.OrdinalIgnoreCase)
        {
            ["N-WE-RI-VWA_RIOOLPUT-S"] = new(7, 0, 0)
        };
        var entries = LegendGrouping.Build(
            Parse("N-WE-RI-VWA_RIOOLPUT-S"),
            new LegendSettings(), null, metrics);

        Assert.Equal("7 st", entries[0].QuantityText());
    }

    [Fact]
    public void QuantityText_SymbolWithLengthNoise_StaysCount()
    {
        // Een symbool kan door geometrie in het blok wat "lengte" oppikken; toch moet de
        // hoeveelheid in stuks blijven, niet in meters.
        var metrics = new Dictionary<string, LayerMetric>(StringComparer.OrdinalIgnoreCase)
        {
            ["N-WE-VW-VERKEERSTEKEN_BORD-S"] = new(5, 123, 0)
        };
        var entries = LegendGrouping.Build(
            Parse("N-WE-VW-VERKEERSTEKEN_BORD-S"),
            new LegendSettings(), null, metrics);

        Assert.Equal("5 st", entries[0].QuantityText());
    }

    [Fact]
    public void QuantityText_RespectsDecimals()
    {
        var metrics = new Dictionary<string, LayerMetric>(StringComparer.OrdinalIgnoreCase)
        {
            ["N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G"] = new(3, 245.678, 0)
        };
        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G"),
            new LegendSettings(), null, metrics);

        Assert.Equal("246 m", entries[0].QuantityText());
        Assert.Equal("245,68 m", entries[0].QuantityText(2));
    }

    [Fact]
    public void Csv_HasHeaderAndRowPerEntry()
    {
        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_TEGEL-G", "B-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G"),
            new LegendSettings());

        var lines = LegendExport.ToCsv(entries)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Status;Discipline;Hoofdgroep", lines[0]);
        Assert.Equal(entries.Count + 1, lines.Length);
    }

    [Fact]
    public void Json_IsValidArrayWithFields()
    {
        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_TEGEL-G"), new LegendSettings());

        using var doc = JsonDocument.Parse(LegendExport.ToJson(entries));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(entries.Count, doc.RootElement.GetArrayLength());
        Assert.True(doc.RootElement[0].TryGetProperty("omschrijving", out _));
    }

    [Fact]
    public void Csv_QuotesFieldsWithSeparator()
    {
        var settings = new LegendSettings();
        settings.TextOverrides["OPENVERHARDING_TEGEL"] = "Tegel; grijs";

        var entries = LegendGrouping.Build(Parse("N-WE-VH-OPENVERHARDING_TEGEL-G"), settings);
        var csv = LegendExport.ToCsv(entries);

        Assert.Contains("\"Tegel; grijs\"", csv);
    }

    [Theory]
    [InlineData("=SOM(A1:A2)")]
    [InlineData("+1")]
    [InlineData("-1+2")]
    [InlineData("@cmd")]
    [InlineData("  =1+1")]
    [InlineData("\t=1+1")]
    public void Csv_NeutralizesFormulaInjection(string dangerous)
    {
        var settings = new LegendSettings();
        settings.TextOverrides["OPENVERHARDING_TEGEL"] = dangerous;

        var entries = LegendGrouping.Build(Parse("N-WE-VH-OPENVERHARDING_TEGEL-G"), settings);
        var csv = LegendExport.ToCsv(entries);

        // De cel begint met een apostrof, zodat Excel/Calc de tekst niet als formule uitvoert,
        // ook wanneer er voorloopspaties vóór de formule staan.
        Assert.Contains("'" + dangerous, csv);
    }

    [Fact]
    public void CombinedCsv_HasTekeningColumnPerDrawing()
    {
        var a = LegendGrouping.Build(Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), new LegendSettings());
        var b = LegendGrouping.Build(Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G"), new LegendSettings());
        var perDrawing = new (string, IReadOnlyList<LegendEntry>)[] { ("ontwerp-A.dwg", a), ("ontwerp-B.dwg", b) };

        var csv = LegendExport.ToCombinedCsv(perDrawing);
        var lines = csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Tekening;Status;", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("ontwerp-A.dwg;"));
        Assert.Contains(lines, l => l.StartsWith("ontwerp-B.dwg;"));
        // Kop + één regel per tekening.
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void CombinedJson_IncludesTekeningField()
    {
        var a = LegendGrouping.Build(Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), new LegendSettings());
        var perDrawing = new (string, IReadOnlyList<LegendEntry>)[] { ("tek.dwg", a) };

        var json = LegendExport.ToCombinedJson(perDrawing);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var first = doc.RootElement[0];
        Assert.Equal("tek.dwg", first.GetProperty("tekening").GetString());
    }
}
