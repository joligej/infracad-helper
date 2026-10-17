using System.Text.RegularExpressions;

namespace NlcsLegenda.Core;

public static partial class LayerDescription
{
    [GeneratedRegex(@"^NLCS[\s-]+basis(?:\s*[:;/|–—-]\s*|\s+|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BasisPrefix();

    [GeneratedRegex(@"^NLCS\s*\d+(?:\.\d+)*\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionPrefix();

    [GeneratedRegex(@"[.;]\s*Projectaanvulling\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProjectSuffix();

    public static string? Clean(string text, NlcsLayerName layer)
    {
        var value = BasisPrefix().Replace(text.Trim(), string.Empty, 1).Trim();
        value = VersionPrefix().Replace(value, string.Empty, 1).Trim();
        // gegenereerde nlcs-velden laten beschrijven door de catalogus
        var technical = ProjectSuffix().Split(value)[0].Trim().TrimEnd(';', '.');
        var prefix = layer.Hoofdgroep + "-";
        if (technical.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var element = technical[prefix.Length..].Replace(';', '_').Replace(" ", string.Empty).Trim('_');
            if (element.Equals(layer.Element, StringComparison.OrdinalIgnoreCase)
                || layer.Element.StartsWith(element + "_", StringComparison.OrdinalIgnoreCase)) return null;
        }
        if (technical.Equals(layer.Element, StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Length == 0 || value.Equals("KLIC", StringComparison.OrdinalIgnoreCase)
            || value.Equals("NLCS", StringComparison.OrdinalIgnoreCase)
            || value.Equals(layer.Element, StringComparison.OrdinalIgnoreCase)
            || value.Equals(StandardTexts.Humanize(layer.Element), StringComparison.OrdinalIgnoreCase)
            || value.Equals(layer.LocalName, StringComparison.OrdinalIgnoreCase)
            || value.Equals(layer.Raw, StringComparison.OrdinalIgnoreCase)) return null;
        if (NlcsLayerParser.TryParse(value, out var parsed)
            && parsed.LocalName.Equals(layer.LocalName, StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }
}
