using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendDomainTests
{
    [Fact]
    public void ManualEntry_Clone_IsIndependent()
    {
        var a = new ManualEntry { Layer = "N-WE-VH-X-G", Type = NlcsDrawType.Vlakvulling, Description = "A", SymbolBlock = "B" };
        var b = a.Clone();
        b.Description = "B";
        b.Type = NlcsDrawType.Symbool;
        Assert.Equal("A", a.Description);
        Assert.Equal(NlcsDrawType.Vlakvulling, a.Type);
    }

    [Fact]
    public void CustomStatus_Clone_IsIndependent()
    {
        var a = new CustomStatus { Name = "S", Members = { "x", "y" } };
        var b = a.Clone();
        b.Members.Add("z");
        b.Name = "T";
        Assert.Equal(2, a.Members.Count);
        Assert.Equal("S", a.Name);
    }

    [Theory]
    [InlineData(LegendScope.WholeDrawing, "hele tekening")]
    [InlineData(LegendScope.Selection, "selectie")]
    public void Scope_ToDisplay(LegendScope scope, string expected) =>
        Assert.Equal(expected, scope.ToDisplay());

    [Fact]
    public void Registry_MixedScopes_Roundtrip()
    {
        var reg = new LegendRegistry();
        reg.Add(new LegendDefinition { Name = "Heel", Scope = LegendScope.WholeDrawing, GroupName = "g1" });
        reg.Add(new LegendDefinition
        {
            Name = "Sel",
            Scope = LegendScope.Selection,
            GroupName = "g2",
            SourceHandles = { "A", "1B", "2C3" }
        });

        Assert.True(LegendRegistry.TryParse(reg.ToJson(), out var restored, out _));
        Assert.Equal(LegendScope.WholeDrawing, restored.Legends[0].Scope);
        Assert.Equal(LegendScope.Selection, restored.Legends[1].Scope);
        Assert.Equal(3, restored.Legends[1].SourceHandles.Count);
    }

    [Fact]
    public void Registry_NormalizesNullCollections()
    {
        // JSON zonder legends-array en zonder sourceHandles.
        const string json = """{ "schemaVersion": 1 }""";
        Assert.True(LegendRegistry.TryParse(json, out var reg, out _));
        Assert.NotNull(reg.Legends);
        Assert.Empty(reg.Legends);
    }

    [Fact]
    public void Definition_KeepsIdAcrossRename()
    {
        var def = new LegendDefinition { Name = "Oud", GroupName = "g" };
        var id = def.Id;
        def.Name = "Nieuw";
        Assert.Equal(id, def.Id);
    }
}
