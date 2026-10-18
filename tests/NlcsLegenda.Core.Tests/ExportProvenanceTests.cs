using System.Text.Json;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class ExportProvenanceTests
{
    private static LegendEntry Entry() => new()
    {
        Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "KL", Element = "WATER",
        Description = "Waterleiding", Netbeheerders = new[] { "Net A" },
        KlicDescriptionFields = new[] { "Materiaal" },
        KlicObjects = new[] { new KlicObjectData("bron.dwg", "AB", "KLIC", new[] {
            new KlicRecord(KlicSource.InfraCAD, "KLIC_LeidingLijn", new Dictionary<string, string> { ["Materiaal"] = "PE" })
        }, Array.Empty<string>(), KlicSource.InfraCAD) },
        SourceLayers = new[] {
            new NlcsLayerSource("B-WE-KL-WATER-G", "KLIC|B-WE-KL-WATER-G", "KLIC"),
            new NlcsLayerSource("B-WE-KL-WATER-G", "KLIC|B-WE-KL-WATER-G", "KLIC"),
            new NlcsLayerSource("B-WE-KL-WATER-G", "DETAIL|B-WE-KL-WATER-G", "DETAIL") }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CsvIncludesDisplayedDescriptionAndDistinctSourceLayers(bool batch)
    {
        var entry = Entry();
        var entries = new[] { entry };
        var drawings = new[] { ("ontwerp.dwg", (IReadOnlyList<LegendEntry>)entries) };
        string csv = batch ? LegendExport.ToCombinedCsv(drawings, klicFields: new[] { "Materiaal" })
            : LegendExport.ToCsv(entries, klicFields: new[] { "Materiaal" });
        Assert.EndsWith("Legenda-omschrijving;Bronlagen", csv.Split('\n')[0].TrimEnd('\r'));
        Assert.Contains("Waterleiding (Net A)", csv);
        Assert.Contains(entry.DisplayDescription, csv);
        Assert.Contains("\"KLIC|B-WE-KL-WATER-G; DETAIL|B-WE-KL-WATER-G\"", csv);
        Assert.DoesNotContain("KLIC|B-WE-KL-WATER-G; KLIC|B-WE-KL-WATER-G", csv);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonKeepsOriginalDescriptionAndSourceArray(bool batch)
    {
        var entry = Entry();
        var entries = new[] { entry };
        var drawings = new[] { ("ontwerp.dwg", (IReadOnlyList<LegendEntry>)entries) };
        using var json = JsonDocument.Parse(batch ? LegendExport.ToCombinedJson(drawings) : LegendExport.ToJson(entries));
        var row = json.RootElement[0];
        Assert.Equal("Waterleiding", row.GetProperty("omschrijving").GetString());
        Assert.Equal(entry.DisplayDescription, row.GetProperty("legendaOmschrijving").GetString());
        Assert.Equal(new[] { "KLIC|B-WE-KL-WATER-G", "DETAIL|B-WE-KL-WATER-G" },
            row.GetProperty("bronlagen").EnumerateArray().Select(value => value.GetString()));
    }
}
