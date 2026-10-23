using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class MetadataSelectionTests
{
    [Fact]
    public void DiameterGroupingRetainsSourceKeysAndExclusions()
    {
        var settings = new LegendSettings();
        var objects = new[] { "0.2", "0.3" }.Select((d, i) => new KlicObjectData("klic.dwg", i.ToString(), "", new[] {
            new KlicRecord(KlicSource.ObjectData, "Leiding", new Dictionary<string,string> { ["DiameterM"] = d }) }, Array.Empty<string>(), KlicSource.ObjectData)).ToArray();
        var layers = objects.Select((data, i) => new NlcsLayerName {
            Raw = "L" + i, LocalName = "L" + i, TypeSuffix = "G", Discipline = "WE", StatusCode = "B", Status = NlcsStatus.Bestaand, Hoofdgroep = "KL",
            Element = "WATER", DrawType = NlcsDrawType.Geometrie, KlicGroupKey = data.GroupKey(settings.KlicGroupFields()) }).ToArray();
        IReadOnlyList<LegendEntry> Build() => LegendGrouping.Build(layers, settings, _ => "Waterleiding",
            renderIdentity: _ => "blauw", selectionIdentity: _ => "blauw", klicObjects: name => new[] { objects[int.Parse(name[1..])] });
        var merged = Assert.Single(Build());
        Assert.Equal("Waterleiding (200, 300)", merged.DisplayDescription);
        Assert.Equal(2, merged.ExclusionKeys.Count);
        settings.MergedDimensions.Remove(GroupDimension.Diameter);
        var separate = Build(); Assert.Equal(2, separate.Count);
        Assert.Equal(merged.ExclusionKeys.Order(), separate.Select(LegendSettings.SelectionKey).Order());
        settings.ExcludedEntries.Add(LegendSettings.SelectionKey(separate[0]));
        settings.MergedDimensions.Add(GroupDimension.Diameter);
        Assert.Single(Build());
        settings.ExcludedEntries.UnionWith(merged.ExclusionKeys);
        Assert.Empty(Build());

        settings.ExcludedEntries.Clear();
        var legacy = new LegendEntry { Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "KL", Element = "WATER", Description = "Waterleiding", RenderIdentity = "Geometrie=blauw" };
        settings.ExcludedEntries.Add(LegendSettings.SelectionKey(legacy));
        Assert.Empty(Build());
    }
}
