using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class RequestedBehaviorTests
{
    [Theory]
    [InlineData("DATA_COAXKABEL_17_ZIGGO BV", "Ziggo B.V.", "DATA", "COAXKABEL", "17")]
    [InlineData("DATA_MANTELBUIS_POLYETHYLEEN PE_110_KPN BV", "KPN B.V.", "DATA", "MANTELBUIS_POLYETHYLEEN PE", "110")]
    [InlineData("GAS_LD_MANTELBUIS_STAAL_200_LIANDER NV", "Liander N.V.", "GAS", "MANTELBUIS_STAAL", "200")]
    public void PipeConstructionAndDiameterAreIndependent(string text, string provider, string kind, string construction, string diameter)
    {
        var properties = ElementProperties.From(text, provider);
        Assert.Equal(kind, properties.Soort);
        Assert.Equal(construction, properties.Uitvoering);
        Assert.Equal(diameter, properties.Diameter);
        Assert.Equal("", properties.Nummer);
    }

    [Fact]
    public void DiameterMergingKeepsGasAndWaterDistinct()
    {
        var layers = new[] { "B-WE-KL-GAS_110-G", "B-WE-KL-GAS_200-G", "B-WE-KL-WATER_110-G" }
            .Select(name => { Assert.True(NlcsLayerParser.TryParse(name, out var layer)); return layer!; });
        var entries = LegendGrouping.Build(layers, new LegendSettings(), renderIdentity: _ => "same line");
        Assert.Equal(2, entries.Count);
        var gas = Assert.Single(entries, e => e.Element.StartsWith("GAS"));
        Assert.Contains("110", gas.DisplayDescription);
        Assert.Contains("200", gas.DisplayDescription);
    }

    [Fact]
    public void QualifiedLayerNamesRemainReferences()
    {
        const string source = "Hoofd|DTM-BS-BGT_Hoogtes|Hoogtepunten";
        Assert.True(LayerNaming.IsValidReference(source));
        Assert.False(LayerNaming.IsValid(source));
        Assert.Equal("Hoofd|DTM-BS-BGT_Hoogtes", LayerNaming.SourceName(source));
        Assert.Equal("Hoogtepunten", LayerNaming.LocalName(source));
        Assert.True(new ManualEntry { Layer = source, Description = "Hoogtepunt" }.IsValid);
        Assert.False(LayerNaming.IsValidReference("Hoofd||Hoogtepunten"));
        Assert.True(new CustomLayerRule { Layer = "Hoogtepunten", Element = "Hoogtepunt", BlockName = "MEASURED" }
            .MatchtBlok("Hoofd|DTM-BS-BGT_Hoogtes|MEASURED"));
    }

    [Theory]
    [InlineData(ViewportAnchor.Linksboven, 50, -30)]
    [InlineData(ViewportAnchor.Middelpunt, 0, 0)]
    public void PlacementAnchorOffsetsTheViewport(ViewportAnchor anchor, double x, double y)
    {
        Assert.Equal((x, y), ViewportMath.CenterOffset(100, 60, anchor));
    }

    [Fact]
    public void NewSettingsSurviveCopyAndPersistence()
    {
        var defaults = new LegendSettings();
        Assert.Equal(ViewportAnchor.Linksboven, defaults.ViewportAnchor);
        Assert.Equal("VIEWPORT-NOPLOT", defaults.ViewportLayer);
        Assert.False(defaults.PlotViewportFrame);
        var settings = new LegendSettings { ViewportAnchor = ViewportAnchor.Middelpunt, ViewportLayer = "Eigen viewport",
            PlotViewportFrame = true, NormalizeSymbols = true, MergeSymbolSizes = true, MergeSymbolRotations = true, MergeSymbolAttributes = true };
        var copy = settings.Clone();
        Assert.Equal(settings.ToJson(), copy.ToJson());
        Assert.True(copy.NormalizeSymbols && copy.MergeSymbolSizes && copy.MergeSymbolRotations && copy.MergeSymbolAttributes && copy.PlotViewportFrame);
        Assert.Equal("Eigen viewport", copy.ViewportLayer);
    }

    [Fact]
    public void SplitRowsKeepAnOldCombinedExclusion()
    {
        LegendEntry Row(NlcsDrawType type, string identity) => new() { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH",
            Element = "ASFALT", Description = "Asfalt", RenderIdentity = identity, SelectionIdentity = identity,
            LayersByType = new() { [type] = "laag" } };
        var line = Row(NlcsDrawType.Geometrie, "Geometrie=line");
        var hatch = Row(NlcsDrawType.Arcering, "Arcering=hatch");
        var settings = new LegendSettings();
        settings.ExcludedEntries.Add(LegendSettings.SelectionKey(line, "Geometrie=line;Arcering=hatch"));
        settings.RememberEntries(new[] { line, hatch });
        Assert.True(settings.IsEntryExcluded(LegendSettings.SelectionKey(line)));
        Assert.True(settings.IsEntryExcluded(LegendSettings.SelectionKey(hatch)));
    }

    [Fact]
    public void HeightAndRowLimitsBothApplyWithMultilineText()
    {
        var rows = Enumerable.Range(0, 12).Select(i => new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE",
            Hoofdgroep = "VH", Element = i.ToString(), Description = "Meerregelige omschrijving" }).ToArray();
        var settings = new LegendSettings { Scale = 500, MaxLegendHeightMm = 90, MaxRowsPerColumn = 3 };
        var layout = LegendLayoutEngine.Compute(rows, settings, (_, _) => 7);
        Assert.True(layout.Height <= settings.ToModel(90));
        Assert.All(layout.Items.Where(i => i.Entry is not null).GroupBy(i => i.Column), column => Assert.InRange(column.Count(), 1, 3));
    }
}
