using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Numeriek templatecontract: de layout is volledig in model-eenheden (mm * schaal / 1000),
// dus teruggerekend naar papier-mm moet hij op elke schaal identiek zijn. Zo vangen we
// zichtbare layoutregressies zonder een echte render/DWG nodig te hebben.
public class LegendGeometryContractTests
{
    private const string Base1 = "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN";
    private const string Base2 = "B-WE-VH-GESLOTENVERHARDING_ASFALT";

    private static List<LegendEntry> Entries()
    {
        var layers = new List<NlcsLayerName>();
        foreach (var l in new[] { $"{Base1}-G", $"{Base1}-A", $"{Base2}-GV" })
            if (NlcsLayerParser.TryParse(l, out var p))
                layers.Add(p!);
        return LegendGrouping.Build(layers, new LegendSettings()).ToList();
    }

    private static double ToPaperMm(double model, double scale) => model * 1000.0 / scale;

    [Fact]
    public void Layout_IsScaleInvariant_InPaperMm()
    {
        var entries = Entries();
        double[] scales = { 100, 200, 500, 1000 };

        // Referentie op 1:100, alles teruggerekend naar papier-mm.
        var reference = Reference(entries, 100);

        foreach (var scale in scales)
        {
            var s = new LegendSettings { Scale = scale };
            var layout = LegendLayoutEngine.Compute(entries, s);
            Assert.Equal(reference.Count, layout.Items.Count);
            for (int i = 0; i < layout.Items.Count; i++)
            {
                Assert.Equal(reference[i].kind, layout.Items[i].Kind);
                Assert.Equal(reference[i].x, ToPaperMm(layout.Items[i].X, scale), 3);
                Assert.Equal(reference[i].y, ToPaperMm(layout.Items[i].YTop, scale), 3);
            }
            // Totale breedte in papier-mm blijft gelijk aan de kolombreedte (1 kolom).
            Assert.Equal(67.0, ToPaperMm(layout.MaxX - layout.MinX, scale), 3);
        }
    }

    private static List<(LegendItemKind kind, double x, double y)> Reference(
        IReadOnlyList<LegendEntry> entries, double scale)
    {
        var layout = LegendLayoutEngine.Compute(entries, new LegendSettings { Scale = scale });
        return layout.Items
            .Select(i => (i.Kind, ToPaperMm(i.X, scale), ToPaperMm(i.YTop, scale)))
            .ToList();
    }

    [Fact]
    public void Title_StartsAtTopInPaperMm()
    {
        var entries = Entries();
        var s = new LegendSettings { Scale = 200 };
        var layout = LegendLayoutEngine.Compute(entries, s);

        var title = layout.Items.First(i => i.Kind == LegendItemKind.Title);
        Assert.Equal(0.0, ToPaperMm(title.YTop, 200), 3);

        // Eerste inhoud (kop of regel) begint onder de titelband: titel 7mm + witruimte 6mm.
        var firstBelow = layout.Items.First(i => i.Kind != LegendItemKind.Title);
        Assert.Equal(-(7.0 + 6.0), ToPaperMm(firstBelow.YTop, 200), 3);
    }

    [Fact]
    public void Columns_HaveTemplatePitchInPaperMm()
    {
        // Genoeg regels forceren meerdere kolommen; kolomsprong = kolombreedte + tussenruimte.
        var layers = new List<NlcsLayerName>();
        for (int i = 0; i < 80; i++)
            if (NlcsLayerParser.TryParse($"N-WE-VH-ELEMENT{i:D3}_X-G", out var p))
                layers.Add(p!);
        var entries = LegendGrouping.Build(layers, new LegendSettings()).ToList();

        var s = new LegendSettings { Scale = 500, MaxRowsPerColumn = 20 };
        var layout = LegendLayoutEngine.Compute(entries, s);
        Assert.True(layout.Columns >= 2);

        var col0 = layout.Items.Where(i => i.Column == 0).Select(i => ToPaperMm(i.X, 500)).Distinct().Single();
        var col1 = layout.Items.Where(i => i.Column == 1).Select(i => ToPaperMm(i.X, 500)).Distinct().Single();
        Assert.Equal(0.0, col0, 3);
        Assert.Equal(67.0 + 10.0, col1 - col0, 3); // kolombreedte 67 + tussenruimte 10
    }
}
