using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class TemplateLayoutRegressionTests
{
    private static LegendEntry Entry(NlcsStatus status) => new()
    { Status = status, Discipline = "WE", Hoofdgroep = "VH", Element = status.ToString(), Description = status.ToString() };

    [Fact]
    public void AutomaticOrderStartsWithExistingSituationLikeReference()
    {
        var sorted = LegendGrouping.Sort(new[] { Entry(NlcsStatus.Nieuw), Entry(NlcsStatus.Bestaand) }, new());
        Assert.Equal(new[] { NlcsStatus.Bestaand, NlcsStatus.Nieuw }, sorted.Select(e => e.Status));
    }

    [Fact]
    public void TemplateResetRestoresColumnsAndHeadersButRetainsUserContent()
    {
        var settings = new LegendSettings { Columns = 7, MaxRowsPerColumn = 2, BalanceColumns = false,
            MaxLegendHeightMm = 12, IncludeGroupHeaders = false, IncludeHoofdgroepHeaders = true,
            IncludeQuantities = true, IncludeTotalsRow = true, InsertSymbolBlocks = false,
            Scale = 500, RemarksText = "Eigen tekst", SortMode = LegendSortMode.Handmatig };
        settings.ManualEntryOrder.Add("EIGEN");
        settings.ResetFormattingToTemplate();
        var defaults = new LegendSettings();
        Assert.Equal(defaults.Columns, settings.Columns);
        Assert.Equal(defaults.MaxRowsPerColumn, settings.MaxRowsPerColumn);
        Assert.Equal(defaults.BalanceColumns, settings.BalanceColumns);
        Assert.Equal(defaults.MaxLegendHeightMm, settings.MaxLegendHeightMm);
        Assert.True(settings.IncludeGroupHeaders);
        Assert.False(settings.IncludeHoofdgroepHeaders);
        Assert.False(settings.IncludeQuantities);
        Assert.False(settings.IncludeTotalsRow);
        Assert.True(settings.InsertSymbolBlocks);
        Assert.Equal(500, settings.Scale);
        Assert.Equal("Eigen tekst", settings.RemarksText);
        Assert.Equal(LegendSortMode.Handmatig, settings.SortMode);
        Assert.Equal(new[] { "EIGEN" }, settings.ManualEntryOrder);
    }

    [Fact]
    public void MeasuredHeadingHeightReservesUnderliningAndFontExtents()
    {
        var settings = new LegendSettings { Scale = 1000, HeaderSpacingMm = 0 };
        var layout = LegendLayoutEngine.Compute(new[] { Entry(NlcsStatus.Bestaand) }, settings,
            measureHeadingHeight: (text, height) => text == "LEGENDA" ? 40 : 30);
        var header = Assert.Single(layout.Items, i => i.Kind == LegendItemKind.Header);
        var row = Assert.Single(layout.Items, i => i.Kind == LegendItemKind.Entry);
        Assert.True(header.YTop < -40);
        Assert.True(row.YTop < header.YTop - 30);
    }

    [Fact]
    public void LargeStatusHeaderCannotOverlapPreviousGroup()
    {
        var settings = new LegendSettings { Scale = 1000, HeaderTextHeightMm = 20 };
        var layout = LegendLayoutEngine.Compute(new[] { Entry(NlcsStatus.Bestaand), Entry(NlcsStatus.Nieuw) }, settings);
        var rows = layout.Items.Where(i => i.Kind == LegendItemKind.Entry).ToArray();
        var headers = layout.Items.Where(i => i.Kind == LegendItemKind.Header).ToArray();
        Assert.True(headers[1].YTop < rows[0].YTop - rows[0].RowHeight);
    }

    [Theory]
    [InlineData(2.5, 0)]
    [InlineData(20, 6.9257)]
    public void LargeHeaderOrSmallSpacingCannotOverlapTitle(double height, double gap)
    {
        var settings = new LegendSettings { Scale = 1000, HeaderTextHeightMm = height, HeaderSpacingMm = gap };
        var layout = LegendLayoutEngine.Compute(new[] { Entry(NlcsStatus.Bestaand) }, settings);
        var title = Assert.Single(layout.Items, i => i.Kind == LegendItemKind.Title);
        var header = Assert.Single(layout.Items, i => i.Kind == LegendItemKind.Header);
        Assert.True(header.YTop < title.YTop - settings.TitleTextHeightMm);
    }
}
