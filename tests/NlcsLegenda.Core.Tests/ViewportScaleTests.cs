using NlcsLegenda.Core;
using Xunit;
namespace NlcsLegenda.Core.Tests;
public class ViewportScaleTests
{
    [Theory]
    [InlineData(0)] [InlineData(-200)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidScaleIsRejected(double scale)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ViewportMath.Compute(20, 10, scale, 5));
}
