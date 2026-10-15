namespace NlcsLegenda.Core;

public enum NlcsDrawType
{
    Geometrie,
    Vlak,
    Arcering,
    Vlakvulling,
    Symbool,
    Tekst,
    Overig
}

public static class NlcsDrawTypeExtensions
{
    public static NlcsDrawType FromSuffix(string? suffix)
    {
        var s = (suffix ?? string.Empty).Trim().ToUpperInvariant();
        if (s.Length == 0) return NlcsDrawType.Overig;
        if (s == "A" || s == "AD") return NlcsDrawType.Arcering;
        if (s == "V") return NlcsDrawType.Vlakvulling;
        if (s == "S" || s == "SD" || s == "SN" || s == "SV") return NlcsDrawType.Symbool;
        if (s == "GV") return NlcsDrawType.Vlak;
        if (s == "G" || s == "GD" || s == "GS") return NlcsDrawType.Geometrie;
        if (s[0] == 'T' && (s.Length == 1 || s.Skip(1).All(char.IsDigit))) return NlcsDrawType.Tekst;
        return NlcsDrawType.Overig;
    }

    public static string DisplayName(this NlcsDrawType type) => type switch
    {
        NlcsDrawType.Geometrie => "geometrie",
        NlcsDrawType.Vlak => "vlakken",
        NlcsDrawType.Arcering => "arceringen",
        NlcsDrawType.Vlakvulling => "vlakvullingen",
        NlcsDrawType.Symbool => "symbolen",
        NlcsDrawType.Tekst => "tekst",
        _ => "overig"
    };
}

