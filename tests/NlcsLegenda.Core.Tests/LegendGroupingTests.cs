using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendGroupingTests
{
    private static IReadOnlyList<LegendEntry> Build(params string[] layers)
    {
        var parsed = new List<NlcsLayerName>();
        foreach (var l in layers)
            if (NlcsLayerParser.TryParse(l, out var p))
                parsed.Add(p);
        return LegendGrouping.Build(parsed, new LegendSettings());
    }

    [Fact]
    public void LineAndHatchOfSameElement_CollapseToOneEntry()
    {
        var entries = Build(
            "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G",
            "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A",
            "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A-200");

        var entry = Assert.Single(entries);
        Assert.True(entry.HasHatch);
        Assert.NotNull(entry.GeometryLayer);
        Assert.Equal("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G", entry.GeometryLayer);
    }

    [Fact]
    public void Entries_AreSortedByStatusThenGroup()
    {
        var entries = Build(
            "V-WE-RI-HWA_RIOOLLEIDING_PVC_160-G",
            "N-WE-VH-OPENVERHARDING_TEGEL-G",
            "B-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G");

        Assert.Equal(NlcsStatus.Nieuw, entries[0].Status);
        Assert.Equal(NlcsStatus.Bestaand, entries[1].Status);
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
    public void SameLogicalLayer_DifferentRenderGroup_SplitsIntoSeparateRows()
    {
        // Zoals de analyse ze maakt bij bronnen met zichtbaar verschillende weergave: dezelfde
        // logische NLCS-laag, maar twee render-partities. Build houdt ze als aparte regels.
        var a = WithRender("N-WE-VH-WEG-G", "N-WE-VH-WEG-G", "0");
        var b = WithRender("N-WE-VH-WEG-G", "N-WE-VH-WEG-G #2", "1");

        var entries = LegendGrouping.Build(new[] { a, b }, new LegendSettings());

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("WEG", e.Element));
    }

    [Fact]
    public void SameLogicalLayer_SameRenderGroup_StaysOneRow()
    {
        var a = WithRender("N-WE-VH-WEG-G", "N-WE-VH-WEG-G", string.Empty);

        Assert.Single(LegendGrouping.Build(new[] { a }, new LegendSettings()));
    }

    private static NlcsLayerName WithRender(string parseName, string localName, string renderGroup)
    {
        Assert.True(NlcsLayerParser.TryParse(parseName, out var p));
        return new NlcsLayerName
        {
            Raw = p!.Raw,
            LocalName = localName,
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
            RenderGroup = renderGroup
        };
    }
}
