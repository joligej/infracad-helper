using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class TemplateDefaultsTests
{
    [Fact]
    public void TextHeights_MatchMeasuredReference()
    {
        Assert.Equal(2.5, TemplateDefaults.TextHeightMm);
        Assert.Equal(2.5, TemplateDefaults.HeaderTextHeightMm);
        Assert.Equal(5.0, TemplateDefaults.TitleTextHeightMm);
    }

    [Fact]
    public void SwatchAndColumns_MatchTemplate()
    {
        Assert.Equal(24.0, TemplateDefaults.SwatchWidthMm);
        Assert.Equal(5.0, TemplateDefaults.SwatchHeightMm);
        Assert.Equal(6.3099, TemplateDefaults.RowPitchMm);
        Assert.Equal(143.0, TemplateDefaults.ColumnWidthMm);
    }

    [Fact]
    public void Settings_DefaultsComeFromCentralTemplate()
    {
        var s = new LegendSettings();
        Assert.Equal(TemplateDefaults.SwatchWidthMm, s.SwatchWidthMm);
        Assert.Equal(TemplateDefaults.TextHeightMm, s.TextHeightMm);
        Assert.Equal(TemplateDefaults.HeaderTextHeightMm, s.HeaderTextHeightMm);
        Assert.Equal(TemplateDefaults.TitleTextHeightMm, s.TitleTextHeightMm);
        Assert.Equal(TemplateDefaults.ColumnWidthMm, s.ColumnWidthMm);
    }

    [Fact]
    public void ResetToTemplate_RestoresCentralValues()
    {
        var s = new LegendSettings { SwatchWidthMm = 99, TextHeightMm = 99, ColumnWidthMm = 99 };
        s.ResetFormattingToTemplate();
        Assert.Equal(TemplateDefaults.SwatchWidthMm, s.SwatchWidthMm);
        Assert.Equal(TemplateDefaults.TextHeightMm, s.TextHeightMm);
        Assert.Equal(TemplateDefaults.ColumnWidthMm, s.ColumnWidthMm);
    }

    [Fact]
    public void ToModel_ScalesTextHeightLikeReference()
    {
        var s = new LegendSettings { Scale = 200 };
        Assert.Equal(0.5, s.ToModel(s.TextHeightMm), 3);
        Assert.Equal(1.0, s.ToModel(s.TitleTextHeightMm), 3);
    }

    [Theory]
    [InlineData(200, 0.5, 1.0)]
    [InlineData(1000, 2.5, 5.0)]
    public void ToModel_MatchesMeasuredStyleHeights(double scale, double expT25, double expT50)
    {
        var s = new LegendSettings { Scale = scale };
        Assert.Equal(expT25, s.ToModel(TemplateDefaults.TextHeightMm), 3);
        Assert.Equal(expT50, s.ToModel(TemplateDefaults.TitleTextHeightMm), 3);
    }

    [Fact]
    public void SampleConfig_MatchesTemplateDefaults()
    {
        var path = FindRepoFile(System.IO.Path.Join("deploy", "settings.sample.json"));
        var s = LegendSettings.FromJson(System.IO.File.ReadAllText(path));
        Assert.Equal(TemplateDefaults.TextHeightMm, s.TextHeightMm);
        Assert.Equal(TemplateDefaults.HeaderTextHeightMm, s.HeaderTextHeightMm);
        Assert.Equal(TemplateDefaults.TitleTextHeightMm, s.TitleTextHeightMm);
        Assert.Equal(TemplateDefaults.TextGapMm, s.TextGapMm);
        Assert.Equal(TemplateDefaults.SwatchWidthMm, s.SwatchWidthMm);
        Assert.Equal(TemplateDefaults.SwatchHeightMm, s.SwatchHeightMm);
        Assert.Equal(TemplateDefaults.RowPitchMm, s.RowPitchMm);
        Assert.Equal(TemplateDefaults.ColumnWidthMm, s.ColumnWidthMm);

        var defaults = new LegendSettings();
        Assert.Equal("Nieuwe situatie:", defaults.LabelNieuw);
        Assert.Equal("Bestaande situatie:", defaults.LabelBestaand);
        Assert.Equal("Vervallen situatie:", defaults.LabelVervallen);
        Assert.Equal("Tijdelijke situatie:", defaults.LabelTijdelijk);
        Assert.Equal("Revisie", defaults.LabelRevisie);
        Assert.Equal(defaults.LabelNieuw, s.LabelNieuw);
        Assert.Equal(defaults.LabelBestaand, s.LabelBestaand);
        Assert.Equal(defaults.LabelVervallen, s.LabelVervallen);
        Assert.Equal(defaults.LabelTijdelijk, s.LabelTijdelijk);
        Assert.Equal(defaults.LabelRevisie, s.LabelRevisie);
    }

    private static string FindRepoFile(string relative)
    {
        var dir = System.AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = System.IO.Path.Join(dir, relative);
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }
        throw new System.IO.FileNotFoundException($"Kon {relative} niet vinden vanaf {System.AppContext.BaseDirectory}");
    }
}
