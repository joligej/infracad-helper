using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class ManagedLegendTests
{
    private static LegendEntry Entry(string handle, string color = "rood", NlcsStatus status = NlcsStatus.Bestaand) => new()
    {
        Status = status, Discipline = "WE", Hoofdgroep = "RI", Element = "GWA", Description = "GWA-riool",
        Sources = new[] { new LegendSource("RIOOL:" + handle, "RIOOL|B-WE-RI-GWA-G", NlcsDrawType.Geometrie, "", status) { DrawingId = "bron:insert", SourceHandle = handle } },
        LayersByType = new() { [NlcsDrawType.Geometrie] = "RIOOL|B-WE-RI-GWA-G" }, RenderIdentity = color,
        SourceDisplay = new[] { new ExampleDisplay(color, "streep", 25, 0) }
    };
    private static SavedLegendRow Saved(LegendEntry entry) => new() { OrderKey = LegendOrder.Key(entry), Sources = entry.Sources.ToList(),
        Description = entry.Description, Status = entry.Status, ExclusionKeys = entry.ExclusionKeys.ToList(), SampleBlock = "voorbeeld", Appearance = entry.RenderIdentity };

    [Fact]
    public void LockedCompositionFollowsChangedColorButDoesNotAdmitNewItems()
    {
        var old = Entry("A"); var legend = new LegendDefinition { Rows = new() { Saved(old) } }; LegendComposition.Lock(legend);
        var changed = Entry("A", "groen"); var newItem = Entry("B", "blauw");
        var result = LegendComposition.Apply(legend, new[] { changed, newItem });
        Assert.Same(changed, Assert.Single(result.Entries)); Assert.Same(newItem, Assert.Single(result.Proposals));
        Assert.Equal(legend.Rows[0].Id, changed.ApprovedRowId); Assert.Equal("groen", changed.RenderIdentity);
        Assert.Equal("Voorstel", Assert.Single(result.Notices).Change);
    }

    [Fact]
    public void SplitWithoutUniqueSuccessorKeepsApprovedExampleAndOffersCandidates()
    {
        var first = Entry("A"); var second = Entry("B");
        var old = Saved(first); old.Sources.AddRange(second.Sources); old.OrderKey = "oud-gecombineerd";
        var legend = new LegendDefinition { Rows = new() { old } }; LegendComposition.Lock(legend);
        var result = LegendComposition.Apply(legend, new[] { Entry("A", "groen"), Entry("B", "blauw") });
        var row = Assert.Single(result.Entries); Assert.True(row.Retained); Assert.False(row.MissingSource);
        Assert.Equal("voorbeeld", row.SampleBlock); Assert.DoesNotContain("bron ontbreekt", row.DisplayDescription);
        Assert.Equal(2, result.Proposals.Count); Assert.Contains(result.Notices, n => n.Change == "Bronkeuze nodig");
    }

    [Fact]
    public void AmbiguousSplitCannotBeApprovedAsAnExtraRuleBeforeChoosingTheExistingRule()
    {
        var a = Entry("A", "groen"); var b = Entry("B", "blauw");
        var old = Saved(a); old.OrderKey = "oud-gecombineerd"; old.Sources.AddRange(b.Sources);
        var legend = new LegendDefinition { Rows = new() { old } }; LegendComposition.Lock(legend);
        Assert.Contains("Bron koppelen", LegendComposition.ApprovalProblem(legend, b, new[] { a, b }));
        var choice = LegendContinuity.SetChoice(legend.Settings, a, ChoiceScope.Variant); choice.PreviousRowId = old.Id;
        Assert.Empty(LegendComposition.ApprovalProblem(legend, b, new[] { a, b }));
        Assert.Same(a, Assert.Single(LegendComposition.Apply(legend, new[] { a, b }).Entries));
    }

    [Fact]
    public void LinkingRetainedSplitLeavesTheOtherVariantAvailableAsProposal()
    {
        var a = Entry("A", "groen"); var b = Entry("B", "blauw");
        var old = Saved(a); old.OrderKey = "oud-gecombineerd"; old.Sources.AddRange(b.Sources);
        var legend = new LegendDefinition { Rows = new() { old } }; LegendComposition.Lock(legend);
        LegendContinuity.LinkRow(legend.Settings, old.RetainedEntry(), a, keepExample: false);
        LegendContinuity.ApplyChoices(legend.Settings, new() { a, b });
        Assert.NotEqual(true, b.SourceExcluded);
        var result = LegendComposition.Apply(legend, new[] { a, b });
        Assert.Same(a, Assert.Single(result.Entries)); Assert.Same(b, Assert.Single(result.Proposals));
        Assert.Equal(old.Id, a.ApprovedRowId);
    }

    [Fact]
    public void SwappedStyleCombinationsAreReported()
    {
        var a = new[] { new ExampleDisplay("rood", "streep", 25, 0), new ExampleDisplay("blauw", "continu", 25, 0) };
        var b = new[] { new ExampleDisplay("rood", "continu", 25, 0), new ExampleDisplay("blauw", "streep", 25, 0) };
        Assert.Contains("combinatie", ExampleDisplay.Difference(a, b));
    }

    [Fact]
    public void ExcludedApprovedRowDoesNotComeBackAndExplicitManualAdditionSurvives()
    {
        var legend = new LegendDefinition { Rows = new() { Saved(Entry("A")) } }; LegendComposition.Lock(legend);
        var manual = new ManualEntry { Id = "eigen", Layer = "0", Description = "Overlap", SampleBlock = "rood" }.ToLegendEntry();
        var result = LegendComposition.Apply(legend, new[] { manual });
        Assert.Same(manual, Assert.Single(result.Entries)); Assert.Empty(result.Proposals);
        LegendComposition.Remember(legend, new[] { new SavedLegendRow { Id = "manual-row", Manual = true } });
        Assert.Equal(2, legend.ApprovedRows.Count);
    }

    [Fact]
    public void ExplicitNewSourceChoiceKeepsApprovedRowIdentity()
    {
        var old = Saved(Entry("A")); var legend = new LegendDefinition { Rows = new() { old } }; LegendComposition.Lock(legend);
        var replacement = Entry("B"); var choice = LegendContinuity.SetChoice(legend.Settings, replacement, ChoiceScope.Variant);
        choice.PreviousRowId = old.Id;
        var result = LegendComposition.Apply(legend, new[] { replacement });
        Assert.Equal(old.Id, Assert.Single(result.Entries).ApprovedRowId); Assert.Empty(result.Proposals);
    }

    [Fact]
    public void FixedCompositionNeverMatchesNewAndExistingStatus()
    {
        var legend = new LegendDefinition { Rows = new() { Saved(Entry("A")) } }; LegendComposition.Lock(legend);
        var result = LegendComposition.Apply(legend, new[] { Entry("A", status: NlcsStatus.Nieuw) });
        Assert.Empty(result.Entries); Assert.Single(result.Proposals);
    }

    [Theory]
    [InlineData("blauw", "streep", 25, 0, "kleur")]
    [InlineData("rood", "continu", 25, 0, "lijntype")]
    [InlineData("rood", "streep", 50, 0, "lijndikte")]
    [InlineData("rood", "streep", 25, 120, "transparantie")]
    public void ExampleComparisonNamesEachChangedProperty(string color, string line, int weight, int transparency, string expected) =>
        Assert.Equal(expected, ExampleDisplay.Difference(new[] { new ExampleDisplay("rood", "streep", 25, 0) }, new[] { new ExampleDisplay(color, line, weight, transparency) }));

    [Fact]
    public void ExampleComparisonIgnoresEnumerationOrderAndDuplicatePrimitives()
    {
        var a = new ExampleDisplay("rood", "streep", 25, 0); var b = new ExampleDisplay("zwart", "continu", 50, 0);
        Assert.Equal("", ExampleDisplay.Difference(new[] { a, b }, new[] { b, a, b }));
    }

    [Fact]
    public void PaperDimensionsAreIndependentOfMapScaleAndFooterIsUnambiguous()
    {
        var settings = new LegendSettings { Scale = 500, PaperSpace = true };
        Assert.Equal(2.5, settings.ToModel(2.5)); Assert.DoesNotContain("1:", settings.FormatScale());
        Assert.Equal(500, settings.Scale); Assert.Equal(1000, settings.RenderScale);
        settings.PaperSpace = false; Assert.Equal(1.25, settings.ToModel(2.5)); Assert.Contains("1:500", settings.FormatScale());
    }

    [Fact]
    public void SavedRegistryPreservesApprovalSourceModesAndComparisonExceptions()
    {
        var row = Saved(Entry("A")); row.ExampleMode = ExampleMode.Fixed; row.ComparisonKey = "GWA";
        var definition = new LegendDefinition { Rows = new() { row } }; LegendComposition.Lock(definition);
        definition.ComparisonExceptions.Add(new("item", "fingerprint"));
        definition.Settings.ManualEntries.Add(new() { Id = "own", Description = "Maaiveld", Layer = "0", ExampleMode = ExampleMode.FollowSource,
            Link = new() { DrawingId = "source", ContainerHandles = new() { "A" }, ObjectHandle = "B", ProfileViewHandle = "C", MapViewportHandle = "D", Component = "Line" } });
        var registry = new LegendRegistry(); registry.Add(definition);
        Assert.True(LegendRegistry.TryParse(registry.ToJson(), out var restored, out var error), error);
        var copy = Assert.Single(restored.Legends); Assert.True(copy.CompositionLocked);
        Assert.Equal(ExampleMode.Fixed, Assert.Single(copy.ApprovedRows).ExampleMode);
        Assert.Equal("C", Assert.Single(copy.Settings.ManualEntries).Link!.ProfileViewHandle);
        Assert.Equal("D", Assert.Single(copy.Settings.ManualEntries).Link!.MapViewportHandle);
        Assert.Equal("fingerprint", Assert.Single(copy.ComparisonExceptions).Fingerprint);
        var independent = copy.Clone(); independent.ApprovedRows[0].Sources.Clear(); independent.Settings.ManualEntries[0].Link!.ContainerHandles.Clear();
        Assert.NotEmpty(copy.ApprovedRows[0].Sources); Assert.NotEmpty(copy.Settings.ManualEntries[0].Link!.ContainerHandles);
    }

    [Fact]
    public void OlderRegistryUpgradesWithoutLosingItsRows()
    {
        var registry = new LegendRegistry { SchemaVersion = 2 };
        registry.Add(new LegendDefinition { Rows = new() { Saved(Entry("A")) } });
        Assert.True(LegendRegistry.TryParse(registry.ToJson(), out var restored, out var error), error);
        Assert.Equal(3, restored.SchemaVersion);
        Assert.Equal(registry.Legends[0].Rows[0].Id, restored.Legends[0].Rows.Single().Id);
    }

    [Fact]
    public void SheetExceptionsExpireWhenAppearanceOrTextChanges()
    {
        var a = Saved(Entry("A")); var b = Saved(Entry("B")); a.Description = "GWA-riool"; b.Description = "Vuilwaterriool";
        a.ExampleDisplay = new() { new("rood", "streep", 25, 0) }; b.ExampleDisplay = new() { new("groen", "streep", 25, 0) };
        var rows = new[] { new SheetLegendRow("0060", "legend", a), new SheetLegendRow("0061", "legend", b) };
        var first = Assert.Single(LegendSheetComparison.Compare(rows, Array.Empty<SheetComparisonException>()));
        var exceptions = new[] { new SheetComparisonException(first.Key, first.Fingerprint) };
        Assert.True(Assert.Single(LegendSheetComparison.Compare(rows, exceptions)).Accepted);
        b.Description = "GWA-leiding"; Assert.False(Assert.Single(LegendSheetComparison.Compare(rows, exceptions)).Accepted);
        b.Status = NlcsStatus.Nieuw; b.Sources = b.Sources.Select(s => s with { Status = NlcsStatus.Nieuw }).ToList();
        Assert.Empty(LegendSheetComparison.Compare(rows, exceptions));
    }
    [Fact]
    public void SameVariantSetsAcrossSheetsAreNotReportedAsDifferences()
    {
        var a = Saved(Entry("A", "rood")); var b = Saved(Entry("B", "blauw"));
        var rows = new[] { new SheetLegendRow("0060", "l1", a), new SheetLegendRow("0060", "l1", b),
            new SheetLegendRow("0061", "l2", b.Clone()), new SheetLegendRow("0061", "l2", a.Clone()) };
        Assert.Empty(LegendSheetComparison.Compare(rows, Array.Empty<SheetComparisonException>()));
    }

    [Fact]
    public void ManualRuleWithoutComparableIdentityIsReportedInsteadOfClaimingAgreement()
    {
        var row = new SavedLegendRow { Description = "Eigen markering", Manual = true };
        var result = LegendSheetComparison.Compare(new[] { new SheetLegendRow("0061", "l", row) }, Array.Empty<SheetComparisonException>());
        Assert.Contains("niet vergeleken", Assert.Single(result).Details);
    }

    [Fact]
    public void LinkedExampleRebuildKeepsManualExclusionAndOrder()
    {
        var manual = new ManualEntry { Id = "vaste-regel", Layer = "0", Description = "Bestaand maaiveld", SymbolBlock = "oude-kopie", SampleBlock = "oude-kopie" };
        var previous = manual.ToLegendEntry(); var settings = new LegendSettings();
        settings.ExcludedEntries.Add(LegendSettings.SelectionKey(previous, previous.LegacySelectionIdentity));
        settings.ManualEntryOrder.Add(LegendOrder.Key(previous));
        var definition = new LegendDefinition { Rows = new() { Saved(previous) } }; definition.Rows[0].Manual = true; LegendComposition.Lock(definition);
        manual.SampleBlock = manual.SymbolBlock = "nieuwe-kopie";
        var current = manual.ToLegendEntry(); settings.RememberEntries(new[] { current });
        Assert.True(settings.IsEntryExcluded(LegendSettings.SelectionKey(current)));
        Assert.Equal(LegendOrder.Key(previous), LegendOrder.Key(current));
        Assert.Equal(definition.Rows[0].Id, Assert.Single(LegendComposition.Apply(definition, new[] { current }).Entries).ApprovedRowId);
    }

    [Fact]
    public void LocalSourceLinkDoesNotLeakIntoGlobalDefaults()
    {
        string directory = Path.Join(Path.GetTempPath(), "nlcs-global-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new LegendSettings();
            settings.ManualEntries.Add(new ManualEntry { Id = "local", Layer = "0", Description = "Maaiveld", Link = new() { ObjectHandle = "AB" } });
            GlobalLegendSettings.Save(directory, settings);
            Assert.Empty(GlobalLegendSettings.Load(directory).ManualEntries); Assert.Single(settings.ManualEntries);
        }
        finally
        {
            string resolved = Path.GetFullPath(directory), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Tijdelijke map ligt buiten de testlocatie.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }

}
