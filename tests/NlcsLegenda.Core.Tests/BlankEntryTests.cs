using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class BlankEntryTests
{
    [Fact]
    public void NewBlank_HasDefaultTextAndUniqueId()
    {
        var a = new BlankEntry();
        var b = new BlankEntry();
        Assert.Equal("[blanco]", a.Text);
        Assert.Equal("[blanco]", a.EffectiveText);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void EmptyText_FallsBackToPlaceholder()
    {
        var blank = new BlankEntry { Text = "   " };
        Assert.Equal("[blanco]", blank.EffectiveText);
    }

    [Fact]
    public void Clone_KeepsIdentity_CloneWithNewId_Changes()
    {
        var blank = new BlankEntry { Text = "Reservering", Status = NlcsStatus.Bestaand };
        var clone = blank.Clone();
        Assert.Equal(blank.Id, clone.Id);
        Assert.Equal("Reservering", clone.Text);

        var dup = blank.CloneWithNewId();
        Assert.NotEqual(blank.Id, dup.Id);
        Assert.Equal("Reservering", dup.Text);
    }

    [Fact]
    public void ToLegendEntry_IsBlank_NoQuantity_NoSwatch()
    {
        var entry = new BlankEntry().ToLegendEntry();
        Assert.True(entry.IsBlank);
        Assert.Equal(DescriptionSource.Blanco, entry.DescriptionSource);
        Assert.Equal(QuantityKind.None, entry.QuantityType);
        Assert.Empty(entry.LayersByType);
        Assert.Equal("[blanco]", entry.Description);
    }

    [Fact]
    public void FiveBlanks_RemainFiveDistinctRows()
    {
        var settings = new LegendSettings();
        for (int i = 0; i < 5; i++)
            settings.BlankEntries.Add(new BlankEntry());

        var entries = LegendGrouping.Build(System.Array.Empty<NlcsLayerName>(), settings);
        Assert.Equal(5, entries.Count(e => e.IsBlank));
    }

    [Fact]
    public void Blanks_DoNotMerge_EvenWithKlicGrouping()
    {
        var settings = new LegendSettings();
        settings.MergedDimensions.Add(GroupDimension.Soort);
        for (int i = 0; i < 3; i++)
            settings.BlankEntries.Add(new BlankEntry());

        var entries = LegendGrouping.Build(System.Array.Empty<NlcsLayerName>(), settings);
        Assert.Equal(3, entries.Count(e => e.IsBlank));
    }

    [Fact]
    public void Blank_WithDisabledStatus_IsHidden()
    {
        var settings = new LegendSettings();
        settings.ToonBestaand = false;
        settings.BlankEntries.Add(new BlankEntry { Status = NlcsStatus.Bestaand });
        settings.BlankEntries.Add(new BlankEntry { Status = NlcsStatus.Nieuw });

        var entries = LegendGrouping.Build(System.Array.Empty<NlcsLayerName>(), settings);
        Assert.Equal(1, entries.Count(e => e.IsBlank));
    }

    [Fact]
    public void Blanks_SurviveJsonRoundtrip()
    {
        var settings = new LegendSettings();
        settings.BlankEntries.Add(new BlankEntry { Text = "Eigen regel", Status = NlcsStatus.Vervallen });
        var json = settings.ToJson();

        var restored = LegendSettings.FromJson(json).Normalize();
        Assert.Single(restored.BlankEntries);
        Assert.Equal("Eigen regel", restored.BlankEntries[0].Text);
        Assert.Equal(NlcsStatus.Vervallen, restored.BlankEntries[0].Status);
        Assert.Equal(settings.BlankEntries[0].Id, restored.BlankEntries[0].Id);
    }

    [Fact]
    public void CloneSettings_DeepCopiesBlanks()
    {
        var settings = new LegendSettings();
        settings.BlankEntries.Add(new BlankEntry { Text = "A" });
        var copy = settings.Clone();
        copy.BlankEntries[0].Text = "B";
        Assert.Equal("A", settings.BlankEntries[0].Text);
    }

    [Fact]
    public void Export_ShowsBlancoProvenance()
    {
        var entry = new BlankEntry().ToLegendEntry();
        var csv = LegendExport.ToCsv(new[] { entry });
        Assert.Contains("blanco", csv);
        Assert.Contains("[blanco]", csv);
    }
}
