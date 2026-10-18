namespace NlcsLegenda.Core;

public static class GlobalLegendSettings
{
    public static LegendSettings Load(string directory)
    {
        var settingsPath = Path.Join(directory, "settings.json");
        var descriptionsPath = Path.Join(directory, "omschrijvingen.json");
        var settings = LegendSettings.Load(settingsPath);
        var descriptions = DescriptionCatalog.Load(descriptionsPath);
        // voorrang van oude profielteksten behouden bij migratie
        descriptions.MergeFrom(settings.DescriptionOverrides);
        settings.DescriptionOverrides = descriptions.Clone();
        return settings;
    }

    public static void SaveDescriptions(string directory, DescriptionCatalog descriptions)
    {
        var settings = Load(directory);
        settings.DescriptionOverrides = descriptions.Clone();
        Save(directory, settings);
    }

    public static void Save(string directory, LegendSettings settings)
    {
        var stored = settings.Clone();
        var descriptions = stored.DescriptionOverrides;
        stored.DescriptionOverrides = new DescriptionCatalog();
        Directory.CreateDirectory(directory);
        var contents = new Dictionary<string, string>
        {
            [Path.Join(directory, "settings.json")] = stored.ToJson(),
            [Path.Join(directory, "omschrijvingen.json")] = descriptions.ToJson()
        };
        AtomicFiles.Write(contents, new FileOperations());
    }
}
