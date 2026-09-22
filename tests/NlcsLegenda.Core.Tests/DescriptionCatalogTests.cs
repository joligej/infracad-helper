using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class DescriptionCatalogTests
{
    private static NlcsLayerName Layer(string raw)
    {
        NlcsLayerParser.TryParse(raw, out var p);
        return p!;
    }

    [Fact]
    public void Default_UsesSpecificPartWithoutGeneral()
    {
        var catalog = DescriptionCatalog.Default();
        var layer = Layer("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A");

        Assert.Equal("Betonstraatsteen", catalog.Describe(layer, includeGeneral: false));
        Assert.Equal("Verharding - Betonstraatsteen", catalog.Describe(layer, includeGeneral: true));
    }

    [Fact]
    public void Merge_OverridesEntries()
    {
        var catalog = DescriptionCatalog.Default();
        var extra = new DescriptionCatalog();
        extra.Elementen["VH|OPENVERHARDING_BETONSTRAATSTEEN"] =
            new DescriptionEntry { Algemeen = "Bestrating", Specifiek = "Klinkers" };
        catalog.MergeFrom(extra);

        var layer = Layer("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A");
        Assert.Equal("Klinkers", catalog.Describe(layer, false));
        Assert.Equal("Bestrating - Klinkers", catalog.Describe(layer, true));
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nlcs_desc_{Guid.NewGuid():N}.json");
        try
        {
            var catalog = new DescriptionCatalog();
            catalog.Elementen["VH|OPENVERHARDING_TEGEL"] =
                new DescriptionEntry { Algemeen = "Verharding", Specifiek = "Tegel 30x30" };
            catalog.Save(path);

            var loaded = DescriptionCatalog.Load(path);
            Assert.True(loaded.Elementen.ContainsKey("VH|OPENVERHARDING_TEGEL"));
            Assert.Equal("Tegel 30x30", loaded.Elementen["VH|OPENVERHARDING_TEGEL"].Specifiek);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Diff_KeepsOnlyChangedOrNewEntries()
    {
        var baseline = DescriptionCatalog.Default();
        var edited = DescriptionCatalog.Default();
        edited.Elementen["VH|OPENVERHARDING_BETONSTRAATSTEEN"] =
            new DescriptionEntry { Algemeen = "Verharding", Specifiek = "Klinkers keiformaat" };
        edited.Elementen["XX|EIGEN_ELEMENT"] =
            new DescriptionEntry { Specifiek = "Zelfbedacht" };

        var diff = edited.Diff(baseline);

        Assert.Equal(2, diff.Elementen.Count);
        Assert.True(diff.Elementen.ContainsKey("VH|OPENVERHARDING_BETONSTRAATSTEEN"));
        Assert.True(diff.Elementen.ContainsKey("XX|EIGEN_ELEMENT"));
    }

    [Fact]
    public void Diff_IsEmptyWhenNothingChanged()
    {
        var diff = DescriptionCatalog.Default().Diff(DescriptionCatalog.Default());
        Assert.Empty(diff.Elementen);
    }
}
