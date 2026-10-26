namespace NlcsLegenda.Core;

public static class LayerNaming
{
    private static readonly char[] Forbidden =
        { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' };

    public static string LocalName(string? databaseLayerName)
    {
        if (string.IsNullOrEmpty(databaseLayerName))
            return string.Empty;
        var bar = databaseLayerName.LastIndexOf('|');
        return bar < 0 ? databaseLayerName : databaseLayerName[(bar + 1)..];
    }

    public static bool IsValidReference(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Split('|').All(IsValid);

    public static string SourceName(string? name)
    {
        int separator = name?.LastIndexOf('|') ?? -1;
        return separator < 0 ? string.Empty : name![..separator];
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
