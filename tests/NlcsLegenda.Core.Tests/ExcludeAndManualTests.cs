using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class ExcludeAndManualTests
{
    private static List<NlcsLayerName> Parse(params string[] layers)
    {
        var list = new List<NlcsLayerName>();
        foreach (var l in layers)
            if (NlcsLayerParser.TryParse(l, out var p))
                list.Add(p!);
        return list;
    }

    [Fact]
    public void XrefInclusion_PerXrefOverridesGlobalSwitch()
    {
        var settings = new LegendSettings { IncludeXrefLayers = false };
        NlcsLayerParser.TryParse("SIT-ARCERINGEN|N-WE-VH-OPENVERHARDING_TEGEL-A", out var lyr);

        // Algemeen uit: xref-laag valt buiten de legenda.
        Assert.False(settings.IsIncluded(lyr!));

        // Tekeningspecifiek deze xref aan: nu wel meegenomen.
        settings.XrefInclusion["SIT-ARCERINGEN"] = true;
        Assert.True(settings.IsIncluded(lyr!));

        // Een andere xref volgt nog steeds de algemene schakelaar (uit).
        NlcsLayerParser.TryParse("ANDERE|N-WE-VH-OPENVERHARDING_TEGEL-A", out var other);
        Assert.False(settings.IsIncluded(other!));
    }

    [Fact]
    public void XrefInclusion_NestedXref_FollowsTopLevelChoice()
    {
        var settings = new LegendSettings { IncludeXrefLayers = false };
        // Een geneste xref-laag: 'boven|onder|N-...'. De gebruiker kan alleen de
        // bovenliggende xref kiezen; die keuze bepaalt ook de geneste inhoud.
        NlcsLayerParser.TryParse("BOVEN|ONDER|N-WE-VH-OPENVERHARDING_TEGEL-A", out var nested);
        Assert.False(settings.IsIncluded(nested!));

        settings.XrefInclusion["BOVEN"] = true;
        Assert.True(settings.IsIncluded(nested!));
        Assert.True(settings.IsXrefIncluded("BOVEN|ONDER"));

        // De bovenliggende xref uitzetten sluit de geneste inhoud ook uit.
        settings.XrefInclusion["BOVEN"] = false;
        Assert.False(settings.IsXrefIncluded("BOVEN|ONDER"));
    }

    [Fact]
    public void ExcludedEntry_IsSkipped()
    {
        var settings = new LegendSettings();
        var layers = Parse(
            "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A",
            "N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G");

        var before = LegendGrouping.Build(layers, settings, _ => null);
        Assert.Equal(2, before.Count);

        // Vink de rioolleiding uit.
        settings.ExcludedEntries.Add(LegendSettings.EntryKey("N", "WE", "RI", "HWA_RIOOLLEIDING_PVC_160"));
        var after = LegendGrouping.Build(layers, settings, _ => null);

        Assert.Single(after);
        Assert.DoesNotContain(after, e => e.Hoofdgroep == "RI");
    }

    [Fact]
    public void EntryKey_FromLayerAndEntry_Match()
    {
        var layer = Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G")[0];
        var entries = LegendGrouping.Build(new[] { layer }, new LegendSettings(), _ => null);
        Assert.Equal(LegendSettings.EntryKey(layer), LegendSettings.EntryKey(entries[0]));
    }

    [Fact]
    public void ManualEntry_IsAddedToLegend()
    {
        var settings = new LegendSettings();
        settings.ManualEntries.Add(new ManualEntry
        {
            Layer = "EIGEN-DUIKER",
            Type = NlcsDrawType.Arcering,
            Description = "Eigen duiker",
            Status = NlcsStatus.Nieuw,
            HatchPattern = "ANSI31"
        });

        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), settings, _ => null);

        Assert.Equal(2, entries.Count);
        var manual = Assert.Single(entries, e => e.Description == "Eigen duiker");
        Assert.True(manual.HasHatch);
        Assert.Equal("EIGEN-DUIKER", manual.HatchLayer);
    }

    [Fact]
    public void ManualEntry_LineType_IsNotArea()
    {
        var m = new ManualEntry { Layer = "MIJN-LIJN", Type = NlcsDrawType.Geometrie, Description = "Kabelgoot" };
        var e = m.ToLegendEntry();
        Assert.Equal("MIJN-LIJN", e.GeometryLayer);
        Assert.False(e.IsArea);
        Assert.False(e.HasHatch);
    }

    [Fact]
    public void ManualEntry_Invalid_IsIgnored()
    {
        var settings = new LegendSettings();
        settings.ManualEntries.Add(new ManualEntry { Layer = "", Description = "Zonder laag" });
        settings.ManualEntries.Add(new ManualEntry { Layer = "X", Description = "" });

        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), settings, _ => null);

        Assert.Single(entries);
    }

    [Fact]
    public void ExcludedEntries_SurviveJsonRoundtrip()
    {
        var settings = new LegendSettings();
        settings.ExcludedEntries.Add("N|WE|RI|HWA_RIOOLLEIDING_PVC_160");
        settings.ManualEntries.Add(new ManualEntry { Layer = "L", Type = NlcsDrawType.Symbool, Description = "Put" });

        var back = LegendSettings.FromJson(settings.ToJson());

        Assert.Contains("N|WE|RI|HWA_RIOOLLEIDING_PVC_160", back.ExcludedEntries);
        Assert.Single(back.ManualEntries);
        Assert.Equal(NlcsDrawType.Symbool, back.ManualEntries[0].Type);
        Assert.Equal("Put", back.ManualEntries[0].Description);
    }

    [Fact]
    public void EntryKey_NonStandardStatusCode_LayerAndEntryMatch()
    {
        // Statuscode "Q" valt onder Overig; laag- en entry-sleutel moeten identiek zijn.
        var settings = new LegendSettings();
        settings.IncludedStatuses.Add(NlcsStatus.Overig);
        var layer = Parse("Q-WE-VH-TESTELEMENT-A")[0];
        var entries = LegendGrouping.Build(new[] { layer }, settings, _ => null);

        Assert.Equal(LegendSettings.EntryKey(layer), LegendSettings.EntryKey(entries[0]));
        Assert.StartsWith("X|", LegendSettings.EntryKey(layer));
    }

    [Theory]
    [InlineData("N-WE-VH-EIGEN-A", true)]
    [InlineData("Eigen laag met spaties", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("fout/schuine", false)]
    [InlineData("met\"quote", false)]
    [InlineData("komma,fout", false)]
    [InlineData("is=gelijk", false)]
    public void LayerNaming_ValidatesNames(string name, bool expected)
    {
        Assert.Equal(expected, LayerNaming.IsValid(name));
    }

    [Fact]
    public void ManualEntry_WithForbiddenLayerChar_IsIgnored()
    {
        var settings = new LegendSettings();
        settings.ManualEntries.Add(new ManualEntry
        {
            Layer = "fout/laag",
            Type = NlcsDrawType.Geometrie,
            Description = "Ongeldige laagnaam"
        });

        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), settings, _ => null);

        Assert.Single(entries);
        Assert.DoesNotContain(entries, e => e.Description == "Ongeldige laagnaam");
    }
}
