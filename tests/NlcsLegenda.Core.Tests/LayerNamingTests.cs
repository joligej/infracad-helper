using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LayerNamingTests
{
    [Theory]
    [InlineData("Eigen kabels", "Eigen kabels")]
    [InlineData("XREFA|Eigen kabels", "Eigen kabels")]
    [InlineData("BUITEN|BINNEN|Eigen kabels", "Eigen kabels")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void LocalName_StripsXrefPrefix(string? raw, string expected)
    {
        Assert.Equal(expected, LayerNaming.LocalName(raw));
    }
}
