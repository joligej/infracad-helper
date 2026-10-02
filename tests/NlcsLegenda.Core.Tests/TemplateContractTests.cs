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
        double SwatchBreedteMm, double RijafstandMm, double[] TeksthoogtesMm);

    private static Contract Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "reference", "template-contract.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new Contract(
            root.GetProperty("swatchBreedteMm").GetDouble(),
            root.GetProperty("rijafstandMm").GetDouble(),
            root.GetProperty("teksthoogtesMm").EnumerateArray().Select(e => e.GetDouble()).ToArray());
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
    public void Swatchkader_is_niet_smaller_dan_de_gemeten_sample_en_blijft_dichtbij()
    {
        // De gemeten waarde is de sample-lijn binnen het kader; het kader mag iets breder zijn.
        double sample = Load().SwatchBreedteMm;
        Assert.True(TemplateDefaults.SwatchWidthMm >= sample - 0.1,
            $"kader {TemplateDefaults.SwatchWidthMm} < sample {sample}");
        Assert.True(TemplateDefaults.SwatchWidthMm <= sample + 3.0,
            $"kader {TemplateDefaults.SwatchWidthMm} te breed t.o.v. sample {sample}");
    }
}
