using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class NlcsLayerParserTests
{
    [Theory]
    [InlineData("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A", "N", "WE", "VH", "OPENVERHARDING_BETONSTRAATSTEEN", "A", null)]
    [InlineData("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A-200", "N", "WE", "VH", "OPENVERHARDING_BETONSTRAATSTEEN", "A", 200)]
    [InlineData("V-WE-RI-HWA_RIOOLLEIDING_PVC_160-G", "V", "WE", "RI", "HWA_RIOOLLEIDING_PVC_160", "G", null)]
    [InlineData("X-XX-AL-TEKENBLAD_KADER_ROOSTERVERDELING-T35", "X", "XX", "AL", "TEKENBLAD_KADER_ROOSTERVERDELING", "T35", null)]
    [InlineData("B-WE-GW-TALUDARCERING_KORT-G", "B", "WE", "GW", "TALUDARCERING_KORT", "G", null)]
    public void TryParse_ValidNlcsLayers_ParsesFields(
        string raw, string status, string disc, string hg, string element, string type, int? scale)
    {
        Assert.True(NlcsLayerParser.TryParse(raw, out var parsed));
        Assert.Equal(status, parsed!.StatusCode);
        Assert.Equal(disc, parsed.Discipline);
        Assert.Equal(hg, parsed.Hoofdgroep);
        Assert.Equal(element, parsed.Element);
        Assert.Equal(type, parsed.TypeSuffix);
        Assert.Equal(scale, parsed.Scale);
    }

    [Theory]
    [InlineData("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A", NlcsDrawType.Arcering)]
    [InlineData("N-WE-VH-GESLOTENVERHARDING_ASFALT-V", NlcsDrawType.Vlakvulling)]
    [InlineData("V-WE-RI-HWA_RIOOLLEIDING_PVC_160-G", NlcsDrawType.Geometrie)]
    [InlineData("B-WE-OG-TERREIN_ERF-GV", NlcsDrawType.Vlak)]
    [InlineData("N-WE-RI-VWA_RIOOLPUT-S", NlcsDrawType.Symbool)]
    [InlineData("X-XX-AL-TEKENBLAD_KADER-T35", NlcsDrawType.Tekst)]
    public void TryParse_MapsDrawType(string raw, NlcsDrawType expected)
    {
        Assert.True(NlcsLayerParser.TryParse(raw, out var parsed));
        Assert.Equal(expected, parsed!.DrawType);
    }

    [Theory]
    [InlineData("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A", NlcsStatus.Nieuw)]
    [InlineData("B-WE-VH-VERHARDING_TEGEL-S", NlcsStatus.Bestaand)]
    [InlineData("V-WE-RI-HWA_RIOOLLEIDING_PVC_160-G", NlcsStatus.Vervallen)]
    public void TryParse_MapsStatus(string raw, NlcsStatus expected)
    {
        Assert.True(NlcsLayerParser.TryParse(raw, out var parsed));
        Assert.Equal(expected, parsed!.Status);
    }

    [Fact]
    public void TryParse_StripsXrefPrefix()
    {
        Assert.True(NlcsLayerParser.TryParse("SIT-NW-ONTWERP|N-WE-AM-AS_WEG-G", out var parsed));
        Assert.True(parsed!.IsXref);
        Assert.Equal("N-WE-AM-AS_WEG-G", parsed.LocalName);
        Assert.Equal("SIT-NW-ONTWERP", parsed.XrefName);
        Assert.Equal("AS_WEG", parsed.Element);
        Assert.Equal(NlcsStatus.Nieuw, parsed.Status);
    }

    [Fact]
    public void TryParse_NestedXref_UsesLastSegmentAsLocalName()
    {
        // Een geneste xref levert host-gekwalificeerde lagen 'buiten|binnen|N-...';
        // het deel na de laatste pijp is de NLCS-laag, de rest is de xref-naam.
        Assert.True(NlcsLayerParser.TryParse("BUITEN|BINNEN|N-WE-VH-GOOT-V", out var parsed));
        Assert.True(parsed!.IsXref);
        Assert.Equal("BUITEN|BINNEN", parsed.XrefName);
        Assert.Equal("N-WE-VH-GOOT-V", parsed.LocalName);
        Assert.Equal(NlcsDrawType.Vlakvulling, parsed.DrawType);
    }

    [Theory]
    [InlineData("SIT-BS-INMETING-2D")]   // xref-laag, geen NLCS-opbouw
    [InlineData("SIT-NW-ONTWERP")]
    [InlineData("Defpoints")]
    [InlineData("0")]
    [InlineData("N-WE-VH")]              // te weinig velden
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_NonNlcsLayers_ReturnsFalse(string? raw)
    {
        Assert.False(NlcsLayerParser.TryParse(raw, out _));
    }

    [Fact]
    public void GroupKey_IgnoresTypeAndScale()
    {
        NlcsLayerParser.TryParse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A-200", out var hatch);
        NlcsLayerParser.TryParse("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-G", out var line);
        Assert.Equal(hatch!.GroupKey, line!.GroupKey);
    }
}
