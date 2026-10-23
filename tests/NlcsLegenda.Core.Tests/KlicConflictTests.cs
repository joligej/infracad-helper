using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class KlicConflictTests
{
    private static KlicRecord Record(string table, params (string Key, string Value)[] fields) =>
        new(KlicSource.InfraCAD, table, fields.ToDictionary(f => f.Key, f => f.Value));

    [Fact]
    public void ConflictsKeepFieldOrderAndRespectTableAndValueRules()
    {
        var data = new KlicObjectData("", "A", "", new[] {
            Record("T", ("Materiaal", " PE "), ("DiameterM", "0.3000"), ("Thema", "Gas"), ("Label", "")),
            Record("OTHER", ("Materiaal", "Staal"), ("DiameterM", "9")),
            Record("t", ("Thema", "Water"), ("Materiaal", "pe"), ("DiameterM", "3e-1"), ("Label", "tekst")),
            Record("T", ("Materiaal", "PVC"), ("Label", "derde waarde"))
        }, Array.Empty<string>(), KlicSource.PropertySets);
        Assert.Equal(new[] { "Materiaal", "Thema", "Label" }, data.Conflicts);
        Assert.Equal("Bronverschil: Bronlabel; Bronverschil: Materiaal; Bronverschil: Thema", KlicSummary.Issues(new[] { data }));
    }

    [Fact]
    public void FieldLookupKeepsTheSourceDictionaryComparison()
    {
        var first = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Materiaal"] = "PE" };
        var second = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["materiaal"] = "PVC" };
        var data = new KlicObjectData("", "A", "", new[] {
            new KlicRecord(KlicSource.InfraCAD, "T", first), new KlicRecord(KlicSource.ObjectData, "t", second)
        }, Array.Empty<string>(), KlicSource.InfraCAD);
        Assert.Equal(new[] { "Materiaal", "materiaal" }, data.Conflicts);
    }

    [Fact]
    public void ConflictCheckReadsChangedRecordsAgain()
    {
        var values = new Dictionary<string, string> { ["Materiaal"] = "PE" };
        var data = new KlicObjectData("", "A", "", new[] {
            Record("T", ("Materiaal", "PE")), new KlicRecord(KlicSource.ObjectData, "T", values)
        }, Array.Empty<string>(), KlicSource.InfraCAD);
        Assert.Empty(data.Conflicts);
        values["Materiaal"] = "PVC";
        Assert.Equal(new[] { "Materiaal" }, data.Conflicts);
        Assert.Empty((data with { Records = Array.Empty<KlicRecord>() }).Conflicts);
    }
}
