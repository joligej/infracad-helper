using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class DiameterGroupingTests
{
    [Theory]
    [InlineData("VRIJVERVAL_200_GEMEENTE HILVERSUM", "Gemeente Hilversum", "VRIJVERVAL", "200", "")]
    [InlineData("VRIJVERVAL_1250", "", "VRIJVERVAL", "1250", "")]
    [InlineData("WATER_DN_200", "", "WATER", "200", "")]
    [InlineData("WATER_Ø_200", "", "WATER", "200", "")]
    [InlineData("WATER_DN200", "", "WATER", "200", "")]
    [InlineData("WATER_Ø200", "", "WATER", "200", "")]
    [InlineData("VRIJVERVAL_0.2M", "", "VRIJVERVAL", "200", "")]
    [InlineData("VRIJVERVAL_20CM", "", "VRIJVERVAL", "200", "")]
    [InlineData("VRIJVERVAL_200_HULPSTUK", "", "VRIJVERVAL", "200", "")]
    [InlineData("VRIJVERVAL_200_BETON", "", "VRIJVERVAL_BETON", "200", "")]
    [InlineData("DATA2", "", "DATA", "", "2")]
    [InlineData("WEG_200", "", "WEG_200", "", "")]
    [InlineData("VRIJVERVAL_0", "", "VRIJVERVAL_0", "", "")]
    public void DiameterIsSeparateFromTypeAndSequence(string element, string provider, string type, string diameter, string number)
    {
        var properties = ElementProperties.From(element, provider);
        Assert.Equal(type, properties.Soort);
        Assert.Equal(diameter, properties.Diameter);
        Assert.Equal(number, properties.Nummer);
    }

    private static NlcsLayerName Layer(string element, string provider = "", string appearance = "210:RI-OVERIG-SO", string metadataGroup = "") => new()
    {
        Raw = "SIT-BS-KLIC|B-WE-RI-" + element + "-G", LocalName = "B-WE-RI-" + element + "-G",
        StatusCode = "B", Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "RI",
        Element = element, TypeSuffix = "G", DrawType = NlcsDrawType.Geometrie,
        IsXref = true, XrefName = "SIT-BS-KLIC", Netbeheerder = provider,
        RenderIdentity = appearance, KlicGroupKey = metadataGroup
    };
    private static IReadOnlyList<KlicObjectData> Metadata(string layer) => new[] {
        new KlicObjectData("bron.dwg", layer, "SIT-BS-KLIC", new[] {
            new KlicRecord(KlicSource.InfraCAD, "Vrijvervalriool", new Dictionary<string, string> {
                ["Netbeheerder"] = "Gemeente Hilversum", ["Thema"] = "Riool vrijverval" })
        }, Array.Empty<string>(), KlicSource.InfraCAD)
    };

    [Fact]
    public void HilversumSewersMergeReversiblyWithAllSourcesAndQuantities()
    {
        var diameters = new[] { 125, 160, 200, 250, 300, 315, 400, 500, 600, 800, 1000, 1250 };
        var layers = diameters.Select(d => Layer($"VRIJVERVAL_{d}_GEMEENTE HILVERSUM", "Gemeente Hilversum")).ToArray();
        var metrics = layers.ToDictionary(l => l.LocalName, _ => new LayerMetric(1, 10, 0));
        var settings = new LegendSettings { IncludeXrefLayers = true };
        var entry = Assert.Single(LegendGrouping.Build(layers, settings, metrics: metrics, klicObjects: Metadata));
        Assert.True(entry.IsKlic);
        Assert.Equal(120, entry.Metric.Length);
        Assert.Equal(12, entry.SourceLayers.Count);
        Assert.Equal(12, entry.SourceRenderLayers.Count);
        Assert.Equal("Vrijvervalriool (Gemeente Hilversum)", entry.DisplayDescription);
        Assert.Equal(12, entry.KlicObjects.Count);
        Assert.DoesNotContain("125", entry.DisplayDescription);
        Assert.Contains("Gemeente Hilversum", entry.DisplayDescription);
        settings.MergedDimensions.Remove(GroupDimension.Diameter);
        Assert.Equal(12, LegendGrouping.Build(layers, settings, klicObjects: Metadata).Count);
        settings.MergedDimensions.Add(GroupDimension.Diameter);
        Assert.Single(LegendGrouping.Build(layers, settings, klicObjects: Metadata));
    }

    [Fact]
    public void OrdinarySewersHaveAnIndependentDiameterChoice()
    {
        var layers = new[] { Layer("VRIJVERVAL_200"), Layer("VRIJVERVAL_300") };
        var settings = new LegendSettings { IncludeXrefLayers = true };
        Assert.Equal(2, LegendGrouping.Build(layers, settings).Count);
        settings.NonKlicMergedDimensions.Add(GroupDimension.Nummer);
        Assert.Equal(2, LegendGrouping.Build(layers, settings).Count);
        settings.NonKlicMergedDimensions.Add(GroupDimension.Diameter);
        Assert.Single(LegendGrouping.Build(layers, settings));
        Assert.True(LegendSettings.TryParse(settings.ToJson(), out var saved));
        saved!.NonKlicMergedDimensions.Remove(GroupDimension.Diameter);
        Assert.Equal(2, LegendGrouping.Build(layers, saved).Count);
        Assert.Contains(GroupDimension.Diameter, settings.Clone().NonKlicMergedDimensions);
    }

    [Fact]
    public void AppearanceTypeMaterialAndExplicitMetadataPartitionsStayDistinct()
    {
        var layers = new[] { Layer("VRIJVERVAL_200"), Layer("VRIJVERVAL_300"),
            Layer("VRIJVERVAL_400", appearance: "rood:continuous"), Layer("DRUKRIOOL_200"),
            Layer("VRIJVERVAL_200_BETON"), Layer("VRIJVERVAL_200_PVC"),
            Layer("VRIJVERVAL_200", metadataGroup: "diameter=200"), Layer("VRIJVERVAL_300", metadataGroup: "diameter=300") };
        Assert.Equal(7, LegendGrouping.Build(layers, new LegendSettings { IncludeXrefLayers = true }, klicObjects: Metadata).Count);
    }

    [Fact]
    public void KlicAndOrdinarySewersCannotAccidentallyMerge()
    {
        var layers = new[] { Layer("VRIJVERVAL_200"), Layer("VRIJVERVAL_300") };
        var settings = new LegendSettings { IncludeXrefLayers = true };
        settings.NonKlicMergedDimensions.Add(GroupDimension.Diameter);
        Assert.Equal(2, LegendGrouping.Build(layers, settings,
            klicObjects: name => name.Contains("200") ? Metadata(name) : Array.Empty<KlicObjectData>()).Count);
    }

    [Fact]
    public void KnownProvidersAndExecutionRemainIndependent()
    {
        var layers = new[] { Layer("VRIJVERVAL_200_GEMEENTE HILVERSUM", "Gemeente Hilversum"),
            Layer("VRIJVERVAL_300_PRORAIL", "ProRail"), Layer("VRIJVERVAL_400_HULPSTUK_PRORAIL", "ProRail") };
        var settings = new LegendSettings { IncludeXrefLayers = true };
        Assert.Single(LegendGrouping.Build(layers, settings, klicObjects: Metadata));
        settings.MergedDimensions.Remove(GroupDimension.Netbeheerder);
        Assert.Equal(2, LegendGrouping.Build(layers, settings, klicObjects: Metadata).Count);
        settings.MergedDimensions.Remove(GroupDimension.Uitvoering);
        Assert.Equal(3, LegendGrouping.Build(layers, settings, klicObjects: Metadata).Count);
    }
}
