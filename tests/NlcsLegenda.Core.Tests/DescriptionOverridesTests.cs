using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Per-legenda omschrijvingen: overrides horen bij de legenda-snapshot, zijn onafhankelijk per
// legenda en overleven serialisatie. Kritieke-staat parsing faalt hard op kapotte JSON.
public class DescriptionOverridesTests
{
    [Fact]
    public void Clone_KeepsOverridesIndependent()
    {
        var a = new LegendSettings();
        a.DescriptionOverrides.Elementen["RI|PUT"] = new DescriptionEntry { Specifiek = "Tekst A" };
        var b = a.Clone();
        b.DescriptionOverrides.Elementen["RI|PUT"] = new DescriptionEntry { Specifiek = "Tekst B" };

        Assert.Equal("Tekst A", a.DescriptionOverrides.Elementen["RI|PUT"].Specifiek);
        Assert.Equal("Tekst B", b.DescriptionOverrides.Elementen["RI|PUT"].Specifiek);
    }

    [Fact]
    public void Overrides_SurviveRegistryRoundTrip_PerLegend()
    {
        var reg = new LegendRegistry();
        var sa = new LegendSettings();
        sa.DescriptionOverrides.Elementen["RI|PUT"] = new DescriptionEntry { Specifiek = "A-tekst" };
        var sb = new LegendSettings();
        sb.DescriptionOverrides.Elementen["RI|PUT"] = new DescriptionEntry { Specifiek = "B-tekst" };
        reg.Add(new LegendDefinition { Name = "A", Settings = sa });
        reg.Add(new LegendDefinition { Name = "B", Settings = sb });

        Assert.True(LegendRegistry.TryParse(reg.ToJson(), out var back, out _));
        Assert.Equal("A-tekst", back.Legends[0].Settings.DescriptionOverrides.Elementen["RI|PUT"].Specifiek);
        Assert.Equal("B-tekst", back.Legends[1].Settings.DescriptionOverrides.Elementen["RI|PUT"].Specifiek);
    }

    [Fact]
    public void EffectiveCatalog_OverrideBeatsGlobal()
    {
        var global = DescriptionCatalog.Default();
        var effective = global.Clone();
        effective.MergeFrom(new DescriptionCatalog
        {
            Elementen = { ["RI|PUT"] = new DescriptionEntry { Specifiek = "Eigen put" } }
        });
        Assert.Equal("Eigen put", effective.Elementen["RI|PUT"].Specifiek);
    }

    [Fact]
    public void TryParse_Corrupt_FailsHard()
    {
        Assert.False(DescriptionCatalog.TryParse("{ kapot", out _));
        Assert.False(DescriptionCatalog.TryParse("", out _));
        Assert.True(DescriptionCatalog.TryParse("{\"elementen\":{}}", out _));
    }

    [Fact]
    public void Normalize_NullOverrides_Safe()
    {
        var s = new LegendSettings { DescriptionOverrides = null! };
        s.Normalize();
        Assert.NotNull(s.DescriptionOverrides);
    }
}
