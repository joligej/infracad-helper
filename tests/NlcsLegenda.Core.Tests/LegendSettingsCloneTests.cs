using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LegendSettingsCloneTests
{
    [Fact]
    public void Clone_ProducesIndependentCollections()
    {
        var a = new LegendSettings();
        a.ExcludedEntries.Add("N|WE|VH|X");
        a.TextOverrides["EL"] = "Tekst";
        a.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-EIGEN-G", Description = "Eigen" });
        a.CustomStatuses.Add(new CustomStatus { Name = "Onder voorbehoud", Members = { "N|WE|RI|R" } });

        var b = a.Clone();

        Assert.NotSame(a.ExcludedEntries, b.ExcludedEntries);
        Assert.NotSame(a.IncludedStatuses, b.IncludedStatuses);
        Assert.NotSame(a.IncludedDrawTypes, b.IncludedDrawTypes);
        Assert.NotSame(a.TextOverrides, b.TextOverrides);
        Assert.NotSame(a.XrefInclusion, b.XrefInclusion);
        Assert.NotSame(a.ManualEntries, b.ManualEntries);
        Assert.NotSame(a.CustomStatuses, b.CustomStatuses);
        Assert.NotSame(a.ManualEntries[0], b.ManualEntries[0]);
        Assert.NotSame(a.CustomStatuses[0], b.CustomStatuses[0]);
        Assert.NotSame(a.CustomStatuses[0].Members, b.CustomStatuses[0].Members);
    }

    [Fact]
    public void MutatingClone_DoesNotAffectSource()
    {
        var a = new LegendSettings { Scale = 200 };
        a.ExcludedEntries.Add("keep");
        a.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-X-G", Description = "A" });

        var b = a.Clone();
        b.Scale = 500;
        b.ExcludedEntries.Add("only-b");
        b.ExcludedEntries.Remove("keep");
        b.ManualEntries[0].Description = "B";
        b.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-Y-G", Description = "extra" });

        Assert.Equal(200, a.Scale);
        Assert.Contains("keep", a.ExcludedEntries);
        Assert.DoesNotContain("only-b", a.ExcludedEntries);
        Assert.Single(a.ManualEntries);
        Assert.Equal("A", a.ManualEntries[0].Description);
    }

    [Fact]
    public void MutatingSource_DoesNotAffectClone()
    {
        var a = new LegendSettings();
        a.CustomStatuses.Add(new CustomStatus { Name = "S", Members = { "x" } });

        var b = a.Clone();
        a.CustomStatuses[0].Members.Add("y");
        a.CustomStatuses.Add(new CustomStatus { Name = "T" });

        Assert.Single(b.CustomStatuses);
        Assert.Single(b.CustomStatuses[0].Members);
    }

    [Fact]
    public void Clone_PreservesCaseInsensitiveSemantics()
    {
        var a = new LegendSettings();
        a.XrefInclusion["xref-a"] = true;
        a.ExcludedEntries.Add("N|WE|VH|Tegel");

        var b = a.Clone();

        Assert.True(b.IsXrefIncluded("XREF-A"));
        Assert.Contains("n|we|vh|tegel", b.ExcludedEntries);
    }

    [Fact]
    public void FromJson_RestoresCaseInsensitiveComparers()
    {
        var a = new LegendSettings();
        a.XrefInclusion["Xref-B"] = true;
        a.ExcludedEntries.Add("N|WE|RI|Riool");
        a.TextOverrides["Element"] = "Waarde";

        var restored = LegendSettings.FromJson(a.ToJson());

        Assert.True(restored.IsXrefIncluded("xref-b"));
        Assert.Contains("n|we|ri|riool", restored.ExcludedEntries);
        Assert.True(restored.TextOverrides.ContainsKey("ELEMENT"));
    }

    // De instellingen-dialog bewerkt een werkkopie en past die bij Opslaan/Toepassen toe via
    // CopyFrom. Dit borgt dat toepassen de doel-collecties volledig vervangt en dat bron en doel
    // daarna geen enkele collectie-instance delen (annuleren laat het doel dus ongemoeid).
    [Fact]
    public void CopyFrom_VervangtInhoudCollectiesEnBlijftOnafhankelijk()
    {
        var target = new LegendSettings { Scale = 100 };
        target.ExcludedEntries.Add("oud");
        target.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-OUD-G", Description = "oud" });
        target.CustomStatuses.Add(new CustomStatus { Name = "OudeStatus", Members = { "a" } });
        target.XrefInclusion["oud"] = true;

        var working = new LegendSettings { Scale = 500 };
        working.ExcludedEntries.Add("nieuw");
        working.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VH-NIEUW-G", Description = "nieuw" });
        working.CustomStatuses.Add(new CustomStatus { Name = "NieuweStatus", Members = { "b", "c" } });
        working.XrefInclusion["nieuw"] = false;

        target.CopyFrom(working);

        // Doel is nu gelijk aan de werkkopie, niet gemengd met de oude inhoud.
        Assert.Equal(500, target.Scale);
        Assert.Contains("nieuw", target.ExcludedEntries);
        Assert.DoesNotContain("oud", target.ExcludedEntries);
        Assert.Single(target.ManualEntries);
        Assert.Equal("nieuw", target.ManualEntries[0].Description);
        Assert.Single(target.CustomStatuses);
        Assert.Equal("NieuweStatus", target.CustomStatuses[0].Name);
        Assert.True(target.XrefInclusion.ContainsKey("nieuw"));
        Assert.False(target.XrefInclusion.ContainsKey("oud"));

        // Geen gedeelde instances: na toepassen blijft het doel los van de werkkopie.
        Assert.NotSame(working.ExcludedEntries, target.ExcludedEntries);
        Assert.NotSame(working.ManualEntries, target.ManualEntries);
        Assert.NotSame(working.ManualEntries[0], target.ManualEntries[0]);
        Assert.NotSame(working.CustomStatuses[0].Members, target.CustomStatuses[0].Members);

        working.ManualEntries[0].Description = "gewijzigd";
        working.CustomStatuses[0].Members.Add("d");
        working.ExcludedEntries.Add("later");
        Assert.Equal("nieuw", target.ManualEntries[0].Description);
        Assert.Equal(2, target.CustomStatuses[0].Members.Count);
        Assert.DoesNotContain("later", target.ExcludedEntries);
    }
}
