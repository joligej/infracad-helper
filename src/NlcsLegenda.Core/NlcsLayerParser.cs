using System.Diagnostics.CodeAnalysis;

namespace NlcsLegenda.Core;

public static class NlcsLayerParser
{
    public static bool TryParse(string? rawLayerName, [NotNullWhen(true)] out NlcsLayerName? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(rawLayerName))
            return false;

        var raw = rawLayerName.Trim();

        // Xref-lagen: "xrefnaam|laagnaam" -> neem het deel na de laatste '|'.
        var isXref = raw.Contains('|');
        var local = isXref ? raw[(raw.LastIndexOf('|') + 1)..] : raw;
        var xrefName = isXref ? raw[..raw.LastIndexOf('|')] : string.Empty;

        var parts = local.Split('-');
        if (parts.Length < 5)
            return false;

        var status = parts[0];
        var discipline = parts[1];
        var hoofdgroep = parts[2];

        if (!IsField(status, 1) || !IsField(discipline, 2) || !IsField(hoofdgroep, 2))
            return false;

        var lastIndex = parts.Length - 1;
        int? scale = null;
        if (IsPositiveInteger(parts[lastIndex]))
        {
            scale = int.Parse(parts[lastIndex]);
            lastIndex--;
        }

        if (lastIndex < 4)
            return false;

        var typeSuffix = parts[lastIndex];
        lastIndex--;

        // Elementdelen zijn parts[3..lastIndex]; join met '-' voor het zeldzame geval
        // dat een elementnaam zelf een koppelteken bevat.
        var element = string.Join('-', parts[3..(lastIndex + 1)]);
        if (string.IsNullOrEmpty(element))
            return false;

        result = new NlcsLayerName
        {
            Raw = raw,
            LocalName = local,
            IsXref = isXref,
            XrefName = xrefName,
            StatusCode = status.ToUpperInvariant(),
            Status = NlcsStatusExtensions.FromCode(status),
            Discipline = discipline.ToUpperInvariant(),
            Hoofdgroep = hoofdgroep.ToUpperInvariant(),
            Element = element,
            TypeSuffix = typeSuffix.ToUpperInvariant(),
            DrawType = NlcsDrawTypeExtensions.FromSuffix(typeSuffix),
            Scale = scale
        };
        return true;
    }

    private static bool IsField(string value, int length) =>
        value.Length == length && value.All(char.IsLetterOrDigit);

    private static bool IsPositiveInteger(string value) =>
        value.Length > 0 && value.All(char.IsDigit);
}
