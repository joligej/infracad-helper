using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Eigen-laagregels moeten via dezelfde grouping-pipeline een gelijkwaardig LegendEntry opleveren
// als NLCS-lagen: eigen omschrijving, status, type, en meerdere regels met dezelfde Element-sleutel
// combineren tot één item.
public class CustomLayerRuleTests
{
    private static LegendEntry BuildSingle(CustomLayerRule rule, LayerMetric metric)
    {
        var settings = new LegendSettings();
        settings.CustomLayerRules.Add(rule);
        var canonical = rule.ToCanonical(string.Empty);
        var metrics = new Dictionary<string, LayerMetric> { [canonical.LocalName] = metric };
        var entries = LegendGrouping.Build(
            new[] { canonical }, settings,
            layerDescription: name => name == canonical.LocalName ? rule.Description : null,
            metrics: metrics);
        return Assert.Single(entries);
    }

    [Fact]
    public void CustomRule_ProducesEntry_WithOwnDescriptionAndLength()
    {
        var rule = new CustomLayerRule
        {
            Layer = "Eigen kabels",
            Element = "Datakabel",
            Type = NlcsDrawType.Geometrie,
            Status = NlcsStatus.Nieuw,
            Description = "Datakabel",
            QuantityMode = CustomQuantityMode.Lengte
        };
        var entry = BuildSingle(rule, new LayerMetric(2, 12.0, 0));
        Assert.Equal("Datakabel", entry.Description);
        Assert.Equal(NlcsStatus.Nieuw, entry.Status);
        Assert.Equal(QuantityKind.Length, entry.QuantityType);
        Assert.Equal(12.0, entry.Metric.Length, 3);
    }

    [Fact]
    public void CustomRule_DefaultDisciplineHoofdgroep_AreNotExcludedByXxAl()
    {
        // XX/AL staan standaard in de uitsluitingslijsten; een eigen regel (EI/EI) mag niet
        // per ongeluk wegvallen.
        var settings = new LegendSettings();
        var rule = new CustomLayerRule { Layer = "Eigen laag", Element = "E", Type = NlcsDrawType.Geometrie, Description = "E" };
        Assert.True(settings.IsIncluded(rule.ToCanonical(string.Empty)));
    }

    [Fact]
    public void MultipleRules_SameElement_CombineIntoOneEntry()
    {
        var settings = new LegendSettings();
        var line = new CustomLayerRule { Layer = "Eigen asfalt lijn", Element = "Eigen asfalt", Type = NlcsDrawType.Geometrie, Description = "Eigen asfalt" };
        var hatch = new CustomLayerRule { Layer = "Eigen asfalt arcering", Element = "Eigen asfalt", Type = NlcsDrawType.Arcering, Description = "Eigen asfalt" };
        settings.CustomLayerRules.Add(line);
        settings.CustomLayerRules.Add(hatch);

        var cl = line.ToCanonical(string.Empty);
        var ch = hatch.ToCanonical(string.Empty);
        var entries = LegendGrouping.Build(new[] { cl, ch }, settings,
            layerDescription: _ => "Eigen asfalt");

        var entry = Assert.Single(entries);
        Assert.NotNull(entry.GeometryLayer);
        Assert.NotNull(entry.HatchLayer);
    }

    [Fact]
    public void SpecificXrefScope_QualifiesCanonicalLayer_SoSourcesDoNotCollide()
    {
        var local = new CustomLayerRule { Layer = "Eigen kabels", Element = "Lokaal", Scope = CustomSourceScope.Local };
        var inXref = new CustomLayerRule { Layer = "Eigen kabels", Element = "Xref", Scope = CustomSourceScope.SpecificXref, XrefName = "ref1" };
        Assert.NotEqual(local.CanonicalLayer, inXref.CanonicalLayer);
        Assert.Equal("Eigen kabels", local.CanonicalLayer);
        Assert.Equal("ref1|Eigen kabels", inXref.CanonicalLayer);
    }

    [Theory]
    [InlineData("MIJN_KABELS")]
    [InlineData("Mijn kabels")]
    [InlineData("Kabels opdrachtgever")]
    [InlineData("\u00c9l\u00e9ment sp\u00e9cial")]
    [InlineData("2027_concept")]
    [InlineData("Speciale-laag_01")]
    public void ArbitraryValidLayerNames_AreAccepted(string layer)
    {
        var rule = new CustomLayerRule { Layer = layer, Element = "E", Description = "E" };
        Assert.True(rule.IsValid);
    }

    [Fact]
    public void ScopeMatching_RespectsSource()
    {
        var local = new CustomLayerRule { Layer = "L", Element = "E", Scope = CustomSourceScope.Local };
        Assert.True(local.Matches("L", string.Empty));
        Assert.False(local.Matches("L", "ref1"));

        var any = new CustomLayerRule { Layer = "L", Element = "E", Scope = CustomSourceScope.AnySource };
        Assert.True(any.Matches("L", string.Empty));
        Assert.True(any.Matches("L", "ref1"));

        var specific = new CustomLayerRule { Layer = "L", Element = "E", Scope = CustomSourceScope.SpecificXref, XrefName = "ref1" };
        Assert.True(specific.Matches("L", "ref1"));
        Assert.False(specific.Matches("L", "ref2"));
        Assert.False(specific.Matches("L", string.Empty));
    }
}
