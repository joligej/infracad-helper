using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class MeasuredLayoutTests
{
    private static LegendEntry Entry(NlcsStatus status, string text) => new()
    { Status = status, Discipline = "WE", Hoofdgroep = "VH", Element = text, Description = text };

    [Fact]
    public void NativeTextAndSymbolHeightReserveTheirActualSpace()
    {
        var entries = new[] { Entry(NlcsStatus.Nieuw, "Lange omschrijving"), Entry(NlcsStatus.Nieuw, "Symbool") };
        var settings = new LegendSettings { Scale = 200, IncludeTitle = false, IncludeGroupHeaders = false };
        var measuredWidths = new List<double>();
        var layout = LegendLayoutEngine.Compute(entries, settings,
            (text, width) => { measuredWidths.Add(width); return text == "Lange omschrijving" ? 4 : 0.5; },
            entry => entry.Description == "Symbool" ? 18 : 0);
        var rows = layout.Items.Where(i => i.Kind == LegendItemKind.Entry).ToArray();
        Assert.Equal(4, rows[0].RowHeight);
        Assert.Equal(18, rows[1].RowHeight);
        Assert.True(rows[1].YTop < rows[0].YTop - rows[0].RowHeight);
        Assert.All(measuredWidths, width => Assert.Equal((143 - 24 - 5.7188) * 0.2, width, 8));
        Assert.True(layout.MinY <= rows[1].YTop - 18);
    }

    [Fact]
    public void HeightLimitIncludesRepeatedStatusHeaders()
    {
        var settings = new LegendSettings { Scale = 1000, IncludeTitle = false, MaxLegendHeightMm = 20 };
        var layout = LegendLayoutEngine.Compute(new[]
            { Entry(NlcsStatus.Bestaand, "Bestaand"), Entry(NlcsStatus.Nieuw, "Nieuw") }, settings);
        Assert.Equal(2, layout.Columns);
        foreach (var column in layout.Items.GroupBy(i => i.Column))
        {
            Assert.Single(column, i => i.Kind == LegendItemKind.Header);
            var row = Assert.Single(column, i => i.Kind == LegendItemKind.Entry);
            Assert.True(-row.YTop + row.RowHeight <= 20);
        }
    }

    [Fact]
    public void TemplateResetRestoresHelpersButKeepsContentAndScale()
    {
        var settings = new LegendSettings { Scale = 500, DrawBorder = true, DrawSwatchFrame = false,
            SwatchFrameLayer = "Eigen hulp", RemarksText = "Eigen notitie", LabelNieuw = "Eigen kop" };
        settings.ResetFormattingToTemplate();
        Assert.True(settings.DrawSwatchFrame);
        Assert.False(settings.DrawBorder);
        Assert.Equal("X-XX-AL-HULPLIJN-G", settings.SwatchFrameLayer);
        Assert.Equal(500, settings.Scale);
        Assert.Equal("Eigen notitie", settings.RemarksText);
        Assert.Equal("Eigen kop", settings.LabelNieuw);
    }
    [Fact]
    public void FixedWindowHeightBalancesAutomaticColumnsIncludingMultilineRows()
    {
        var settings = new LegendSettings { Scale = 1000, IncludeTitle = false, BalanceColumns = true,
            MaxLegendHeightMm = 150, MaxRowsPerColumn = 1000 };
        var entries = Enumerable.Range(0, 23).Select(i => Entry(i < 14 ? NlcsStatus.Bestaand : NlcsStatus.Nieuw, "Regel " + i)).ToArray();
        var layout = LegendLayoutEngine.Compute(entries, settings, (text, width) => text.EndsWith("2") ? 15 : 3);
        var columns = layout.Items.GroupBy(i => i.Column).Select(g => g.Max(i => -i.YTop + i.RowHeight)).ToArray();
        Assert.Equal(2, columns.Length);
        Assert.True(Math.Abs(columns[0] - columns[1]) < 30, string.Join(",", columns));
        Assert.All(layout.Items.Where(i => i.Kind == LegendItemKind.Entry), i => Assert.True(-i.YTop + i.RowHeight <= 150));
        Assert.Equal(entries, layout.Items.Where(i => i.Kind == LegendItemKind.Entry).Select(i => i.Entry));
    }
    [Fact]
    public void WholeStatusGroupMovesTogetherButOversizedGroupCanContinue()
    {
        var settings = new LegendSettings { Scale = 1000, IncludeTitle = false, BalanceColumns = false,
            KeepStatusGroupsTogether = true, MaxLegendHeightMm = 50, MaxRowsPerColumn = 100 };
        var entries = Enumerable.Range(0, 8).Select(i => Entry(i < 3 ? NlcsStatus.Bestaand : NlcsStatus.Nieuw, "Regel " + i)).ToArray();
        var layout = LegendLayoutEngine.Compute(entries, settings);
        var newRows = layout.Items.Where(i => i.Entry?.Status == NlcsStatus.Nieuw).ToArray();
        Assert.Single(newRows.Select(i => i.Column).Distinct());
        Assert.NotEqual(layout.Items.First(i => i.Entry is not null).Column, newRows[0].Column);
        settings.MaxLegendHeightMm = 30;
        layout = LegendLayoutEngine.Compute(entries, settings);
        Assert.True(layout.Items.Where(i => i.Entry?.Status == NlcsStatus.Nieuw).Select(i => i.Column).Distinct().Count() > 1);
        Assert.All(layout.Items.GroupBy(i => i.Column), c => Assert.Contains(c, i => i.Kind == LegendItemKind.Entry));
    }
    [Fact]
    public void AlternatingManualStatusGroupsBalanceWithoutReordering()
    {
        var settings = new LegendSettings { Scale = 1000, IncludeTitle = false, Columns = 2,
            MaxLegendHeightMm = 0, MaxRowsPerColumn = 100 };
        var entries = Enumerable.Range(0, 8).Select(i => Entry(i % 2 == 0 ? NlcsStatus.Bestaand : NlcsStatus.Nieuw, "Regel " + i)).ToArray();
        var layout = LegendLayoutEngine.Compute(entries, settings);
        var rows = layout.Items.Where(i => i.Kind == LegendItemKind.Entry).ToArray();
        Assert.Equal(entries, rows.Select(i => i.Entry));
        Assert.Equal(new[] { 4, 4 }, rows.GroupBy(i => i.Column).Select(g => g.Count()));
    }
}
