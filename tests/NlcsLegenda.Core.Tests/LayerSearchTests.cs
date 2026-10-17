using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class LayerSearchTests
{
    [Theory]
    [InlineData("bron|N-WE-GR-BOOM-V", "bron", true)]
    [InlineData("bron|N-WE-GR-BOOM-V", "boom", true)]
    [InlineData("bron|N-WE-GR-BOOM-V", "-v", true)]
    [InlineData("bron|N-WE-GR-BOOM-V", "*we*boom*", true)]
    [InlineData("bron|N-WE-GR-BOOM-V", "*boom*we*", false)]
    [InlineData("bron|N-WE-GR-BOOM-V", "  *  ", true)]
    public void SubstringAndWildcardSearch(string name, string query, bool expected) =>
        Assert.Equal(expected, LayerSearch.Matches(name, query));

    [Fact]
    public void FilterFindsLayerNamesAndKeepsHiddenExclusions()
    {
        var tree = CompositionTree.Build(new[] {
            ("a", "Waterleiding", "KLIC", "BRON|B-WE-KL-WATER-G"),
            ("b", "Datakabel", "KLIC", "BRON|B-WE-KL-DATA-G") }, new HashSet<string> { "b", "missing" });
        var group = Assert.Single(tree.Filter("-water-"));
        var visible = Assert.Single(tree.VisibleLeaves(group, "-water-"));
        Assert.Equal("a", visible.Key);
        visible.Included = false;
        Assert.Equal(3, tree.ExcludedKeys().Count);
        Assert.Equal(2, tree.VisibleLeaves(group, "klic").Count());
    }

    [Fact]
    public void LongWordsBreakWithoutLosingTextOrUnicode()
    {
        string text = "Eigen tekst: ABCDEFGHIJKLMNOPQRSTUVWXYZ éééééééé";
        string wrapped = TextWrap.BreakLongWords(text, t => System.Globalization.StringInfo.ParseCombiningCharacters(t).Length <= 6);
        Assert.Equal(text, wrapped.Replace("\n", ""));
        Assert.Contains("ABCDEF\nGHIJKL", wrapped);
        Assert.DoesNotContain("\ń", wrapped);
        Assert.Equal("gewone tekst", TextWrap.BreakLongWords("gewone tekst", _ => true));
    }
}
