using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class SettingsCustomizationTests
{
    private static LegendEntry Entry(NlcsStatus status) => new()
    {
        Status = status,
        Discipline = "WE",
        Hoofdgroep = "VH",
        Element = "OPENVERHARDING",
        Description = "Verharding"
    };

    [Fact]
    public void StatusLabel_ReturnsConfiguredLabel()
    {
        var settings = new LegendSettings { LabelNieuw = "Aan te leggen" };

        Assert.Equal("Aan te leggen", settings.StatusLabel(NlcsStatus.Nieuw));
        Assert.Equal("Bestaand", settings.StatusLabel(NlcsStatus.Bestaand));
    }

    [Fact]
    public void HeaderText_UsesCustomStatusLabel()
    {
        var settings = new LegendSettings
        {
            IncludeTitle = false,
            IncludeGroupHeaders = true,
            LabelNieuw = "Nieuwe situatie"
        };

        var layout = LegendLayoutEngine.Compute(new[] { Entry(NlcsStatus.Nieuw) }, settings);

        var header = layout.Items.Single(i => i.Kind == LegendItemKind.Header);
        Assert.Equal("Nieuwe situatie", header.Text);
    }

    [Fact]
    public void GeneralSeparator_IsRespected()
    {
        var settings = new LegendSettings
        {
            IncludeGeneralDescription = true,
            GeneralSeparator = ": "
        };
        var layers = new List<NlcsLayerName>();
        if (NlcsLayerParser.TryParse("N-WE-GR-BEPLANTING_SIERGRAS-G", out var p))
            layers.Add(p!);

        var entries = LegendGrouping.Build(layers, settings, _ => null);

        Assert.Equal("Groen: Beplanting Siergras", entries[0].Description);
    }

    [Theory]
    [InlineData(3, 0, "3 st")]
    [InlineData(0, 12, "12 m")]
    public void QuantityText_LineAndCount_UseCustomUnits(int count, double length, string expected)
    {
        var entry = new LegendEntry
        {
            Status = NlcsStatus.Nieuw,
            Discipline = "WE",
            Hoofdgroep = "VH",
            Element = "KANTOPSLUITING",
            Description = "Kantopsluiting",
            Metric = new LayerMetric(count, length, 0)
        };

        Assert.Equal(expected, entry.QuantityText(0, "vierkante meter", "m", "st"));
    }

    [Fact]
    public void QuantityText_Area_UsesCustomUnit()
    {
        var entry = new LegendEntry
        {
            Status = NlcsStatus.Nieuw,
            Discipline = "WE",
            Hoofdgroep = "VH",
            Element = "OPENVERHARDING",
            Description = "Verharding",
            LayersByType = new() { [NlcsDrawType.Arcering] = "N-WE-VH-OPENVERHARDING-A" },
            Metric = new LayerMetric(0, 0, 250)
        };

        Assert.Equal("250 vierkante meter", entry.QuantityText(0, "vierkante meter", "m", "st"));
    }

    [Fact]
    public void Settings_RoundtripKeepsCustomTexts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nlcs-settings-{Guid.NewGuid():N}.json");
        try
        {
            var original = new LegendSettings
            {
                Title = "Verklaring",
                LabelVervallen = "Te verwijderen",
                ScaleFormat = "1:{0:0}",
                UnitArea = "vierkante meter"
            };
            original.Save(path);

            var loaded = LegendSettings.Load(path);

            Assert.Equal("Verklaring", loaded.Title);
            Assert.Equal("Te verwijderen", loaded.LabelVervallen);
            Assert.Equal("1:{0:0}", loaded.ScaleFormat);
            Assert.Equal("vierkante meter", loaded.UnitArea);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void FormatScale_UsesCustomFormat()
    {
        var settings = new LegendSettings { Scale = 500, ScaleFormat = "Schaal 1:{0:0}" };
        Assert.Equal("Schaal 1:500", settings.FormatScale());
    }

    [Fact]
    public void FormatScale_FallsBackOnInvalidFormat()
    {
        var settings = new LegendSettings { Scale = 250, ScaleFormat = "1:{1}" };
        Assert.Equal("Schaal 1:250", settings.FormatScale());
    }

    [Fact]
    public void FormatDate_FallsBackOnInvalidFormat()
    {
        var settings = new LegendSettings { DateFormat = "\\" };
        var date = new DateTime(2026, 3, 7);
        Assert.Equal("7-3-2026", settings.FormatDate(date));
    }
}
