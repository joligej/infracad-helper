namespace NlcsLegenda.Core;

public static class SymbolDescription
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BORD_VERKEER"] = "Verkeersbord", ["BORD_INFORMATIE"] = "Informatiebord",
        ["KAST_ELEKTRA"] = "Elektrakast", ["PAAL_PORTAAL"] = "Portaalpaal",
        ["PUT_KOLK"] = "Kolk", ["PAAL_VLAGGENMAST"] = "Vlaggenmast",
        ["PAAL_VRI"] = "Verkeersregelinstallatie (paal)", ["PUT_BRANDKRAAN"] = "Brandkraan",
        ["PAAL_AFSLUITPAAL"] = "Afsluitpaal", ["PAAL_LICHTMAST"] = "Lichtmast",
        ["MAST_BOVENLEIDING"] = "Bovenleidingmast", ["PUT_RIOOLPUT"] = "Rioolput",
        ["PUT_DRAINAGEPUT"] = "Drainageput"
    };

    public static bool IsManhole(string block, string element) =>
        System.Text.RegularExpressions.Regex.IsMatch(block + "_" + element,
            @"(?:^|[-_ ])(?:PUT|RIOOLPUT|INSPECTIEPUT)(?:$|[-_ ])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool IsNumberAttribute(string tag) => new[] { "PUTNUMMER", "PUTNR", "PUT_NR", "PUT_NUMMER", "NUMMER", "PUTNAAM", "PUTID", "PUT_ID" }
        .Contains(tag, StringComparer.OrdinalIgnoreCase);

    public static string Variant(string? block)
    {
        if (block is null || !block.Contains("BOOM", StringComparison.OrdinalIgnoreCase)) return "";
        var match = System.Text.RegularExpressions.Regex.Match(block,
            @"(?:^|[-_ ])(?<size>[123])(?:E|DE|STE)?[-_ ]*(?:GROOTTE|GROOTTEKLASSE|GROOT|ORD[E]?)(?:$|[-_ ])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["size"].Value + "e grootte" : "";
    }

    public static string ManholeKind(string block, string element)
    {
        if (!IsManhole(block, element)) return "";
        var name = LayerNaming.LocalName(block).ToUpperInvariant();
        var match = System.Text.RegularExpressions.Regex.Match(name, @"(?:^|[-_ ])(?:RIOOLPUT|INSPECTIEPUT|PUT)(?<kind>.*)");
        if (!match.Success) return "";
        var parts = match.Groups["kind"].Value.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("_", parts.Where(p => p != "SO" && p != "S" && !p.All(char.IsDigit)));
    }

    public static string? Describe(string? block, NlcsLayerName layer)
    {
        var variant = Variant(block);
        if (variant.Length > 0) return "Boom " + variant;
        if (block is not null && IsManhole(block, layer.Element))
        {
            var name = (block + "_" + layer.Element).ToUpperInvariant();
            foreach (string type in new[] { "GWA", "HWA", "DWA", "VWA", "IT" })
                if (System.Text.RegularExpressions.Regex.IsMatch(name, "(?:^|[-_ ])" + type + "(?:$|[-_ ])")) return type + "-put";
        }
        if (block is null || !layer.Hoofdgroep.Equals("IE", StringComparison.OrdinalIgnoreCase)
            || !layer.Element.StartsWith("MEUBILAIR", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = LayerNaming.LocalName(block).Split('-');
        if (parts.Length != 3 || !parts[0].Equals("SIE", StringComparison.OrdinalIgnoreCase)
            || !parts[2].Equals("SO", StringComparison.OrdinalIgnoreCase)) return null;
        string element = parts[1];
        foreach (string prefix in new[] { "MEUBILAIR_", "WEG_", "WATER_" })
            if (element.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                element = element[prefix.Length..];
                return element.Length == 0 ? null : Names.GetValueOrDefault(element) ?? StandardTexts.Humanize(element);
            }
        return null;
    }
}
