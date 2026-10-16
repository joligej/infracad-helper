using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class SeparationBandProfileTests
{
    private static LegendEntry Entry(string element, DescriptionSource source = DescriptionSource.Catalogus,
        string group = "VH", bool geometry = true) => new()
    {
        Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = group,
        Element = element, Description = "Eigen omschrijving", DescriptionSource = source,
        LayersByType = geometry ? new() { [NlcsDrawType.Geometrie] = "XREF|bronlaag" } : new()
    };

    [Theory]
    [InlineData("100X300", 1.5, 0.5)]
    [InlineData("115X345", 1.75, 0.6)]
    [InlineData("220X440", 2.2, 1.1)]
    public void TemplateWidthsDoNotDependOnDescriptionOrXref(string dimension, double outer, double inner)
    {
        var profile = SeparationBandProfile.For(Entry("KANTOPSLUITING_SCHEIDINGSBAND_" + dimension));
        Assert.NotNull(profile);
        Assert.Equal(outer, 2 * profile.OuterOffsetMm);
        Assert.Equal(inner, 2 * profile.InnerOffsetMm);
    }

    [Theory]
    [InlineData("KANTOPSLUITING_SCHEIDINGSBAND_100X300_EXTRA")]
    [InlineData("KANTOPSLUITING_SCHEIDINGSBAND_150X300")]
    [InlineData("KANTVERHARDING")]
    public void OtherElementsKeepTheirExistingExample(string element) =>
        Assert.Null(SeparationBandProfile.For(Entry(element)));

    [Fact]
    public void ManualExamplesOtherGroupsAndSymbolsKeepTheirOwnGeometry()
    {
        const string element = "KANTOPSLUITING_SCHEIDINGSBAND_100X300";
        Assert.Null(SeparationBandProfile.For(Entry(element, DescriptionSource.Handmatig)));
        Assert.Null(SeparationBandProfile.For(Entry(element, group: "AL")));
        Assert.Null(SeparationBandProfile.For(Entry(element, geometry: false)));
        Assert.NotNull(SeparationBandProfile.For(Entry(element.ToLowerInvariant())));
    }
}
