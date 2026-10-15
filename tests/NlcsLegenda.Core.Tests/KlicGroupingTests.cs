using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class KlicGroupingTests
{
    private static List<NlcsLayerName> Parse(params string[] layers)
    {
        var list = new List<NlcsLayerName>();
        foreach (var l in layers)
            if (NlcsLayerParser.TryParse(l, out var p))
                list.Add(p!);
        return list;
    }

    [Theory]
    [InlineData("DATA", "DATA", "", "", "")]
    [InlineData("DATA2", "DATA", "", "", "2")]
    [InlineData("ET_MS", "ET", "MS", "", "")]
    [InlineData("ET_LS2", "ET", "LS", "", "2")]
    [InlineData("GAS_LD_MANTELBUIS", "GAS", "LD", "MANTELBUIS", "")]
    [InlineData("DATA_HULPSTUK", "DATA", "", "HULPSTUK", "")]
    [InlineData("OPENVERHARDING_BETONSTRAATSTEEN", "OPENVERHARDING_BETONSTRAATSTEEN", "", "", "")]
    public void From_DecomposesElement(string element, string soort, string spec, string uitv, string num)
    {
        var p = ElementProperties.From(element);
        Assert.Equal(soort, p.Soort);
        Assert.Equal(spec, p.Specificatie);
        Assert.Equal(uitv, p.Uitvoering);
        Assert.Equal(num, p.Nummer);
    }

    private static LegendSettings KlicSettings()
    {
        var s = new LegendSettings();
        s.ExcludedHoofdgroepen.Clear();
        s.ExcludedDisciplines.Clear();
        return s;
    }

    [Theory]
    [InlineData("ET_HS", "ET", "HS", "", "")]
    [InlineData("ET_HS2", "ET", "HS", "", "2")]
    public void HighVoltageIsAnIndependentDimension(string element, string soort, string spec, string uitv, string num)
        => From_DecomposesElement(element, soort, spec, uitv, num);

    [Fact]
    public void KlicDimensionsDoNotMergeRoadObjects()
    {
        var settings = KlicSettings();
        settings.MergedDimensions = Enum.GetValues<GroupDimension>().ToHashSet();
        var result = LegendGrouping.Build(Parse("N-WE-VH-WEG-G", "N-WE-VH-PAD-G", "B-WE-KL-DATA-G", "B-WE-KL-DATA2-G"), settings);
        Assert.Equal(3, result.Count);
        Assert.Single(result, e => e.MergedMembers.Count == 2);
    }

    [Fact]
    public void MergeNummer_CombinesDataCables()
    {
        var layers = Parse("B-WE-KL-DATA-G", "B-WE-KL-DATA2-G", "B-WE-KL-DATA3-G");
        var s = KlicSettings();

        var ungrouped = LegendGrouping.Build(layers, s);
        Assert.Equal(3, ungrouped.Count);

        s.SamenvoegenNummer = true;
        var grouped = LegendGrouping.Build(layers, s);
        var one = Assert.Single(grouped);
        Assert.Equal(3, one.MergedMembers.Count);
    }

    [Fact]
    public void MergeSpecificatie_CombinesVoltageLevels()
    {
        var layers = Parse("B-WE-KL-ET_LS-G", "B-WE-KL-ET_MS-G");
        var s = KlicSettings();
        Assert.Equal(2, LegendGrouping.Build(layers, s).Count);

        s.SamenvoegenSpecificatie = true;
        var grouped = LegendGrouping.Build(layers, s);
        Assert.Single(grouped);
    }

    [Fact]
    public void MergeIsReversible_AndIndependentPerDimension()
    {
        var layers = Parse("B-WE-KL-DATA-G", "B-WE-KL-DATA2-G", "B-WE-KL-ET_LS-G", "B-WE-KL-ET_MS-G");
        var s = KlicSettings();

        s.SamenvoegenNummer = true;
        Assert.Equal(3, LegendGrouping.Build(layers, s).Count);

        s.SamenvoegenNummer = false;
        Assert.Equal(4, LegendGrouping.Build(layers, s).Count);
    }

    [Fact]
    public void Merge_SumsQuantities()
    {
        var layers = Parse("B-WE-KL-DATA-G", "B-WE-KL-DATA2-G");
        var metrics = new Dictionary<string, LayerMetric>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["B-WE-KL-DATA-G"] = new LayerMetric(0, 100.0, 0),
            ["B-WE-KL-DATA2-G"] = new LayerMetric(0, 50.0, 0)
        };
        var s = KlicSettings();
        s.SamenvoegenNummer = true;
        s.IncludeQuantities = true;

        var one = Assert.Single(LegendGrouping.Build(layers, s, metrics: metrics));
        Assert.Equal(150.0, one.Metric.Length, 3);
    }

    [Fact]
    public void MergedDimensions_SurviveClone()
    {
        var s = new LegendSettings();
        s.SamenvoegenNummer = true;
        s.SamenvoegenSpecificatie = true;
        var clone = s.Clone();
        Assert.True(clone.SamenvoegenNummer);
        Assert.True(clone.SamenvoegenSpecificatie);
        clone.SamenvoegenNummer = false;
        Assert.True(s.SamenvoegenNummer);
    }

    [Fact]
    public void MergedDimensions_RoundTripJson()
    {
        var s = new LegendSettings();
        s.SamenvoegenNummer = true;
        Assert.True(LegendSettings.TryParse(s.ToJson(), out var back));
        Assert.True(back!.SamenvoegenNummer);
    }

    [Fact]
    public void MergeStatuses_CombinesOnlyWhenRenderIdentical()
    {
        var layers = Parse("N-WE-KL-DATA-G", "B-WE-KL-DATA-G");
        var s = KlicSettings();
        s.MergeIdenticalStatuses = true;

        var same = LegendGrouping.Build(layers, s, renderIdentity: _ => "kleur3");
        Assert.Single(same);

        var diff = LegendGrouping.Build(layers, s,
            renderIdentity: l => l.StartsWith("N-") ? "groen" : "grijs");
        Assert.Equal(2, diff.Count);
    }

    [Fact]
    public void MergeStatuses_Off_KeepsStatusesSeparate()
    {
        var layers = Parse("N-WE-KL-DATA-G", "B-WE-KL-DATA-G");
        var s = KlicSettings();
        s.MergeIdenticalStatuses = false;
        Assert.Equal(2, LegendGrouping.Build(layers, s, renderIdentity: _ => "kleur3").Count);
    }

    [Fact]
    public void MergeStatuses_NoRenderInfo_NeverMerges()
    {
        var layers = Parse("N-WE-KL-DATA-G", "B-WE-KL-DATA-G");
        var s = KlicSettings();
        s.MergeIdenticalStatuses = true;
        Assert.Equal(2, LegendGrouping.Build(layers, s).Count);
    }
}
