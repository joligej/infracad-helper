using System.Globalization;

namespace NlcsLegenda.Core;

public static class StandardTexts
{
    private static readonly Dictionary<string, string> Hoofdgroepen = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VH"] = "Verharding",
        ["RI"] = "Riolering",
        ["GW"] = "Grondwerk",
        ["IE"] = "Inrichtingselement",
        ["VW"] = "Verkeersvoorziening",
        ["GR"] = "Groen",
        ["KW"] = "Kunstwerk",
        ["KG"] = "Kadaster",
        ["KL"] = "Kabel/leiding",
        ["AM"] = "Alignement",
        ["OG"] = "Ondergrond",
        ["FC"] = "Funderingsconstructie",
        ["AL"] = "Algemeen"
    };

    private static readonly Dictionary<string, string> ElementOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VH|OPENVERHARDING_BETONSTRAATSTEEN"] = "Betonstraatsteen",
        ["VH|OPENVERHARDING_STRAATBAKSTEEN"] = "Straatbaksteen (gebakken)",
        ["VH|OPENVERHARDING_NATUURSTEEN"] = "Natuursteen",
        ["VH|OPENVERHARDING_TEGEL"] = "Betontegel",
        ["VH|OPENVERHARDING_TEGEL_GRAS"] = "Grastegel",
        ["VH|OPENVERHARDING_SIERBESTRATING"] = "Sierbestrating",
        ["VH|GESLOTENVERHARDING_ASFALT"] = "Asfaltverharding",
        ["VH|GESLOTENVERHARDING_ASFALT_FREESVAK"] = "Freesvak asfalt",
        ["VH|GESLOTENVERHARDING_BETON"] = "Betonverharding",
        ["VH|HALFVERHARDING_GRIND"] = "Halfverharding grind",
        ["VH|HALFVERHARDING_GRAVEL"] = "Halfverharding gravel",
        ["VH|HALFVERHARDING_GRASBETONSTEEN"] = "Grasbetonsteen",
        ["VH|KANTOPSLUITING_TROTTOIRBAND"] = "Trottoirband",
        ["VH|KANTOPSLUITING_OPSLUITBAND"] = "Opsluitband",
        ["VH|KANTOPSLUITING_VOORKANTBAND"] = "Voorkantband",
        ["VH|KANTOPSLUITING_ACHTERKANTBAND"] = "Achterkantband",
        ["VH|KANTOPSLUITING_INRITBAND"] = "Inritband",
        ["VH|KANTOPSLUITING_GELEIDEBAND"] = "Geleideband",
        ["VH|KANTOPSLUITING_BUSHALTEBAND"] = "Bushalteband",
        ["VH|KANTOPSLUITING_SCHEIDINGSBAND"] = "Scheidingsband",
        ["VH|KANTOPSLUITING_ELEMENT_INRITBAND"] = "Inritband",
        ["VH|KANTVERHARDING_STREKLAAG"] = "Streklaag",
        ["VH|DREMPEL_TALUD"] = "Verkeersdrempel",
        ["RI|HWA_RIOOLLEIDING"] = "Hemelwaterriool",
        ["RI|VWA_RIOOLLEIDING"] = "Vuilwaterriool",
        ["RI|GWA_RIOOLLEIDING"] = "Gemengd riool",
        ["RI|HWA_KOLK_STRAATKOLK"] = "Straatkolk (HWA)",
        ["RI|HWA_KOLK_TROTTOIRKOLK"] = "Trottoirkolk (HWA)",
        ["RI|GWA_KOLK_STRAATKOLK"] = "Straatkolk",
        ["RI|GWA_KOLK_TROTTOIRKOLK"] = "Trottoirkolk",
        ["RI|HWA_RIOOLPUT"] = "Inspectieput (HWA)",
        ["RI|HWA_RIOOLPUT_BETON"] = "Betonput (HWA)",
        ["RI|HWA_RIOOLPUT_INSPECTIEPUT"] = "Inspectieput (HWA)",
        ["RI|VWA_RIOOLPUT"] = "Inspectieput (VWA)",
        ["RI|HWA_GOOT_MOLGOOT"] = "Molgoot",
        ["RI|HWA_GOOT_LIJNGOOT"] = "Lijngoot",
        ["GW|GRONDWERKLIJN_KRUIN"] = "Grondwerklijn kruin",
        ["GW|GRONDWERKLIJN_TEEN"] = "Grondwerklijn teen",
        ["GW|TALUDARCERING_KORT"] = "Talud (kort)",
        ["GW|TALUDARCERING_LANG"] = "Talud (lang)",
        ["VW|MARKERING_LANGS_STREEP_DOORGETROKKEN"] = "Doorgetrokken streep",
        ["VW|MARKERING_LANGS_STREEP"] = "Onderbroken streep",
        ["VW|MARKERING_DWARS_OVERSTEEK_VOETGANGERS"] = "Voetgangersoversteek",
        ["VW|MARKERING_DWARS_OVERSTEEK_BLOKSTREEP"] = "Blokmarkering oversteek",
        ["VW|MARKERING_DWARS_DRIEHOEK"] = "Haaientanden",
        ["VW|MARKERING_SYMBOOL"] = "Wegmarkering symbool",
        ["IE|MEUBILAIR_WEGMEUBILAIR_LICHTMAST"] = "Lichtmast",
        ["IE|MEUBILAIR_WEGMEUBILAIR_AFVALBAK"] = "Afvalbak",
        ["IE|MEUBILAIR_WEGMEUBILAIR_ZITBANK"] = "Zitbank",
        ["IE|MEUBILAIR_WEGMEUBILAIR_FIETSENREK"] = "Fietsenrek",
        ["IE|MEUBILAIR_WEGMEUBILAIR_ABRI"] = "Abri",
        ["IE|MEUBILAIR_WEGMEUBILAIR_HALTEPAAL"] = "Haltepaal",
        ["IE|MEUBILAIR_WEGMEUBILAIR_PAAL"] = "Paal",
        ["IE|TERREINAFSCHEIDING_KUNSTMATIG_HEKWERK"] = "Hekwerk",
        ["IE|TERREINAFSCHEIDING_KUNSTMATIG_MUUR"] = "Muur",
        ["KG|GRENS_PERCEEL"] = "Perceelsgrens",
        ["KG|KADASTRAAL_BEBOUWING"] = "Kadastrale bebouwing",
        ["OG|BEBOUWING_ENTREE"] = "Entree",
        ["OG|TERREIN_ERF"] = "Erf",
        ["GR|BEPLANTING_CULTUUR_HEESTERS"] = "Heesters",
        ["GR|BEPLANTING_CULTUUR_BOSPLANTSOEN"] = "Bosplantsoen",
        ["GR|BEPLANTING_CULTUUR_BODEMBEDEKKER"] = "Bodembedekker",
        ["GR|BEPLANTING_CULTUUR_VASTE"] = "Vaste planten"
    };

    public static string HoofdgroepName(string code) =>
        Hoofdgroepen.TryGetValue(code ?? string.Empty, out var name) ? name : (code ?? string.Empty);

    public static IReadOnlyDictionary<string, string> DefaultElementDescriptions => ElementOverrides;

    public static string Humanize(string element)
    {
        if (string.IsNullOrWhiteSpace(element)) return string.Empty;
        var words = element.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ti = CultureInfo.GetCultureInfo("nl-NL").TextInfo;
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            // Behoud codes/afmetingen met cijfers en korte acroniemen (bijv. PVC, HWA, CAI).
            if (w.Any(char.IsDigit) || (w.Length <= 3 && w.All(char.IsUpper)))
                continue;
            words[i] = ti.ToTitleCase(w.ToLowerInvariant());
        }
        return string.Join(' ', words);
    }
}
