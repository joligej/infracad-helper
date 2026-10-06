using System.Linq;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Bronvrije geometrietest van de legenda-layout: pint rij-afstand, kolomorigin en titelpositie
// vast zodat de opmaak niet stilletjes verschuift. Geen lokale bestanden of maten nodig.
public class LegendaLayoutGeometrieTests
{
    private static List<NlcsLayerName> Parse(params string[] layers)
    {
        var list = new List<NlcsLayerName>();
        foreach (var l in layers)
            if (NlcsLayerParser.TryParse(l, out var p))
                list.Add(p!);
        return list;
    }

    private static LegendSettings PlainSettings()
    {
        return new LegendSettings
        {
            Scale = 200,
            IncludeGroupHeaders = false,
            IncludeHoofdgroepHeaders = false,
            IncludeTitle = true,
            Title = "LEGENDA"
        };
    }

    [Fact]
    public void Titel_staat_bovenaan_in_de_eerste_kolom()
    {
        var s = PlainSettings();
        var entries = LegendGrouping.Build(Parse("N-WE-VH-AAA-G", "N-WE-VH-BBB-G"), s, _ => null);
        var layout = LegendLayoutEngine.Compute(entries, s);

        var title = Assert.Single(layout.Items, i => i.Kind == LegendItemKind.Title);
        Assert.Equal(0.0, title.X, 6);
        Assert.Equal(layout.MaxY, title.YTop, 6);
        Assert.Equal(0.0, layout.MinX, 6);
    }

    [Fact]
    public void Rijafstand_tussen_regels_is_de_regelhoogte()
    {
        var s = PlainSettings();
        var entries = LegendGrouping.Build(Parse("N-WE-VH-AAA-G", "N-WE-VH-BBB-G", "N-WE-VH-CCC-G"), s, _ => null);
        var layout = LegendLayoutEngine.Compute(entries, s);

        double rowGap = System.Math.Max(s.ToModel(s.RowPitchMm - s.SwatchHeightMm), s.ToModel(s.TextHeightMm) * 0.3);
        var rows = layout.Items.Where(i => i.Kind == LegendItemKind.Entry).OrderByDescending(i => i.YTop).ToList();
        Assert.True(rows.Count >= 2);
        for (int i = 1; i < rows.Count; i++)
            Assert.Equal(rows[i - 1].YTop - rows[i - 1].RowHeight - rowGap, rows[i].YTop, 6);
    }

    [Fact]
    public void Tweede_kolom_staat_op_kolombreedte_plus_tussenruimte()
    {
        var s = PlainSettings();
        s.Columns = 2;
        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-AAA-G", "N-WE-VH-BBB-G", "N-WE-VH-CCC-G", "N-WE-VH-DDD-G"), s, _ => null);
        var layout = LegendLayoutEngine.Compute(entries, s);

        double colWidth = s.ToModel(s.ColumnWidthMm);
        double colGap = s.ToModel(s.ColumnGapMm);
        var second = layout.Items.Where(i => i.Column == 1).ToList();
        Assert.NotEmpty(second);
        Assert.All(second, i => Assert.Equal(colWidth + colGap, i.X, 6));
    }
}
