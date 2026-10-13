using System.Text.Json.Nodes;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class RegistryIntegrityTests
{
    private static JsonObject StoredSelection()
    {
        var registry = new LegendRegistry();
        registry.Add(new LegendDefinition
        {
            Id = "vaste-identiteit",
            GroupName = "NLCS-Legenda-test",
            Scope = LegendScope.Selection,
            SourceHandles = new List<string> { "1A" },
            Settings = new LegendSettings { Scale = 500 }
        });
        return JsonNode.Parse(registry.ToJson())!.AsObject();
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("legends")]
    public void MissingRegistryField_IsRejected(string property)
    {
        var json = StoredSelection();
        Assert.True(json.Remove(property));
        Assert.False(LegendRegistry.TryParse(json.ToJsonString(), out _, out var error));
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("scope")]
    [InlineData("sourceHandles")]
    [InlineData("groupName")]
    [InlineData("settings")]
    public void MissingLegendField_DoesNotInventReplacement(string property)
    {
        var json = StoredSelection();
        Assert.True(json["legends"]![0]!.AsObject().Remove(property));
        Assert.False(LegendRegistry.TryParse(json.ToJsonString(), out _, out var error));
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("sourceHandles")]
    [InlineData("groupName")]
    [InlineData("settings")]
    public void NullLegendField_DoesNotReplaceStoredData(string property)
    {
        var json = StoredSelection();
        json["legends"]![0]![property] = null;
        Assert.False(LegendRegistry.TryParse(json.ToJsonString(), out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void NullLegends_IsNotAnEmptyRegistry()
    {
        Assert.False(LegendRegistry.TryParse("""{"schemaVersion":2,"legends":null}""", out _, out _));
    }

    [Fact]
    public void UnknownScope_DoesNotBecomeWholeDrawing()
    {
        var json = StoredSelection();
        json["legends"]![0]!["scope"] = 99;
        Assert.False(LegendRegistry.TryParse(json.ToJsonString(), out _, out _));
    }

    [Fact]
    public void RepeatedRead_PreservesIdentityAndSelection()
    {
        var json = StoredSelection().ToJsonString();
        for (int i = 0; i < 3; i++)
        {
            Assert.True(LegendRegistry.TryParse(json, out var registry, out _));
            var legend = Assert.Single(registry.Legends);
            Assert.Equal("vaste-identiteit", legend.Id);
            Assert.Equal("NLCS-Legenda-test", legend.GroupName);
            Assert.Equal(LegendScope.Selection, legend.Scope);
            Assert.Equal(new[] { "1A" }, legend.SourceHandles);
            Assert.Equal(500, legend.Settings.Scale);
            json = registry.ToJson();
        }
    }
}
