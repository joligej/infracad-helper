using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Pint de belangrijkste legenda-opmaakwaarden vast zodat ze niet per ongeluk veranderen. De
// waarden volgen de NLCS-tekststijl (NLCS-ISO) en de gangbare legenda-maatvoering op 1:200.
public class LegendaOpmaakTests
{
    [Fact]
    public void Teksthoogtes_volgen_NLCS_ISO()
    {
        Assert.Equal(2.5, TemplateDefaults.TextHeightMm, 1);
        Assert.Equal(5.0, TemplateDefaults.HeaderTextHeightMm, 1);
    }

    [Fact]
    public void Rijafstand_is_stabiel()
    {
        Assert.Equal(6.3, TemplateDefaults.RowPitchMm, 1);
    }

    [Fact]
    public void Swatchbreedte_biedt_ruimte_voor_het_sample()
    {
        Assert.True(TemplateDefaults.SwatchWidthMm >= 20.0 && TemplateDefaults.SwatchWidthMm <= 26.0,
            $"SwatchWidthMm {TemplateDefaults.SwatchWidthMm} buiten het verwachte bereik");
    }

    [Fact]
    public void Symbool_insertschaal_is_schaal_gedeeld_door_1000()
    {
        // Symbolen worden op legendaschaal geplaatst: op 1:200 is dat 0,2 (schaal/1000).
        var settings = new LegendSettings { Scale = 200 };
        Assert.Equal(0.2, settings.ModelUnitsPerPaperMm, 3);
    }
}
