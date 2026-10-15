using System.Text.RegularExpressions;

namespace NlcsLegenda.Core;

// Technische bronlabels zijn geen omschrijving van het getekende object.
public static partial class LayerDescription
{
    [GeneratedRegex(@"^NLCS[\s-]+basis(?:\s*[:;/|–—-]\s*|\s+|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BasisPrefix();

    public static string? Clean(string text, NlcsLayerName layer)
    {
        var value = BasisPrefix().Replace(text.Trim(), string.Empty, 1).Trim();
        if (value.Length == 0 || value.Equals("KLIC", StringComparison.OrdinalIgnoreCase)
            || value.Equals("NLCS", StringComparison.OrdinalIgnoreCase)
            || value.Equals(layer.Element, StringComparison.OrdinalIgnoreCase)
            || value.Equals(layer.LocalName, StringComparison.OrdinalIgnoreCase)
            || value.Equals(layer.Raw, StringComparison.OrdinalIgnoreCase)) return null;
        // Een complete laagnaam blijft technisch, ook met een andere xref-prefix.
        if (NlcsLayerParser.TryParse(value, out var parsed)
            && parsed.LocalName.Equals(layer.LocalName, StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }
}
