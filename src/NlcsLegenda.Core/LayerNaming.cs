namespace NlcsLegenda.Core;

// AutoCAD weigert laagnamen met gereserveerde tekens.
public static class LayerNaming
{
    private static readonly char[] Forbidden =
        { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' };

    // Lokale laagnaam zonder xref-prefix: AutoCAD kwalificeert xref-afhankelijke lagen als
    // "xrefnaam|laagnaam" (geneste xrefs ketenen door). Het deel na de laatste '|' is de laag
    // zoals die in de bron heet; eigen-laagregels matchen daarop, niet op de hostprefix.
    public static string LocalName(string? databaseLayerName)
    {
        if (string.IsNullOrEmpty(databaseLayerName))
            return string.Empty;
        var bar = databaseLayerName.LastIndexOf('|');
        return bar < 0 ? databaseLayerName : databaseLayerName[(bar + 1)..];
    }

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        foreach (var ch in name)
        {
            if (char.IsControl(ch))
                return false;
            if (Array.IndexOf(Forbidden, ch) >= 0)
                return false;
        }
        return true;
    }
}
