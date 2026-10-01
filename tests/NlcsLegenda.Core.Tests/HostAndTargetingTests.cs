using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class HostEnvironmentTests
{
    [Theory]
    [InlineData("accoreconsole", true)]
    [InlineData("accoreconsole.exe", true)]
    [InlineData("ACCORECONSOLE", true)]
    [InlineData("acad", false)]
    [InlineData("AutoCAD", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void DetectCoreConsole_MatchesProcessName(string? processName, bool expected)
    {
        Assert.Equal(expected, HostEnvironment.DetectCoreConsole(processName));
    }

    [Fact]
    public void Override_ForcesValue_AndResets()
    {
        try
        {
            HostEnvironment.SetCoreConsoleOverride(true);
            Assert.True(HostEnvironment.IsCoreConsole);
            HostEnvironment.SetCoreConsoleOverride(false);
            Assert.False(HostEnvironment.IsCoreConsole);
        }
        finally
        {
            HostEnvironment.SetCoreConsoleOverride(null);
        }
    }
}

public class LegendTargetingTests
{
    private static LegendRegistry Registry(params (string id, string name)[] legends)
    {
        var reg = new LegendRegistry();
        foreach (var (id, name) in legends)
            reg.Add(new LegendDefinition { Id = id, Name = name, GroupName = "G-" + id });
        return reg;
    }

    [Fact]
    public void ResolveAuto_Zero_None()
    {
        Assert.Equal(TargetResolution.None, LegendTargeting.ResolveAuto(Registry(), out var t));
        Assert.Null(t);
    }

    [Fact]
    public void ResolveAuto_One_Single()
    {
        var reg = Registry(("a", "Legenda 1"));
        Assert.Equal(TargetResolution.Single, LegendTargeting.ResolveAuto(reg, out var t));
        Assert.Equal("a", t!.Id);
    }

    [Fact]
    public void ResolveAuto_Many_Ambiguous()
    {
        var reg = Registry(("a", "A"), ("b", "B"));
        Assert.Equal(TargetResolution.Ambiguous, LegendTargeting.ResolveAuto(reg, out var t));
        Assert.Null(t);
    }

    [Fact]
    public void TryResolveByToken_ById()
    {
        var reg = Registry(("abc123", "Rijbaan"), ("def456", "Groen"));
        Assert.True(LegendTargeting.TryResolveByToken(reg, "def456", out var t, out _));
        Assert.Equal("Groen", t!.Name);
    }

    [Fact]
    public void TryResolveByToken_ByUniqueName()
    {
        var reg = Registry(("abc123", "Rijbaan"), ("def456", "Groen"));
        Assert.True(LegendTargeting.TryResolveByToken(reg, "Rijbaan", out var t, out _));
        Assert.Equal("abc123", t!.Id);
    }

    [Fact]
    public void TryResolveByToken_DuplicateName_FailsWithReason()
    {
        var reg = Registry(("a", "Zelfde"), ("b", "Zelfde"));
        Assert.False(LegendTargeting.TryResolveByToken(reg, "Zelfde", out var t, out var err));
        Assert.Null(t);
        Assert.Contains("meerdere", err);
    }

    [Fact]
    public void TryResolveByToken_Unknown_Fails()
    {
        var reg = Registry(("a", "A"));
        Assert.False(LegendTargeting.TryResolveByToken(reg, "bestaat-niet", out _, out var err));
        Assert.Contains("geen legenda", err);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryResolveByToken_Empty_Fails(string? token)
    {
        var reg = Registry(("a", "A"));
        Assert.False(LegendTargeting.TryResolveByToken(reg, token, out _, out var err));
        Assert.NotEqual(string.Empty, err);
    }
}
