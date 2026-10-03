using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class MTextFormatTests
{
    [Fact]
    public void Escape_SingleBackslash_DoublesExactlyOnce()
    {
        Assert.Equal("\\\\", MTextFormat.Escape("\\"));
    }

    [Fact]
    public void Escape_Braces_AreEscaped()
    {
        Assert.Equal("\\{a\\}", MTextFormat.Escape("{a}"));
    }

    [Fact]
    public void Escape_BackslashBeforeBrace_OrderIsCorrect()
    {
        // "\{" letterlijk: backslash -> "\\", daarna accolade -> "\{"  => "\\\{"
        Assert.Equal("\\\\\\{", MTextFormat.Escape("\\{"));
    }

    [Fact]
    public void Escape_AllNewlineVariants_BecomeParagraphBreaks()
    {
        Assert.Equal("a\\Pb\\Pc", MTextFormat.Escape("a\r\nb\rc"));
    }

    [Fact]
    public void Escape_PercentAndUnicode_PassThrough()
    {
        Assert.Equal("100% \u00f8120 \u20ac", MTextFormat.Escape("100% \u00f8120 \u20ac"));
    }

    [Fact]
    public void Escape_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MTextFormat.Escape(null));
        Assert.Equal(string.Empty, MTextFormat.Escape(string.Empty));
    }
}
