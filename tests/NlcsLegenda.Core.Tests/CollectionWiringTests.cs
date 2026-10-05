using System.Reflection;
using System.Text.Json.Serialization;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Sluit de wiring-keten voor de collectie-instellingen: elke persistente collectie moet een
// JSON-roundtrip overleven en bij Clone onafhankelijk zijn. De reflectie-completenesscheck dwingt
// af dat een nieuwe collectie hier wordt opgenomen.
public class CollectionWiringTests
{
    private static readonly HashSet<string> Covered = new()
    {
        nameof(LegendSettings.IncludedStatuses),
        nameof(LegendSettings.IncludedDrawTypes),
        nameof(LegendSettings.MergedDimensions),
        nameof(LegendSettings.ExcludedEntries),
        nameof(LegendSettings.ManualEntries),
        nameof(LegendSettings.CustomStatuses),
        nameof(LegendSettings.CustomLayerRules),
        nameof(LegendSettings.XrefInclusion),
        nameof(LegendSettings.DescriptionOverrides),
        nameof(LegendSettings.ExcludedDisciplines),
        nameof(LegendSettings.ExcludedHoofdgroepen),
    };

    private static bool IsCollection(Type t) =>
        t != typeof(string) && (t.IsGenericType || t == typeof(DescriptionCatalog));

    private static PropertyInfo[] PersistentCollections() =>
        typeof(LegendSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true }
                        && p.GetCustomAttribute<JsonIgnoreAttribute>() is null
                        && IsCollection(p.PropertyType))
            .ToArray();

    [Fact]
    public void EveryPersistentCollection_IsCovered()
    {
        var actual = PersistentCollections().Select(p => p.Name).ToHashSet();
        var uncovered = actual.Where(n => !Covered.Contains(n)).ToList();
        Assert.True(uncovered.Count == 0, "Niet-afgedekte collecties: " + string.Join(", ", uncovered));
        var stale = Covered.Where(n => !actual.Contains(n)).ToList();
        Assert.True(stale.Count == 0, "Afdekking voor verdwenen collecties: " + string.Join(", ", stale));
    }

    private static LegendSettings Populated()
    {
        var s = new LegendSettings();
        s.IncludedStatuses.Clear();
        s.IncludedStatuses.Add(NlcsStatus.Nieuw);
        s.IncludedDrawTypes.Clear();
        s.IncludedDrawTypes.Add(NlcsDrawType.Symbool);
        s.MergedDimensions.Add(GroupDimension.Soort);
        s.ExcludedEntries.Add("N|WE|VH|X");
        s.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-X-G", Description = "Eigen" });
        s.CustomStatuses.Add(new CustomStatus { Name = "Onder voorbehoud", Members = { "N|WE|RI|R" } });
        s.CustomLayerRules.Add(new CustomLayerRule { Layer = "Eigen kabels", Element = "Datakabel", Type = NlcsDrawType.Geometrie, Description = "Datakabel" });
        s.XrefInclusion["ref1"] = true;
        s.DescriptionOverrides.Elementen["VH|EL"] = new DescriptionEntry { Specifiek = "Tekst" };
        s.ExcludedDisciplines.Clear();
        s.ExcludedDisciplines.Add("ZZ");
        s.ExcludedHoofdgroepen.Clear();
        s.ExcludedHoofdgroepen.Add("QQ");
        return s;
    }

    [Fact]
    public void AllCollections_SurviveJsonRoundtrip()
    {
        var back = LegendSettings.FromJson(Populated().ToJson());

        Assert.Equal(new[] { NlcsStatus.Nieuw }, back.IncludedStatuses);
        Assert.Equal(new[] { NlcsDrawType.Symbool }, back.IncludedDrawTypes);
        Assert.Contains(GroupDimension.Soort, back.MergedDimensions);
        Assert.Contains("N|WE|VH|X", back.ExcludedEntries);
        Assert.Single(back.ManualEntries);
        Assert.Equal("Eigen", back.ManualEntries[0].Description);
        Assert.Single(back.CustomStatuses);
        Assert.Contains("N|WE|RI|R", back.CustomStatuses[0].Members);
        Assert.Single(back.CustomLayerRules);
        Assert.Equal("Eigen kabels", back.CustomLayerRules[0].Layer);
        Assert.Equal("Datakabel", back.CustomLayerRules[0].Element);
        Assert.True(back.IsXrefIncluded("ref1"));
        Assert.Equal("Tekst", back.DescriptionOverrides.Elementen["VH|EL"].Specifiek);
        Assert.Contains("ZZ", back.ExcludedDisciplines);
        Assert.Contains("QQ", back.ExcludedHoofdgroepen);
    }

    [Fact]
    public void AllCollections_CloneIndependently()
    {
        var original = Populated();
        var clone = original.Clone();

        original.IncludedStatuses.Add(NlcsStatus.Bestaand);
        original.IncludedDrawTypes.Add(NlcsDrawType.Geometrie);
        original.MergedDimensions.Add(GroupDimension.Specificatie);
        original.ExcludedEntries.Add("extra");
        original.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-Y-G", Description = "Extra" });
        original.CustomStatuses[0].Members.Add("extra");
        original.CustomLayerRules.Add(new CustomLayerRule { Layer = "Extra laag", Element = "Extra" });
        original.XrefInclusion["ref2"] = false;
        original.DescriptionOverrides.Elementen["VH|EXTRA"] = new DescriptionEntry { Specifiek = "x" };
        original.ExcludedDisciplines.Add("extra");
        original.ExcludedHoofdgroepen.Add("extra");

        Assert.Single(clone.IncludedStatuses);
        Assert.Single(clone.IncludedDrawTypes);
        Assert.Single(clone.MergedDimensions);
        Assert.Single(clone.ExcludedEntries);
        Assert.Single(clone.ManualEntries);
        Assert.Single(clone.CustomStatuses[0].Members);
        Assert.Single(clone.CustomLayerRules);
        Assert.Single(clone.XrefInclusion);
        Assert.Single(clone.DescriptionOverrides.Elementen);
        Assert.Single(clone.ExcludedDisciplines);
        Assert.Single(clone.ExcludedHoofdgroepen);
    }
}
