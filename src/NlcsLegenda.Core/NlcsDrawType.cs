namespace NlcsLegenda.Core;

// Elementsoort afgeleid van het TYPE-suffix van een NLCS-laagnaam. Zie FromSuffix voor
// de mapping. Vlakvulling (V) en Arcering (A) renderen allebei als hatch, maar zijn
// bewust aparte soorten zodat de gebruiker ze los kan filteren.
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
        if (s == "A") return NlcsDrawType.Arcering;
        if (s == "V") return NlcsDrawType.Vlakvulling;
        if (s == "S") return NlcsDrawType.Symbool;
        if (s == "GV") return NlcsDrawType.Vlak;
        if (s == "G" || s == "GD" || s == "GS") return NlcsDrawType.Geometrie;
        // T gevolgd door een schaalgetal (T25/T35/T50) of enkel "T".
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

