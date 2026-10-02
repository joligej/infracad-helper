using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class CompositionTreeTests
{
    private static CompositionTree Sample(params string[] excluded)
    {
        var items = new[]
        {
            ("N-WE-KL-DATA-G", "Data A", "Data"),
            ("N-WE-KL-DATA2-G", "Data B", "Data"),
            ("N-WE-KL-DATA3-G", "Data C", "Data"),
            ("N-WE-RIO-PUT-G", "Put", "Riolering"),
        };
        return CompositionTree.Build(items, new HashSet<string>(excluded));
    }

    [Fact]
    public void Build_GroupsByLabel_PreservesOrder()
    {
        var tree = Sample();
        Assert.Equal(2, tree.Groups.Count);
        Assert.Equal("Data", tree.Groups[0].Label);
        Assert.Equal(3, tree.Groups[0].Leaves.Count);
        Assert.Equal("Riolering", tree.Groups[1].Label);
    }

    [Fact]
    public void GroupState_AllOn_Off_Partial()
    {
        var tree = Sample();
        Assert.Equal(TriState.On, tree.Groups[0].State);

        tree.SetGroup(tree.Groups[0], false);
        Assert.Equal(TriState.Off, tree.Groups[0].State);

        tree.Groups[0].Leaves[0].Included = true;
        Assert.Equal(TriState.Partial, tree.Groups[0].State);
    }

    [Fact]
    public void SetGroup_CascadesToChildren()
    {
        var tree = Sample();
        tree.SetGroup(tree.Groups[0], false);
        Assert.All(tree.Groups[0].Leaves, l => Assert.False(l.Included));

        tree.SetGroup(tree.Groups[0], true);
        Assert.All(tree.Groups[0].Leaves, l => Assert.True(l.Included));
    }

    [Fact]
    public void ExcludedKeys_ReflectsUncheckedLeaves()
    {
        var tree = Sample("N-WE-KL-DATA2-G");
        Assert.False(tree.Groups[0].Leaves[1].Included);
        Assert.Equal(TriState.Partial, tree.Groups[0].State);

        var excluded = tree.ExcludedKeys();
        Assert.Contains("N-WE-KL-DATA2-G", excluded);
        Assert.Single(excluded);
    }

    [Fact]
    public void Filter_MatchesGroupOrLeaf()
    {
        var tree = Sample();
        Assert.Single(tree.Filter("Riol"));
        Assert.Single(tree.Filter("Put"));
        Assert.Equal(2, tree.Filter(null).Count());
        Assert.Equal(2, tree.Filter("  ").Count());
    }

    [Fact]
    public void UnknownGroup_FallsBackToOverig()
    {
        var tree = CompositionTree.Build(
            new[] { ("K", "L", "") }, new HashSet<string>());
        Assert.Equal("Overig", tree.Groups[0].Label);
    }

    [Fact]
    public void Filter_DoesNotChangeIncludeState()
    {
        var tree = Sample();
        tree.Groups[0].Leaves[1].Included = false;
        _ = tree.Filter("Data").ToList();      // filteren mag selectie niet wissen
        _ = tree.Filter("Put").ToList();
        _ = tree.Filter(null).ToList();
        Assert.False(tree.Groups[0].Leaves[1].Included);
        Assert.Single(tree.ExcludedKeys());
    }

    [Fact]
    public void ExclusionRoundTrip_RebuildsSameState()
    {
        var tree = Sample();
        tree.SetGroup(tree.Groups[0], false);          // Data-groep uit
        var excluded = tree.ExcludedKeys();
        Assert.Equal(3, excluded.Count);

        var rebuilt = Sample(excluded.ToArray());
        Assert.Equal(TriState.Off, rebuilt.Groups[0].State);
        Assert.Equal(TriState.On, rebuilt.Groups[1].State);
        Assert.Equal(excluded, rebuilt.ExcludedKeys());
    }

    [Fact]
    public void RealKlicStructure_GroupsBySoort()
    {
        var items = new[]
        {
            ("N-WE-KL-DATA-G", "Data", "DATA"),
            ("N-WE-KL-DATA2-G", "Data 2", "DATA"),
            ("N-WE-KL-DATA3-G", "Data 3", "DATA"),
            ("N-WE-KL-GAS-G", "Gas", "GAS"),
        };
        var tree = CompositionTree.Build(items, new HashSet<string>());
        Assert.Equal(2, tree.Groups.Count);
        Assert.Equal(3, tree.Groups.Single(g => g.Label == "DATA").Leaves.Count);
        tree.SetGroup(tree.Groups[0], false);
        Assert.Equal(3, tree.ExcludedKeys().Count);
    }
}
