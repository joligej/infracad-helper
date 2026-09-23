using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendSettingsCloneTests
{
    [Fact]
    public void Clone_ProducesIndependentCollections()
    {
        var a = new LegendSettings();
        a.ExcludedEntries.Add("N|WE|VH|X");
        a.TextOverrides["EL"] = "Tekst";
        a.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-EIGEN-G", Description = "Eigen" });
        a.CustomStatuses.Add(new CustomStatus { Name = "Onder voorbehoud", Members = { "N|WE|RI|R" } });

        var b = a.Clone();

        Assert.NotSame(a.ExcludedEntries, b.ExcludedEntries);
        Assert.NotSame(a.IncludedStatuses, b.IncludedStatuses);
        Assert.NotSame(a.IncludedDrawTypes, b.IncludedDrawTypes);
        Assert.NotSame(a.TextOverrides, b.TextOverrides);
        Assert.NotSame(a.XrefInclusion, b.XrefInclusion);
        Assert.NotSame(a.ManualEntries, b.ManualEntries);
        Assert.NotSame(a.CustomStatuses, b.CustomStatuses);
        Assert.NotSame(a.ManualEntries[0], b.ManualEntries[0]);
        Assert.NotSame(a.CustomStatuses[0], b.CustomStatuses[0]);
        Assert.NotSame(a.CustomStatuses[0].Members, b.CustomStatuses[0].Members);
    }

    [Fact]
    public void MutatingClone_DoesNotAffectSource()
    {
        var a = new LegendSettings { Scale = 200 };
        a.ExcludedEntries.Add("keep");
        a.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-X-G", Description = "A" });

        var b = a.Clone();
        b.Scale = 500;
        b.ExcludedEntries.Add("only-b");
        b.ExcludedEntries.Remove("keep");
        b.ManualEntries[0].Description = "B";
        b.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-Y-G", Description = "extra" });

        Assert.Equal(200, a.Scale);
        Assert.Contains("keep", a.ExcludedEntries);
        Assert.DoesNotContain("only-b", a.ExcludedEntries);
        Assert.Single(a.ManualEntries);
        Assert.Equal("A", a.ManualEntries[0].Description);
    }

    [Fact]
    public void MutatingSource_DoesNotAffectClone()
    {
        var a = new LegendSettings();
        a.CustomStatuses.Add(new CustomStatus { Name = "S", Members = { "x" } });

        var b = a.Clone();
        a.CustomStatuses[0].Members.Add("y");
        a.CustomStatuses.Add(new CustomStatus { Name = "T" });

        Assert.Single(b.CustomStatuses);
        Assert.Single(b.CustomStatuses[0].Members);
    }

    [Fact]
    public void Clone_PreservesCaseInsensitiveSemantics()
    {
        var a = new LegendSettings();
        a.XrefInclusion["xref-a"] = true;
        a.ExcludedEntries.Add("N|WE|VH|Tegel");

        var b = a.Clone();

        Assert.True(b.IsXrefIncluded("XREF-A"));
        Assert.Contains("n|we|vh|tegel", b.ExcludedEntries);
    }

    [Fact]
    public void FromJson_RestoresCaseInsensitiveComparers()
    {
        var a = new LegendSettings();
        a.XrefInclusion["Xref-B"] = true;
        a.ExcludedEntries.Add("N|WE|RI|Riool");
        a.TextOverrides["Element"] = "Waarde";

        var restored = LegendSettings.FromJson(a.ToJson());

        Assert.True(restored.IsXrefIncluded("xref-b"));
        Assert.Contains("n|we|ri|riool", restored.ExcludedEntries);
        Assert.True(restored.TextOverrides.ContainsKey("ELEMENT"));
    }
}
