namespace NlcsLegenda.Core;

public static class MTextFormat
{
    // backslashes eerst escapen, vóór de toegevoegde opmaakcodes
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
            .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\P");
    }
}
