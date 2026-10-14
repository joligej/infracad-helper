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
}
