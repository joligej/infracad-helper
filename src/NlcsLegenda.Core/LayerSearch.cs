namespace NlcsLegenda.Core;

public static class LayerSearch
{
    public static bool Matches(string text, string? query)
    {
        int start = 0;
        foreach (var part in (query ?? "").Trim().Split('*', StringSplitOptions.RemoveEmptyEntries))
        {
            int found = text.IndexOf(part, start, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            start = found + part.Length;
        }
        return true;
    }
}
