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

    public static string? Describe(string? block, NlcsLayerName layer)
    {
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
