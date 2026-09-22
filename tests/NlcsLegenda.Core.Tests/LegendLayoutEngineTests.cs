using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendLayoutEngineTests
{
    private static LegendEntry Entry(NlcsStatus status, string element) => new()
    {
        Status = status,
        Discipline = "WE",
        Hoofdgroep = "VH",
        Element = element,
        Description = element
    };

    private static LegendSettings NoTitle(int maxRows) => new()
    {
        IncludeTitle = false,
        IncludeGroupHeaders = true,
        MaxRowsPerColumn = maxRows
    };

    [Fact]
    public void SingleColumn_WhenWithinRowLimit()
    {
        var entries = new[]
        {
            Entry(NlcsStatus.Nieuw, "A"), Entry(NlcsStatus.Nieuw, "B"), Entry(NlcsStatus.Nieuw, "C")
        };
        var layout = LegendLayoutEngine.Compute(entries, NoTitle(30));

        Assert.Equal(1, layout.Columns);
        Assert.Equal(3, layout.Items.Count(i => i.Kind == LegendItemKind.Entry));
        Assert.Equal(1, layout.Items.Count(i => i.Kind == LegendItemKind.Header));
    }

    [Fact]
    public void WrapsIntoColumns_AndRepeatsHeader()
    {
        var entries = Enumerable.Range(0, 5)
            .Select(i => Entry(NlcsStatus.Nieuw, $"E{i}")).ToArray();

        var layout = LegendLayoutEngine.Compute(entries, NoTitle(2));

        Assert.Equal(3, layout.Columns); // 2 + 2 + 1
        Assert.Equal(5, layout.Items.Count(i => i.Kind == LegendItemKind.Entry));
        // Kop wordt bovenaan elke kolom herhaald voor de doorlopende groep.
        Assert.Equal(3, layout.Items.Count(i => i.Kind == LegendItemKind.Header));
    }

    [Fact]
    public void EachColumnStartsWithHeader()
    {
        var entries = Enumerable.Range(0, 4)
            .Select(i => Entry(NlcsStatus.Bestaand, $"E{i}")).ToArray();

        var layout = LegendLayoutEngine.Compute(entries, NoTitle(2));

        foreach (var column in layout.Items.GroupBy(i => i.Column))
        {
            Assert.Equal(LegendItemKind.Header, column.OrderByDescending(i => i.YTop).First().Kind);
        }
    }

    [Fact]
    public void SeparateHeaderPerStatusGroup()
    {
        var entries = new[]
        {
            Entry(NlcsStatus.Nieuw, "A"),
            Entry(NlcsStatus.Bestaand, "B"),
            Entry(NlcsStatus.Vervallen, "C")
        };
        var layout = LegendLayoutEngine.Compute(entries, NoTitle(30));

        var headers = layout.Items.Where(i => i.Kind == LegendItemKind.Header).ToList();
        Assert.Equal(3, headers.Count);
        Assert.Contains(headers, h => h.Status == NlcsStatus.Nieuw);
        Assert.Contains(headers, h => h.Status == NlcsStatus.Bestaand);
        Assert.Contains(headers, h => h.Status == NlcsStatus.Vervallen);
    }

    [Fact]
    public void Title_IsPlacedWhenEnabled()
    {
        var entries = new[] { Entry(NlcsStatus.Nieuw, "A") };
        var settings = new LegendSettings { IncludeTitle = true, Title = "LEGENDA" };

        var layout = LegendLayoutEngine.Compute(entries, settings);

        var title = layout.Items.Single(i => i.Kind == LegendItemKind.Title);
        Assert.Equal("LEGENDA", title.Text);
        Assert.Equal(0, title.YTop);
    }

    [Fact]
    public void Columns_AreHorizontallyOffset()
    {
        var entries = Enumerable.Range(0, 4)
            .Select(i => Entry(NlcsStatus.Nieuw, $"E{i}")).ToArray();

        var layout = LegendLayoutEngine.Compute(entries, NoTitle(2));

        var col0X = layout.Items.First(i => i.Column == 0).X;
        var col1X = layout.Items.First(i => i.Column == 1).X;
        Assert.True(col1X > col0X);
    }
}
