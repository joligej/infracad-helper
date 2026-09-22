using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LayoutAdvancedTests
{
    private static LegendEntry Entry(NlcsStatus status, string hoofdgroep, string element,
        LayerMetric metric = default) => new()
    {
        Status = status,
        Discipline = "WE",
        Hoofdgroep = hoofdgroep,
        Element = element,
        Description = element,
        Metric = metric
    };

    [Fact]
    public void MaxLegendHeight_StartsNewColumn()
    {
        // 10 regels met een bekende regelhoogte; een lage maxhoogte moet meerdere
        // kolommen forceren.
        var entries = Enumerable.Range(0, 10)
            .Select(i => Entry(NlcsStatus.Nieuw, "VH", $"E{i}")).ToArray();
        var settings = new LegendSettings
        {
            IncludeTitle = false, IncludeGroupHeaders = false, IncludeHoofdgroepHeaders = false,
            BalanceColumns = false, MaxRowsPerColumn = 1000, MaxLegendHeightMm = 20.0
        };

        var layout = LegendLayoutEngine.Compute(entries, settings);

        Assert.True(layout.Columns > 1, "Verwacht meerdere kolommen door de maximale hoogte.");
        double maxColH = settings.ToModel(settings.MaxLegendHeightMm);
        // Elke kolom (behalve met één regel) blijft binnen de maxhoogte.
        var byCol = layout.Items.Where(i => i.Kind == LegendItemKind.Entry).GroupBy(i => i.Column);
        foreach (var col in byCol)
        {
            double top = col.Max(i => i.YTop);
            double bottom = col.Min(i => i.YTop - i.RowHeight);
            if (col.Count() > 1)
                Assert.True(top - bottom <= maxColH + 1e-6, $"Kolom te hoog: {top - bottom} > {maxColH}");
        }
    }

    [Fact]
    public void BalanceColumns_DistributesRowsEvenly()
    {
        var entries = Enumerable.Range(0, 5)
            .Select(i => Entry(NlcsStatus.Nieuw, "VH", $"E{i}")).ToArray();
        var settings = new LegendSettings
        {
            IncludeTitle = false, IncludeGroupHeaders = false,
            MaxRowsPerColumn = 4, BalanceColumns = true
        };

        var layout = LegendLayoutEngine.Compute(entries, settings);

        Assert.Equal(2, layout.Columns); // ceil(5/4)=2 kolommen
        var perColumn = layout.Items
            .Where(i => i.Kind == LegendItemKind.Entry)
            .GroupBy(i => i.Column)
            .Select(g => g.Count())
            .ToList();
        Assert.All(perColumn, c => Assert.True(c <= 3)); // gebalanceerd: max 3 per kolom
    }

    [Fact]
    public void ExplicitColumns_ForcesColumnCount()
    {
        var entries = Enumerable.Range(0, 10)
            .Select(i => Entry(NlcsStatus.Nieuw, "VH", $"E{i}")).ToArray();
        var settings = new LegendSettings
        {
            IncludeTitle = false, IncludeGroupHeaders = false, Columns = 2
        };

        var layout = LegendLayoutEngine.Compute(entries, settings);

        Assert.Equal(2, layout.Columns);
    }

    [Fact]
    public void HoofdgroepHeaders_AreEmittedPerHoofdgroep()
    {
        var entries = new[]
        {
            Entry(NlcsStatus.Nieuw, "VH", "A"),
            Entry(NlcsStatus.Nieuw, "VH", "B"),
            Entry(NlcsStatus.Nieuw, "RI", "C")
        };
        var settings = new LegendSettings
        {
            IncludeTitle = false, IncludeGroupHeaders = true, IncludeHoofdgroepHeaders = true,
            MaxRowsPerColumn = 30
        };

        var layout = LegendLayoutEngine.Compute(entries, settings);

        var subHeaders = layout.Items.Where(i => i.Kind == LegendItemKind.SubHeader).ToList();
        Assert.Equal(2, subHeaders.Count); // VH en RI
    }

    [Fact]
    public void SortMode_Naam_OrdersAlphabetically()
    {
        var parsed = new List<NlcsLayerName>();
        foreach (var l in new[]
                 {
                     "N-WE-VH-OPENVERHARDING_TEGEL-G",       // "Betontegel"
                     "N-WE-VH-OPENVERHARDING_NATUURSTEEN-G"  // "Natuursteen"
                 })
            if (NlcsLayerParser.TryParse(l, out var p)) parsed.Add(p!);

        var entries = LegendGrouping.Build(parsed, new LegendSettings { SortMode = LegendSortMode.Naam });

        Assert.Equal("Betontegel", entries[0].Description);
        Assert.Equal("Natuursteen", entries[1].Description);
    }

    [Fact]
    public void SortMode_Hoeveelheid_OrdersDescending()
    {
        var parsed = new List<NlcsLayerName>();
        foreach (var l in new[] { "N-WE-VH-OPENVERHARDING_TEGEL-G", "N-WE-VH-OPENVERHARDING_NATUURSTEEN-G" })
            if (NlcsLayerParser.TryParse(l, out var p)) parsed.Add(p!);

        var metrics = new Dictionary<string, LayerMetric>(StringComparer.OrdinalIgnoreCase)
        {
            ["N-WE-VH-OPENVERHARDING_TEGEL-G"] = new(1, 10, 0),
            ["N-WE-VH-OPENVERHARDING_NATUURSTEEN-G"] = new(1, 500, 0)
        };

        var entries = LegendGrouping.Build(
            parsed, new LegendSettings { SortMode = LegendSortMode.Hoeveelheid }, null, metrics);

        Assert.Equal("Natuursteen", entries[0].Description); // grootste hoeveelheid eerst
    }
}
