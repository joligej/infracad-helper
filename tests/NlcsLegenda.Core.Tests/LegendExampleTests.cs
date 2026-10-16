using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendExampleTests
{
    private static LegendEntry Entry(string description = "Band") => new() { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH", Element = "BAND", Description = description };
    private static LegendExample Sample(string name, double scale) => new(name, name, "WEG", scale, new[] { LegendExample.SourceKey(Entry()) });

    [Fact]
    public void Match_PrefersClosestScale()
    {
        var a = Sample("SO", 1000); var b = Sample("DO", 200);
        Assert.Equal(b, LegendExample.Match(new[] { a, b }, Entry(), 250));
        Assert.Equal(a, LegendExample.Match(new[] { a, b }, Entry(), 800));
    }

    [Fact]
    public void Match_DoesNotGuessAmbiguousVariant()
    {
        var a = Sample("100 mm", 200); var b = Sample("150 mm", 200);
        Assert.Null(LegendExample.Match(new[] { a, b }, Entry(), 200));
        Assert.Equal(b, LegendExample.Match(new[] { a, b }, Entry("150 mm"), 200));
    }

    [Fact]
    public void Match_CompositeIsOnlyAvailableAsExplicitChoice()
    {
        var sample = Sample("Dubbel", 200) with { SourceKeys = new[] { LegendExample.SourceKey(Entry()), "BESTAAND|WE|VH|BAND" } };
        Assert.Null(LegendExample.Match(new[] { sample }, Entry(), 200));
    }

    [Fact]
    public void Settings_KeepEmptyOverrideAndCloneIndependently()
    {
        var s = new LegendSettings { ExampleLibraryPath = "voorbeelden.dwg" }; s.ExampleOverrides["row"] = "";
        var copy = LegendSettings.FromJson(s.ToJson()); copy.ExampleOverrides["row"] = "example";
        Assert.Equal("", s.ExampleOverrides["row"]); Assert.Equal("voorbeelden.dwg", copy.ExampleLibraryPath);
        copy.ResetFormattingToTemplate(); Assert.Equal("example", copy.ExampleOverrides["row"]);
    }

    [Fact]
    public void Match_UnrelatedElementDoesNotReceiveExample()
    {
        var example = Sample("Band", 200) with { SourceKeys = new[] { "NIEUW|WE|VH|WEG" } };
        Assert.Null(LegendExample.Match(new[] { example }, Entry(), 200));
    }
    [Theory]
    [InlineData("voorbeeld")]
    [InlineData("")]
    public void Choices_SurviveUniqueSourceStyleChange(string choice)
    {
        var before = new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH", Element = "BAND", Description = "Band", RenderIdentity = "red" };
        var after = new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH", Element = "BAND", Description = "Band", RenderIdentity = "blue" };
        var saved = new Dictionary<string, string> { [LegendOrder.Key(before)] = choice };
        var result = LegendOrder.AlignChoices(saved, new[] { after });
        Assert.Equal(choice, result[LegendOrder.Key(after)]);
        Assert.True(saved.ContainsKey(LegendOrder.Key(before)));
        Assert.False(result.ContainsKey(LegendOrder.Key(before)));
    }

    [Fact]
    public void Choices_DoNotSpreadToMultipleSourceVariants()
    {
        LegendEntry E(string style) => new() { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH", Element = "BAND", Description = "Band", RenderIdentity = style };
        var old = E("old"); var a = E("red"); var b = E("blue");
        var result = LegendOrder.AlignChoices(new Dictionary<string, string> { [LegendOrder.Key(old)] = "voorbeeld" }, new[] { a, b });
        Assert.False(result.ContainsKey(LegendOrder.Key(a)));
        Assert.False(result.ContainsKey(LegendOrder.Key(b)));
        Assert.Equal("voorbeeld", result[LegendOrder.Key(old)]);
    }
}
