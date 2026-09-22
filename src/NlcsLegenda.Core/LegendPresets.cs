namespace NlcsLegenda.Core;

public static class LegendPresets
{
    private const string Extension = ".json";

    public static IReadOnlyList<string> List(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(dir, "*" + Extension)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string Save(string dir, string name, LegendSettings settings)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profielnaam mag niet leeg zijn.", nameof(name));
        Directory.CreateDirectory(dir);
        var path = PathFor(dir, name);
        settings.Save(path);
        return path;
    }

    public static LegendSettings? Load(string dir, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var path = PathFor(dir, name);
        return File.Exists(path) ? LegendSettings.Load(path) : null;
    }

    public static bool Exists(string dir, string name) =>
        !string.IsNullOrWhiteSpace(name) && File.Exists(PathFor(dir, name));

    public static string Export(string dir, string name, string targetPath)
    {
        var settings = Load(dir, name)
            ?? throw new FileNotFoundException($"Profiel '{name}' bestaat niet.");
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
            Directory.CreateDirectory(targetDir);
        settings.Save(targetPath);
        return targetPath;
    }

    public static bool TryImport(string dir, string sourcePath, string name, out string error)
    {
        error = string.Empty;
        if (!File.Exists(sourcePath))
        {
            error = "Bestand niet gevonden.";
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            error = ex.Message;
            return false;
        }

        if (!LegendSettings.TryParse(json, out var settings))
        {
            error = "Geen geldig profiel (ongeldige JSON).";
            return false;
        }

        Save(dir, name, settings);
        return true;
    }

    public static bool Delete(string dir, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var path = PathFor(dir, name);
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    private static string PathFor(string dir, string name) =>
        Path.Combine(dir, SafeFileName(name) + Extension);

    // Vaste Windows-set; Path.GetInvalidFileNameChars() is platformafhankelijk.
    private static readonly char[] InvalidFileChars =
        { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    // Namen die Windows voor apparaten reserveert; ook met extensie onbruikbaar als bestand.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SafeFileName(string name)
    {
        var trimmed = name.Trim();
        var chars = trimmed
            .Select(ch => char.IsControl(ch) || Array.IndexOf(InvalidFileChars, ch) >= 0 ? '_' : ch)
            .ToArray();
        var safe = new string(chars).TrimEnd(' ', '.').Trim();
        if (safe.Length == 0)
            return "profiel";
        // Windows blokkeert apparaatnamen ook mét extensie.
        var baseSegment = safe.Split('.')[0];
        if (ReservedNames.Contains(baseSegment))
            safe = "_" + safe;
        return safe;
    }
}
