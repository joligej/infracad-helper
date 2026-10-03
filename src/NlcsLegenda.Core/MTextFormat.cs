namespace NlcsLegenda.Core;

public static class MTextFormat
{
    // MText kent eigen besturingstekens: '\' leidt codes in, '{' en '}' groeperen opmaak
    // en '\P' is een alinea-einde. Backslash moet als eerste, anders escapen we de accolades
    // die we zelf toevoegen nog een keer dubbel.
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
            .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\P");
    }
}
