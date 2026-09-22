using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class ElementTypeFilterTests
{
    private static List<NlcsLayerName> Parse(params string[] layers)
    {
        var list = new List<NlcsLayerName>();
        foreach (var l in layers)
            if (NlcsLayerParser.TryParse(l, out var p))
                list.Add(p!);
        return list;
    }

    // Eén element met alle vier de tekentypen als aparte lagen.
    private static string[] AllTypeLayers() => new[]
    {
        "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G",   // geometrie
        "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A",   // arcering
        "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-V",   // vlakvulling
        "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-S"    // symbool
    };

    [Fact]
    public void AllTypesOn_ByDefault_KeepsEntry()
    {
        var s = new LegendSettings();
        Assert.True(s.ToonGeometrie && s.ToonVlakken && s.ToonArceringen && s.ToonVlakvullingen && s.ToonSymbolen);

        var entries = LegendGrouping.Build(Parse(AllTypeLayers()), s);
        var e = Assert.Single(entries);
        Assert.True(e.LayersByType.ContainsKey(NlcsDrawType.Geometrie));
        Assert.True(e.LayersByType.ContainsKey(NlcsDrawType.Arcering));
        Assert.True(e.LayersByType.ContainsKey(NlcsDrawType.Vlakvulling));
        Assert.True(e.LayersByType.ContainsKey(NlcsDrawType.Symbool));
    }

    [Theory]
    [InlineData(NlcsDrawType.Geometrie)]
    [InlineData(NlcsDrawType.Arcering)]
    [InlineData(NlcsDrawType.Vlakvulling)]
    [InlineData(NlcsDrawType.Symbool)]
    public void DisablingOneType_RemovesOnlyThatRepresentation(NlcsDrawType off)
    {
        var s = new LegendSettings();
        s.IncludedDrawTypes.Remove(off);

        var entries = LegendGrouping.Build(Parse(AllTypeLayers()), s);
        var e = Assert.Single(entries);
        Assert.False(e.LayersByType.ContainsKey(off));
        // De overige typen blijven bestaan.
        foreach (var t in new[] { NlcsDrawType.Geometrie, NlcsDrawType.Arcering, NlcsDrawType.Vlakvulling, NlcsDrawType.Symbool })
            if (t != off)
                Assert.True(e.LayersByType.ContainsKey(t));
    }

    [Fact]
    public void DisablingClosedSurface_RemovesGvEntry()
    {
        var s = new LegendSettings();
        s.ToonVlakken = false;

        var entries = LegendGrouping.Build(Parse("B-WE-OG-TERREIN_ERF-GV"), s);
        Assert.Empty(entries);
    }

    [Fact]
    public void DisablingAllRelevantTypes_RemovesEntry()
    {
        var s = new LegendSettings();
        foreach (var t in new[] { NlcsDrawType.Geometrie, NlcsDrawType.Arcering, NlcsDrawType.Vlakvulling, NlcsDrawType.Symbool })
            s.IncludedDrawTypes.Remove(t);

        var entries = LegendGrouping.Build(Parse(AllTypeLayers()), s);
        Assert.Empty(entries);
    }

    [Fact]
    public void GeometryOff_HatchOn_KeepsEntry()
    {
        var s = new LegendSettings();
        s.ToonGeometrie = false;

        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G", "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), s);
        var e = Assert.Single(entries);
        Assert.False(e.LayersByType.ContainsKey(NlcsDrawType.Geometrie));
        Assert.True(e.HasHatch);
    }

    [Fact]
    public void GeometryOff_FillOn_KeepsEntry()
    {
        var s = new LegendSettings();
        s.ToonGeometrie = false;

        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-GESLOTENVERHARDING_ASFALT-G", "N-WE-VH-GESLOTENVERHARDING_ASFALT-V"), s);
        var e = Assert.Single(entries);
        Assert.False(e.LayersByType.ContainsKey(NlcsDrawType.Geometrie));
        Assert.Equal(NlcsDrawType.Vlakvulling, e.LayersByType.Keys.Single());
        Assert.True(e.HasHatch);
    }

    [Fact]
    public void RepresentativeRecomputed_WhenPreferredTypeOff()
    {
        var s = new LegendSettings();
        s.ToonGeometrie = false;
        var settingsWith = new LegendSettings();
        settingsWith.TextOverrides["OPENVERHARDING_BETONSTRAATSTEEN"] = "Steen";
        s.TextOverrides["OPENVERHARDING_BETONSTRAATSTEEN"] = "Steen";

        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G", "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-S"), s);
        var e = Assert.Single(entries);
        // Geometrie is weg; het symbool wordt de representatie en de regel blijft geldig.
        Assert.False(e.LayersByType.ContainsKey(NlcsDrawType.Geometrie));
        Assert.True(e.LayersByType.ContainsKey(NlcsDrawType.Symbool));
    }

    [Fact]
    public void Quantities_ExcludeHiddenType()
    {
        var s = new LegendSettings();
        var line = Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G")[0];
        var fill = Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-V")[0];
        var metrics = new Dictionary<string, LayerMetric>
        {
            [line.LocalName] = new LayerMetric(0, 100.0, 0),
            [fill.LocalName] = new LayerMetric(0, 0, 500.0)
        };

        // Met vlakvulling uit telt alleen de lengte van de lijn mee.
        s.ToonVlakvullingen = false;
        var entries = LegendGrouping.Build(new[] { line, fill }, s, _ => null, metrics);
        var e = Assert.Single(entries);
        Assert.Equal(100.0, e.Metric.Length, 3);
        Assert.Equal(0.0, e.Metric.Area);
    }

    [Fact]
    public void SuffixA_And_V_FilterIndependently()
    {
        var arcering = "N-WE-GW-TALUDARCERING_KORT-A";
        var vulling = "N-WE-VH-GESLOTENVERHARDING_ASFALT-V";

        var offA = new LegendSettings { ToonArceringen = false };
        var entriesNoA = LegendGrouping.Build(Parse(arcering, vulling), offA);
        Assert.Single(entriesNoA);   // arcering weg, vulling blijft
        Assert.Equal("GESLOTENVERHARDING_ASFALT", entriesNoA[0].Element);

        var offV = new LegendSettings { ToonVlakvullingen = false };
        var entriesNoV = LegendGrouping.Build(Parse(arcering, vulling), offV);
        Assert.Single(entriesNoV);   // vulling weg, arcering blijft
        Assert.Equal("TALUDARCERING_KORT", entriesNoV[0].Element);
    }

    [Fact]
    public void ManualEntry_RespectsTypeFilter()
    {
        var s = new LegendSettings { ToonGeometrie = false };
        s.ManualEntries.Add(new ManualEntry
        {
            Layer = "N-WE-VH-EIGEN-G",
            Type = NlcsDrawType.Geometrie,
            Description = "Eigen lijn"
        });

        var entries = LegendGrouping.Build(Array.Empty<NlcsLayerName>(), s);
        Assert.Empty(entries);

        s.ToonGeometrie = true;
        var entries2 = LegendGrouping.Build(Array.Empty<NlcsLayerName>(), s);
        Assert.Single(entries2);
    }

    [Fact]
    public void CustomStatus_PlusTypeFilter()
    {
        var s = new LegendSettings { ToonArceringen = false };
        var arcering = Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A")[0];
        s.CustomStatuses.Add(new CustomStatus
        {
            Name = "Onder voorbehoud",
            Members = { LegendSettings.EntryKey(arcering) }
        });

        // Ook al is de regel aan een eigen status toegewezen, de uitgeschakelde
        // elementsoort verwijdert de enige representatie -> regel verdwijnt.
        var entries = LegendGrouping.Build(new[] { arcering }, s);
        Assert.Empty(entries);
    }

    [Fact]
    public void Exclusion_WinsOverTypeFilter()
    {
        var s = new LegendSettings();
        var line = Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G")[0];
        s.ExcludedEntries.Add(LegendSettings.EntryKey(line));

        var entries = LegendGrouping.Build(new[] { line }, s);
        Assert.Empty(entries);
    }

    [Fact]
    public void OldJson_WithoutNewFields_EnablesAllTypes()
    {
        // Instellingen-JSON zoals vóór deze feature: geen includedDrawTypes.
        const string json = """
        { "scale": 200, "includedStatuses": ["Nieuw"] }
        """;
        var s = LegendSettings.FromJson(json);

        Assert.True(s.ToonGeometrie);
        Assert.True(s.ToonVlakken);
        Assert.True(s.ToonArceringen);
        Assert.True(s.ToonVlakvullingen);
        Assert.True(s.ToonSymbolen);
    }

    [Fact]
    public void Json_Roundtrip_PreservesTypeFilter()
    {
        var s = new LegendSettings { ToonArceringen = false, ToonSymbolen = false };
        var restored = LegendSettings.FromJson(s.ToJson());

        Assert.False(restored.ToonArceringen);
        Assert.False(restored.ToonSymbolen);
        Assert.True(restored.ToonGeometrie);
        Assert.True(restored.ToonVlakken);
        Assert.True(restored.ToonVlakvullingen);
    }

    [Fact]
    public void DisabledDrawTypes_ListsOnlyDisabled()
    {
        var s = new LegendSettings { ToonArceringen = false, ToonSymbolen = false };
        var disabled = s.DisabledDrawTypes();
        Assert.Equal(2, disabled.Count);
        Assert.Contains(NlcsDrawType.Arcering, disabled);
        Assert.Contains(NlcsDrawType.Symbool, disabled);
    }

    [Fact]
    public void Filtering_IsDeterministic()
    {
        var s = new LegendSettings { ToonGeometrie = false };
        var layers = AllTypeLayers();

        var first = LegendGrouping.Build(Parse(layers), s).Single();
        var second = LegendGrouping.Build(Parse(layers), s).Single();
        Assert.Equal(
            string.Join(",", first.LayersByType.Keys.OrderBy(k => k)),
            string.Join(",", second.LayersByType.Keys.OrderBy(k => k)));
    }
}
