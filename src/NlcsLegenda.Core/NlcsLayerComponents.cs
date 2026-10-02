using System.Text.RegularExpressions;

namespace NlcsLegenda.Core;

// Ontleedt en stelt een NLCS-laagnaam samen uit zijn formele onderdelen, conform de Formele
// Beschrijving NLCS (versie 5.0, par. 5.1): [STATUS]-DISCIPLINE-HOOFDGROEP-OBJECT[-SUBOBJECT..]-
// ELEMENT[-SCHAAL]. Alleen de formeel vastgelegde STATUS- en ELEMENT-codes worden als geldig
// gezien; voor DISCIPLINE/HOOFDGROEP/OBJECT geldt de vormregel (de volledige codelijsten staan
// in de externe objectentabellen, niet in deze bron). Bewust zonder verzonnen componenten.
public sealed class NlcsLayerComponents
{
    // Formele STATUS-codes (par. 5.1.1). R gedraagt zich als N; X alleen met discipline XX + AL.
    public static readonly string[] StatusCodes = { "N", "B", "V", "T", "X", "R" };

    // Formele ELEMENT-codes (par. 5.1.5). T** is Tekst met teksthoogte (bijv. T25), optioneel
    // vlakvormend (T25V).
    public static readonly string[] ElementCodes =
    {
        "G", "GN", "GD", "GV", "A", "AD", "S", "SD", "SN", "SV", "O", "M", "V"
    };

    private static readonly Regex Field = new("^[A-Z0-9]{1,}$", RegexOptions.Compiled);
    private static readonly Regex TextElement = new(@"^T\d+V?$", RegexOptions.Compiled);

    public string Status { get; set; } = "N";
    public string SubStatus { get; set; } = string.Empty;   // "", of 01-99
    public string Discipline { get; set; } = string.Empty;
    public string Hoofdgroep { get; set; } = string.Empty;
    public List<string> ObjectParts { get; set; } = new();  // OBJECT t/m SUBOBJECT
    public string Element { get; set; } = "G";
    public string Scale { get; set; } = string.Empty;       // "", of getal

    public static bool TryParse(string raw, out NlcsLayerComponents comp, out string error)
    {
        comp = new NlcsLayerComponents();
        error = string.Empty;
        var local = (raw ?? string.Empty).Trim();
        int bar = local.LastIndexOf('|');
        if (bar >= 0)
            local = local[(bar + 1)..];

        var tokens = local.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 5)
        {
            error = "geen geldige NLCS-laagnaam (minimaal STATUS-DISCIPLINE-HOOFDGROEP-OBJECT-ELEMENT)";
            return false;
        }

        int last = tokens.Length - 1;
        string scale = string.Empty;
        if (IsInteger(tokens[last]))
        {
            scale = tokens[last];
            last--;
        }
        if (last < 3)
        {
            error = "geen geldige NLCS-laagnaam (OBJECT en ELEMENT ontbreken)";
            return false;
        }
        var element = tokens[last];
        last--;

        var (letters, digits) = SplitTrailingDigits(tokens[0]);
        comp = new NlcsLayerComponents
        {
            Status = letters,
            SubStatus = digits,
            Discipline = tokens[1],
            Hoofdgroep = tokens[2],
            ObjectParts = tokens[3..(last + 1)].ToList(),
            Element = element,
            Scale = scale
        };
        return true;
    }

    public string Compose()
    {
        var parts = new List<string>
        {
            Status.Trim().ToUpperInvariant() + SubStatus.Trim(),
            Discipline.Trim().ToUpperInvariant(),
            Hoofdgroep.Trim().ToUpperInvariant()
        };
        foreach (var o in ObjectParts)
            if (!string.IsNullOrWhiteSpace(o))
                parts.Add(o.Trim().ToUpperInvariant());
        parts.Add(Element.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(Scale))
            parts.Add(Scale.Trim());
        return string.Join('-', parts);
    }

    // Lijst met concrete problemen; leeg = geldig. Valideert STATUS/ELEMENT strikt tegen de
    // formele codes en de bekende combinatieregels; overige velden op vorm.
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var status = Status.Trim().ToUpperInvariant();
        if (!StatusCodes.Contains(status))
            errors.Add($"STATUS '{status}' is geen NLCS-code (N, B, V, T, X of R).");
        if (SubStatus.Length > 0 && !(SubStatus.Length == 2 && int.TryParse(SubStatus, out var n) && n is >= 1 and <= 99))
            errors.Add("SUBSTATUS moet 01\u201399 zijn.");

        var disc = Discipline.Trim().ToUpperInvariant();
        var hoofd = Hoofdgroep.Trim().ToUpperInvariant();
        if (!Field.IsMatch(disc))
            errors.Add("DISCIPLINE ontbreekt of bevat ongeldige tekens.");
        if (!Field.IsMatch(hoofd))
            errors.Add("HOOFDGROEP ontbreekt of bevat ongeldige tekens.");

        if (ObjectParts.Count == 0 || ObjectParts.All(string.IsNullOrWhiteSpace))
            errors.Add("Er moet minstens één OBJECT zijn.");
        else
            foreach (var o in ObjectParts.Where(o => !string.IsNullOrWhiteSpace(o)))
                if (!Field.IsMatch(o.Trim().ToUpperInvariant()))
                    errors.Add($"OBJECT '{o}' bevat ongeldige tekens.");

        if (!IsValidElement(Element.Trim().ToUpperInvariant()))
            errors.Add($"ELEMENT '{Element}' is geen NLCS-element (bijv. G, GD, A, S, V, T25).");

        if (Scale.Length > 0 && !int.TryParse(Scale.Trim(), out _))
            errors.Add("SCHAAL moet een getal zijn.");

        // Combinatieregels (par. 5.1.1/5.1.3): X hoort bij XX+AL en omgekeerd.
        if (status == "X" && !(disc == "XX" && hoofd == "AL"))
            errors.Add("STATUS 'X' mag alleen met DISCIPLINE 'XX' en HOOFDGROEP 'AL'.");
        if (hoofd == "AL" && status != "X")
            errors.Add("HOOFDGROEP 'AL' mag alleen met STATUS 'X'.");

        return errors;
    }

    public static bool IsValidElement(string element)
        => ElementCodes.Contains(element) || TextElement.IsMatch(element);

    private static (string letters, string digits) SplitTrailingDigits(string token)
    {
        int i = token.Length;
        while (i > 0 && char.IsDigit(token[i - 1])) i--;
        return (token[..i], token[i..]);
    }

    private static bool IsInteger(string value) => value.Length > 0 && value.All(char.IsDigit);
}
