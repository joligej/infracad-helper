using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class ScaleBarMathTests
{
    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-5.0, 1.0)]
    [InlineData(1.2, 1.0)]
    [InlineData(2.3, 2.0)]
    [InlineData(3.5, 2.5)]
    [InlineData(6.0, 5.0)]
    [InlineData(8.0, 10.0)]
    [InlineData(23.0, 20.0)]
    [InlineData(47.0, 50.0)]
    [InlineData(120.0, 100.0)]
    public void NiceStep_RoundsToOneTwoTwoAndHalfOrFive(double input, double expected)
    {
        Assert.Equal(expected, ScaleBarMath.NiceStep(input), 6);
    }
}
