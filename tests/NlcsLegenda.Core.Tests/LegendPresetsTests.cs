using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendPresetsTests : IDisposable
{
    private readonly string _dir;

    public LegendPresetsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nlcs_presets_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Save_Then_Load_RoundTripsSettings()
    {
        var settings = new LegendSettings { Scale = 500, Title = "Mijn profiel", IncludeScaleBar = false };
        var path = LegendPresets.Save(_dir, "Compact", settings);

        Assert.True(File.Exists(path));
        var loaded = LegendPresets.Load(_dir, "Compact");
        Assert.NotNull(loaded);
        Assert.Equal(500, loaded!.Scale);
        Assert.Equal("Mijn profiel", loaded.Title);
        Assert.False(loaded.IncludeScaleBar);
    }

    [Fact]
    public void List_ReturnsSavedNames_Sorted()
    {
        LegendPresets.Save(_dir, "Zeta", new LegendSettings());
        LegendPresets.Save(_dir, "Alpha", new LegendSettings());

        Assert.Equal(new[] { "Alpha", "Zeta" }, LegendPresets.List(_dir).ToArray());
    }

    [Fact]
    public void Delete_RemovesPreset()
    {
        LegendPresets.Save(_dir, "Weg", new LegendSettings());
        Assert.True(LegendPresets.Exists(_dir, "Weg"));

        Assert.True(LegendPresets.Delete(_dir, "Weg"));
        Assert.False(LegendPresets.Exists(_dir, "Weg"));
        Assert.False(LegendPresets.Delete(_dir, "Weg"));
    }

    [Fact]
    public void Load_MissingPreset_ReturnsNull()
    {
        Assert.Null(LegendPresets.Load(_dir, "bestaatniet"));
    }

    [Fact]
    public void SafeFileName_ReplacesInvalidChars()
    {
        var safe = LegendPresets.SafeFileName("a/b:c*d");
        Assert.DoesNotContain('/', safe);
        Assert.DoesNotContain(':', safe);
        Assert.DoesNotContain('*', safe);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("prn")]
    [InlineData("Nul")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("CON.txt")]
    [InlineData("nul.json")]
    public void SafeFileName_PrefixesReservedDeviceNames(string reserved)
    {
        var safe = LegendPresets.SafeFileName(reserved);
        Assert.NotEqual(reserved, safe, StringComparer.OrdinalIgnoreCase);
        Assert.EndsWith(reserved, safe, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("naam.")]
    [InlineData("naam ")]
    [InlineData("naam. . ")]
    public void SafeFileName_StripsTrailingDotsAndSpaces(string name)
    {
        Assert.Equal("naam", LegendPresets.SafeFileName(name));
    }

    [Fact]
    public void SafeFileName_OnlyInvalid_FallsBackToDefault()
    {
        Assert.Equal("profiel", LegendPresets.SafeFileName("   "));
    }

    [Fact]
    public void Save_ReservedName_RoundTrips()
    {
        // Een profiel met een gereserveerde naam moet gewoon opslaan en terugladen.
        LegendPresets.Save(_dir, "CON", new LegendSettings { Scale = 300 });
        Assert.Equal(300, LegendPresets.Load(_dir, "CON")!.Scale);
    }

    [Fact]
    public void List_EmptyOrMissingDir_ReturnsEmpty()
    {
        Assert.Empty(LegendPresets.List(Path.Combine(_dir, "leeg")));
    }

    [Fact]
    public void Export_Then_Import_RoundTripsAcrossLocations()
    {
        var settings = new LegendSettings { Scale = 250, ToonArceringen = false };
        LegendPresets.Save(_dir, "Bron", settings);

        var shared = Path.Combine(_dir, "gedeeld.json");
        LegendPresets.Export(_dir, "Bron", shared);
        Assert.True(File.Exists(shared));

        // Importeren in een verse map (andere "computer").
        var other = Path.Combine(_dir, "andere");
        Assert.True(LegendPresets.TryImport(other, shared, "Overgenomen", out var error));
        Assert.Equal(string.Empty, error);

        var imported = LegendPresets.Load(other, "Overgenomen");
        Assert.NotNull(imported);
        Assert.Equal(250, imported!.Scale);
        Assert.False(imported.ToonArceringen);
    }

    [Fact]
    public void Import_InvalidJson_FailsAndLeavesExistingUntouched()
    {
        LegendPresets.Save(_dir, "Bestaand", new LegendSettings { Scale = 100 });
        var bad = Path.Combine(_dir, "kapot.json");
        File.WriteAllText(bad, "{ dit is geen geldige json ");

        Assert.False(LegendPresets.TryImport(_dir, bad, "Bestaand", out var error));
        Assert.NotEqual(string.Empty, error);

        // Het bestaande profiel is niet overschreven.
        Assert.Equal(100, LegendPresets.Load(_dir, "Bestaand")!.Scale);
    }

    [Fact]
    public void Import_MissingFile_Fails()
    {
        Assert.False(LegendPresets.TryImport(_dir, Path.Combine(_dir, "weg.json"), "X", out var error));
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void Export_MissingPreset_Throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => LegendPresets.Export(_dir, "bestaatniet", Path.Combine(_dir, "uit.json")));
    }
}
