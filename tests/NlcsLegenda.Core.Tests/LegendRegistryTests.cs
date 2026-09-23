using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendRegistryTests
{
    private static LegendDefinition MakeLegend(string name, LegendScope scope = LegendScope.WholeDrawing) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Scope = scope,
        GroupName = LegendRegistry.NewGroupName(),
        CreatedWithVersion = "1.14.0"
    };

    [Fact]
    public void Roundtrip_PreservesDefinitions()
    {
        var reg = new LegendRegistry();
        var a = MakeLegend("Legenda 1");
        a.Settings.Scale = 250;
        a.Settings.ToonArceringen = false;
        var b = MakeLegend("Rijbaan", LegendScope.Selection);
        b.SourceHandles.AddRange(new[] { "2A0", "2A1", "2A2" });
        reg.Add(a);
        reg.Add(b);

        Assert.True(LegendRegistry.TryParse(reg.ToJson(), out var restored, out _));
        Assert.Equal(2, restored.Legends.Count);
        var ra = restored.FindById(a.Id)!;
        Assert.Equal(250, ra.Settings.Scale);
        Assert.False(ra.Settings.ToonArceringen);
        var rb = restored.FindById(b.Id)!;
        Assert.Equal(LegendScope.Selection, rb.Scope);
        Assert.Equal(new[] { "2A0", "2A1", "2A2" }, rb.SourceHandles);
    }

    [Fact]
    public void UniqueIds_AndNamesAreNotTheKey()
    {
        var reg = new LegendRegistry();
        var a = MakeLegend("Zelfde naam");
        var b = MakeLegend("Zelfde naam");
        reg.Add(a);
        reg.Add(b);

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.GroupName, b.GroupName);
        Assert.Same(a, reg.FindById(a.Id));
        Assert.Same(b, reg.FindById(b.Id));
    }

    [Fact]
    public void NextDefaultName_IsDeterministicAfterRemoval()
    {
        var reg = new LegendRegistry();
        reg.Add(MakeLegend(reg.NextDefaultName())); // Legenda 1
        reg.Add(MakeLegend(reg.NextDefaultName())); // Legenda 2
        reg.Add(MakeLegend(reg.NextDefaultName())); // Legenda 3
        Assert.Equal(new[] { "Legenda 1", "Legenda 2", "Legenda 3" }, reg.Legends.Select(l => l.Name));

        reg.Remove(reg.Legends[1].Id); // verwijder Legenda 2
        Assert.Equal("Legenda 2", reg.NextDefaultName()); // laagste vrije nummer
    }

    [Fact]
    public void TwoLegends_AreIndependent()
    {
        var reg = new LegendRegistry();
        var a = MakeLegend("A");
        var b = MakeLegend("B");
        a.Settings.ToonSymbolen = false;
        reg.Add(a);
        reg.Add(b);

        // A's instelling verandert B niet.
        Assert.False(a.Settings.ToonSymbolen);
        Assert.True(b.Settings.ToonSymbolen);
        // Delete A raakt B niet.
        reg.Remove(a.Id);
        Assert.Null(reg.FindById(a.Id));
        Assert.NotNull(reg.FindById(b.Id));
    }

    [Fact]
    public void RenameLegend_KeepsId()
    {
        var reg = new LegendRegistry();
        var a = MakeLegend("A");
        var originalId = a.Id;
        reg.Add(a);

        a.Name = "Nieuwe naam";
        Assert.Equal(originalId, reg.FindById(originalId)!.Id);
        Assert.Equal("Nieuwe naam", reg.FindById(originalId)!.Name);
    }

    [Fact]
    public void TryParse_InvalidJson_Fails()
    {
        Assert.False(LegendRegistry.TryParse("{ kapot ", out _, out var err));
        Assert.NotEqual(string.Empty, err);
    }

    [Fact]
    public void TryParse_FutureSchema_ReportsUnsupported()
    {
        var json = $$"""{ "schemaVersion": {{LegendRegistry.CurrentSchemaVersion + 5}}, "legends": [] }""";
        Assert.False(LegendRegistry.TryParse(json, out var reg, out var err));
        Assert.False(reg.IsSupported);
        Assert.Contains("niet ondersteund", err);
    }

    [Fact]
    public void DefinitionClone_IsIndependent()
    {
        var a = MakeLegend("A", LegendScope.Selection);
        a.SourceHandles.Add("1F0");
        a.Settings.ExcludedEntries.Add("x");

        var copy = a.Clone();
        copy.SourceHandles.Add("2F0");
        copy.Settings.ExcludedEntries.Add("y");
        copy.Name = "B";

        Assert.Single(a.SourceHandles);
        Assert.Single(a.Settings.ExcludedEntries);
        Assert.Equal("A", a.Name);
    }
}
