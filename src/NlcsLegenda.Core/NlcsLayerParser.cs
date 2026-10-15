using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NlcsLegenda.Core;

public static class NlcsLayerParser
{
    public static bool TryParse(string? rawLayerName, [NotNullWhen(true)] out NlcsLayerName? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(rawLayerName))
            return false;

        var raw = rawLayerName.Trim();

        var isXref = raw.Contains('|');
        var local = LayerNaming.LocalName(raw);
        var xrefName = isXref ? raw[..raw.LastIndexOf('|')] : string.Empty;

        var parts = local.Split('-');
        if (parts.Length < 5)
            return false;

        var status = parts[0];
        var discipline = parts[1];
        var hoofdgroep = parts[2];

        if (!IsStatus(status) || !IsField(discipline) || !IsField(hoofdgroep))
            return false;

        var lastIndex = parts.Length - 1;
        int? scale = null;
        if (IsPositiveInteger(parts[lastIndex]))
        {
            if (!int.TryParse(parts[lastIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                return false;
            scale = value;
            lastIndex--;
        }

        if (lastIndex < 4)
            return false;

        var typeSuffix = parts[lastIndex];
        lastIndex--;

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
            Status = NlcsStatusExtensions.FromCode(status[..1]),
            Discipline = discipline.ToUpperInvariant(),
            Hoofdgroep = hoofdgroep.ToUpperInvariant(),
            Element = element,
            TypeSuffix = typeSuffix.ToUpperInvariant(),
            DrawType = NlcsDrawTypeExtensions.FromSuffix(typeSuffix),
            Scale = scale
        };
        return true;
    }

    private static bool IsField(string value) =>
        value.Length > 0 && value.All(char.IsLetterOrDigit);

    private static bool IsStatus(string value) =>
        value.Length == 1 && char.IsLetterOrDigit(value[0])
        || value.Length == 3 && NlcsLayerComponents.StatusCodes.Contains(value[..1].ToUpperInvariant())
            && value[1..].All(char.IsAsciiDigit)
            && int.TryParse(value[1..], out var subStatus) && subStatus is >= 1 and <= 99;

    private static bool IsPositiveInteger(string value) =>
        value.Length > 0 && value.All(char.IsDigit);
}
