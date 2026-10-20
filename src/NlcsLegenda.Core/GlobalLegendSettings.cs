namespace NlcsLegenda.Core;

public static class GlobalLegendSettings
{
    public static LegendSettings Load(string directory)
    {
        var settingsPath = Path.Join(directory, "settings.json");
        var descriptionsPath = Path.Join(directory, "omschrijvingen.json");
        var settings = LegendSettings.Load(settingsPath);
        UpgradeLegacyTemplate(settings);
        if (File.Exists(settingsPath))
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (!json.RootElement.TryGetProperty("globalDefaultsVersion", out _)
                && settings.MergedDimensions.Count == 0)
                settings.MergedDimensions = LegendSettings.DefaultKlicMergeDimensions();
        }
        var descriptions = DescriptionCatalog.Load(descriptionsPath);
        // voorrang van oude profielteksten behouden bij migratie
        descriptions.MergeFrom(settings.DescriptionOverrides);
        settings.DescriptionOverrides = descriptions.Clone();
        return settings;
    }

    public static void UpgradeLegacyTemplate(LegendSettings s)
    {
        // uitsluitend de oude standaardmaten, geen eigen opmaak
        if (s.SwatchWidthMm != 20 || s.SwatchHeightMm != 8 || s.RowPitchMm != 11 || s.ColumnWidthMm != 75) return;
        var d = new LegendSettings();
        s.SwatchWidthMm = d.SwatchWidthMm;
        s.SwatchHeightMm = d.SwatchHeightMm;
        s.RowPitchMm = d.RowPitchMm;
        s.TextGapMm = d.TextGapMm;
        s.HeaderTextHeightMm = d.HeaderTextHeightMm;
        s.TitleTextHeightMm = d.TitleTextHeightMm;
        s.HeaderSpacingMm = d.HeaderSpacingMm;
        s.ColumnWidthMm = d.ColumnWidthMm;
        s.LineSpacingFactor = d.LineSpacingFactor;
        s.RemarksWidthMm = d.RemarksWidthMm;
        if (s.LabelNieuw == "Nieuw") s.LabelNieuw = d.LabelNieuw;
        if (s.LabelBestaand == "Bestaand") s.LabelBestaand = d.LabelBestaand;
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
        var profile = System.Text.Json.Nodes.JsonNode.Parse(stored.ToJson())!;
        profile["globalDefaultsVersion"] = 1;
        var contents = new Dictionary<string, string>
        {
            [Path.Join(directory, "settings.json")] = profile.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            [Path.Join(directory, "omschrijvingen.json")] = descriptions.ToJson()
        };
        AtomicFiles.Write(contents, new FileOperations());
    }
}
