using System.Globalization;
using System.Text.RegularExpressions;

namespace NlcsLegenda.Core;

public enum GroupDimension
{
    Soort,
    Specificatie,
    Uitvoering,
    Nummer,
    Netbeheerder,
    Diameter,
    Materiaal
}

public sealed class ElementProperties
{
    public static bool IsKlic(string discipline, string hoofdgroep) =>
        string.Equals(discipline, "KL", StringComparison.OrdinalIgnoreCase)
        || string.Equals(hoofdgroep, "KL", StringComparison.OrdinalIgnoreCase);

    public static bool IsKlicSource(string source) => source.Split(new[] { '|', '-', '_', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries)
        .Any(part => part.Equals("KLIC", StringComparison.OrdinalIgnoreCase));

    private static readonly string[] Specificaties = { "LS", "MS", "HS", "HD", "LD" };
    private static readonly string[] Uitvoeringen = { "MANTELBUIS", "HULPSTUK", "KABELBED", "COAXKABEL", "GLASVEZEL" };

    public string Soort { get; private init; } = string.Empty;
    public string Specificatie { get; private init; } = string.Empty;
    public string Uitvoering { get; private init; } = string.Empty;
    public string Nummer { get; private init; } = string.Empty;
    public string Diameter { get; private init; } = string.Empty;
    public string Materiaal { get; private init; } = string.Empty;

    public string Netbeheerder { get; private init; } = string.Empty;

    public static ElementProperties From(string element, string netbeheerder = "")
    {
        var e = WithoutProviderSuffix(element, netbeheerder).ToUpperInvariant();
        var (withoutDiameter, diameter) = SplitDiameter(e);
        var tokens = withoutDiameter.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        var materials = tokens.Where(token => Materials.Contains(token)).ToArray();
        tokens.RemoveAll(token => Materials.Contains(token));
        string uitvoering = string.Empty;
        int construction = tokens.Count > 1 ? tokens.FindIndex(1, token => Uitvoeringen.Contains(token)) : -1;
        if (construction >= 0)
        {
            uitvoering = string.Join('_', tokens.Skip(construction));
            tokens.RemoveRange(construction, tokens.Count - construction);
        }
        string specificatie = string.Empty;
        string nummer = string.Empty;
        if (tokens.Count >= 2)
        {
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
            Nummer = nummer,
            Diameter = diameter,
            Materiaal = string.Join("_", materials),
            Netbeheerder = netbeheerder.Trim().ToUpperInvariant()
        };
    }

    private static readonly Regex DiameterToken = new(
        @"^(?<prefix>DN|Ø|DIAM(?:ETER)?)?\s*(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>MM|CM|M)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> PipeTypes = new(StringComparer.OrdinalIgnoreCase)
        { "VRIJVERVAL", "DRUKRIOOL", "PERSLEIDING", "RIOOL", "RIOLERING", "HEMELWATER", "VUILWATER", "DRAINAGE", "DRAIN", "WATER", "GAS", "DATA", "ELEKTRA", "ELEKTRICITEIT", "WARMTE", "WARMTEKOUD", "OLIE" };

    private static readonly HashSet<string> Materials = new(StringComparer.OrdinalIgnoreCase)
        { "PVC", "PE", "HDPE", "PEHD", "POLYETHYLEEN PE", "POLYETHYLEEN", "PP", "BETON", "STAAL", "GIETIJZER", "ASBESTCEMENT", "GRES", "KOPER", "RVS" };

    internal static string WithoutDiameter(string element) => SplitDiameter(element).Element;

    private static (string Element, string Diameter) SplitDiameter(string element)
    {
        var tokens = element.Split('_', StringSplitOptions.RemoveEmptyEntries);
        bool pipe = tokens.Any(token => PipeTypes.Contains(token) || token is "RIOOLLEIDING" or "LEIDING" or "BUIS");
        for (int i = 1; i < tokens.Length; i++)
        {
            var match = DiameterToken.Match(tokens[i]);
            bool separatePrefix = i > 1 && tokens[i - 1].ToUpperInvariant() is "DN" or "Ø" or "DIAM" or "DIAMETER";
            if (!match.Success || (match.Groups["prefix"].Length == 0 && !separatePrefix && !pipe)) continue;
            if (!decimal.TryParse(match.Groups["value"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value) || value <= 0) continue;
            decimal factor = match.Groups["unit"].Value.ToUpperInvariant() switch { "M" => 1000, "CM" => 10, _ => 1 };
            if (value > decimal.MaxValue / factor) continue;
            // kale getallen alleen bij bekende leidingsoorten
            return (string.Join('_', tokens.Where((_, index) => index != i && (!separatePrefix || index != i - 1))),
                (value * factor).ToString("G29", CultureInfo.InvariantCulture));
        }
        return (element, string.Empty);
    }

    internal static string WithoutProviderSuffix(string? text, string? provider)
    {
        var value = (text ?? string.Empty).Trim();
        var known = ProviderKey(provider ?? string.Empty);
        if (known.Length == 0) return value;
        // alleen een volledige, bekende beheerder aan het einde
        for (int i = 1; i < value.Length; i++)
        {
            if (char.IsLetterOrDigit(value[i - 1]) || !char.IsLetterOrDigit(value[i])) continue;
            if (ProviderKey(value[i..]) != known) continue;
            var prefix = value[..i].TrimEnd(' ', '_', '(', ';', ',');
            if (prefix.Length > 0) return prefix;
        }
        return value;
    }

    private static string ProviderKey(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public string Value(GroupDimension dim) => dim switch
    {
        GroupDimension.Soort => Soort,
        GroupDimension.Specificatie => Specificatie,
        GroupDimension.Uitvoering => Uitvoering,
        GroupDimension.Nummer => Nummer,
        GroupDimension.Netbeheerder => Netbeheerder,
        GroupDimension.Diameter => Diameter,
        GroupDimension.Materiaal => Materiaal,
        _ => string.Empty
    };

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
        return i > 0 && i < token.Length ? (token[..i], token[i..]) : (token, string.Empty);
    }
}
