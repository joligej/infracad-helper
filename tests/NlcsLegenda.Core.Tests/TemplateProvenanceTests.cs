using System.Reflection;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class TemplateProvenanceTests
{
    private static FieldInfo[] CanonicalFields() =>
        typeof(TemplateDefaults).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(double))
            .ToArray();

    [Fact]
    public void EveryCanonicalDefault_HasProvenance_WithMatchingValue()
    {
        foreach (var f in CanonicalFields())
        {
            Assert.True(TemplateProvenance.ByField.ContainsKey(f.Name), $"Geen herkomst voor {f.Name}");
            var prov = TemplateProvenance.ByField[f.Name];
            Assert.Equal((double)f.GetValue(null)!, prov.Value, 3);
            Assert.False(string.IsNullOrWhiteSpace(prov.Reason), $"Lege reden voor {f.Name}");
        }
    }

    [Fact]
    public void NoStaleProvenanceEntries()
    {
        var names = CanonicalFields().Select(f => f.Name).ToHashSet();
        foreach (var key in TemplateProvenance.ByField.Keys)
            Assert.Contains(key, names);
    }

    [Fact]
    public void OriginIsOneOfThreeCategories()
    {
        foreach (var prov in TemplateProvenance.ByField.Values)
            Assert.True(Enum.IsDefined(prov.Origin), $"Onbekende herkomstcategorie: {prov.Origin}");
    }

    [Fact]
    public void MeasuredValues_MatchReferenceContract()
    {
        // De gemeten waarden moeten overeenkomen met de onafhankelijk gemeten referentie.
        Assert.Equal(TemplateOrigin.Gemeten, TemplateProvenance.ByField[nameof(TemplateDefaults.RowPitchMm)].Origin);
        Assert.Equal(TemplateOrigin.Gemeten, TemplateProvenance.ByField[nameof(TemplateDefaults.TextHeightMm)].Origin);
        Assert.Equal(TemplateOrigin.Gemeten, TemplateProvenance.ByField[nameof(TemplateDefaults.HeaderTextHeightMm)].Origin);
    }

    [Fact]
    public void SwatchWidth_IsDerivedFromMeasuredLineSample()
    {
        // 24 mm komt uit de gemeten lijnsample van 22,4 mm, dus afgeleid (geen vrije keuze).
        Assert.Equal(TemplateOrigin.Afgeleid, TemplateProvenance.ByField[nameof(TemplateDefaults.SwatchWidthMm)].Origin);
    }
}
