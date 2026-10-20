using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class CompositionImprovementsTests
{
    private static LegendEntry Entry(string element, string color = "red", double length = 2) => new()
    {
        Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "KL", Element = element,
        Description = element, RenderIdentity = color, AppearanceLabel = color,
        LayersByType = new() { [NlcsDrawType.Geometrie] = element }, Metric = new(0, length, 0)
    };

    [Theory]
    [InlineData("N-WE-VH-ASFALT_FIETSPAD-G", "fiets asfalt", true)]
    [InlineData("N-WE-VH-ASFALT_FIETSPAD-G", "*asfalt*fiets*", true)]
    [InlineData("N-WE-VH-ASFALT_FIETSPAD-G", "fiets*asfalt", false)]
    [InlineData("N-WE-VH-ASFALT_FIETSPAD-G", "gras", false)]
    public void SearchWordsAndWildcards(string source, string query, bool expected) =>
        Assert.Equal(expected, LayerSearch.Matches(source, query));

    [Fact]
    public void DefaultsKeepTypesApartAndIgnoreOtherKlicDimensions()
    {
        var s = new LegendSettings();
        Assert.DoesNotContain(GroupDimension.Soort, s.MergedDimensions);
        Assert.Equal(4, s.MergedDimensions.Count);
        Assert.Empty(s.NonKlicMergedDimensions);
        Assert.NotEqual(ElementProperties.From("GAS").MergeKey(s.MergedDimensions),
            ElementProperties.From("WATER").MergeKey(s.MergedDimensions));
        Assert.Equal(ElementProperties.From("DATA_LS2", "A").MergeKey(s.MergedDimensions),
            ElementProperties.From("DATA_MS3_MANTELBUIS", "B").MergeKey(s.MergedDimensions));
    }

    [Fact]
    public void AppearanceExclusionsRemainSeparateButOldExclusionsCoverAllVariants()
    {
        var red = Entry("GAS", "red");
        var green = Entry("GAS", "green");
        var tree = CompositionTree.Build(new[] {
            (LegendSettings.SelectionKey(red), "Gas rood", "KLIC"),
            (LegendSettings.SelectionKey(green), "Gas groen", "KLIC")
        }, new HashSet<string> { LegendSettings.EntryKey(red) });
        Assert.All(tree.AllLeaves(), l => Assert.False(l.Included));
        tree.AllLeaves().Last().Included = true;
        var settings = new LegendSettings { ExcludedEntries = tree.ExcludedKeys() };
        Assert.True(settings.IsEntryExcluded(LegendSettings.SelectionKey(red)));
        Assert.False(settings.IsEntryExcluded(LegendSettings.SelectionKey(green)));
    }

    [Fact]
    public void ManualMergeUsesMainAppearanceAndSumsQuantities()
    {
        var red = Entry("GAS", "red", 2);
        var green = Entry("VERKEERD_INGETEKEND", "green", 3);
        var group = ManualMergeGroup.Create(new[] { red, green }, green);
        var merged = Assert.Single(ManualMergeGroup.Apply(new() { red, green }, new[] { group }));
        Assert.Equal("green", merged.RenderIdentity);
        Assert.Equal(green.Description, merged.Description);
        Assert.Equal(5, merged.Metric.Length);
        Assert.Equal(group.Id, merged.ManualMergeId);
        Assert.Same(red, Assert.Single(ManualMergeGroup.Apply(new() { red }, new[] { group })));
    }

    [Fact]
    public void ManualMergeAndNonKlicChoicesAreIndependentAfterSavingAndCloning()
    {
        var a = Entry("A"); var b = Entry("B");
        var s = new LegendSettings();
        s.ManualMergeGroups.Add(ManualMergeGroup.Create(new[] { a, b }, a));
        s.NonKlicMergedDimensions.Add(GroupDimension.Nummer);
        var clone = s.Clone();
        clone.ManualMergeGroups[0].Members.Clear();
        clone.NonKlicMergedDimensions.Clear();
        Assert.Equal(2, s.ManualMergeGroups[0].Members.Count);
        Assert.Single(s.NonKlicMergedDimensions);
        Assert.True(LegendSettings.TryParse(s.ToJson(), out var saved));
        Assert.Single(saved!.ManualMergeGroups);
        Assert.Single(saved.NonKlicMergedDimensions);
    }

    [Fact]
    public void OldGlobalTemplateUpgradesWithoutLosingContentOrCustomSizing()
    {
        var s = new LegendSettings { SwatchWidthMm = 20, SwatchHeightMm = 8, RowPitchMm = 11,
            ColumnWidthMm = 75, Scale = 500, IncludeQuantities = true, RemarksText = "eigen tekst" };
        GlobalLegendSettings.UpgradeLegacyTemplate(s);
        Assert.Equal(24, s.SwatchWidthMm);
        Assert.Equal(5, s.SwatchHeightMm);
        Assert.Equal(500, s.Scale);
        Assert.True(s.IncludeQuantities);
        Assert.Equal("eigen tekst", s.RemarksText);
        s.SwatchWidthMm = 30;
        GlobalLegendSettings.UpgradeLegacyTemplate(s);
        Assert.Equal(30, s.SwatchWidthMm);
    }

    [Fact]
    public void GlobalAllUncheckedSurvivesAnExplicitSave()
    {
        var directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var s = new LegendSettings();
            s.MergedDimensions.Clear();
            GlobalLegendSettings.Save(directory, s);
            Assert.Empty(GlobalLegendSettings.Load(directory).MergedDimensions);
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(directory));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
