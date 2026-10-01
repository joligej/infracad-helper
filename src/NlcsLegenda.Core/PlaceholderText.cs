namespace NlcsLegenda.Core;

// Herkent generieke placeholdertekst uit KLIC-symbolen, zoals "TYPE \ LABEL \ OMSCHRIJVING".
// Dat zijn lege attribuut-tags, geen echte inhoud. Bewust streng: alleen als de tekst
// uitsluitend uit generieke tokens bestaat, zodat echte omschrijvingen die toevallig een
// woord als "type" bevatten niet worden weggefilterd.
public static class PlaceholderText
{
    private static readonly HashSet<string> GenericTokens =
        new(StringComparer.OrdinalIgnoreCase) { "TYPE", "LABEL", "OMSCHRIJVING" };

    public static bool IsGeneric(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // MText-regeleindes en scheidingstekens normaliseren naar spaties.
        var cleaned = text.Replace("\\P", " ").Replace("\\", " ")
            .Replace("/", " ").Replace("|", " ");
        var tokens = cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        foreach (var t in tokens)
            if (!GenericTokens.Contains(t))
                return false;
        return true;
    }
}
