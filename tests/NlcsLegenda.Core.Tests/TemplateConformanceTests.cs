using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class TemplateConformanceTests
{
    private const string Base = "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN";

    private static List<NlcsLayerName> Parse(params string[] layers)
    {
        var list = new List<NlcsLayerName>();
        foreach (var l in layers)
            if (NlcsLayerParser.TryParse(l, out var p))
                list.Add(p!);
        return list;
    }

    // De kern van de render-bug (J/K): een G-lijn blijft een G-lijn, ook naast een arcering.
    [Fact]
    public void GeometryLayer_DoesNotFallBackToVlak()
    {
        var entry = Assert.Single(LegendGrouping.Build(Parse($"{Base}-G", $"{Base}-A"), new LegendSettings()));
        Assert.Equal($"{Base}-G", entry.GeometryLayer);
        Assert.Equal($"{Base}-A", entry.HatchLayer);
        Assert.Null(entry.VlakLayer);
        Assert.True(entry.HasHatch);
    }

    [Fact]
    public void PureVlak_HasVlakLayer_NoGeometryLine()
    {
        var entry = Assert.Single(LegendGrouping.Build(Parse($"{Base}-GV"), new LegendSettings()));
        Assert.Null(entry.GeometryLayer);
        Assert.Equal($"{Base}-GV", entry.VlakLayer);
        Assert.True(entry.IsArea);
    }

    [Fact]
    public void GeometryAndVlak_KeepBothLayersSeparate()
    {
        var entry = Assert.Single(LegendGrouping.Build(Parse($"{Base}-G", $"{Base}-GV"), new LegendSettings()));
        Assert.Equal($"{Base}-G", entry.GeometryLayer);
        Assert.Equal($"{Base}-GV", entry.VlakLayer);
    }

    [Fact]
    public void PureVlak_WithArea_ReportsAreaQuantity()
    {
        var entry = new LegendEntry
        {
            Status = NlcsStatus.Nieuw,
            Discipline = "WE",
            Hoofdgroep = "VH",
            Element = "GRAS",
            Description = "Gras",
            LayersByType = new() { [NlcsDrawType.Vlak] = "N-WE-VH-GROEN_GRAS-GV" },
            Metric = new LayerMetric(1, 0, 42.0)
        };
        Assert.Equal(QuantityKind.Area, entry.QuantityType);
    }

    // Template-conforme standaardwaarden: NLCS-standaardteksthoogtes, ExplodeOnPlace uit,
    // en de echte NLCS-tekstlagen (-T25/-T50).
    [Fact]
    public void Defaults_MatchTemplateConventions()
    {
        var s = new LegendSettings();
        Assert.False(s.ExplodeOnPlace);
        Assert.Equal(2.5, s.TextHeightMm);
        Assert.Equal(5.0, s.HeaderTextHeightMm);
        Assert.Equal(7.0, s.TitleTextHeightMm);
        Assert.Equal("X-XX-AL-LEGENDA-T25", s.TextLayer);
        Assert.Equal("X-XX-AL-LEGENDA-T50", s.HeaderTextLayer);
    }

    [Fact]
    public void ResetFormattingToTemplate_ResetsLayout_KeepsContent()
    {
        var s = new LegendSettings
        {
            Scale = 500,
            TitleTextHeightMm = 12,
            SwatchWidthMm = 99,
            ExplodeOnPlace = true
        };
        s.IncludedStatuses.Remove(NlcsStatus.Vervallen);
        s.ExcludedDisciplines.Add("RIO");
        s.ManualEntries.Add(new ManualEntry { Layer = "EIGEN-LIJN", Type = NlcsDrawType.Geometrie });

        s.ResetFormattingToTemplate();

        // Opmaak terug naar template.
        Assert.Equal(7.0, s.TitleTextHeightMm);
        Assert.Equal(24.0, s.SwatchWidthMm);
        Assert.False(s.ExplodeOnPlace);
        // Inhoud/identiteit blijft.
        Assert.Equal(500, s.Scale);
        Assert.DoesNotContain(NlcsStatus.Vervallen, s.IncludedStatuses);
        Assert.Contains("RIO", s.ExcludedDisciplines);
        Assert.Single(s.ManualEntries);
    }

    [Fact]
    public void ExplainExclusion_GivesSpecificReason()
    {
        Assert.True(NlcsLayerParser.TryParse($"{Base}-G", out var layer));
        var s = new LegendSettings();
        Assert.Null(s.ExplainExclusion(layer!));

        s.IncludedStatuses.Remove(layer!.Status);
        var reason = s.ExplainExclusion(layer!);
        Assert.NotNull(reason);
        Assert.Contains("status", reason!);

        var s2 = new LegendSettings();
        s2.ExcludedDisciplines.Add(layer!.Discipline);
        Assert.Contains("discipline", s2.ExplainExclusion(layer!)!);
    }

    // Viewport: de teruggerekende papier-mm marge moet exact de ingestelde marge zijn, en de
    // schaal in beide richtingen 1:schaal (aspect ratio klopt).
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(500)]
    [InlineData(1000)]
    public void ViewportMath_MarginsRoundTripExactly(double scale)
    {
        double modelW = 12.5, modelH = 6.25, margin = 5.0;
        var plan = ViewportMath.Compute(modelW, modelH, scale, margin);
        var (h, v) = ViewportMath.PaperMargins(plan, modelW, modelH, scale);
        Assert.Equal(margin, h, 6);
        Assert.Equal(margin, v, 6);

        double sx = (plan.PaperWidthMm - 2 * margin) / modelW;
        double sy = (plan.PaperHeightMm - 2 * margin) / modelH;
        Assert.Equal(sx, sy, 6);
        Assert.Equal(1000.0 / scale, sx, 6);
    }
}
