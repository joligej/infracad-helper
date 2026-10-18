using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public sealed class ConfigurationReadTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "nlcs-read-" + Guid.NewGuid().ToString("N"));
    public ConfigurationReadTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private static void Load(string path, bool settings)
    {
        if (settings) LegendSettings.Load(path);
        else DescriptionCatalog.Load(path);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "")]
    [InlineData(true, "null")]
    [InlineData(false, "null")]
    [InlineData(true, "{ invalid")]
    [InlineData(false, "{ invalid")]
    [InlineData(true, "{\"schemaVersion\":999}")]
    public void InvalidFile_IsReportedWithoutReplacingItsContents(bool settings, string json)
    {
        string path = Path.Join(_directory, "config.json");
        File.WriteAllText(path, json);
        var error = Assert.Throws<InvalidDataException>(() => Load(path, settings));
        Assert.Contains(path, error.Message);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingDirectory_AllowsFirstUse(bool settings)
    {
        Load(Path.Join(_directory, "ontbreekt", "config.json"), settings);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("omschrijvingen.json")]
    public void UnreadableGlobalPath_DoesNotSilentlyReturnDefaults(string name)
    {
        GlobalLegendSettings.Save(_directory, new LegendSettings { Scale = 500 });
        string path = Path.Join(_directory, name);
        File.Delete(path);
        Directory.CreateDirectory(path);
        string other = Path.Join(_directory, name == "settings.json" ? "omschrijvingen.json" : "settings.json");
        var original = File.ReadAllBytes(other);
        var error = Record.Exception(() => GlobalLegendSettings.Load(_directory));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(path));
        Assert.Equal(original, File.ReadAllBytes(other));
    }
}
