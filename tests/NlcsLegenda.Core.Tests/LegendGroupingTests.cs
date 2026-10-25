using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendGroupingTests
{
    private static IReadOnlyList<LegendEntry> Build(params string[] layers)
    {
        var parsed = new List<NlcsLayerName>();
        foreach (var l in layers)
        {
            Assert.True(NlcsLayerParser.TryParse(l, out var p), "Ongeldige testlaag");
            parsed.Add(p!);
        }
        return LegendGrouping.Build(parsed, new LegendSettings());
    }

    [Fact]
    public void LineAndHatchOfSameElement_StaySeparate()
    {
        var entries = Build(
            "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G",
            "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A",
            "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A-200");

        Assert.Equal(2, entries.Count);
        var line = Assert.Single(entries, e => e.GeometryLayer is not null);
        var hatch = Assert.Single(entries, e => e.HasHatch);
        Assert.Null(line.HatchLayer);
        Assert.Null(hatch.GeometryLayer);
        Assert.Equal("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G", line.GeometryLayer);
    }

    [Fact]
    public void Entries_AreSortedByStatusThenGroup()
    {
        var entries = Build(
            "V-WE-RI-HWA_RIOOLLEIDING_PVC_160-G",
            "N-WE-VH-OPENVERHARDING_TEGEL-G",
            "B-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G");

        Assert.Equal(NlcsStatus.Bestaand, entries[0].Status);
        Assert.Equal(NlcsStatus.Nieuw, entries[1].Status);
        Assert.Equal(NlcsStatus.Vervallen, entries[2].Status);
    }

    [Fact]
    public void ExcludedFrameLayers_AreIgnored()
    {
        var entries = Build(
            "X-XX-AL-TEKENBLAD_KADER-G",
            "N-WE-VH-OPENVERHARDING_TEGEL-G");

        Assert.Single(entries);
        Assert.Equal("VH", entries[0].Hoofdgroep);
    }

    [Fact]
    public void TextOnlyGroup_ProducesNoSwatch()
    {
        var entries = Build("N-WE-VW-MARKERING_TEKST-T50");
        Assert.Empty(entries);
    }

    [Fact]
    public void Description_UsesStandardText()
    {
        var entries = Build("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A");
        Assert.Equal("Betonstraatsteen", entries[0].Description);
    }

    [Fact]
    public void XrefLayers_ExcludedByDefault_IncludedWhenEnabled()
    {
        var parsed = new List<NlcsLayerName>();
        NlcsLayerParser.TryParse("SIT-NW-ONTWERP|N-WE-AM-AS_WEG-G", out var p);
        parsed.Add(p!);

        Assert.Empty(LegendGrouping.Build(parsed, new LegendSettings { IncludeXrefLayers = false }));
        Assert.Single(LegendGrouping.Build(parsed, new LegendSettings { IncludeXrefLayers = true }));
    }

    [Fact]
    public void StatusFilter_KeepsOnlyIncludedStatuses()
    {
        var settings = new LegendSettings
        {
            IncludedStatuses = new HashSet<NlcsStatus> { NlcsStatus.Nieuw }
        };
        var parsed = new[] { "N-WE-VH-OPENVERHARDING_TEGEL-G", "B-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G" }
            .Select(l => NlcsLayerParser.TryParse(l, out var p) ? p! : null!)
            .Where(p => p is not null);

        var entries = LegendGrouping.Build(parsed, settings);

        Assert.Single(entries);
        Assert.Equal(NlcsStatus.Nieuw, entries[0].Status);
    }

    [Fact]
    public void SameLogicalLayer_DifferentRenderIdentity_SplitsIntoSeparateRows()
    {
        var a = WithRender("N-WE-VH-WEG-G", "N-WE-VH-WEG-G", "ACI:1");
        var b = WithRender("XREF|N-WE-VH-WEG-G", "N-WE-VH-WEG-G #2", "ACI:3");

        var entries = LegendGrouping.Build(new[] { a, b }, new LegendSettings { IncludeXrefLayers = true },
            metrics: new Dictionary<string, LayerMetric>
            {
                [a.EffectiveRenderLayerName] = LayerMetric.Empty.Add(1, 5, 0),
                [b.EffectiveRenderLayerName] = LayerMetric.Empty.Add(1, 7, 0)
            });

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("WEG", e.Element));
        Assert.Equal(new[] { 5.0, 7.0 }, entries.Select(e => e.Metric.Length).OrderBy(value => value).ToArray());
        Assert.Equal(2, entries.Sum(e => e.SourceLayers.Count));
    }

    [Fact]
    public void SameLogicalLayer_SameRenderIdentity_StaysOneRow()
    {
        var a = WithRender("N-WE-VH-WEG-G", "N-WE-VH-WEG-G", "ACI:1");
        var b = WithRender("XREF|N-WE-VH-WEG-G", "N-WE-VH-WEG-G #2", "ACI:1");

        var entries = LegendGrouping.Build(new[] { a, b }, new LegendSettings { IncludeXrefLayers = true },
            metrics: new Dictionary<string, LayerMetric>
            {
                [a.EffectiveRenderLayerName] = LayerMetric.Empty.Add(1, 5, 0),
                [b.EffectiveRenderLayerName] = LayerMetric.Empty.Add(1, 7, 0)
            });

        var entry = Assert.Single(entries);
        Assert.Equal(12, entry.Metric.Length);
        Assert.Equal(2, entry.SourceLayers.Count);
    }

    [Fact]
    public void SameDescription_DifferentRenderIdentities_SortDeterministically()
    {
        var red = WithRender("N-WE-VH-WEG-G", "N-WE-VH-WEG-G", "ACI:1");
        var green = WithRender("XREF|N-WE-VH-WEG-G", "N-WE-VH-WEG-G #2", "ACI:3");
        var styles = new Dictionary<string, string>
        {
            [red.EffectiveRenderLayerName] = "ACI:1",
            [green.EffectiveRenderLayerName] = "ACI:3"
        };
        var settings = new LegendSettings { IncludeXrefLayers = true };

        IReadOnlyList<LegendEntry> BuildInOrder(params NlcsLayerName[] layers) =>
            LegendGrouping.Build(layers, settings,
                renderIdentity: layer => styles.TryGetValue(layer, out var style) ? style : null);

        var forward = BuildInOrder(green, red);
        var reverse = BuildInOrder(red, green);

        Assert.Equal(2, forward.Count);
        Assert.Equal(forward.Select(entry => entry.RenderIdentity), reverse.Select(entry => entry.RenderIdentity));
        Assert.Equal(new[] { "Geometrie=ACI:1", "Geometrie=ACI:3" },
            forward.Select(entry => entry.RenderIdentity));
    }

    [Fact]
    public void DescriptionAndProvenance_StayWithTheirRenderPartition()
    {
        var red = WithRender("N-WE-VH-WEG-G", "N-WE-VH-WEG-G", "red");
        var green = WithRender("OUTER|INNER|N-WE-VH-WEG-G", "N-WE-VH-WEG-G #2", "green");
        var entries = LegendGrouping.Build(new[] { green, red }, new LegendSettings { IncludeXrefLayers = true },
            layerDescription: key => key.EndsWith("#2") ? "Groene variant" : "Rode variant",
            descriptionSourceOf: key => key.EndsWith("#2") ? DescriptionSource.EigenKoppeling : null);
        Assert.Equal(2, entries.Count);
        var variant = Assert.Single(entries, e => e.Description == "Groene variant");
        Assert.Equal(DescriptionSource.EigenKoppeling, variant.DescriptionSource);
        Assert.Equal("OUTER|INNER", Assert.Single(variant.SourceLayers).SourceIdentity);
        Assert.Contains(entries, e => e.Description == "Rode variant");
    }

    private static NlcsLayerName WithRender(string parseName, string renderLayerName, string renderIdentity)
    {
        Assert.True(NlcsLayerParser.TryParse(parseName, out var p));
        return new NlcsLayerName
        {
            Raw = p!.Raw,
            LocalName = p.LocalName,
            IsXref = p.IsXref,
            XrefName = p.XrefName,
            StatusCode = p.StatusCode,
            Status = p.Status,
            Discipline = p.Discipline,
            Hoofdgroep = p.Hoofdgroep,
            Element = p.Element,
            TypeSuffix = p.TypeSuffix,
            DrawType = p.DrawType,
            Scale = p.Scale,
            RenderLayerName = renderLayerName,
            RenderIdentity = renderIdentity,
            SourceLayers = new[] { p.Source }
        };
    }
}
