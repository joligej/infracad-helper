namespace NlcsLegenda.Core;

// AutoCAD weigert laagnamen met gereserveerde tekens.
public static class LayerNaming
{
    private static readonly char[] Forbidden =
        { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' };

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
