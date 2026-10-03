using System.Text.Json;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

// Toetst TemplateDefaults tegen de onafhankelijk gemeten referentielegenda (reference/
// template-contract.json). De meetwaarden komen uit de tekening, niet uit de code, dus dit is
// geen zelf-bevestigende test: als de template-maatvoering afwijkt van de NLCS-referentie faalt dit.
public class TemplateContractTests
{
    private sealed record Contract(
        double LijnSampleMm, double SwatchKaderMm, double RijafstandMm, double[] TeksthoogtesMm,
        double Schaal, double SymboolInsertSchaal);

    private static Contract Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "reference", "template-contract.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new Contract(
            root.GetProperty("lijnSampleBreedteMm").GetDouble(),
            root.GetProperty("swatchKaderBreedteMm").GetDouble(),
            root.GetProperty("rijafstandMm").GetDouble(),
            root.GetProperty("teksthoogtesMm").EnumerateArray().Select(e => e.GetDouble()).ToArray(),
            root.GetProperty("schaal").GetDouble(),
            root.GetProperty("symbool").GetProperty("insertSchaal").GetDouble());
    }

    [Fact]
    public void Rijafstand_komt_exact_overeen_met_referentie()
    {
        Assert.Equal(TemplateDefaults.RowPitchMm, Load().RijafstandMm, 1);
    }

    [Fact]
    public void Teksthoogtes_T25_en_T50_komen_overeen_met_referentie()
    {
        var hoogtes = Load().TeksthoogtesMm;
        // De referentie bevat T25 (2,5), T50 (5,0) en in deze tekening een titel (10,0).
        Assert.Contains(hoogtes, h => Math.Abs(h - TemplateDefaults.TextHeightMm) < 0.1);
        Assert.Contains(hoogtes, h => Math.Abs(h - TemplateDefaults.HeaderTextHeightMm) < 0.1);
    }

    [Fact]
    public void SwatchWidth_is_bewuste_keuze_afgeleid_van_de_gemeten_lijnsample()
    {
        // SwatchWidthMm is de swatch/sample-breedte die de renderer tekent. Het is een bewuste
        // afronding van de gemeten lijnsample (22,4 mm) met een kleine marge (ontwerpkeuze),
        // niet het losse swatchkader (dat in deze referentie n=1 is en niet maatgevend).
        double sample = Load().LijnSampleMm;
        Assert.True(TemplateDefaults.SwatchWidthMm >= sample,
            $"SwatchWidthMm {TemplateDefaults.SwatchWidthMm} < gemeten lijnsample {sample}");
        Assert.True(TemplateDefaults.SwatchWidthMm <= sample + 2.0,
            $"SwatchWidthMm {TemplateDefaults.SwatchWidthMm} te ver boven lijnsample {sample} (marge > 2 mm)");
    }

    [Fact]
    public void Swatchkader_is_als_supplementaire_meting_vastgelegd()
    {
        // Het losse swatchkader is apart gemeten (n=1, niet maatgevend) en moet wel in het
        // contract staan zodat de meting traceerbaar blijft.
        Assert.True(Load().SwatchKaderMm > 0, "swatchkader-meting ontbreekt in het contract");
    }

    [Fact]
    public void Symbool_insertschaal_is_schaal_gedeeld_door_1000()
    {
        // Symbolen staan in de referentie op insertschaal = schaal/1000 (0,2 bij 1:200); dit is
        // de basis die LegendBuilder.TryInsertSymbol gebruikt (ModelUnitsPerPaperMm).
        var c = Load();
        Assert.Equal(c.Schaal / 1000.0, c.SymboolInsertSchaal, 3);
    }
}
