using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class ProjectLegendTests
{
    private static LegendSource Source(string xref, string handle, NlcsStatus status = NlcsStatus.Bestaand) =>
        new(xref + ":" + handle, xref + "|B-WE-RI-GWA_RIOOLPUT-S", NlcsDrawType.Symbool, "B-SRI-PUT_GWA-SO", status)
        { DrawingId = "drawing-1:insert-1", SourceHandle = handle };
    private static LegendEntry Row(LegendSource source, string style = "red") => new()
    {
        Status = source.Status, Discipline = "WE", Hoofdgroep = "RI", Element = "GWA_RIOOLPUT", Description = "GWA-put",
        Sources = new[] { source }, LayersByType = new() { [source.Type] = source.Layer }, RenderIdentity = style
    };

    [Fact]
    public void RenamedSourceRetainsChoicesOrderAndExclusionWithoutMatchingAnotherDrawing()
    {
        var old = Row(Source("oud", "A1")); var settings = new LegendSettings();
        LegendContinuity.SetChoice(settings, old, ChoiceScope.Layer, "Eigen put", excluded: true);
        LegendContinuity.Reconcile(settings, new[] { old }, false); LegendContinuity.Reconcile(settings, new[] { old }, true);
        settings.ManualEntryOrder.Add(LegendOrder.Key(old)); settings.ExampleOverrides[LegendOrder.Key(old)] = "putvoorbeeld";
        var current = Row(Source("nieuw", "A1"), "blue");
        LegendContinuity.Reconcile(settings, new[] { current }, false);
        LegendContinuity.ApplyChoices(settings, new() { current }); LegendContinuity.Reconcile(settings, new[] { current }, true);
        Assert.True(current.SourceExcluded); Assert.Equal("Eigen put", current.Description);
        Assert.Equal("putvoorbeeld", settings.ExampleOverrides[LegendOrder.Key(current)]);
        Assert.Equal(LegendOrder.Key(current), Assert.Single(settings.ManualEntryOrder));
        Assert.False(settings.RowChoices[0].Matches(Source("ander", "A1") with { DrawingId = "drawing-2" }));
        Assert.False(settings.RowChoices[0].Matches(Source("nieuw", "A1", NlcsStatus.Nieuw)));
    }

    [Fact]
    public void NarrowInclusionLeavesBroadExclusionAndOtherStatusesUntouched()
    {
        var settings = new LegendSettings { IncludeXrefLayers = true }; var kept = Source("riool", "A1"); var other = Source("riool", "A2");
        LegendContinuity.SetChoice(settings, Row(kept), ChoiceScope.Layer, excluded: true);
        LegendContinuity.SetChoice(settings, Row(kept), ChoiceScope.Variant, excluded: false);
        var index = new RowChoiceIndex(settings.RowChoices);
        Assert.True(index.Includes(kept)); Assert.False(index.Includes(other));
        Assert.True(index.Find(new[] { other }).Last().Excluded);
        Assert.Empty(index.Find(new[] { kept with { Status = NlcsStatus.Nieuw } }));
        NlcsLayerParser.TryParse(kept.Layer, out var layer);
        settings.ExcludedEntries.Add(LegendSettings.EntryKey(layer!));
        Assert.NotNull(settings.ExplainExclusion(layer!));
        Assert.Null(settings.ExplainExclusion(layer!, index.Includes(kept)));
        settings.IncludedStatuses.Remove(NlcsStatus.Bestaand);
        Assert.Contains("status", settings.ExplainExclusion(layer!, true));
    }

    [Fact]
    public void ProjectDescriptionOverridesSourceTextButNotLocalChoice()
    {
        NlcsLayerParser.TryParse("N-WE-VH-WEG_RIJWIELPAD-G", out var layer);
        var project = new DescriptionCatalog(); project.Elementen["VH|WEG_RIJWIELPAD"] = new() { Specifiek = "Fietspad" };
        var settings = new LegendSettings();
        var row = Assert.Single(LegendGrouping.Build(new[] { layer! }, settings, _ => "WEG Rijwielpad", projectDescriptions: project));
        Assert.Equal("Fietspad", row.Description);
        settings.DescriptionOverrides.Elementen["VH|WEG_RIJWIELPAD"] = new() { Specifiek = "Eigen fietsstrook" };
        Assert.Equal("Eigen fietsstrook", Assert.Single(LegendGrouping.Build(new[] { layer! }, settings, projectDescriptions: project)).Description);
    }

    [Theory]
    [InlineData("Liander N.V. Pac 2Ai68G2", "Liander")]
    [InlineData("Ziggo B.V.", "Ziggo")]
    [InlineData("Gemeente Hilversum", "Gemeente Hilversum")]
    [InlineData("Onbekend123", "Onbekend123")]
    public void ReadableProviderKeepsUnknownNames(string source, string expected) =>
        Assert.Equal(expected, ProjectDescriptions.ProviderName(source));

    [Fact]
    public void ProviderAliasDoesNotChangeRawSourceOrGrouping()
    {
        var row = new LegendEntry { Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "KL", Element = "GAS", Description = "Gasleiding",
            Netbeheerders = new[] { "Liander N.V. Pac 2Ai68G2" } };
        string key = LegendSettings.SelectionKey(row);
        row.ProviderLabels = new Dictionary<string, string> { [row.Netbeheerders[0]] = "Liander" };
        Assert.Equal("Gasleiding (Liander)", row.DisplayDescription);
        row.ShowProviderNames = false;
        Assert.Equal("Gasleiding", row.DisplayDescription);
        Assert.Equal("Liander N.V. Pac 2Ai68G2", row.Netbeheerders[0]); Assert.Equal(key, LegendSettings.SelectionKey(row));
    }

    [Fact]
    public void ViewportSelectionClonesAndSerializesIndependently()
    {
        var settings = new LegendSettings { SourceViewportHandles = new() { "A1", "B1" }, UseViewportAppearance = false };
        var copy = settings.Clone(); copy.SourceViewportHandles.RemoveAt(0);
        Assert.Equal(2, settings.SourceViewportHandles.Count);
        var saved = LegendSettings.FromJson(settings.ToJson());
        Assert.Equal(settings.SourceViewportHandles, saved.SourceViewportHandles); Assert.False(saved.UseViewportAppearance);
    }

    [Fact]
    public void GlobalDefaultsNeverCarryDrawingViewportButCanShareChosenProjectFile()
    {
        string folder = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            GlobalLegendSettings.Save(folder, new LegendSettings { SourceViewportHandles = new() { "A1" }, ProjectDescriptionsPath = "private-project.json" });
            var defaults = GlobalLegendSettings.Load(folder);
            Assert.Empty(defaults.SourceViewportHandles); Assert.Equal("private-project.json", defaults.ProjectDescriptionsPath);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void DetailPresetKeepsTypesStatusesAndSymbolChoicesSeparate()
    {
        var settings = new LegendSettings { MergeIdenticalStatuses = true, MergeSymbolSizes = false };
        settings.SetDetailLevel(true);
        Assert.False(settings.MergeIdenticalStatuses); Assert.False(settings.ShowMergedValues);
        foreach (bool klic in new[] { true, false })
        {
            Assert.Contains(GroupDimension.Diameter, settings.MergeDimensionsFor(klic, NlcsDrawType.Geometrie));
            Assert.Contains(GroupDimension.Materiaal, settings.MergeDimensionsFor(klic, NlcsDrawType.Geometrie));
            Assert.DoesNotContain(GroupDimension.Soort, settings.MergeDimensionsFor(klic, NlcsDrawType.Geometrie));
        }
        Assert.False(settings.MergeSymbolSizes);
        settings.SetDetailLevel(false);
        Assert.True(settings.ShowMergedValues);
        Assert.DoesNotContain(GroupDimension.Diameter, settings.MergeDimensionsFor(false, NlcsDrawType.Geometrie));
    }
    [Fact]
    public void AdoptedWindowRejectsInvalidSizeAndKeepsLayoutOnRoundTrip()
    {
        var registry = new LegendRegistry();
        registry.Add(new LegendDefinition { SpaceHandle = "AB", WindowWidthMm = 140, WindowHeightMm = 24 });
        Assert.True(LegendRegistry.TryParse(registry.ToJson(), out var read, out _));
        Assert.Equal("AB", read.Legends[0].SpaceHandle);
        Assert.Equal(24, read.Legends[0].WindowHeightMm);
        registry.Legends[0].WindowHeightMm = -1;
        Assert.False(LegendRegistry.TryParse(registry.ToJson(), out _, out _));
    }
    [Fact]
    public void MetadataProviderLabelsApplyPerValueAndCanBeHiddenWithoutLosingSourceData()
    {
        string[] providers = { "Liander N.V. Pac 2Ai68G2", "Ziggo B.V." };
        var data = providers.Select((p, i) => new KlicObjectData("klic", i.ToString(), "", new[] {
            new KlicRecord(KlicSource.ObjectData, "KLIC", new Dictionary<string, string> { ["Netbeheerder"] = p }) },
            Array.Empty<string>(), KlicSource.ObjectData)).ToArray();
        var row = new LegendEntry { Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "KL", Element = "GAS",
            Description = "Gasleiding", KlicObjects = data, KlicDescriptionFields = new[] { "Netbeheerder" },
            ProviderLabels = providers.ToDictionary(p => p, p => ProjectDescriptions.ProviderName(p)) };
        Assert.Contains("Netbeheerder: Liander; Ziggo", row.DisplayDescription);
        row.ShowProviderNames = false;
        Assert.Equal("Gasleiding", row.DisplayDescription);
        Assert.Equal(string.Join("; ", providers), KlicSummary.Field(data, "Netbeheerder"));
    }
}
