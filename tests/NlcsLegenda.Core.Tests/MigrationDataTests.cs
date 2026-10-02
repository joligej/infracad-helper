using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// De legacy-migratie draait alleen als er nog geen beheerde legenda's zijn (idempotent) en
// leest oude instellingen via FromJson, dat nooit mag crashen op kapotte of gedeeltelijke
// data. Deze tests dekken die data-laag; de AutoCAD-grouprename zit in LegendManagement.
public class MigrationDataTests
{
    [Fact]
    public void FromJson_Corrupt_ReturnsDefaults()
    {
        Assert.NotNull(LegendSettings.FromJson("{ niet eens json"));
        Assert.NotNull(LegendSettings.FromJson(""));
        Assert.NotNull(LegendSettings.FromJson(null));
    }

    [Fact]
    public void FromJson_Partial_KeepsKnownFieldsDefaultsRest()
    {
        var s = LegendSettings.FromJson("{ \"scale\": 500 }");
        Assert.Equal(500, s.Scale);
        Assert.Equal(TemplateDefaults.TextHeightMm, s.TextHeightMm);
    }

    [Fact]
    public void Registry_RoundTrip_IsStableAndIndependent()
    {
        var reg = new LegendRegistry();
        reg.Add(new LegendDefinition { Name = "A", Settings = new LegendSettings { Scale = 200 } });
        reg.Add(new LegendDefinition { Name = "B", Settings = new LegendSettings { Scale = 500 } });
        reg.Legends[1].Settings.ExcludedEntries.Add("X");

        var json = reg.ToJson();
        Assert.True(LegendRegistry.TryParse(json, out var back, out _));
        Assert.Equal(2, back.Legends.Count);
        Assert.Equal(200, back.Legends[0].Settings.Scale);
        Assert.Equal(500, back.Legends[1].Settings.Scale);
        Assert.Empty(back.Legends[0].Settings.ExcludedEntries);
        Assert.Contains("X", back.Legends[1].Settings.ExcludedEntries);

        // Idempotent: nog een ronde verandert niets.
        Assert.True(LegendRegistry.TryParse(back.ToJson(), out var again, out _));
        Assert.Equal(json, again.ToJson());
    }

    [Fact]
    public void Clone_ProducesIndependentSettings()
    {
        var a = new LegendSettings { Scale = 200 };
        var b = a.Clone();
        b.Scale = 500;
        b.ExcludedEntries.Add("X");
        Assert.Equal(200, a.Scale);
        Assert.Empty(a.ExcludedEntries);
    }
}
