using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class MergedValuesTests
{
    private static NlcsLayerName Layer(string name, string source = "")
    {
        Assert.True(NlcsLayerParser.TryParse((source.Length > 0 ? source + "|" : "") + name, out var parsed));
        return parsed!;
    }
    [Fact]
    public void NamedKlicSourceUsesKlicDiameterChoiceWithoutObjectData()
    {
        var layers = new[] { Layer("B-WE-RI-VRIJVERVAL_200-G", "SIT-BS-KLIC"), Layer("B-WE-RI-VRIJVERVAL_300-G", "SIT-BS-KLIC") };
        var settings = new LegendSettings { IncludeXrefLayers = true };
        var entry = Assert.Single(LegendGrouping.Build(layers, settings));
        Assert.True(entry.IsKlic);
        Assert.Equal("Vrijvervalriool (200, 300)", entry.DisplayDescription);
        Assert.Equal(2, entry.ExclusionKeys.Count);
        settings.MergedDimensions.Remove(GroupDimension.Diameter);
        Assert.Equal(2, LegendGrouping.Build(layers, settings).Count);
    }
    [Theory]
    [InlineData(true, "Leiding (Dorp, Stad)")]
    [InlineData(false, "Leiding")]
    public void DescriptiveVariantsCanBeShownOrHidden(bool show, string expected)
    {
        var layers = new[] { Layer("N-WE-KL-LEIDING_DORP-G"), Layer("N-WE-KL-LEIDING_STAD-G") };
        var settings = new LegendSettings { ShowMergedValues = show }; settings.MergedDimensions.Add(GroupDimension.Soort);
        var entry = Assert.Single(LegendGrouping.Build(layers, settings, name => name.Contains("DORP") ? "Leiding Dorp" : "Leiding Stad"));
        Assert.Equal(expected, entry.DisplayDescription);
    }
    [Theory]
    [InlineData(true, "Leiding (200, 300)")]
    [InlineData(false, "Leiding")]
    public void ManualGroupsKeepTheMainAppearanceAndAllValues(bool show, string expected)
    {
        var entries = new[] { new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "RI", Element = "A", Description = "Leiding 200", RenderIdentity = "rood" },
            new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "RI", Element = "B", Description = "Leiding 300", RenderIdentity = "groen" } };
        var merge = ManualMergeGroup.Create(entries, entries[0]);
        var result = Assert.Single(ManualMergeGroup.Apply(entries.ToList(), new[] { merge }, show));
        Assert.Equal(expected, result.DisplayDescription); Assert.Equal("rood", result.RenderIdentity); Assert.Equal(2, result.ExclusionKeys.Count);
    }
    [Fact]
    public void EqualLayerNamesStillShowDifferentObjectDataDiameters()
    {
        var layer = Layer("B-WE-KL-WATER-G");
        var data = new[] { "0.2", "0.3" }.Select((diameter, index) => new KlicObjectData("klic.dwg", index.ToString(), "", new[] {
            new KlicRecord(KlicSource.InfraCAD, "Leiding", new Dictionary<string,string> { ["DiameterM"] = diameter }) }, Array.Empty<string>(), KlicSource.InfraCAD)).ToArray();
        var settings = new LegendSettings();
        var entry = Assert.Single(LegendGrouping.Build(new[] { layer }, settings, _ => "Waterleiding", klicObjects: _ => data));
        Assert.Equal("Waterleiding (200, 300)", entry.DisplayDescription);
        settings.ShowMergedValues = false;
        Assert.Equal("Waterleiding", Assert.Single(LegendGrouping.Build(new[] { layer }, settings, _ => "Waterleiding", klicObjects: _ => data)).DisplayDescription);
    }
    [Fact]
    public void DiameterDisplayRemovesFloatingPointNoiseWithoutChangingTheSource()
    {
        var raw = new[] { "0.118", "0.11800000000000001", "0.222" };
        var data = raw.Select((diameter, index) => new KlicObjectData("klic.dwg", index.ToString(), "", new[] {
            new KlicRecord(KlicSource.InfraCAD, "Leiding", new Dictionary<string,string> { ["DiameterM"] = diameter }) },
            Array.Empty<string>(), KlicSource.InfraCAD)).ToArray();
        var entry = Assert.Single(LegendGrouping.Build(new[] { Layer("B-WE-KL-WATER-G") }, new(),
            _ => "Waterleiding", klicObjects: _ => data));
        Assert.Equal("Waterleiding (118, 222)", entry.DisplayDescription);
        Assert.Equal(raw, data.SelectMany(o => o.Values("DiameterM")));
    }
}
