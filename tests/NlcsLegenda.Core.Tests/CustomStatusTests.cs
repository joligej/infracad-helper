using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class CustomStatusTests
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
    public void CustomStatus_AssignsAutomaticLayer_AndSortsAfterNatural()
    {
        var settings = new LegendSettings();
        var riool = Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G")[0];
        var verharding = Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A")[0];

        settings.CustomStatuses.Add(new CustomStatus
        {
            Name = "Onder voorbehoud",
            Members = { LegendSettings.EntryKey(riool) }
        });

        var entries = LegendGrouping.Build(new[] { riool, verharding }, settings, _ => null);

        var assigned = Assert.Single(entries, e => e.Hoofdgroep == "RI");
        Assert.Equal("Onder voorbehoud", assigned.CustomStatusName);
        Assert.Equal("Onder voorbehoud", assigned.StatusGroupId);

        // De eigen status komt ná de vaste statussen: de verharding (Nieuw) staat vooraan.
        Assert.Equal("VH", entries[0].Hoofdgroep);
        Assert.Equal("RI", entries[^1].Hoofdgroep);
    }

    [Fact]
    public void CustomStatus_OverridesStatusFilter()
    {
        var settings = new LegendSettings();
        // Vervallen staat uit.
        settings.IncludedStatuses.Remove(NlcsStatus.Vervallen);
        var vervallen = Parse("V-WE-RI-HWA_RIOOLLEIDING_PVC_160-G")[0];

        // Zonder toewijzing valt de vervallen laag buiten de legenda.
        Assert.False(settings.IsIncluded(vervallen));

        settings.CustomStatuses.Add(new CustomStatus
        {
            Name = "Te verwijderen",
            Members = { LegendSettings.EntryKey(vervallen) }
        });

        // De handmatige toewissing haalt hem alsnog binnen.
        Assert.True(settings.IsIncluded(vervallen));
        var entries = LegendGrouping.Build(new[] { vervallen }, settings, _ => null);
        Assert.Single(entries);
        Assert.Equal("Te verwijderen", entries[0].CustomStatusName);
    }

    [Fact]
    public void CustomStatus_AssignsManualEntry()
    {
        var settings = new LegendSettings();
        settings.ManualEntries.Add(new ManualEntry
        {
            Layer = "EIGEN-KABEL",
            Type = NlcsDrawType.Geometrie,
            Description = "Eigen kabel"
        });
        var key = LegendSettings.EntryKey(settings.ManualEntries[0].ToLegendEntry());
        settings.CustomStatuses.Add(new CustomStatus { Name = "Nutsvoorziening", Members = { key } });

        var entries = LegendGrouping.Build(
            Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A"), settings, _ => null);

        var manual = Assert.Single(entries, e => e.Description == "Eigen kabel");
        Assert.Equal("Nutsvoorziening", manual.CustomStatusName);
    }

    [Fact]
    public void CustomStatus_ExcludedEntryStillWins()
    {
        var settings = new LegendSettings();
        var riool = Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G")[0];
        var key = LegendSettings.EntryKey(riool);
        settings.CustomStatuses.Add(new CustomStatus { Name = "Eigen", Members = { key } });
        settings.ExcludedEntries.Add(key);

        Assert.False(settings.IsIncluded(riool));
    }

    [Fact]
    public void CustomStatus_LayoutHeaderUsesName()
    {
        var settings = new LegendSettings { IncludeGroupHeaders = true };
        var riool = Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G")[0];
        settings.CustomStatuses.Add(new CustomStatus
        {
            Name = "Onder voorbehoud",
            Members = { LegendSettings.EntryKey(riool) }
        });

        var entries = LegendGrouping.Build(new[] { riool }, settings, _ => null);
        var layout = LegendLayoutEngine.Compute(entries, settings);

        Assert.Contains(layout.Items,
            i => i.Kind == LegendItemKind.Header && i.Text == "Onder voorbehoud");
    }

    [Fact]
    public void CustomStatuses_SurviveJsonRoundtrip()
    {
        var settings = new LegendSettings();
        settings.CustomStatuses.Add(new CustomStatus
        {
            Name = "Fase 2",
            Members = { "N|WE|RI|HWA_RIOOLLEIDING_PVC_160", "N|XX|HM|EIGEN_KABEL" }
        });

        var back = LegendSettings.FromJson(settings.ToJson());

        var cs = Assert.Single(back.CustomStatuses);
        Assert.Equal("Fase 2", cs.Name);
        Assert.Equal(2, cs.Members.Count);
        Assert.Contains("N|WE|RI|HWA_RIOOLLEIDING_PVC_160", cs.Members);
    }

    [Fact]
    public void CustomStatuses_KeepDefinitionOrder()
    {
        var settings = new LegendSettings();
        var a = Parse("N-WE-RI-HWA_RIOOLLEIDING_PVC_160-G")[0];
        var b = Parse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A")[0];

        settings.CustomStatuses.Add(new CustomStatus { Name = "Later", Members = { LegendSettings.EntryKey(a) } });
        settings.CustomStatuses.Add(new CustomStatus { Name = "Eerder", Members = { LegendSettings.EntryKey(b) } });

        var entries = LegendGrouping.Build(new[] { a, b }, settings, _ => null);
        var customEntries = entries.Where(e => e.CustomStatusName is not null).ToList();

        // "Later" is als eerste gedefinieerd en komt dus vóór "Eerder".
        Assert.Equal("Later", customEntries[0].CustomStatusName);
        Assert.Equal("Eerder", customEntries[^1].CustomStatusName);
    }
}
