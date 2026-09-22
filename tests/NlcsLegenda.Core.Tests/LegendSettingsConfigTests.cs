using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendSettingsConfigTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nlcs_{Guid.NewGuid():N}.json");
        try
        {
            var original = new LegendSettings
            {
                Scale = 500,
                IncludeText = false,
                MaxRowsPerColumn = 12,
                ColumnWidthMm = 90,
                Title = "MIJN LEGENDA"
            };
            original.TextOverrides["OPENVERHARDING_BETONSTRAATSTEEN"] = "Klinkers";
            original.IncludedStatuses = new HashSet<NlcsStatus> { NlcsStatus.Nieuw, NlcsStatus.Bestaand };

            original.Save(path);
            var loaded = LegendSettings.Load(path);

            Assert.Equal(500, loaded.Scale);
            Assert.False(loaded.IncludeText);
            Assert.Equal(12, loaded.MaxRowsPerColumn);
            Assert.Equal(90, loaded.ColumnWidthMm);
            Assert.Equal("MIJN LEGENDA", loaded.Title);
            Assert.Equal("Klinkers", loaded.TextOverrides["OPENVERHARDING_BETONSTRAATSTEEN"]);
            Assert.Equal(2, loaded.IncludedStatuses.Count);
            Assert.Contains(NlcsStatus.Nieuw, loaded.IncludedStatuses);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = LegendSettings.Load(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.json"));
        Assert.Equal(200, loaded.Scale);
        Assert.True(loaded.IncludeText);
    }

    [Fact]
    public void ToModel_ConvertsPaperMmByScale()
    {
        var s = new LegendSettings { Scale = 200 };
        Assert.Equal(4.0, s.ToModel(20.0), 6); // 20 mm @ 1:200 = 4 m
        Assert.Equal(0.5, s.ToModel(2.5), 6);  // 2.5 mm @ 1:200 = 0.5 m
    }
}
