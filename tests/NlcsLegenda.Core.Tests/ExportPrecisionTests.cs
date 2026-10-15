using System.Globalization;
using System.Text.Json;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class ExportPrecisionTests
{
    [Theory]
    [InlineData(0, 12, 45)]
    [InlineData(2, 12.35, 45.12)]
    [InlineData(4, 12.3457, 45.1235)]
    [InlineData(6, 12.345678, 45.123456)]
    public void CsvAndJsonUseChosenPrecision(int decimals, double length, double area)
    {
        var entries = new[] { new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH", Element = "WEG", Description = "Weg",
            Metric = new LayerMetric(1, 12.345678, 45.123456) } };
        var csv = LegendExport.ToCsv(entries, decimals).Trim().Split('\n')[1].Split(';');
        Assert.Equal(length, double.Parse(csv[8], CultureInfo.InvariantCulture));
        Assert.Equal(area, double.Parse(csv[9], CultureInfo.InvariantCulture));
        using var json = JsonDocument.Parse(LegendExport.ToJson(entries, decimals));
        Assert.Equal(length, json.RootElement[0].GetProperty("lengteM").GetDouble());
        Assert.Equal(area, json.RootElement[0].GetProperty("oppervlakM2").GetDouble());
        var drawings = new[] { ("weg.dwg", (IReadOnlyList<LegendEntry>)entries) };
        var combined = LegendExport.ToCombinedCsv(drawings, decimals).Trim().Split('\n')[1].Split(';');
        Assert.Equal(length, double.Parse(combined[9], CultureInfo.InvariantCulture));
        Assert.Equal(area, double.Parse(combined[10], CultureInfo.InvariantCulture));
    }
}
