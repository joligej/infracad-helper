using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NlcsLegenda.Core;

public static class LegendExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string ToCsv(IEnumerable<LegendEntry> entries, int decimals = 0,
        string unitArea = "m\u00B2", string unitLength = "m", string unitCount = "st", IEnumerable<string>? klicFields = null)
    {
        var fields = KlicFields.Valid(klicFields);
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        string fmt = "N" + Math.Max(0, decimals);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', CsvHeader().Concat(KlicHeaders(fields))));
        foreach (var e in entries)
            sb.AppendLine(string.Join(';', CsvFields(e, fmt, nl, unitArea, unitLength, unitCount).Concat(KlicValues(e, fields))));
        return sb.ToString();
    }

    public static string ToCombinedCsv(
        IEnumerable<(string Drawing, IReadOnlyList<LegendEntry> Entries)> perDrawing, int decimals = 0,
        string unitArea = "m\u00B2", string unitLength = "m", string unitCount = "st", IEnumerable<string>? klicFields = null)
    {
        var fields = KlicFields.Valid(klicFields);
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        string fmt = "N" + Math.Max(0, decimals);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', new[] { "Tekening" }.Concat(CsvHeader()).Concat(KlicHeaders(fields))));
        foreach (var (drawing, entries) in perDrawing)
            foreach (var e in entries)
                sb.AppendLine(string.Join(';',
                    new[] { Csv(drawing) }.Concat(CsvFields(e, fmt, nl, unitArea, unitLength, unitCount).Concat(KlicValues(e, fields)))));
        return sb.ToString();
    }

    private static IEnumerable<string> KlicHeaders(IReadOnlyList<string> fields) => fields.Select(k => "KLIC " + KlicFields.Find(k)!.Label)
        .Concat(fields.Count == 0 ? Array.Empty<string>() : new[] { "KLIC gebruikte bronnen", "KLIC broncontrole" });

    private static IEnumerable<string> KlicValues(LegendEntry entry, IReadOnlyList<string> fields) =>
        fields.Select(k => Csv(KlicSummary.Field(entry.KlicObjects, k)))
            .Concat(fields.Count == 0 ? Array.Empty<string>() : new[] {
                Csv(string.Join("; ", fields.Select(k => KlicFields.Find(k)!.Label + ": " + KlicSummary.Sources(entry.KlicObjects, k)))),
                Csv(KlicSummary.Issues(entry.KlicObjects)) });

    private static string[] CsvHeader() => new[]
    {
        "Status", "Discipline", "Hoofdgroep", "Element", "Omschrijving",
        "Herkomst", "Types", "Aantal", "Lengte_m", "Oppervlak_m2", "Hoeveelheid", "Eenheid", "Netbeheerders"
    };

    private static string[] CsvFields(LegendEntry e, string fmt, CultureInfo nl,
        string unitArea, string unitLength, string unitCount)
    {
        var types = string.Join('+', e.LayersByType.Keys.Select(k => k.ToString()));
        return new[]
        {
            Csv(e.CustomStatusName ?? e.Status.DisplayName()),
            Csv(ExportDiscipline(e)),
            Csv(ExportHoofdgroep(e)),
            Csv(ExportElement(e)),
            Csv(e.Description),
            Csv(SourceLabel(e.DescriptionSource)),
            Csv(types),
            e.Metric.Count.ToString(Inv),
            e.Metric.Length.ToString("F" + fmt[1..], Inv),
            e.Metric.Area.ToString("F" + fmt[1..], Inv),
            Csv(QuantityValue(e, fmt, nl)),
            Csv(QuantityUnit(e, unitArea, unitLength, unitCount)),
            Csv(string.Join("; ", e.Netbeheerders))
        };
    }

    private static string ExportDiscipline(LegendEntry e) => e.IsBlank ? string.Empty : e.Discipline;
    private static string ExportHoofdgroep(LegendEntry e) => e.IsBlank ? string.Empty : e.Hoofdgroep;
    private static string ExportElement(LegendEntry e) => e.IsBlank ? string.Empty : e.Element;

    public static string ToJson(IEnumerable<LegendEntry> entries, int decimals = 0,
        string unitArea = "m\u00B2", string unitLength = "m", string unitCount = "st", IEnumerable<string>? klicFields = null)
    {
        var fields = KlicFields.Valid(klicFields);
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        string fmt = "N" + Math.Max(0, decimals);
        int round = Math.Max(0, decimals);
        var rows = entries.Select(e => new
        {
            status = e.CustomStatusName ?? e.Status.DisplayName(),
            discipline = ExportDiscipline(e),
            hoofdgroep = ExportHoofdgroep(e),
            element = ExportElement(e),
            omschrijving = e.Description,
            netbeheerders = e.Netbeheerders,
            legendaOmschrijving = e.DisplayDescription,
            klic = fields.ToDictionary(k => k, k => KlicSummary.Field(e.KlicObjects, k)),
            klicBronnen = fields.ToDictionary(k => k, k => KlicSummary.Sources(e.KlicObjects, k)),
            klicBroncontrole = KlicSummary.Issues(e.KlicObjects),
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
        string unitArea = "m\u00B2", string unitLength = "m", string unitCount = "st", IEnumerable<string>? klicFields = null)
    {
        var fields = KlicFields.Valid(klicFields);
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        string fmt = "N" + Math.Max(0, decimals);
        int round = Math.Max(0, decimals);
        var rows = perDrawing.SelectMany(d => d.Entries.Select(e => new
        {
            tekening = d.Drawing,
            status = e.CustomStatusName ?? e.Status.DisplayName(),
            discipline = ExportDiscipline(e),
            hoofdgroep = ExportHoofdgroep(e),
            element = ExportElement(e),
            omschrijving = e.Description,
            netbeheerders = e.Netbeheerders,
            legendaOmschrijving = e.DisplayDescription,
            klic = fields.ToDictionary(k => k, k => KlicSummary.Field(e.KlicObjects, k)),
            klicBronnen = fields.ToDictionary(k => k, k => KlicSummary.Sources(e.KlicObjects, k)),
            klicBroncontrole = KlicSummary.Issues(e.KlicObjects),
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
        DescriptionSource.Laagbeschrijving => "laagbeschrijving",
        DescriptionSource.Catalogus => "catalogus",
        DescriptionSource.Handmatig => "handmatig",
        DescriptionSource.Laagnaam => "laagnaam",
        DescriptionSource.EigenKoppeling => "eigen koppeling",
        DescriptionSource.Blanco => "blanco",
        _ => string.Empty
    };

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // formules in spreadsheetcellen onschadelijk maken
        var text = value;
        var firstNonSpace = text.AsSpan().TrimStart();
        if (firstNonSpace.Length > 0 && (firstNonSpace[0] is '=' or '+' or '-' or '@'))
            text = "'" + text;

        if (text.Contains(';') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }
}
