using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class ContinuityTests
{
    private static LegendEntry Row(string color, string handle = "xref:A1", NlcsStatus status = NlcsStatus.Nieuw) => new()
    {
        Status = status, Discipline = "WE", Hoofdgroep = "GR", Element = "BOOM", Description = "Boom",
        RenderIdentity = color, Sources = new[] { new LegendSource(handle, "xref|N-WE-GR-BOOM-S", NlcsDrawType.Symbool, "BOOM_2E_GROOTTE", status) },
        LayersByType = new() { [NlcsDrawType.Symbool] = "N-WE-GR-BOOM-S" }
    };

    [Fact]
    public void AppearanceChangeKeepsExclusionDescriptionExampleAndOrder()
    {
        var settings = new LegendSettings(); var old = Row("red"); var other = Row("blue", "xref:A2");
        settings.RememberEntries(new[] { old, other });
        LegendContinuity.Reconcile(settings, new[] { old, other }, false);
        LegendContinuity.Reconcile(settings, new[] { old, other }, true);
        settings.ExcludedEntries.Add(LegendSettings.SelectionKey(old));
        settings.ExampleOverrides[LegendOrder.Key(other)] = "eigen voorbeeld";
        settings.ManualEntryOrder = new() { LegendOrder.Key(other), LegendOrder.Key(old) };
        LegendContinuity.SetChoice(settings, other, ChoiceScope.Variant, "Mijn boom");
        var current = new[] { Row("green"), Row("yellow", "xref:A2") };
        LegendContinuity.Reconcile(settings, current, false);
        LegendContinuity.ApplyChoices(settings, current.ToList());
        LegendContinuity.Reconcile(settings, current, true);
        Assert.True(settings.IsEntryExcluded(LegendSettings.SelectionKey(current[0])));
        Assert.False(settings.IsEntryExcluded(LegendSettings.SelectionKey(current[1])));
        Assert.Equal("Mijn boom", current[1].Description);
        Assert.Equal("eigen voorbeeld", settings.ExampleOverrides[LegendOrder.Key(current[1])]);
        Assert.Equal(LegendOrder.Key(current[1]), settings.ManualEntryOrder[0]);
        Assert.Equal("eigen voorbeeld", LegendOrder.AlignChoices(settings.ExampleOverrides, new[] { current[1] })[LegendOrder.Key(current[1])]);
    }

    [Fact]
    public void SimilarHandlesInOtherXrefOrStatusDoNotInherit()
    {
        var settings = new LegendSettings(); var original = Row("red");
        LegendContinuity.SetChoice(settings, original, ChoiceScope.Variant, "A", true);
        var entries = new[] { Row("green", "other:A1"), Row("green", status: NlcsStatus.Bestaand) }.ToList();
        LegendContinuity.ApplyChoices(settings, entries);
        Assert.All(entries, e => { Assert.False(e.ExcludedBySource); Assert.Equal("Boom", e.Description); });
    }

    [Fact]
    public void LayerChoiceCoversNewObjectsButRespectsStatus()
    {
        var settings = new LegendSettings(); LegendContinuity.SetChoice(settings, Row("red"), ChoiceScope.Layer, excluded: true);
        var entries = new[] { Row("blue", "xref:A9"), Row("green", "xref:B9", NlcsStatus.Bestaand) }.ToList();
        LegendContinuity.ApplyChoices(settings, entries);
        Assert.True(entries[0].ExcludedBySource); Assert.False(entries[1].ExcludedBySource);
    }

    [Fact]
    public void ConflictingExamplesAreNotArbitrarilyChosen()
    {
        var settings = new LegendSettings(); var a = Row("red"); var b = Row("blue", "xref:A2");
        LegendContinuity.Reconcile(settings, new[] { a, b }, false);
        settings.ExampleOverrides[LegendOrder.Key(a)] = "A"; settings.ExampleOverrides[LegendOrder.Key(b)] = "B";
        var merged = new LegendEntry { Status = a.Status, Discipline = a.Discipline, Hoofdgroep = a.Hoofdgroep,
            Element = a.Element, Description = "Boom", RenderIdentity = "new", Sources = a.Sources.Concat(b.Sources).ToArray() };
        LegendContinuity.Reconcile(settings, new[] { merged }, false);
        Assert.Contains("Meerdere", merged.SourceNotice); Assert.False(settings.ExampleOverrides.ContainsKey(LegendOrder.Key(merged)));
    }

    [Fact]
    public void ExplicitReenableSurvivesAnotherAnalysis()
    {
        var settings = new LegendSettings(); var before = Row("red"); var after = Row("blue");
        LegendContinuity.Reconcile(settings, new[] { before }, false); settings.RememberEntries(new[] { before });
        settings.ExcludedEntries.Add(LegendSettings.SelectionKey(before));
        LegendContinuity.Reconcile(settings, new[] { after }, false); settings.RememberEntries(new[] { after });
        settings.ExcludedEntries.Clear();
        LegendContinuity.Reconcile(settings, new[] { after }, false);
        Assert.False(settings.IsEntryExcluded(LegendSettings.SelectionKey(after)));
    }

    [Fact]
    public void PreviewReportsSplitMergeAndChangedSample()
    {
        var old = new SavedLegendRow { Description = "Boom", Sources = Row("red").Sources.Concat(Row("red", "xref:A2").Sources).ToList(), Appearance = "red" };
        var split = new[] { Row("red"), Row("blue", "xref:A2") };
        Assert.Contains(LegendUpdatePlan.Compare(new[] { old }, split), c => c.Change == "Gesplitst");
        var rows = split.Select(e => new SavedLegendRow { Description = e.Description, Sources = e.Sources.ToList(), Appearance = e.RenderIdentity }).ToArray();
        Assert.Contains(LegendUpdatePlan.Compare(rows, new[] { old.RetainedEntry() }), c => c.Change == "Samengevoegd");
        Assert.Contains(LegendUpdatePlan.Compare(new[] { rows[0] }, new[] { Row("green") }), c => c.Change == "Voorbeeld gewijzigd");
    }

    [Fact]
    public void NewCollectionsAndAdoptedRowsRoundtripAndCloneIndependently()
    {
        var settings = new LegendSettings(); LegendContinuity.SetChoice(settings, Row("red"), ChoiceScope.ObjectKind, "Boom");
        LegendContinuity.Reconcile(settings, new[] { Row("red") }, false);
        settings.ManualEntries.Add(new ManualEntry { Id = "hoogte", Layer = "0", Description = "Hoogte NAP", SampleBlock = "sample", SampleScale = 500 });
        var registry = new LegendRegistry { Legends = { new LegendDefinition { Settings = settings,
            Rows = { new SavedLegendRow { Description = "Boom", Sources = Row("red").Sources.ToList(), SampleBlock = "example", Scale = 500 } } } } };
        Assert.True(LegendRegistry.TryParse(registry.ToJson(), out var loaded, out var error), error);
        var copy = loaded.Legends[0].Clone(); copy.Settings.RowChoices[0].Sources.Clear(); copy.Rows[0].Sources.Clear();
        Assert.Single(loaded.Legends[0].Rows[0].Sources); Assert.Single(loaded.Legends[0].Settings.RowChoices[0].Sources);
        Assert.Equal("hoogte", loaded.Legends[0].Settings.ManualEntries[0].Id);
    }

    [Theory]
    [InlineData("SGR-BOOM_1E_GROOTTE-SO", "1e grootte")]
    [InlineData("SGR-BOOM_2_GROOTTEKLASSE-SO", "2e grootte")]
    [InlineData("SGR-BOOM_01-SO", "")]
    [InlineData("SGR-BOOM_1E GROOTTE", "1e grootte")]
    public void TreeClassMustBeExplicit(string block, string expected) => Assert.Equal(expected, SymbolDescription.Variant(block));
    [Fact]
    public void ExplicitAutomaticExampleOverridesEarlierLayerChoice()
    {
        var settings = new LegendSettings(); var row = Row("red");
        LegendContinuity.SetChoice(settings, row, ChoiceScope.Layer, example: "eigen");
        LegendContinuity.SetChoice(settings, row, ChoiceScope.Variant, "Mijn boom", excluded: false);
        LegendContinuity.SetChoice(settings, row, ChoiceScope.Variant, automaticExample: true);
        LegendContinuity.ApplyChoices(settings, new() { row });
        Assert.Equal(2, settings.RowChoices.Count);
        Assert.Equal("Mijn boom", row.Description);
        Assert.False(row.SourceExcluded);
        Assert.Empty(settings.ExampleOverrides);
    }

    [Fact]
    public void IndividualReenableTakesPrecedenceOverLayerExclusion()
    {
        var settings = new LegendSettings(); var row = Row("red"); var other = Row("blue", "xref:B1");
        LegendContinuity.SetChoice(settings, row, ChoiceScope.Layer, excluded: true);
        LegendContinuity.SetChoice(settings, row, ChoiceScope.Variant, excluded: false);
        LegendContinuity.ApplyChoices(settings, new() { row, other });
        Assert.False(row.SourceExcluded); Assert.True(other.SourceExcluded);
    }

    [Fact]
    public void CustomDescriptionDoesNotReportChangedAppearance()
    {
        var before = new SavedLegendRow { Description = "Boom", Sources = Row("red").Sources.ToList(), Appearance = "red" };
        var after = Row("red|choice"); after.Description = "Mijn boom"; after.SampleIdentity = "red";
        Assert.DoesNotContain(LegendUpdatePlan.Compare(new[] { before }, new[] { after }), c => c.Change == "Voorbeeld gewijzigd");
    }

    [Fact]
    public void ManholeTypeSurvivesButInstanceNumbersDoNot()
    {
        Assert.Equal(SymbolDescription.ManholeKind("SRI-PUT_GWA_101-SO", "PUT"),
            SymbolDescription.ManholeKind("SRI-PUT_GWA_102-SO", "PUT"));
        Assert.NotEqual(SymbolDescription.ManholeKind("SRI-PUT_GWA_ROND-SO", "PUT"),
            SymbolDescription.ManholeKind("SRI-PUT_GWA_VIERKANT-SO", "PUT"));
    }

    [Fact]
    public void RetainedRowCanBeRenamedAndExcludedWithoutKnownSource()
    {
        var settings = new LegendSettings(); var entry = new SavedLegendRow { Id = "old", Description = "Oud" }.RetainedEntry();
        LegendContinuity.SetChoice(settings, entry, ChoiceScope.Variant, "Eigen regel", true);
        LegendContinuity.ApplyChoices(settings, new() { entry });
        Assert.Equal("Eigen regel", entry.Description); Assert.True(entry.SourceExcluded);
        var other = new SavedLegendRow { Id = "other", Description = "Anders" }.RetainedEntry();
        LegendContinuity.ApplyChoices(settings, new() { other });
        Assert.False(other.SourceExcluded == true);
    }

    [Fact]
    public void InvalidSourceChoiceIsRejectedWithoutReplacingIt()
    {
        var settings = new LegendSettings();
        settings.RowChoices.Add(new RowChoice { Sources = new() { new("", "Layer", NlcsDrawType.Symbool, "Block", NlcsStatus.Nieuw) } });
        Assert.False(LegendSettings.TryParse(settings.ToJson(), out _));
        Assert.Single(settings.RowChoices);
    }

    [Fact]
    public void PreviewKeepsAnUnresolvedLegacyRowRecognizable()
    {
        var old = new SavedLegendRow { Id = "legacy", Description = "Put", OrderKey = "oud" };
        var changes = LegendUpdatePlan.Compare(new[] { old }, new[] { old.RetainedEntry() });
        Assert.Single(changes); Assert.Equal("Bewaard", changes[0].Change);
    }

    [Fact]
    public void ExistingHeightLimitRespectsDeliberatelyChosenColumns()
    {
        var settings = new LegendSettings { Columns = 3, MaxLegendHeightMm = 200,
            IncludeTitle = false, IncludeGroupHeaders = false };
        var rows = Enumerable.Range(0, 9).Select(i => Row("red", "xref:" + i)).ToArray();
        var layout = LegendLayoutEngine.Compute(rows, settings);
        Assert.Equal(3, layout.Columns);
        Assert.True(layout.Height <= settings.ToModel(200));
    }

}
