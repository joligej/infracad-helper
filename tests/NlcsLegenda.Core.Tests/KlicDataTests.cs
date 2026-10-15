using System.IO.Compression;
using System.Text;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class KlicDataTests
{
    private static KlicRecord Record(KlicSource source, params (string Key, string Value)[] values) =>
        new(source, "KLIC_LeidingLijn", values.ToDictionary(v => v.Key, v => v.Value));
    private static KlicObjectData Data(string handle, params KlicRecord[] records) =>
        new("bron.dwg", handle, "", records, Array.Empty<string>(), KlicSource.InfraCAD);
    private static NlcsLayerName Layer(string name, KlicObjectData data, LegendSettings settings) => new()
    {
        Raw = "B-WE-KL-WATER-G", LocalName = "B-WE-KL-WATER-G", StatusCode = "B", Status = NlcsStatus.Bestaand,
        Discipline = "WE", Hoofdgroep = "KL", Element = "WATER", TypeSuffix = "G", DrawType = NlcsDrawType.Geometrie,
        RenderLayerName = name, Netbeheerder = data.Provider, KlicGroupKey = data.GroupKey(settings.Klic.GroupFields)
    };

    [Fact]
    public void PropertySetEditIsVisibleAndPreferenceCanUseItWithoutChangingTheSource()
    {
        var obj = Data("A", Record(KlicSource.InfraCAD, ("Materiaal", "PVC"), ("DiameterM", "0.11")),
            Record(KlicSource.PropertySets, ("Materiaal", "Staal"), ("DiameterM", "0.1100")));
        Assert.Equal(new[] { "Materiaal" }, obj.Conflicts);
        Assert.Equal("PVC", Assert.Single(obj.Values("Materiaal")));
        var edited = obj with { PreferredSource = KlicSource.PropertySets };
        Assert.Equal("Staal", Assert.Single(edited.Values("Materiaal")));
        Assert.Contains("Materiaal", KlicSummary.Issues(new[] { edited }));
        Assert.Equal("PVC", obj.Records[0].Values["Materiaal"]);
    }

    [Fact]
    public void ExplicitEmptyPreferredValueIsNotReplacedWithAnOldValue()
    {
        var obj = Data("A", Record(KlicSource.InfraCAD, ("Materiaal", "PVC"), ("Thema", "Water")),
            Record(KlicSource.PropertySets, ("Materiaal", ""))) with { PreferredSource = KlicSource.PropertySets };
        Assert.Equal("Niet opgegeven", KlicSummary.Field(new[] { obj }, "Materiaal"));
        Assert.Equal("Water", Assert.Single(obj.Values("Thema")));
        Assert.Equal(KlicSource.InfraCAD, Assert.Single(obj.Selected("Thema")).Source);
    }

    [Theory]
    [InlineData("DiameterM", "0.11", "110 mm")]
    [InlineData("DiepteLeggingM", "0", "Niet opgegeven (0)")]
    [InlineData("DiepteLeggingM", "-1", "Niet opgegeven (-1)")]
    [InlineData("StandaardDiepteLeggingM", "0.8", "0,8 m")]
    [InlineData("OperatingVoltageV", "10000", "10000 V")]
    [InlineData("BuisDrukBar", "0.03", "0,03 bar")]
    [InlineData("Materiaal", "Onbekend", "Niet opgegeven")]
    public void UnitsAndUnknownValuesAreExplicit(string key, string raw, string expected) => Assert.Equal(expected, KlicFields.Format(key, raw));

    [Fact]
    public void ConflictingRecordsWithinOneSourceKeepAllValues()
    {
        var obj = Data("A", Record(KlicSource.ObjectData, ("Materiaal", "PE")), Record(KlicSource.ObjectData, ("Materiaal", "PVC")));
        Assert.Equal(new[] { "PE", "PVC" }, obj.Values("Materiaal"));
        Assert.Contains("Materiaal", obj.Conflicts);
        Assert.Equal("PE; PVC", KlicSummary.Field(new[] { obj }, "Materiaal"));
    }

    [Fact]
    public void DifferentTablesAreNotSourceConflictsAndFallbackIsPerTable()
    {
        var admin = new KlicRecord(KlicSource.InfraCAD, "KLIC_Administratief", new Dictionary<string,string> { ["Omschrijving"] = "" });
        var line = Record(KlicSource.PropertySets, ("Omschrijving", "Aansluitleiding"));
        var obj = Data("A", admin, admin with { Source = KlicSource.PropertySets }, line);
        Assert.Empty(obj.Conflicts);
        Assert.Equal(new[] { "", "Aansluitleiding" }, obj.Values("Omschrijving"));
        Assert.Equal(2, obj.Selected("Omschrijving").Count);
        Assert.Equal(KlicSource.InfraCAD, obj.Selected("Omschrijving")[0].Source);
        Assert.Equal(KlicSource.PropertySets, obj.Selected("Omschrijving")[1].Source);
        var changed = obj with { Records = obj.Records.Append(line with { Source = KlicSource.InfraCAD,
            Values = new Dictionary<string,string> { ["Omschrijving"] = "Distributieleiding" } }).ToArray() };
        Assert.Contains("Omschrijving", changed.Conflicts);
    }

    [Fact]
    public void SameHandleInDifferentDrawingsOrXrefsDoesNotLoseMetadata()
    {
        var a = Data("A", Record(KlicSource.InfraCAD, ("Materiaal", "PE")));
        var b = a with { Drawing = "other.dwg" };
        var c = a with { Xref = "nested|source" };
        Assert.Equal(3, KlicSummary.Combine(new[] { a, a, b, c }).Count);
    }

    [Fact]
    public void MaterialAndDiameterPartitionsPreserveQuantitiesManualOrderAndDescriptions()
    {
        var a = Data("A", Record(KlicSource.InfraCAD, ("Netbeheerder", "Waternet"), ("Materiaal", "PE"), ("DiameterM", "0.11")));
        var b = Data("B", Record(KlicSource.InfraCAD, ("Netbeheerder", "Waternet"), ("Materiaal", "PVC"), ("DiameterM", "0.16")));
        var settings = new LegendSettings();
        var objects = new Dictionary<string, IReadOnlyList<KlicObjectData>> { ["a"] = new[] { a }, ["b"] = new[] { b } };
        var metrics = new Dictionary<string, LayerMetric> { ["a"] = new(1, 12, 0), ["b"] = new(1, 7, 0) };
        IReadOnlyList<LegendEntry> Build() => LegendGrouping.Build(new[] { Layer("a", a, settings), Layer("b", b, settings) }, settings,
            metrics: metrics, klicObjects: key => objects[key]);
        var merged = Assert.Single(Build());
        Assert.Equal(19, merged.Metric.Length); Assert.Equal("PE; PVC", KlicSummary.Field(merged.KlicObjects, "Materiaal"));
        Assert.DoesNotContain("Diameter", merged.DisplayDescription);
        settings.Klic.GroupFields.AddRange(new[] { "Materiaal", "DiameterM" });
        settings.Klic.DescriptionFields.AddRange(new[] { "Materiaal", "DiameterM" });
        settings.MergedDimensions.UnionWith(Enum.GetValues<GroupDimension>());
        var split = Build();
        Assert.Equal(2, split.Count); Assert.Equal(19, split.Sum(e => e.Metric.Length));
        Assert.Contains(split, e => e.DisplayDescription.Contains("110 mm") && Math.Abs(e.Metric.Length - 12) < 1e-9);
        Assert.Contains(split, e => e.DisplayDescription.Contains("160 mm") && Math.Abs(e.Metric.Length - 7) < 1e-9);
        Assert.NotEqual(LegendOrder.Key(split[0]), LegendOrder.Key(split[1]));
        settings.SortMode = LegendSortMode.Handmatig;
        settings.ManualEntryOrder = LegendOrder.Remember(new List<string>(), split.Reverse());
        Assert.Equal(split.Reverse().Select(LegendOrder.Key), Build().Select(LegendOrder.Key));
        settings.Klic.GroupFields.Clear();
        Assert.Equal(19, Assert.Single(Build()).Metric.Length);
    }

    [Fact]
    public void ProviderExclusionAndCustomStatusSurviveNewMetadataPartitions()
    {
        var obj = Data("A", Record(KlicSource.InfraCAD, ("Netbeheerder", "Provider"), ("Materiaal", "PE")));
        var s = new LegendSettings(); var oldKey = LegendSettings.EntryKey(Layer("a", obj, s));
        s.Klic.GroupFields.Add("Materiaal"); var layer = Layer("a", obj, s); var newKey = LegendSettings.EntryKey(layer);
        s.ExcludedEntries.Add(oldKey); Assert.False(s.IsIncluded(layer));
        var tree = CompositionTree.Build(new[] { (newKey, "PE", "Water") }, s.ExcludedEntries);
        Assert.False(Assert.Single(tree.AllLeaves()).Included);
        s.ExcludedEntries.Clear(); s.CustomStatuses.Add(new CustomStatus { Name = "Mijn status", Members = new() { oldKey } });
        Assert.Equal("Mijn status", s.FindCustomStatus(newKey)!.Name);
    }

    [Fact]
    public void SettingsRoundTripAndCloneDoNotShareKlicLists()
    {
        var s = new LegendSettings { Klic = new() { Columns = new() { "Materiaal" }, DescriptionFields = new() { "DiameterM" }, GroupFields = new() { "Thema" }, PreferredSource = KlicSource.ObjectData } };
        var clone = s.Clone(); clone.Klic.Columns.Clear(); clone.Klic.GroupFields.Clear(); clone.Klic.DescriptionFields.Clear();
        Assert.Single(s.Klic.Columns); Assert.Single(s.Klic.GroupFields); Assert.Single(s.Klic.DescriptionFields);
        Assert.True(LegendSettings.TryParse(s.ToJson(), out var back));
        Assert.Equal(KlicSource.ObjectData, back!.Klic.PreferredSource);
        Assert.Equal(s.Klic.GroupFields, back.Klic.GroupFields);
        Assert.True(LegendSettings.TryParse("{\"klic\":null}", out var old)); Assert.NotNull(old!.Klic);
    }

    [Fact]
    public void SelectedExportColumnsKeepMixedValuesAndEscapeFormulas()
    {
        var a = Data("A", Record(KlicSource.InfraCAD, ("Materiaal", "=HYPERLINK(\"x\")"), ("DiameterM", "0")));
        var b = Data("B", Record(KlicSource.InfraCAD, ("Materiaal", "PVC"), ("DiameterM", "0.11")));
        var s = new LegendSettings();
        var entry = Assert.Single(LegendGrouping.Build(new[] { Layer("a", a, s) }, s, klicObjects: _ => new[] { a, b }));
        var csv = LegendExport.ToCsv(new[] { entry }, klicFields: new[] { "Materiaal", "DiameterM" });
        Assert.Contains("KLIC Materiaal", csv); Assert.Contains("'=HYPERLINK", csv); Assert.Contains("Niet opgegeven (0)", csv); Assert.Contains("110 mm", csv);
        var json = LegendExport.ToJson(new[] { entry }, klicFields: new[] { "Materiaal" });
        Assert.DoesNotContain("DiameterM", json); Assert.Contains("PVC", json);
        Assert.Contains("klicBronnen", json); Assert.Contains("InfraCAD", json);
        Assert.Contains("KLIC gebruikte bronnen", csv);
    }

    [Fact]
    public void XmlReaderPreservesDifferentTablesOnTheSameHandle()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("data.xml").Open(), Encoding.UTF8))
            writer.Write("<KLIC><KLIC_Administratief><Handle>00AB</Handle><Netbeheerder>A</Netbeheerder></KLIC_Administratief><KLIC_LeidingLijn><Handle>AB</Handle><DiameterM>0.11</DiameterM><Materiaal>PE</Materiaal></KLIC_LeidingLijn></KLIC>");
        var data = KlicMetadata.ReadRecords(new[] { bytes.ToArray() });
        Assert.Equal(2, data["AB"].Count); Assert.Equal("0.11", data["AB"][1].Values["DiameterM"]);
    }

    [Fact]
    public void MissingMetadataInAMixedRowRemainsVisible()
    {
        var known = Data("A", Record(KlicSource.InfraCAD, ("Materiaal", "PE")));
        var missing = Data("B");
        var failed = Data("C") with { ReadIssues = new[] { "Object Data kon niet worden gelezen" } };
        Assert.Equal("Niet opgegeven; PE", KlicSummary.Field(new[] { known, missing }, "Materiaal"));
        Assert.Equal("Niet opgegeven; PE", KlicSummary.Field(new[] { known, failed }, "Materiaal"));
        Assert.Equal("", KlicSummary.Field(new[] { missing }, "Materiaal"));
    }
}
