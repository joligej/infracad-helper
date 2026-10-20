using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class KlicProviderSuffixTests
{
    [Theory]
    [InlineData("DATA_BT NEDERLAND NV", "Bt Nederland N.V.", "DATA", "", "")]
    [InlineData("ET_LS2_LIANDER NV PAC 2AI68G2", "Liander N.V. (PAC 2AI68G2)", "ET", "LS", "")]
    [InlineData("GAS_LD_HULPSTUK_LIANDER_NV", "Liander N.V.", "GAS", "LD", "HULPSTUK")]
    [InlineData("WATER_VITENS", "", "WATER_VITENS", "", "")]
    [InlineData("WATER_VITENS", "Liander", "WATER_VITENS", "", "")]
    [InlineData("WATERVITENS", "Vitens", "WATERVITENS", "", "")]
    [InlineData("WATER_VITENS_EXTRA", "Vitens", "WATER_VITENS_EXTRA", "", "")]
    public void KnownProviderDoesNotBecomePartOfTheType(string element, string provider, string type, string spec, string execution)
    {
        var props = ElementProperties.From(element, provider);
        Assert.Equal(type, props.Soort);
        Assert.Equal(spec, props.Specificatie);
        Assert.Equal(execution, props.Uitvoering);
    }

    private static NlcsLayerName Layer(string element, string provider, string appearance = "groen") => new()
    {
        Raw = "B-WE-KL-" + element + "-G", LocalName = "B-WE-KL-" + element + "-G",
        StatusCode = "B", Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "KL",
        Element = element, TypeSuffix = "G", DrawType = NlcsDrawType.Geometrie,
        Netbeheerder = provider, RenderIdentity = appearance
    };

    [Fact]
    public void ImportedProviderSuffixesMergeReversiblyWithoutLosingSourcesOrQuantities()
    {
        var layers = new[] { Layer("DATA_BT NEDERLAND NV", "Bt Nederland N.V."), Layer("DATA_KPN BV", "KPN B.V.") };
        var metrics = layers.ToDictionary(l => l.LocalName, _ => new LayerMetric(1, 5, 0));
        var settings = new LegendSettings();
        var entry = Assert.Single(LegendGrouping.Build(layers, settings, metrics: metrics));
        Assert.Equal(2, entry.SourceLayers.Count);
        Assert.Equal(2, entry.Netbeheerders.Count);
        Assert.Equal(10, entry.Metric.Length);
        Assert.Equal("Datakabel (Bt Nederland N.V., KPN B.V.)", entry.DisplayDescription);
        settings.MergedDimensions.Remove(GroupDimension.Netbeheerder);
        Assert.Equal(2, LegendGrouping.Build(layers, settings, metrics: metrics).Count);
        settings.MergedDimensions.Add(GroupDimension.Netbeheerder);
        Assert.Single(LegendGrouping.Build(layers, settings, metrics: metrics));
    }

    [Fact]
    public void TypesAppearancesAndUserTextRemainDistinct()
    {
        var layers = new[] { Layer("GAS_LD_LIANDER NV", "Liander N.V."), Layer("WATER_VITENS", "Vitens"), Layer("WATER_VITENS", "Vitens", "blauw") };
        Assert.Equal(3, LegendGrouping.Build(layers, new LegendSettings()).Count);
        var custom = LegendGrouping.Build(new[] { layers[0] }, new LegendSettings(),
            layerDescription: _ => "Speciaal voor Liander NV", descriptionSourceOf: _ => DescriptionSource.EigenKoppeling);
        Assert.Equal("Speciaal voor Liander NV (Liander N.V.)", Assert.Single(custom).DisplayDescription);
    }

    [Fact]
    public void SpecificationAndExecutionRemainIndependentOfProvider()
    {
        var layers = new[] { Layer("ET_LS_LIANDER NV", "Liander N.V."), Layer("ET_MS_KPN BV", "KPN B.V.") };
        var settings = new LegendSettings { MergedDimensions = new() { GroupDimension.Netbeheerder } };
        Assert.Equal(2, LegendGrouping.Build(layers, settings).Count);
        settings.MergedDimensions.Add(GroupDimension.Specificatie);
        Assert.Single(LegendGrouping.Build(layers, settings));
        settings.MergedDimensions.Remove(GroupDimension.Netbeheerder);
        Assert.Equal(2, LegendGrouping.Build(layers, settings).Count);
    }
}
