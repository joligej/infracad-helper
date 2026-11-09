using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class SymbolOrientationTests
{
    [Theory]
    [InlineData("SGR-BOOM", "N-WE-GR-BOOM-SD", true)]
    [InlineData("*U15", "BRON|N-WE-GR-BOOM-SD-500", true)]
    [InlineData("BRON|SVH-BAND-D", "N-WE-VH-BAND-S", true)]
    [InlineData("SVH-BAND-SOD", "eigen laag", true)]
    [InlineData("V-SVH-BAND-DMM", "eigen laag", true)]
    [InlineData("SVH-BAND-SODMM", "eigen laag", true)]
    [InlineData("SVH-BAND-D-SO", "eigen laag", true)]
    [InlineData("SGR-BOOM-SO", "N-WE-GR-BOOM-S", false)]
    [InlineData("SGR-BOOM-SOMM", "N-WE-GR-BOOM-S", false)]
    [InlineData("ROOD", "N-WE-GR-BOOM-SN", false)]
    [InlineData("D|SGR-BOOM", "BRON|N-WE-GR-BOOM-S", false)]
    public void SectionUsesLayerOrExplicitBlockOption(string block, string layer, bool expected)
        => Assert.Equal(expected, SymbolDescription.IsSection(block, layer));
}
