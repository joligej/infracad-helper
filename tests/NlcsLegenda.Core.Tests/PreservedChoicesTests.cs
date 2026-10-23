using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class PreservedChoicesTests
{
    private static LegendEntry Entry(string appearance) => new() { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "GR", Element = "BOOM", Description = "Boom", RenderIdentity = appearance };

    [Fact]
    public void UnchangedEntriesDoNotRewriteSavedChoices()
    {
        var entries = Enumerable.Range(1, 5).Select(i => new LegendEntry {
            Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH", Element = "ELEMENT_" + i, Description = "Element " + i, RenderIdentity = "lijn"
        }).ToArray();
        var settings = new LegendSettings();
        settings.RememberEntries(entries);
        settings.ExcludedEntries.UnionWith(entries.Take(3).Select(LegendSettings.SelectionKey));
        string before = settings.ToJson();
        settings = LegendSettings.FromJson(before);
        for (int i = 0; i < 5; i++)
        {
            settings.RememberEntries(i % 2 == 0 ? entries : entries.Reverse());
            Assert.Equal(before, settings.ToJson());
        }
    }

    [Fact]
    public void LegacySymbolChoicesRemainDistinctForSeveralAppearances()
    {
        var layers = new[] { "N-WE-GR-BOOM-S-200", "N-WE-GR-BOOM-S-500" }.Select((name, i) => {
            NlcsLayerParser.TryParse(name, out var layer);
            return new NlcsLayerName { Raw = layer!.Raw, LocalName = layer.LocalName, Discipline = layer.Discipline,
                Status = layer.Status, StatusCode = layer.StatusCode, Hoofdgroep = layer.Hoofdgroep, Element = layer.Element,
                TypeSuffix = layer.TypeSuffix, DrawType = layer.DrawType, RenderIdentity = i.ToString() };
        }).ToArray();
        var settings = new LegendSettings();
        IReadOnlyList<LegendEntry> Build(bool stable) => LegendGrouping.Build(layers, settings, symbolBlock: _ => "BOOM",
            renderIdentity: name => name, selectionIdentity: _ => "", symbolRenderIdentity: name => "oud-" + name,
            symbolSelectionIdentity: stable ? name => "vast-" + name : null);
        var previous = Build(false); Assert.Equal(2, previous.Count);
        settings.ExcludedEntries.UnionWith(previous.Select(LegendSettings.SelectionKey));
        Assert.Empty(Build(true));
        Assert.Equal(2, settings.ExcludedEntries.Count);
        settings.ExcludedEntries.Clear();
        Assert.Equal(2, Build(true).Count);
        Assert.Empty(settings.ExcludedEntries);
    }

    [Fact]
    public void ExcludedEntrySurvivesAUniqueAppearanceChangeAndReopening()
    {
        var old = Entry("groen"); var current = Entry("rood");
        var settings = new LegendSettings();
        settings.RememberEntries(new[] { old });
        settings.ExcludedEntries.Add(LegendSettings.SelectionKey(old));
        settings = LegendSettings.FromJson(settings.ToJson());
        settings.RememberEntries(new[] { current });
        Assert.True(settings.IsEntryExcluded(LegendSettings.SelectionKey(current)));
        settings.RememberEntries(Array.Empty<LegendEntry>());
        Assert.True(settings.IsEntryExcluded(LegendSettings.SelectionKey(current)));
        settings.RememberEntries(new[] { old });
        Assert.True(settings.IsEntryExcluded(LegendSettings.SelectionKey(old)));
        settings.ExcludedEntries.Clear();
        settings.RememberEntries(new[] { current });
        Assert.False(settings.IsEntryExcluded(LegendSettings.SelectionKey(current)));
        Assert.NotSame(settings.KnownEntryKeys, settings.Clone().KnownEntryKeys);
    }

    [Fact]
    public void ANewAppearanceDoesNotInheritTheOtherVariantsExclusion()
    {
        var red = Entry("rood"); var green = Entry("groen");
        var settings = new LegendSettings(); settings.RememberEntries(new[] { red, green });
        settings.ExcludedEntries.Add(LegendSettings.SelectionKey(red));
        settings.RememberEntries(new[] { green });
        Assert.False(settings.IsEntryExcluded(LegendSettings.SelectionKey(green)));
        settings.RememberEntries(new[] { red, green, Entry("blauw") });
        Assert.False(settings.IsEntryExcluded(LegendSettings.SelectionKey(Entry("blauw"))));
    }

    [Fact]
    public void DiameterIdentityDoesNotFollowTheGroupingChoice()
    {
        var settings = new LegendSettings(); settings.Klic.GroupFields.AddRange(new[] { "DiameterM", "Materiaal" });
        Assert.Equal(1, settings.KlicGroupFields().Count(f => f == "DiameterM"));
        Assert.Contains("Materiaal", settings.KlicGroupFields());
        settings.MergedDimensions.Remove(GroupDimension.Diameter);
        Assert.Equal(1, settings.KlicGroupFields().Count(f => f == "DiameterM"));
    }

    [Fact]
    public void ExistingGlobalDefaultsGainDiameterOnceAndRetainAnExplicitOptOut()
    {
        var directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Join(directory, "settings.json"), "{\"mergedDimensions\":[\"Nummer\"],\"globalDefaultsVersion\":1}");
            var settings = GlobalLegendSettings.Load(directory);
            Assert.Contains(GroupDimension.Diameter, settings.MergedDimensions);
            settings.MergedDimensions.Remove(GroupDimension.Diameter);
            GlobalLegendSettings.Save(directory, settings);
            Assert.DoesNotContain(GroupDimension.Diameter, GlobalLegendSettings.Load(directory).MergedDimensions);
        }
        finally { Directory.Delete(directory, true); }
    }
}
