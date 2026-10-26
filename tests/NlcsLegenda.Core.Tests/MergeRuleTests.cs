using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class MergeRuleTests
{
    private static NlcsLayerName Layer(string element, string suffix = "G", string group = "KL", string metadata = "")
    {
        Assert.True(NlcsLayerParser.TryParse($"B-WE-{group}-{element}-{suffix}", out var layer));
        return new NlcsLayerName {
            Raw = layer!.Raw, LocalName = layer.LocalName, Status = layer.Status, StatusCode = layer.StatusCode,
            Discipline = layer.Discipline, Hoofdgroep = layer.Hoofdgroep, Element = layer.Element,
            TypeSuffix = layer.TypeSuffix, DrawType = layer.DrawType, KlicGroupKey = metadata
        };
    }

    [Theory]
    [InlineData("HWA_RIOOLLEIDING_PVC_160", "HWA_RIOOLLEIDING", "PVC", "160")]
    [InlineData("DWA_RIOOLLEIDING_BETON_300", "DWA_RIOOLLEIDING", "BETON", "300")]
    [InlineData("GAS_LD_MANTELBUIS_STAAL_200", "GAS", "STAAL", "200")]
    [InlineData("DATA_MANTELBUIS_POLYETHYLEEN PE_110", "DATA", "POLYETHYLEEN PE", "110")]
    [InlineData("BESCHOEIING_STAAL", "BESCHOEIING", "STAAL", "")]
    [InlineData("WEG_200", "WEG_200", "", "")]
    [InlineData("BOOM_20", "BOOM_20", "", "")]
    public void MaterialsAndPipeDiametersHaveIndependentMeaning(string element, string kind, string material, string diameter)
    {
        var p = ElementProperties.From(element);
        Assert.Equal(kind, p.Soort);
        Assert.Equal(material, p.Materiaal);
        Assert.Equal(diameter, p.Diameter);
    }

    [Theory]
    [InlineData("G", NlcsDrawType.Geometrie)]
    [InlineData("GV", NlcsDrawType.Vlak)]
    [InlineData("A", NlcsDrawType.Arcering)]
    [InlineData("V", NlcsDrawType.Vlakvulling)]
    [InlineData("S", NlcsDrawType.Symbool)]
    public void TypeOverridesKeepOtherTypesAndSourcesIndependent(string suffix, NlcsDrawType type)
    {
        var settings = new LegendSettings();
        var layers = new[] { Layer("DATA", suffix), Layer("DATA2", suffix),
            Layer("DATA", suffix, "RI"), Layer("DATA2", suffix, "RI") };
        Assert.Equal(3, LegendGrouping.Build(layers, settings).Count);
        settings.SetMergeDimension(true, type, GroupDimension.Nummer, false);
        Assert.Equal(4, LegendGrouping.Build(layers, settings).Count);
        settings.SetMergeDimension(false, type, GroupDimension.Nummer, true);
        Assert.Equal(3, LegendGrouping.Build(layers, settings).Count);
        settings.ResetMergeDimensions(true, type);
        Assert.Equal(2, LegendGrouping.Build(layers, settings).Count);
        settings.ResetMergeDimensions(false, type);
        Assert.Equal(3, LegendGrouping.Build(layers, settings).Count);
        Assert.Empty(settings.MergeRules);
    }

    [Fact]
    public void LineChoicesDoNotChangeSymbols()
    {
        var settings = new LegendSettings();
        settings.SetMergeDimension(true, NlcsDrawType.Geometrie, GroupDimension.Nummer, false);
        var entries = LegendGrouping.Build(new[] { Layer("DATA"), Layer("DATA2"), Layer("DATA", "S"), Layer("DATA2", "S") }, settings);
        Assert.Equal(3, entries.Count);
        Assert.Single(entries, e => e.SymbolLayer is not null);
    }

    [Fact]
    public void RulesSurviveRoundtripAndAreCopiedIndependently()
    {
        var settings = new LegendSettings { MergeLineScales = true, MergeHatchAngles = true, MergeHatchScales = true };
        settings.SetMergeDimension(false, NlcsDrawType.Arcering, GroupDimension.Materiaal, true);
        Assert.True(LegendSettings.TryParse(settings.ToJson(), out var stored));
        Assert.True(stored!.MergeLineScales && stored.MergeHatchAngles && stored.MergeHatchScales);
        Assert.Contains(GroupDimension.Materiaal, stored.MergeDimensionsFor(false, NlcsDrawType.Arcering));
        var copy = stored.Clone();
        copy.MergeRules[0].Dimensions.Clear();
        Assert.NotEmpty(stored.MergeRules[0].Dimensions);
        copy.ResetFormattingToTemplate();
        Assert.Single(copy.MergeRules);
    }

    [Fact]
    public void MaterialsMergeWithoutCombiningDifferentPipeTypesOrAppearances()
    {
        var settings = new LegendSettings();
        var layers = new[] { Layer("GAS_PVC_160"), Layer("GAS_STAAL_200"), Layer("WATER_PVC_160") };
        Assert.Equal(2, LegendGrouping.Build(layers, settings, renderIdentity: _ => "same").Count);
        var gas = LegendGrouping.Build(layers, settings, renderIdentity: _ => "same").Single(e => e.Element.StartsWith("GAS"));
        Assert.Contains("PVC", gas.MergedValues, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("STAAL", gas.MergedValues, StringComparer.OrdinalIgnoreCase);
        settings.MergedDimensions.Remove(GroupDimension.Materiaal);
        Assert.Equal(3, LegendGrouping.Build(layers, settings, renderIdentity: _ => "same").Count);
        settings.MergedDimensions.Add(GroupDimension.Materiaal);
        Assert.Equal(3, LegendGrouping.Build(layers, settings, renderIdentity: name => name.Contains("STAAL") ? "red" : "blue").Count);
    }

    private static KlicObjectData Data(string id, string material, string diameter) => new("source.dwg", id, "", new[] {
        new KlicRecord(KlicSource.InfraCAD, "leiding", new Dictionary<string, string> {
            ["Materiaal"] = material, ["DiameterM"] = diameter
        })
    }, Array.Empty<string>(), KlicSource.InfraCAD);

    [Fact]
    public void AttachedMaterialAndDiameterRespectTypeRulesAndKeepQuantities()
    {
        var settings = new LegendSettings();
        var data = new[] { Data("A", "PE", "0.2"), Data("B", "PVC", "0.3") };
        var layers = data.Select((d, i) => Layer("WATER" + i, metadata: d.GroupKey(settings.KlicGroupFields()))).ToArray();
        var objects = layers.Select((l, i) => (l.LocalName, data[i])).ToDictionary(v => v.LocalName, v => v.Item2);
        var metrics = layers.ToDictionary(l => l.LocalName, _ => new LayerMetric(1, 10, 0));
        IReadOnlyList<LegendEntry> Build() => LegendGrouping.Build(layers, settings, metrics: metrics,
            klicObjects: name => new[] { objects[name] });
        var entry = Assert.Single(Build());
        Assert.Equal(20, entry.Metric.Length);
        Assert.Contains("PVC", entry.MergedValues);
        Assert.Contains("200", entry.MergedValues);
        settings.SetMergeDimension(true, NlcsDrawType.Geometrie, GroupDimension.Materiaal, false);
        Assert.Equal(2, Build().Count);
        settings.SetMergeDimension(true, NlcsDrawType.Geometrie, GroupDimension.Materiaal, true);
        settings.SetMergeDimension(true, NlcsDrawType.Geometrie, GroupDimension.Diameter, false);
        Assert.Equal(2, Build().Count);
        settings.SetMergeDimension(true, NlcsDrawType.Geometrie, GroupDimension.Diameter, true);
        Assert.Single(Build());
    }

    [Fact]
    public void OldMaterialPartitionsBecomeVisibleCentralChoices()
    {
        var settings = new LegendSettings();
        settings.Klic.GroupFields.Add("Materiaal");
        Assert.True(LegendSettings.TryParse(settings.ToJson(), out var stored));
        Assert.DoesNotContain("Materiaal", stored!.Klic.GroupFields);
        Assert.DoesNotContain(GroupDimension.Materiaal, stored.MergedDimensions);
        stored.SetMergeDimension(true, null, GroupDimension.Materiaal, true);
        Assert.Contains(GroupDimension.Materiaal, stored.MergedDimensions);
    }

    [Fact]
    public void AddedMaterialMetadataDoesNotLoseOldExclusionOrOrder()
    {
        var settings = new LegendSettings();
        var data = Data("A", "PE", "0.2");
        var old = Layer("WATER", metadata: data.GroupKey(new[] { "DiameterM" }));
        var entry = Assert.Single(LegendGrouping.Build(new[] { old }, settings, renderIdentity: _ => "blue", selectionIdentity: _ => "blue"));
        var key = LegendSettings.SelectionKey(entry);
        settings.ExcludedEntries.Add(key);
        var current = Layer("WATER", metadata: data.GroupKey(settings.KlicGroupFields()));
        Assert.Empty(LegendGrouping.Build(new[] { current }, settings, renderIdentity: _ => "blue", selectionIdentity: _ => "blue"));
        settings.ExcludedEntries.Clear();
        var updated = Assert.Single(LegendGrouping.Build(new[] { current }, settings, renderIdentity: _ => "blue", selectionIdentity: _ => "blue"));
        var choices = new Dictionary<string, string> { [LegendOrder.Key(entry)] = "keuze" };
        var aligned = LegendOrder.AlignChoices(choices, new[] { updated });
        Assert.Equal("keuze", aligned[LegendOrder.Key(updated)]);
        Assert.Equal(new[] { LegendOrder.Key(updated) }, LegendOrder.Remember(choices.Keys.ToArray(), new[] { updated }));
        var other = Layer("WATER", metadata: Data("B", "PVC", "0.2").GroupKey(settings.KlicGroupFields()));
        settings.MergedDimensions.Remove(GroupDimension.Materiaal);
        var split = LegendGrouping.Build(new[] { current, other }, settings, renderIdentity: _ => "blue", selectionIdentity: _ => "blue");
        var ambiguous = LegendOrder.AlignChoices(choices, split);
        Assert.DoesNotContain(split, e => ambiguous.ContainsKey(LegendOrder.Key(e)));
    }

    [Fact]
    public void StatusesStillRequireActualIdenticalAppearance()
    {
        Assert.True(NlcsLayerParser.TryParse("N-WE-KL-DATA-G", out var a));
        Assert.True(NlcsLayerParser.TryParse("B-WE-KL-DATA-G", out var b));
        var settings = new LegendSettings { MergeIdenticalStatuses = true, MergeLineScales = true };
        Assert.Equal(2, LegendGrouping.Build(new[] { a!, b! }, settings, renderIdentity: _ => "group",
            fullRenderIdentity: name => name.StartsWith("N-") ? "scale1" : "scale2").Count);
        Assert.Single(LegendGrouping.Build(new[] { a!, b! }, settings, renderIdentity: _ => "group", fullRenderIdentity: _ => "same"));
    }

    [Fact]
    public void InvalidAndDuplicateRulesAreNormalized()
    {
        var settings = new LegendSettings { MergeRules = new() {
            new() { Type = (NlcsDrawType)999, Dimensions = new() { GroupDimension.Soort } },
            new() { Type = NlcsDrawType.Geometrie, Dimensions = new() { GroupDimension.Soort } },
            new() { Type = NlcsDrawType.Geometrie, Dimensions = new() { GroupDimension.Materiaal, GroupDimension.Materiaal, (GroupDimension)999 } }
        }};
        settings.Normalize();
        Assert.Equal(new[] { GroupDimension.Materiaal }, Assert.Single(settings.MergeRules).Dimensions);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IgnoredAppearanceValuesAreOptionalWithoutChangingCounts(bool show)
    {
        var settings = new LegendSettings { MergeLineScales = true, ShowMergedValues = show };
        var layers = new[] { Layer("DATA"), Layer("DATA2") };
        var entry = Assert.Single(LegendGrouping.Build(layers, settings, renderIdentity: _ => "same",
            appearanceValues: name => new[] { new AppearanceValue("lijnschaal", name.Contains("DATA2") ? 2 : 1) }));
        Assert.Contains("lijnschaal 1 / 2", entry.MergedValues);
        Assert.Equal(show, entry.DisplayDescription.Contains("lijnschaal 1 / 2"));
        Assert.Equal(2, entry.AppearanceValues.Count);
        var unchanged = Assert.Single(LegendGrouping.Build(layers, settings, renderIdentity: _ => "same",
            appearanceValues: _ => new[] { new AppearanceValue("lijnschaal", 1) }));
        Assert.DoesNotContain(unchanged.MergedValues, v => v.StartsWith("lijnschaal"));
    }

    [Fact]
    public void StatusMergingDoesNotUndoOtherMergeChoices()
    {
        var layers = new[] { "B-WE-KL-DATA-G", "B-WE-KL-DATA2-G", "N-WE-KL-DATA-G", "N-WE-KL-DATA2-G" }
            .Select(name => { Assert.True(NlcsLayerParser.TryParse(name, out var layer)); return layer!; }).ToArray();
        var settings = new LegendSettings { MergeIdenticalStatuses = true, MergeLineScales = true };
        IReadOnlyList<LegendEntry> Build(IEnumerable<NlcsLayerName> source) => LegendGrouping.Build(source, settings,
            renderIdentity: _ => "same", fullRenderIdentity: name => name.Contains("DATA2") ? "scale2" : "scale1");
        Assert.Single(Build(layers.Take(2)));
        Assert.Single(Build(layers));
        Assert.Equal(2, Build(layers.Take(3)).Count);
    }

}
