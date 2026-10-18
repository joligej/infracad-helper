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
        var originals = contents.Keys.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        var temporary = new Dictionary<string, string>();
        var replaced = new List<string>();
        try
        {
            // beide bestanden voorbereiden vóór vervanging
            foreach (var item in contents)
            {
                string temp = item.Key + "." + Guid.NewGuid().ToString("N") + ".tmp";
                temporary.Add(item.Key, temp);
                File.WriteAllText(temp, item.Value);
            }
            foreach (var item in temporary)
            {
                File.Move(item.Value, item.Key, overwrite: true);
                replaced.Add(item.Key);
            }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (string path in replaced)
            {
                try
                {
                    if (originals[path] is { } bytes) File.WriteAllBytes(path, bytes);
                    else File.Delete(path);
                }
                catch (Exception restoreFailure) { errors.Add(restoreFailure); }
            }
            if (errors.Count > 1)
                throw new AggregateException("Opslaan en terugzetten van de globale standaard zijn mislukt.", errors);
            throw;
        }
        finally
        {
            foreach (string temp in temporary.Values)
                if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
