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
            if (NlcsLayerParser.TryParse($"{Base}-{s}", out var p))
                layers.Add(p!);
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
    public void Combinatie_BevatBeideSoorten_EnGeometrieBlijftLijn(string a, string b)
    {
        var entry = Single(a, b);
        Assert.Equal(NlcsDrawType.Geometrie, NlcsDrawTypeExtensions.FromSuffix(a));
        Assert.True(entry.LayersByType.ContainsKey(NlcsDrawType.Geometrie),
            "G moet als lijn aanwezig blijven naast de andere soort");
        Assert.True(entry.LayersByType.ContainsKey(NlcsDrawTypeExtensions.FromSuffix(b)),
            $"tweede soort ({b}) ontbreekt in LayersByType");
        Assert.Equal(2, entry.LayersByType.Count);
    }

    [Fact]
    public void DrieSoorten_GAS_VallenSamenInEenRegel()
    {
        var entry = Single("G", "A", "S");
        Assert.True(entry.LayersByType.ContainsKey(NlcsDrawType.Geometrie));
        Assert.True(entry.LayersByType.ContainsKey(NlcsDrawType.Arcering));
        Assert.True(entry.LayersByType.ContainsKey(NlcsDrawType.Symbool));
        Assert.Equal(3, entry.LayersByType.Count);
    }
}
