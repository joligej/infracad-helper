namespace NlcsLegenda.Core;

// Groepeerdimensie: een eigenschap van het element waarop legenda-regels samengevoegd kunnen
// worden. Zet je een dimensie op "samenvoegen", dan telt die eigenschap niet meer mee voor de
// groepering en vallen regels die alleen daarin verschillen samen.
public enum GroupDimension
{
    Soort,         // basis, bijv. DATA, ET, GAS
    Specificatie,  // spanning/druk: LS, MS, HD, LD
    Uitvoering,    // MANTELBUIS, HULPSTUK
    Nummer         // volgnummer, bijv. DATA2 -> 2
}

// Ontleedt een NLCS-elementnaam in groepeerbare eigenschappen. Vooral bedoeld voor de KL-
// hoofdgroep (kabels en leidingen); voor andere elementen blijft alles onder Soort staan,
// zodat samenvoegen die niet per ongeluk combineert.
public sealed class ElementProperties
{
    private static readonly string[] Specificaties = { "LS", "MS", "HD", "LD" };
    private static readonly string[] Uitvoeringen = { "MANTELBUIS", "HULPSTUK" };

    public string Soort { get; private init; } = string.Empty;
    public string Specificatie { get; private init; } = string.Empty;
    public string Uitvoering { get; private init; } = string.Empty;
    public string Nummer { get; private init; } = string.Empty;

    public static ElementProperties From(string element)
    {
        var e = (element ?? string.Empty).Trim().ToUpperInvariant();
        string uitvoering = string.Empty;
        foreach (var u in Uitvoeringen)
        {
            if (e.EndsWith("_" + u, StringComparison.Ordinal))
            {
                uitvoering = u;
                e = e[..^(u.Length + 1)];
                break;
            }
        }

        string specificatie = string.Empty;
        string nummer = string.Empty;
        var tokens = e.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count >= 2)
        {
            // Laatste token met evt. volgnummer losweken: "LS2" -> spec LS, nummer 2.
            var last = tokens[^1];
            var (word, num) = SplitTrailingDigits(last);
            if (Specificaties.Contains(word))
            {
                specificatie = word;
                nummer = num;
                tokens.RemoveAt(tokens.Count - 1);
            }
        }
        if (nummer.Length == 0 && tokens.Count >= 1)
        {
            var (word, num) = SplitTrailingDigits(tokens[^1]);
            if (num.Length > 0)
            {
                nummer = num;
                tokens[^1] = word;
            }
        }

        return new ElementProperties
        {
            Soort = string.Join('_', tokens),
            Specificatie = specificatie,
            Uitvoering = uitvoering,
            Nummer = nummer
        };
    }

    public string Value(GroupDimension dim) => dim switch
    {
        GroupDimension.Soort => Soort,
        GroupDimension.Specificatie => Specificatie,
        GroupDimension.Uitvoering => Uitvoering,
        GroupDimension.Nummer => Nummer,
        _ => string.Empty
    };

    // Sleutel van alle dimensies die NIET worden samengevoegd. Regels met dezelfde sleutel
    // (en gelijke status/discipline/hoofdgroep) vallen samen.
    public string MergeKey(IReadOnlyCollection<GroupDimension> merged)
    {
        var parts = new List<string>(4);
        foreach (GroupDimension dim in Enum.GetValues<GroupDimension>())
            parts.Add(merged.Contains(dim) ? string.Empty : Value(dim));
        return string.Join('|', parts);
    }

    private static (string word, string num) SplitTrailingDigits(string token)
    {
        int i = token.Length;
        while (i > 0 && char.IsDigit(token[i - 1])) i--;
        // Alleen als er ook letters vooraf staan (anders is het geen "basis+nummer").
        return i > 0 && i < token.Length ? (token[..i], token[i..]) : (token, string.Empty);
    }
}
