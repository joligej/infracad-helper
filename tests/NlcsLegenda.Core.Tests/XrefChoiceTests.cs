using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class XrefChoiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwitchingAllRemovesSavedExceptions(bool included)
    {
        var settings = new LegendSettings { IncludeXrefLayers = !included };
        settings.XrefInclusion["KLIC"] = !included;
        settings.XrefInclusion["SITUATIE|KLIC"] = !included;
        settings.SetAllXrefs(included);
        Assert.Equal(included, settings.IsXrefIncluded("KLIC"));
        Assert.Equal(included, settings.IsXrefIncluded("SITUATIE|KLIC"));
        Assert.Equal(included, settings.IsXrefIncluded("NIEUWE XREF"));
        Assert.Empty(settings.XrefInclusion);
    }

    [Fact]
    public void ExcludedParentCannotLeakGrandchild()
    {
        var settings = new LegendSettings { IncludeXrefLayers = true };
        settings.XrefInclusion["SITUATIE|KLIC"] = false;
        settings.XrefInclusion["SITUATIE|KLIC|GAS"] = true;
        Assert.True(settings.IsXrefIncluded("SITUATIE"));
        Assert.False(settings.IsXrefIncluded("SITUATIE|KLIC"));
        Assert.False(settings.IsXrefIncluded("SITUATIE|KLIC|GAS"));
        Assert.True(settings.IsXrefIncluded("SITUATIE|WEGEN"));
    }
}
