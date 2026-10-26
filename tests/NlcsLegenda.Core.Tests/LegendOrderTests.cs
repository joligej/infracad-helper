using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendOrderTests
{
    private static LegendEntry Entry(string name, string style = "red", NlcsStatus status = NlcsStatus.Nieuw) => new()
    { Status = status, Discipline = "WE", Hoofdgroep = "KL", Element = name, Description = name, RenderIdentity = style };

    [Fact]
    public void ManualOrderCanCrossStatusBandsAndSurvivesJsonAndClone()
    {
        var a = Entry("A"); var b = Entry("B", status: NlcsStatus.Bestaand); var c = Entry("C");
        var settings = new LegendSettings { SortMode = LegendSortMode.Handmatig, ManualEntryOrder = new() { LegendOrder.Key(b), LegendOrder.Key(a) } };
        var copy = LegendSettings.FromJson(settings.ToJson()).Clone();
        Assert.Equal(new[] { b, a, c }, LegendGrouping.Sort(new[] { c, a, b }, copy));
        copy.ManualEntryOrder.Clear();
        Assert.Equal(2, settings.ManualEntryOrder.Count);
    }

    [Fact]
    public void AppearanceVariantsKeepSeparatePositions()
    {
        var red = Entry("DATA"); var green = Entry("DATA", "green");
        Assert.NotEqual(LegendOrder.Key(red), LegendOrder.Key(green));
        Assert.Equal(new[] { green, red }, LegendOrder.Apply(new[] { red, green }, new[] { LegendOrder.Key(green), LegendOrder.Key(red) }));
    }

    [Fact]
    public void TemporarilyHiddenRowsKeepTheirSlotWhenVisibleRowsMove()
    {
        var a = Entry("A"); var b = Entry("B"); var c = Entry("C");
        var saved = new[] { LegendOrder.Key(a), LegendOrder.Key(b), LegendOrder.Key(c) };
        var reordered = LegendOrder.Remember(saved, new[] { c, a });
        Assert.Equal(new[] { c, b, a }, LegendOrder.Apply(new[] { a, b, c }, reordered));
    }

    [Fact]
    public void NewRowsAppendAndDuplicateSavedKeysAreHarmless()
    {
        var a = Entry("A"); var b = Entry("B");
        Assert.Equal(new[] { b, a }, LegendOrder.Apply(new[] { a, b }, new[] { LegendOrder.Key(b), LegendOrder.Key(b) }));
        Assert.Equal(new[] { LegendOrder.Key(b), LegendOrder.Key(a) }, LegendOrder.Remember(new[] { LegendOrder.Key(b) }, new[] { b, a }));
    }

    [Fact]
    public void AutomaticModeDoesNotDestroyManualOrder()
    {
        var a = Entry("A"); var b = Entry("B");
        var settings = new LegendSettings { SortMode = LegendSortMode.Naam, ManualEntryOrder = new() { LegendOrder.Key(b), LegendOrder.Key(a) } };
        Assert.Equal(new[] { a, b }, LegendGrouping.Sort(new[] { b, a }, settings));
        settings.SortMode = LegendSortMode.Handmatig;
        Assert.Equal(new[] { b, a }, LegendGrouping.Sort(new[] { a, b }, settings));
    }

    [Fact]
    public void AStyleOrScaleChangeKeepsUniqueRowPositions()
    {
        var a = Entry("A"); var b = Entry("B"); var changed = Entry("B", "new scale");
        var saved = new[] { LegendOrder.Key(b), LegendOrder.Key(a) };
        Assert.Equal(new[] { changed, a }, LegendOrder.Apply(new[] { a, changed }, saved));
        var remembered = LegendOrder.Remember(saved, new[] { changed, a });
        Assert.Equal(new[] { LegendOrder.Key(changed), LegendOrder.Key(a) }, remembered);
    }

    [Fact]
    public void ChangedAppearanceDoesNotClaimAnotherVariantPosition()
    {
        var a = Entry("DATA"); var b = Entry("DATA", "green"); var changed = Entry("DATA", "blue");
        var saved = new[] { LegendOrder.Key(a), LegendOrder.Key(b) };
        Assert.Equal(new[] { b, changed }, LegendOrder.Apply(new[] { changed, b }, saved));
    }

    [Fact]
    public void SplitDrawingTypesKeepTheirPreviousPositionWithoutCopyingAnExample()
    {
        var other = Entry("B");
        var combined = Entry("A", "Geometrie=line;Arcering=hatch");
        var line = Entry("A", "Geometrie=line");
        var hatch = Entry("A", "Arcering=hatch");
        var saved = new[] { LegendOrder.Key(combined), LegendOrder.Key(other) };
        Assert.Equal(new[] { line, hatch, other }, LegendOrder.Apply(new[] { other, line, hatch }, saved));
        var remembered = LegendOrder.Remember(saved, new[] { line, hatch, other });
        Assert.Equal(new[] { LegendOrder.Key(line), LegendOrder.Key(hatch), LegendOrder.Key(other) }, remembered);
        var choices = LegendOrder.AlignChoices(new Dictionary<string, string> { [saved[0]] = "combined example" }, new[] { line, hatch });
        Assert.False(choices.ContainsKey(LegendOrder.Key(line)));
        Assert.False(choices.ContainsKey(LegendOrder.Key(hatch)));
    }

    [Fact]
    public void BlankRowsWithSameTextHaveIndependentKeys()
    {
        var a = new BlankEntry(); var b = a.CloneWithNewId();
        Assert.NotEqual(LegendOrder.Key(a.ToLegendEntry()), LegendOrder.Key(b.ToLegendEntry()));
    }

    [Fact]
    public void ManualRowsWithEqualDescriptionsKeepTheirOwnPositions()
    {
        var settings = new LegendSettings { SortMode = LegendSortMode.Handmatig,
            ManualEntries = new() {
                new() { Layer = "ROOD", Description = "Eigen kabel" },
                new() { Layer = "BLAUW", Description = "Eigen kabel" } } };
        var entries = LegendGrouping.Build(Array.Empty<NlcsLayerName>(), settings);
        Assert.NotEqual(LegendOrder.Key(entries[0]), LegendOrder.Key(entries[1]));
        settings.ManualEntryOrder = LegendOrder.Remember(Array.Empty<string>(), entries.Reverse());
        var saved = LegendSettings.FromJson(settings.ToJson());
        Assert.Equal(entries.Reverse().Select(e => e.PrimaryLayer),
            LegendGrouping.Build(Array.Empty<NlcsLayerName>(), saved).Select(e => e.PrimaryLayer));
    }
}
