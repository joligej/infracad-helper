using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class TextWrapAndReflowTests
{
    [Fact]
    public void Wrap_KeepsExplicitNewlines()
    {
        var lines = TextWrap.Wrap("Regel een\nRegel twee", 100);
        Assert.Equal(new[] { "Regel een", "Regel twee" }, lines);
    }

    [Fact]
    public void Wrap_BreaksOnWordBoundaries()
    {
        var lines = TextWrap.Wrap("aaa bbb ccc ddd", 7);
        Assert.All(lines, l => Assert.True(l.Length <= 7));
        Assert.Equal("aaa bbb ccc ddd", string.Join(" ", lines));
    }

    [Fact]
    public void Wrap_HardBreaksOverlongWord()
    {
        var lines = TextWrap.Wrap("abcdefghij", 4);
        Assert.Equal(new[] { "abcd", "efgh", "ij" }, lines);
    }

    [Fact]
    public void Wrap_EmptyText_ReturnsNothing()
    {
        Assert.Empty(TextWrap.Wrap("", 10));
        Assert.Empty(TextWrap.Wrap(null, 10));
    }

    [Theory]
    [InlineData("e\u0301e\u0301e\u0301", "e\u0301e\u0301", "e\u0301")]
    [InlineData("🌳🌳🌳", "🌳🌳", "🌳")]
    public void Wrap_DoesNotSplitCombinedCharacters(string text, string first, string second)
    {
        Assert.Equal(new[] { first, second }, TextWrap.Wrap(text, 2));
    }

    private static LegendEntry Entry(string element, string description) => new()
    {
        Status = NlcsStatus.Nieuw,
        Discipline = "WE",
        Hoofdgroep = "VH",
        Element = element,
        Description = description
    };

    [Fact]
    public void MultilineEntry_ReservesMoreHeight_AndShiftsNextRow()
    {
        var settings = new LegendSettings { IncludeTitle = false, IncludeGroupHeaders = false };

        var single = LegendLayoutEngine.Compute(
            new[] { Entry("A", "Kort"), Entry("B", "Tweede") }, settings);
        var multi = LegendLayoutEngine.Compute(
            new[] { Entry("A", "Regel een\nRegel twee\nRegel drie"), Entry("B", "Tweede") }, settings);

        var singleSecond = single.Items.Last(i => i.Kind == LegendItemKind.Entry);
        var multiSecond = multi.Items.Last(i => i.Kind == LegendItemKind.Entry);

        Assert.True(multiSecond.YTop < singleSecond.YTop);
        var multiFirst = multi.Items.First(i => i.Kind == LegendItemKind.Entry);
        Assert.Equal(3, multiFirst.Lines.Count);
        var singleFirst = single.Items.First(i => i.Kind == LegendItemKind.Entry);
        Assert.True(multiFirst.RowHeight > singleFirst.RowHeight);
    }

    [Fact]
    public void Columns_BalanceByHeight_NotJustCount()
    {
        var entries = new[]
        {
            Entry("A", "R1\nR2\nR3\nR4\nR5"),
            Entry("B", "kort"), Entry("C", "kort"), Entry("D", "kort"), Entry("E", "kort")
        };
        var settings = new LegendSettings
        {
            IncludeTitle = false, IncludeGroupHeaders = false, Columns = 2
        };

        var layout = LegendLayoutEngine.Compute(entries, settings);
        Assert.Equal(2, layout.Columns);

        int col0 = layout.Items.Count(i => i.Kind == LegendItemKind.Entry && i.Column == 0);
        Assert.True(col0 < 3, $"kolom 0 zou op hoogte < 3 entries moeten hebben, was {col0}");
    }
}

public class SettingsSerializationTests
{
    [Fact]
    public void Date_IsOffByDefault()
    {
        Assert.False(new LegendSettings().IncludeDate);
    }

    [Fact]
    public void Remarks_OnByDefault_WithText()
    {
        var s = new LegendSettings();
        Assert.True(s.IncludeRemarks);
        Assert.Contains("Maten in meters", s.RemarksText);
        Assert.Contains("NAP", s.RemarksText);
        Assert.DoesNotContain("KLIC", s.RemarksText);
        Assert.Equal("OPMERKINGEN", s.RemarksTitle);
    }

    [Fact]
    public void ScaleBar_OnByDefault()
    {
        Assert.True(new LegendSettings().IncludeScaleBar);
    }

    [Fact]
    public void ToJson_FromJson_Roundtrips()
    {
        var s = new LegendSettings { Title = "Verklaring", Scale = 500, IncludeDate = true, RemarksText = "Eigen tekst" };
        var back = LegendSettings.FromJson(s.ToJson());

        Assert.Equal("Verklaring", back.Title);
        Assert.Equal(500, back.Scale);
        Assert.True(back.IncludeDate);
        Assert.Equal("Eigen tekst", back.RemarksText);
    }

    [Fact]
    public void FromJson_InvalidText_ReturnsDefaults()
    {
        var back = LegendSettings.FromJson("dit is geen json");
        Assert.Equal("LEGENDA", back.Title);
    }

    [Fact]
    public void CopyFrom_CopiesAllValues()
    {
        var source = new LegendSettings { Title = "X", Scale = 123, IncludeScaleBar = false };
        var target = new LegendSettings();
        target.CopyFrom(source);

        Assert.Equal("X", target.Title);
        Assert.Equal(123, target.Scale);
        Assert.False(target.IncludeScaleBar);
    }

    [Fact]
    public void StatusToggles_ReflectAndMutateIncludedStatuses()
    {
        var s = new LegendSettings();
        Assert.True(s.ToonNieuw);

        s.ToonNieuw = false;
        Assert.DoesNotContain(NlcsStatus.Nieuw, s.IncludedStatuses);
        Assert.False(s.ToonNieuw);

        s.ToonNieuw = true;
        Assert.Contains(NlcsStatus.Nieuw, s.IncludedStatuses);
    }

    [Fact]
    public void StatusToggles_NotSeparatelySerialized()
    {
        var s = new LegendSettings();
        s.ToonVervallen = false;
        var back = LegendSettings.FromJson(s.ToJson());

        Assert.DoesNotContain(NlcsStatus.Vervallen, back.IncludedStatuses);
        Assert.False(back.ToonVervallen);
        Assert.DoesNotContain("toonVervallen", s.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Catalog_ToJson_FromJson_Roundtrips()
    {
        var c = new DescriptionCatalog();
        c.Elementen["VH|TEST"] = new DescriptionEntry { Algemeen = "Alg", Specifiek = "Regel een\nRegel twee" };
        var back = DescriptionCatalog.FromJson(c.ToJson());

        Assert.True(back.Elementen.TryGetValue("VH|TEST", out var entry));
        Assert.Equal("Regel een\nRegel twee", entry.Specifiek);
    }
}
