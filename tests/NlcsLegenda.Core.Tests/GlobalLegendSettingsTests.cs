using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public sealed class GlobalLegendSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nlcs-global-tests-" + Guid.NewGuid().ToString("N"));
    public GlobalLegendSettingsTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private static DescriptionCatalog Catalog(string key, string text)
    {
        var result = new DescriptionCatalog();
        result.Elementen[key] = new DescriptionEntry { Specifiek = text };
        return result;
    }

    [Fact]
    public void Load_CombinesLegacySettingsAndTextFile_WithoutSharingSnapshot()
    {
        new LegendSettings { DescriptionOverrides = Catalog("A", "Instellingen") }.Save(Path.Combine(_directory, "settings.json"));
        Catalog("B", "Tekstbestand").Save(Path.Combine(_directory, "omschrijvingen.json"));
        var snapshot = GlobalLegendSettings.Load(_directory);
        Assert.Equal(2, snapshot.DescriptionOverrides.Elementen.Count);
        GlobalLegendSettings.Save(_directory, snapshot);
        Assert.Empty(LegendSettings.Load(Path.Combine(_directory, "settings.json")).DescriptionOverrides.Elementen);
        snapshot.DescriptionOverrides.Elementen["A"].Specifiek = "Later gewijzigd";
        Assert.Equal("Instellingen", GlobalLegendSettings.Load(_directory).DescriptionOverrides.Elementen["A"].Specifiek);
    }

    [Fact]
    public void SaveDescriptions_ReplacesOldOverrides_AndPreservesOtherSettings()
    {
        new LegendSettings { Scale = 500, DescriptionOverrides = Catalog("A", "Oud") }.Save(Path.Combine(_directory, "settings.json"));
        GlobalLegendSettings.SaveDescriptions(_directory, Catalog("B", "Nieuw"));
        var loaded = GlobalLegendSettings.Load(_directory);
        Assert.Equal(500, loaded.Scale);
        Assert.Equal("Nieuw", Assert.Single(loaded.DescriptionOverrides.Elementen).Value.Specifiek);
        Assert.False(loaded.DescriptionOverrides.Elementen.ContainsKey("A"));
    }

    [Fact]
    public void EditingTextFile_IsRespectedAfterSavingGlobalProfile()
    {
        GlobalLegendSettings.Save(_directory, new LegendSettings { DescriptionOverrides = Catalog("A", "Profiel") });
        Catalog("A", "Handmatig aangepast").Save(Path.Combine(_directory, "omschrijvingen.json"));
        Assert.Equal("Handmatig aangepast", GlobalLegendSettings.Load(_directory).DescriptionOverrides.Elementen["A"].Specifiek);
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("omschrijvingen.json")]
    public void InvalidGlobalFile_IsReportedAndPreserved(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "{ invalid");
        Assert.Throws<InvalidDataException>(() => GlobalLegendSettings.Load(_directory));
        Assert.Equal("{ invalid", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedSecondReplacement_RestoresFirstFile(bool existed)
    {
        var path = Path.Combine(_directory, "settings.json");
        if (existed) File.WriteAllText(path, "ORIGINAL BYTES");
        Directory.CreateDirectory(Path.Combine(_directory, "omschrijvingen.json"));
        var failure = Record.Exception(() => GlobalLegendSettings.Save(_directory, new LegendSettings()));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal(existed, File.Exists(path));
        if (existed) Assert.Equal("ORIGINAL BYTES", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }
}
