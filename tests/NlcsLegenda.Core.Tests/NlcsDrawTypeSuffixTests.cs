using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class NlcsDrawTypeSuffixTests
{
    [Theory]
    [InlineData("G", NlcsDrawType.Geometrie)]
    [InlineData("GD", NlcsDrawType.Geometrie)]
    [InlineData("GS", NlcsDrawType.Geometrie)]
    [InlineData("GV", NlcsDrawType.Vlak)]
    [InlineData("A", NlcsDrawType.Arcering)]
    [InlineData("AD", NlcsDrawType.Arcering)]
    [InlineData("V", NlcsDrawType.Vlakvulling)]
    [InlineData("S", NlcsDrawType.Symbool)]
    [InlineData("SD", NlcsDrawType.Symbool)]
    [InlineData("SN", NlcsDrawType.Symbool)]
    [InlineData("SV", NlcsDrawType.Symbool)]
    [InlineData("T", NlcsDrawType.Tekst)]
    [InlineData("T25", NlcsDrawType.Tekst)]
    [InlineData("", NlcsDrawType.Overig)]
    [InlineData("XYZ", NlcsDrawType.Overig)]
    public void FromSuffix_MapsFormalAspectCodes(string suffix, NlcsDrawType expected)
    {
        Assert.Equal(expected, NlcsDrawTypeExtensions.FromSuffix(suffix));
    }
}
