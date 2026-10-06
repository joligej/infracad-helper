using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class DescriptionSourceTests
{
    private static NlcsLayerName Parse(string layer)
    {
        Assert.True(NlcsLayerParser.TryParse(layer, out var p));
        return p!;
    }

    [Fact]
    public void UnknownElement_FallsBackToLayerName()
    {
        var layer = Parse("N-WE-VH-VOLSTREKT_ONBEKEND_ELEMENT-G");
        var entries = LegendGrouping.Build(new[] { layer }, new LegendSettings(), _ => null);

        var entry = Assert.Single(entries);
        Assert.Equal(DescriptionSource.Laagnaam, entry.DescriptionSource);
    }

    [Fact]
    public void KnownElement_UsesCatalog()
    {
        var layer = Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A");
        var entries = LegendGrouping.Build(new[] { layer }, new LegendSettings(), _ => null);

        var entry = Assert.Single(entries);
        Assert.Equal(DescriptionSource.Catalogus, entry.DescriptionSource);
    }

    [Fact]
    public void Override_UsesCatalogSource()
    {
        var layer = Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A");
        var settings = new LegendSettings();
        settings.DescriptionOverrides.Elementen["VH|OPENVERHARDING_BETONSTRAATSTEEN"] =
            new DescriptionEntry { Specifiek = "Mijn eigen tekst" };

        var entries = LegendGrouping.Build(new[] { layer }, settings, _ => null);

        var entry = Assert.Single(entries);
        Assert.Equal(DescriptionSource.Catalogus, entry.DescriptionSource);
        Assert.Equal("Mijn eigen tekst", entry.Description);
    }

    [Fact]
    public void LayerDescription_IsUsedWhenNoOverride()
    {
        var layer = Parse("N-WE-VH-VOLSTREKT_ONBEKEND_ELEMENT-G");
        var entries = LegendGrouping.Build(
            new[] { layer }, new LegendSettings(), _ => "Beschrijving uit tekening");

        var entry = Assert.Single(entries);
        Assert.Equal(DescriptionSource.Laagbeschrijving, entry.DescriptionSource);
        Assert.Equal("Beschrijving uit tekening", entry.Description);
    }

    [Fact]
    public void ManualEntry_HasManualSource()
    {
        var manual = new ManualEntry
        {
            Layer = "N-WE-VH-EIGEN-G",
            Type = NlcsDrawType.Geometrie,
            Description = "Eigen regel"
        };

        var entry = manual.ToLegendEntry();
        Assert.Equal(DescriptionSource.Handmatig, entry.DescriptionSource);
    }

    [Fact]
    public void CustomRuleDescription_HasEigenKoppelingSource()
    {
        // De omschrijving van een eigen-laagregel komt uit de config, niet uit de tekening;
        // de herkomst moet daarom EigenKoppeling zijn, niet Laagbeschrijving.
        var rule = new CustomLayerRule
        {
            Layer = "Eigen kabels", Element = "Datakabel", Type = NlcsDrawType.Geometrie, Description = "Datakabel"
        };
        var settings = new LegendSettings();
        settings.CustomLayerRules.Add(rule);
        var canonical = rule.ToCanonical(string.Empty, rule.Layer);

        var entries = LegendGrouping.Build(
            new[] { canonical }, settings,
            layerDescription: name => name == canonical.LocalName ? rule.Description : null,
            descriptionSourceOf: name => name == canonical.LocalName ? DescriptionSource.EigenKoppeling : null);

        var entry = Assert.Single(entries);
        Assert.Equal("Datakabel", entry.Description);
        Assert.Equal(DescriptionSource.EigenKoppeling, entry.DescriptionSource);
    }

    [Fact]
    public void CustomRuleDescription_CanBeOverriddenPerLegend()
    {
        // Een per-legenda override moet de basisomschrijving van een eigen-laagregel kunnen
        // vervangen, via hetzelfde model als NLCS (herkomst wordt dan Catalogus).
        var rule = new CustomLayerRule
        {
            Layer = "Eigen kabels", Element = "Datakabel", Hoofdgroep = "EI",
            Type = NlcsDrawType.Geometrie, Description = "Datakabel"
        };
        var settings = new LegendSettings();
        settings.CustomLayerRules.Add(rule);
        settings.DescriptionOverrides.Elementen["EI|DATAKABEL"] =
            new DescriptionEntry { Specifiek = "Glasvezelkabel" };
        var canonical = rule.ToCanonical(string.Empty, rule.Layer);

        var entries = LegendGrouping.Build(
            new[] { canonical }, settings,
            layerDescription: name => name == canonical.LocalName ? rule.Description : null,
            descriptionSourceOf: name => name == canonical.LocalName ? DescriptionSource.EigenKoppeling : null);

        var entry = Assert.Single(entries);
        Assert.Equal("Glasvezelkabel", entry.Description);
        Assert.Equal(DescriptionSource.Catalogus, entry.DescriptionSource);
    }
}
