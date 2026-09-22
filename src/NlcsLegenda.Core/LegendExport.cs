using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NlcsLegenda.Core;

public static class LegendExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string ToCsv(IEnumerable<LegendEntry> entries, int decimals = 0,
        string unitArea = "m\u00B2", string unitLength = "m", string unitCount = "st")
    {
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        string fmt = "N" + Math.Max(0, decimals);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', CsvHeader()));
        foreach (var e in entries)
            sb.AppendLine(string.Join(';', CsvFields(e, fmt, nl, unitArea, unitLength, unitCount)));
        return sb.ToString();
    }

    public static string ToCombinedCsv(
        IEnumerable<(string Drawing, IReadOnlyList<LegendEntry> Entries)> perDrawing, int decimals = 0,
        string unitArea = "m\u00B2", string unitLength = "m", string unitCount = "st")
    {
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        string fmt = "N" + Math.Max(0, decimals);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', new[] { "Tekening" }.Concat(CsvHeader())));
        foreach (var (drawing, entries) in perDrawing)
            foreach (var e in entries)
                sb.AppendLine(string.Join(';',
                    new[] { Csv(drawing) }.Concat(CsvFields(e, fmt, nl, unitArea, unitLength, unitCount))));
        return sb.ToString();
    }

    private static string[] CsvHeader() => new[]
    {
        "Status", "Discipline", "Hoofdgroep", "Element", "Omschrijving",
        "Herkomst", "Types", "Aantal", "Lengte_m", "Oppervlak_m2", "Hoeveelheid", "Eenheid"
    };

    private static string[] CsvFields(LegendEntry e, string fmt, CultureInfo nl,
        string unitArea, string unitLength, string unitCount)
    {
        var types = string.Join('+', e.LayersByType.Keys.Select(k => k.ToString()));
        return new[]
        {
            Csv(e.Status.DisplayName()),
            Csv(e.Discipline),
            Csv(e.Hoofdgroep),
            Csv(e.Element),
            Csv(e.Description),
            Csv(SourceLabel(e.DescriptionSource)),
            Csv(types),
            e.Metric.Count.ToString(Inv),
            e.Metric.Length.ToString("0.##", Inv),
            e.Metric.Area.ToString("0.##", Inv),
            Csv(QuantityValue(e, fmt, nl)),
            Csv(QuantityUnit(e, unitArea, unitLength, unitCount))
        };
    }

    public static string ToJson(IEnumerable<LegendEntry> entries, int decimals = 0,
        string unitArea = "m\u00B2", string unitLength = "m", string unitCount = "st")
    {
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        string fmt = "N" + Math.Max(0, decimals);
        int round = Math.Max(0, decimals);
        var rows = entries.Select(e => new
        {
            status = e.Status.DisplayName(),
            discipline = e.Discipline,
            hoofdgroep = e.Hoofdgroep,
            element = e.Element,
            omschrijving = e.Description,
            herkomst = SourceLabel(e.DescriptionSource),
            types = e.LayersByType.Keys.Select(k => k.ToString()).ToArray(),
            aantal = e.Metric.Count,
            lengteM = Math.Round(e.Metric.Length, round),
            oppervlakM2 = Math.Round(e.Metric.Area, round),
            hoeveelheid = QuantityValue(e, fmt, nl),
            eenheid = QuantityUnit(e, unitArea, unitLength, unitCount)
        });

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string ToCombinedJson(
        IEnumerable<(string Drawing, IReadOnlyList<LegendEntry> Entries)> perDrawing, int decimals = 0,
        string unitArea = "m\u00B2", string unitLength = "m", string unitCount = "st")
    {
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        string fmt = "N" + Math.Max(0, decimals);
        int round = Math.Max(0, decimals);
        var rows = perDrawing.SelectMany(d => d.Entries.Select(e => new
        {
            tekening = d.Drawing,
            status = e.Status.DisplayName(),
            discipline = e.Discipline,
            hoofdgroep = e.Hoofdgroep,
            element = e.Element,
            omschrijving = e.Description,
            herkomst = SourceLabel(e.DescriptionSource),
            types = e.LayersByType.Keys.Select(k => k.ToString()).ToArray(),
            aantal = e.Metric.Count,
            lengteM = Math.Round(e.Metric.Length, round),
            oppervlakM2 = Math.Round(e.Metric.Area, round),
            hoeveelheid = QuantityValue(e, fmt, nl),
            eenheid = QuantityUnit(e, unitArea, unitLength, unitCount)
        }));

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string QuantityValue(LegendEntry e, string fmt, CultureInfo nl) => e.QuantityType switch
    {
        QuantityKind.Area => e.Metric.Area.ToString(fmt, nl),
        QuantityKind.Length => e.Metric.Length.ToString(fmt, nl),
        QuantityKind.Count => e.Metric.Count.ToString(nl),
        _ => string.Empty
    };

    private static string QuantityUnit(LegendEntry e, string unitArea, string unitLength, string unitCount) =>
        e.QuantityType switch
        {
            QuantityKind.Area => unitArea,
            QuantityKind.Length => unitLength,
            QuantityKind.Count => unitCount,
            _ => string.Empty
        };

    private static string SourceLabel(DescriptionSource source) => source switch
    {
        DescriptionSource.EigenTekst => "eigen tekst",
        DescriptionSource.Laagbeschrijving => "laagbeschrijving",
        DescriptionSource.Catalogus => "catalogus",
        DescriptionSource.Handmatig => "handmatig",
        DescriptionSource.Laagnaam => "laagnaam",
        _ => string.Empty
    };

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Bescherm tegen CSV-formule-injectie in Excel/Calc.
        var text = value;
        var firstNonSpace = text.AsSpan().TrimStart();
        if (firstNonSpace.Length > 0 && (firstNonSpace[0] is '=' or '+' or '-' or '@'))
            text = "'" + text;

        if (text.Contains(';') || text.Contains('"') || text.Contains('\n'))
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }
}
