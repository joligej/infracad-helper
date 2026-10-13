using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class InputBoundaryTests
{
    [Theory]
    [InlineData("N03-WE-KL-DATA-G", "N03", NlcsStatus.Nieuw)]
    [InlineData("V-RI-RIO-PUT-INSPECTIE-A-500", "V", NlcsStatus.Vervallen)]
    public void EditorAcceptedLayer_IsAlsoRecognizedByAnalysis(string name, string code, NlcsStatus status)
    {
        Assert.True(NlcsLayerComponents.TryParse(name, out var components, out _));
        Assert.Empty(components.Validate());
        Assert.True(NlcsLayerParser.TryParse(components.Compose(), out var parsed));
        Assert.Equal(code, parsed.StatusCode);
        Assert.Equal(status, parsed.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnsupportedOldRegistry_IsNotTreatedAsCurrent(int schema)
    {
        Assert.False(LegendRegistry.TryParse($$"""{"schemaVersion":{{schema}}}""", out var registry, out _));
        Assert.False(registry.IsSupported);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999999999999999999999")]
    [InlineData("٢٠٠")]
    public void InvalidLayerScale_DoesNotAbortDrawingAnalysis(string scale)
    {
        Assert.False(NlcsLayerParser.TryParse($"N-WE-VH-WEG-G-{scale}", out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void DescriptionRoundtrip_RemainsCaseInsensitive()
    {
        const string json = """{"elementen":{"vh|weg":{"specifiek":"Rijbaan"}}}""";
        NlcsLayerParser.TryParse("N-WE-VH-WEG-G", out var layer);
        Assert.Equal("Rijbaan", DescriptionCatalog.FromJson(json).Describe(layer!, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DescriptionVariant_UsesMostSpecificPrefix(bool reverse)
    {
        var catalog = new DescriptionCatalog();
        var entries = new[] { ("VH|WEG", "Weg"), ("VH|WEG_ASFALT", "Asfaltverharding") };
        foreach (var (key, value) in reverse ? entries.Reverse() : entries)
            catalog.Elementen[key] = new DescriptionEntry { Specifiek = value };
        NlcsLayerParser.TryParse("N-WE-VH-WEG_ASFALT_ROOD-G", out var layer);
        Assert.Equal("Asfaltverharding Rood", catalog.Describe(layer!, false));
    }

    [Fact]
    public void DescriptionCaseCollision_UsesLastValueWithoutThrowing()
    {
        const string json = """{"elementen":{"VH|WEG":{"specifiek":"Oud"},"vh|weg":{"specifiek":"Nieuw"}}}""";
        Assert.True(DescriptionCatalog.TryParse(json, out var catalog));
        Assert.Single(catalog.Elementen);
        Assert.Equal("Nieuw", catalog.Elementen["VH|WEG"].Specifiek);
    }

    [Fact]
    public void NullDescriptionEntry_IsRejected()
    {
        Assert.False(DescriptionCatalog.TryParse("""{"elementen":{"VH|WEG":null}}""", out _));
    }

    [Theory]
    [InlineData("manualEntries")]
    [InlineData("blankEntries")]
    [InlineData("customStatuses")]
    [InlineData("customLayerRules")]
    public void NullProfileItem_IsRejectedBeforeItCanBeSaved(string property)
    {
        Assert.False(LegendSettings.TryParse($$"""{"{{property}}":[null]}""", out _));
    }

    [Fact]
    public void FutureProfile_IsRejected()
    {
        Assert.False(LegendSettings.TryParse("""{"schemaVersion":999}""", out _));
    }

    [Fact]
    public void NullCustomStatusMembers_AreNormalizedLikeOtherCollections()
    {
        Assert.True(LegendSettings.TryParse("""{"customStatuses":[{"name":"Fase 1","members":null}]}""", out var settings));
        Assert.Empty(settings.Clone().CustomStatuses[0].Members);
    }

    [Fact]
    public void NullRegistryEntry_IsRejectedWithoutThrowing()
    {
        Assert.False(LegendRegistry.TryParse("""{"legends":[null]}""", out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void DuplicateLegendIdentity_IsRejected()
    {
        var registry = new LegendRegistry();
        var legend = new LegendDefinition { Name = "A", GroupName = LegendRegistry.NewGroupName() };
        registry.Add(legend);
        registry.Add(legend.Clone());
        Assert.False(LegendRegistry.TryParse(registry.ToJson(), out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void CorruptProfileLoad_DoesNotReturnDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nlcs_load_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Ontwerp.json"), "{ kapot");
            Assert.Throws<InvalidDataException>(() => LegendPresets.Load(directory, "Ontwerp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void CorruptProfileImport_PreservesExistingProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nlcs_import_" + Guid.NewGuid().ToString("N"));
        try
        {
            LegendPresets.Save(directory, "Ontwerp", new LegendSettings { Scale = 500 });
            var input = Path.Combine(directory, "input.json");
            File.WriteAllText(input, """{"manualEntries":[null]}""");
            Assert.False(LegendPresets.TryImport(directory, input, "Ontwerp", out _));
            Assert.Equal(500, LegendPresets.Load(directory, "Ontwerp")!.Scale);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
