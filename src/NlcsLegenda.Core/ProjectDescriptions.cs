using System.Text.Json;
using System.Text.RegularExpressions;

namespace NlcsLegenda.Core;

public sealed class ProjectDescriptions
{
    public DescriptionCatalog Descriptions { get; set; } = new();
    public Dictionary<string, string> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static ProjectDescriptions Load(string path)
    {
        var result = JsonSerializer.Deserialize<ProjectDescriptions>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Het projectbestand bevat geen omschrijvingen.");
        result.Descriptions ??= new();
        result.Descriptions.Normalize();
        result.Providers = new(result.Providers ?? new(), StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public void Save(string path) => AtomicFiles.Write(new Dictionary<string, string>
    {
        [path] = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })
    }, new FileOperations());

    public static string ProviderName(string source, IReadOnlyDictionary<string, string>? aliases = null)
    {
        if (aliases?.TryGetValue(source, out string? custom) == true) return custom;
        // alleen bekende namen; onbekende beheerders niet raden
        var match = Regex.Match(source, @"\b(Liander|Stedin|Vitens|KPN|Ziggo|Relined)\b", RegexOptions.IgnoreCase);
        if (!match.Success) return source;
        return new[] { "Liander", "Stedin", "Vitens", "KPN", "Ziggo", "Relined" }.First(n => n.Equals(match.Value, StringComparison.OrdinalIgnoreCase));
    }
}
