using System.IO.Compression;
using System.Text;
using System.Xml;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class KlicMetadataTests
{
    private static byte[] Zip(string xml)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("table.xml").Open(), Encoding.UTF8)) writer.Write(xml);
        return buffer.ToArray();
    }

    [Fact]
    public void ReadsAllRecordsCanonicalizesHandlesAndKeepsOriginalNames()
    {
        var result = KlicMetadata.ReadProviders(new[] { Zip("<KLIC><KLIC_Administratief><Handle>00ab</Handle><Netbeheerder> Net A &amp; B </Netbeheerder></KLIC_Administratief><KLIC_LeidingLijn><Handle>AB</Handle><DiameterM>1</DiameterM></KLIC_LeidingLijn><KLIC_Administratief><Handle>AC</Handle><Netbeheerder>Net C</Netbeheerder></KLIC_Administratief></KLIC>") });
        Assert.Equal(2, result.Count); Assert.Equal("Net A & B", result["AB"]); Assert.Equal("Net C", result["AC"]);
    }

    [Fact]
    public void AmbiguousProvidersAreRejectedInsteadOfPickingOne()
    {
        Assert.Throws<InvalidDataException>(() => KlicMetadata.ReadProviders(new[] { Zip("<KLIC><Row><Handle>AB</Handle><Netbeheerder>A</Netbeheerder></Row><Row><Handle>AB</Handle><Netbeheerder>B</Netbeheerder></Row></KLIC>") }));
    }

    [Fact]
    public void DtdAndBrokenArchivesAreRejected()
    {
        Assert.Throws<XmlException>(() => KlicMetadata.ReadProviders(new[] { Zip("<!DOCTYPE KLIC [<!ENTITY external SYSTEM 'file:///private'>]><KLIC>&external;</KLIC>") }));
        Assert.Throws<InvalidDataException>(() => KlicMetadata.ReadProviders(new[] { new byte[] { 1, 2, 3 } }));
    }

    [Fact]
    public void MissingOrUnrelatedMetadataDoesNotInventProviders()
    {
        Assert.Empty(KlicMetadata.ReadProviders(new[] { Zip("<Other><Handle>AB</Handle><Netbeheerder>A</Netbeheerder></Other>") }));
        Assert.Empty(KlicMetadata.ReadProviders(new[] { Zip("<KLIC><Row><Handle>AB</Handle></Row></KLIC>") }));
    }

    [Fact]
    public void OversizedArchiveIsRejectedBeforeDecompression()
    {
        Assert.Throws<InvalidDataException>(() => KlicMetadata.ReadProviders(new[] { new byte[KlicMetadata.MaxCompressedBytes + 1] }));
    }

    [Fact]
    public void XmlFieldsPreserveNamespacesNestedTextEmptyValuesAndRecordOrder()
    {
        var xml = """
            <KLIC xmlns:k="urn:klic">
              <k:Row><k:Handle>00ab</k:Handle><Handle>AC</Handle>
                <Omschrijving> voor <b>tussen</b><![CDATA[ & na ]]><!-- geen tekst --></Omschrijving>
                <Onbekend><Materiaal>niet opnemen</Materiaal></Onbekend><Materiaal/>
              </k:Row>
              <Row/><Row><Handle>AB</Handle><DiameterM>0.3000</DiameterM></Row>
            </KLIC>
            """;
        var result = KlicMetadata.ReadRecords(new[] { Zip(xml) });
        var records = Assert.Single(result).Value;
        Assert.Equal("AB", result.Keys.Single());
        Assert.Equal(2, records.Count);
        Assert.Equal("Row", records[0].Table);
        Assert.Equal("voor tussen & na", records[0].Values["Omschrijving"]);
        Assert.Equal("", records[0].Values["Materiaal"]);
        Assert.Equal("0.3000", records[1].Values["DiameterM"]);
    }

    [Theory]
    [InlineData("AB", true)]
    [InlineData("ongeldig", false)]
    [InlineData("", false)]
    public void DuplicateFieldsAreRejectedOnlyForRecordsWithAValidHandle(string handle, bool reject)
    {
        var bytes = Zip($"<KLIC><Row><Materiaal>PE</Materiaal><Materiaal>PVC</Materiaal><Handle>{handle}</Handle></Row></KLIC>");
        if (reject) Assert.Throws<InvalidDataException>(() => KlicMetadata.ReadRecords(new[] { bytes }));
        else Assert.Empty(KlicMetadata.ReadRecords(new[] { bytes }));
    }

    private static NlcsLayerName Layer(string provider, string renderLayer) => new()
    {
        Raw = "B-WE-KL-DATA-G", LocalName = "B-WE-KL-DATA-G", StatusCode = "B", Status = NlcsStatus.Bestaand,
        Discipline = "WE", Hoofdgroep = "KL", Element = "DATA", TypeSuffix = "G", DrawType = NlcsDrawType.Geometrie,
        Netbeheerder = provider, RenderLayerName = renderLayer
    };

    [Fact]
    public void ProviderMergeIsReversibleAndPreservesQuantitiesAndProvenance()
    {
        var layers = new[] { Layer("Net A", "A"), Layer("Net B", "B") };
        var metrics = new Dictionary<string, LayerMetric> { ["A"] = new(1, 3, 0), ["B"] = new(2, 5, 0) };
        var settings = new LegendSettings { MergedDimensions = new() };
        var split = LegendGrouping.Build(layers, settings, metrics: metrics);
        Assert.Equal(2, split.Count);
        Assert.NotEqual(LegendSettings.EntryKey(split[0]), LegendSettings.EntryKey(split[1]));
        settings.MergedDimensions.Add(GroupDimension.Netbeheerder);
        var merged = Assert.Single(LegendGrouping.Build(layers, settings, metrics: metrics));
        Assert.Equal(new[] { "Net A", "Net B" }, merged.Netbeheerders);
        Assert.Equal(8, merged.Metric.Length); Assert.Equal(3, merged.Metric.Count);
        Assert.Equal(2, merged.SourceLayers.Count);
        Assert.Contains("Net A", LegendExport.ToCsv(new[] { merged }));
        Assert.Contains("netbeheerders", LegendExport.ToJson(new[] { merged }));
        settings.MergedDimensions.Clear();
        Assert.Equal(2, LegendGrouping.Build(layers, settings, metrics: metrics).Count);
    }

    [Fact]
    public void ProviderExclusionAffectsOnlyThatProviderAndLegacyKeysStillWork()
    {
        var a = Layer("Net A", "A"); var b = Layer("Net B", "B");
        var settings = new LegendSettings();
        settings.ExcludedEntries.Add(LegendSettings.EntryKey(a));
        Assert.False(settings.IsIncluded(a)); Assert.True(settings.IsIncluded(b));
        settings.ExcludedEntries.Clear(); settings.ExcludedEntries.Add("B|WE|KL|DATA");
        Assert.False(settings.IsIncluded(a)); Assert.False(settings.IsIncluded(b));
    }

    [Fact]
    public void CompositionExpandsLegacyExclusionsAndPreservesMissingSources()
    {
        var a = LegendSettings.EntryKey(Layer("A", "A")); var b = LegendSettings.EntryKey(Layer("B", "B"));
        var tree = CompositionTree.Build(new[] { (a, "A", "DATA"), (b, "B", "DATA") }, new HashSet<string> { "B|WE|KL|DATA", "missing" });
        Assert.All(tree.AllLeaves(), leaf => Assert.False(leaf.Included));
        tree.AllLeaves().First().Included = true;
        Assert.DoesNotContain(a, tree.ExcludedKeys()); Assert.Contains(b, tree.ExcludedKeys()); Assert.Contains("missing", tree.ExcludedKeys());
    }

    [Fact]
    public void DifferentAppearancesStaySeparateEvenWhenProvidersMerge()
    {
        var settings = new LegendSettings { MergedDimensions = new() { GroupDimension.Netbeheerder } };
        Assert.Equal(2, LegendGrouping.Build(new[] { Layer("A", "red"), Layer("B", "green") }, settings, renderIdentity: x => x).Count);
    }
}
