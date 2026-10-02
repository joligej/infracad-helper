using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class NlcsLayerComponentsTests
{
    [Fact]
    public void Parse_SimpleLayer_SplitsComponents()
    {
        Assert.True(NlcsLayerComponents.TryParse("N-WE-KL-DATA-G", out var c, out _));
        Assert.Equal("N", c.Status);
        Assert.Equal("WE", c.Discipline);
        Assert.Equal("KL", c.Hoofdgroep);
        Assert.Equal(new[] { "DATA" }, c.ObjectParts);
        Assert.Equal("G", c.Element);
        Assert.Equal("", c.Scale);
    }

    [Fact]
    public void Parse_TextElementWithScale()
    {
        Assert.True(NlcsLayerComponents.TryParse("N-WE-AM-AS-T25-1000", out var c, out _));
        Assert.Equal(new[] { "AS" }, c.ObjectParts);
        Assert.Equal("T25", c.Element);
        Assert.Equal("1000", c.Scale);
    }

    [Fact]
    public void Parse_SubStatus_SplitsDigits()
    {
        Assert.True(NlcsLayerComponents.TryParse("N03-WE-KL-DATA-G", out var c, out _));
        Assert.Equal("N", c.Status);
        Assert.Equal("03", c.SubStatus);
    }

    [Fact]
    public void Compose_RoundTrips()
    {
        Assert.True(NlcsLayerComponents.TryParse("V-RI-RIO-PUT-INSPECTIE-A-500", out var c, out _));
        Assert.Equal("V-RI-RIO-PUT-INSPECTIE-A-500", c.Compose());
    }

    [Fact]
    public void Compose_UppercasesAndJoins()
    {
        var c = new NlcsLayerComponents
        {
            Status = "n", Discipline = "we", Hoofdgroep = "kl",
            ObjectParts = new() { "data" }, Element = "g"
        };
        Assert.Equal("N-WE-KL-DATA-G", c.Compose());
    }

    [Theory]
    [InlineData("G")]
    [InlineData("GD")]
    [InlineData("A")]
    [InlineData("SV")]
    [InlineData("V")]
    [InlineData("T25")]
    [InlineData("T25V")]
    public void ValidElement_Accepts(string element) => Assert.True(NlcsLayerComponents.IsValidElement(element));

    [Theory]
    [InlineData("Q")]
    [InlineData("GX")]
    [InlineData("TABC")]
    [InlineData("")]
    public void ValidElement_Rejects(string element) => Assert.False(NlcsLayerComponents.IsValidElement(element));

    [Fact]
    public void Validate_BadStatus_Fails()
    {
        var c = new NlcsLayerComponents { Status = "Q", Discipline = "WE", Hoofdgroep = "KL", ObjectParts = new() { "DATA" }, Element = "G" };
        Assert.Contains(c.Validate(), e => e.Contains("STATUS"));
    }

    [Fact]
    public void Validate_XStatus_RequiresXXAndAL()
    {
        var bad = new NlcsLayerComponents { Status = "X", Discipline = "WE", Hoofdgroep = "KL", ObjectParts = new() { "DATA" }, Element = "G" };
        Assert.Contains(bad.Validate(), e => e.Contains("'X'"));

        var ok = new NlcsLayerComponents { Status = "X", Discipline = "XX", Hoofdgroep = "AL", ObjectParts = new() { "RAND" }, Element = "G" };
        Assert.Empty(ok.Validate());
    }

    [Fact]
    public void Validate_SubStatusRange()
    {
        var c = new NlcsLayerComponents { Status = "N", SubStatus = "100", Discipline = "WE", Hoofdgroep = "KL", ObjectParts = new() { "DATA" }, Element = "G" };
        Assert.Contains(c.Validate(), e => e.Contains("SUBSTATUS"));
    }

    [Fact]
    public void Validate_Clean_IsEmpty()
    {
        Assert.True(NlcsLayerComponents.TryParse("N-WE-KL-DATA-G", out var c, out _));
        Assert.Empty(c.Validate());
    }
}
