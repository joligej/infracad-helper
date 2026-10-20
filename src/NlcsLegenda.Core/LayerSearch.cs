namespace NlcsLegenda.Core;

public static class LayerSearch
{
    public static bool Matches(string text, string? query)
    {
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.All(word => MatchWord(text, word));
    }

    private static bool MatchWord(string text, string word)
    {
        int start = 0;
        foreach (var part in word.Split('*', StringSplitOptions.RemoveEmptyEntries))
        {
            int found = text.IndexOf(part, start, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            start = found + part.Length;
        }
        return true;
    }
}
