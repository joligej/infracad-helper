using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class DrawtypeMatrixTests
{
    private const string Base = "N-WE-VH-GESLOTENVERHARDING_ASFALT";

    private static LegendEntry Single(params string[] suffixes)
    {
        var layers = new List<NlcsLayerName>();
        foreach (var s in suffixes)
        {
            Assert.True(NlcsLayerParser.TryParse($"{Base}-{s}", out var p), "Ongeldige testlaag");
            layers.Add(p!);
        }
        var entries = LegendGrouping.Build(layers, new LegendSettings());
        return Assert.Single(entries);
    }

    [Theory]
    [InlineData("G", NlcsDrawType.Geometrie)]
    [InlineData("GD", NlcsDrawType.Geometrie)]
    [InlineData("GS", NlcsDrawType.Geometrie)]
    [InlineData("GV", NlcsDrawType.Vlak)]
    [InlineData("A", NlcsDrawType.Arcering)]
    [InlineData("V", NlcsDrawType.Vlakvulling)]
    [InlineData("S", NlcsDrawType.Symbool)]
    public void EnkeleSoort_LevertJuisteType(string suffix, NlcsDrawType expected)
    {
        var entry = Single(suffix);
        Assert.True(entry.LayersByType.ContainsKey(expected));
        Assert.Single(entry.LayersByType);
    }

    [Theory]
    [InlineData("G", "A")]
    [InlineData("G", "V")]
    [InlineData("G", "GV")]
    [InlineData("G", "S")]
    public void Combinatie_HoudtSoortenEnGeometrieGescheiden(string a, string b)
    {
        var parsed = new[] { a, b }.Select(suffix => { Assert.True(NlcsLayerParser.TryParse($"{Base}-{suffix}", out var layer)); return layer!; });
        var entries = LegendGrouping.Build(parsed, new LegendSettings());
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Single(entry.LayersByType));
        Assert.Contains(entries, e => e.GeometryLayer == $"{Base}-G");
        Assert.Contains(entries, e => e.LayersByType.ContainsKey(NlcsDrawTypeExtensions.FromSuffix(b)));
    }

    [Fact]
    public void DrieSoorten_GAS_KrijgenAfzonderlijkeRegels()
    {
        var parsed = new[] { "G", "A", "S" }.Select(suffix => { Assert.True(NlcsLayerParser.TryParse($"{Base}-{suffix}", out var layer)); return layer!; });
        var entries = LegendGrouping.Build(parsed, new LegendSettings());
        Assert.Equal(3, entries.Count);
        Assert.All(entries, entry => Assert.Single(entry.LayersByType));
    }
}
