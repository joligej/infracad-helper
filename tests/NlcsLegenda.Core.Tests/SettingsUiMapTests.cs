using System.Reflection;
using System.Text.Json.Serialization;
using NlcsLegenda.Core;
using Xunit;
using Xunit.Abstractions;

namespace NlcsLegenda.Core.Tests;

// Bewaakt dat elke persistente instelling een plek in het instellingenvenster heeft (of bewust
// intern is). De reflectie-check faalt zodra er een instelling bijkomt die hier niet staat, zodat
// een nieuwe setting niet onzichtbaar in de UI kan verdwijnen.
public class SettingsUiMapTests
{
    private readonly ITestOutputHelper _out;
    public SettingsUiMapTests(ITestOutputHelper output) => _out = output;

    private enum Binding { Numeric, Text, CheckBox, Combo, Subdialog, Intern }

    private sealed record Surface(string Tab, Binding Kind);

    // Instelling -> waar de gebruiker hem bedient. Settings die bewust op twee tabs staan, hebben
    // beide tabs in Tab; SyncAll houdt de controls gelijk.
    private static readonly Dictionary<string, Surface> Map = new()
    {
        [nameof(LegendSettings.SchemaVersion)] = new("(intern: schema)", Binding.Intern),
        [nameof(LegendSettings.Scale)] = new("Algemeen", Binding.Numeric),
        [nameof(LegendSettings.Title)] = new("Algemeen", Binding.Text),
        [nameof(LegendSettings.ViewportMarginMm)] = new("Algemeen", Binding.Numeric),
        [nameof(LegendSettings.IncludeTitle)] = new("Algemeen", Binding.CheckBox),
        [nameof(LegendSettings.IncludeText)] = new("Algemeen/Teksten", Binding.CheckBox),
        [nameof(LegendSettings.DrawBorder)] = new("Algemeen/Opmaak", Binding.CheckBox),
        [nameof(LegendSettings.IncludeScaleBar)] = new("Algemeen/Schaalbalk", Binding.CheckBox),
        [nameof(LegendSettings.IncludeQuantities)] = new("Algemeen/Hoeveelheden", Binding.CheckBox),

        [nameof(LegendSettings.MergeIdenticalStatuses)] = new("Inhoud", Binding.CheckBox),
        [nameof(LegendSettings.SuppressKlicPlaceholders)] = new("Inhoud", Binding.CheckBox),
        [nameof(LegendSettings.IncludeInvisibleLayers)] = new("Inhoud", Binding.CheckBox),
        [nameof(LegendSettings.IncludeXrefLayers)] = new("Inhoud", Binding.CheckBox),

        [nameof(LegendSettings.DrawSwatchFrame)] = new("Opmaak", Binding.CheckBox),
        [nameof(LegendSettings.InsertSymbolBlocks)] = new("Opmaak", Binding.CheckBox),
        [nameof(LegendSettings.ExplodeOnPlace)] = new("Opmaak", Binding.CheckBox),
        [nameof(LegendSettings.SortMode)] = new("Opmaak", Binding.Combo),
        [nameof(LegendSettings.HatchScaleFactor)] = new("Opmaak", Binding.Numeric),
        [nameof(LegendSettings.IncludeGroupHeaders)] = new("Opmaak", Binding.CheckBox),
        [nameof(LegendSettings.IncludeHoofdgroepHeaders)] = new("Opmaak", Binding.CheckBox),
        [nameof(LegendSettings.Columns)] = new("Opmaak", Binding.Numeric),
        [nameof(LegendSettings.MaxRowsPerColumn)] = new("Opmaak", Binding.Numeric),
        [nameof(LegendSettings.BalanceColumns)] = new("Opmaak", Binding.CheckBox),
        [nameof(LegendSettings.MaxLegendHeightMm)] = new("Opmaak", Binding.Numeric),
        [nameof(LegendSettings.TextStyle)] = new("Opmaak", Binding.Text),
        [nameof(LegendSettings.FrameLayer)] = new("Opmaak", Binding.Text),
        [nameof(LegendSettings.TextLayer)] = new("Opmaak", Binding.Text),
        [nameof(LegendSettings.HeaderTextLayer)] = new("Opmaak", Binding.Text),

        [nameof(LegendSettings.IncludeGeneralDescription)] = new("Teksten", Binding.CheckBox),
        [nameof(LegendSettings.GeneralSeparator)] = new("Teksten", Binding.Text),
        [nameof(LegendSettings.LabelNieuw)] = new("Teksten", Binding.Text),
        [nameof(LegendSettings.LabelBestaand)] = new("Teksten", Binding.Text),
        [nameof(LegendSettings.LabelVervallen)] = new("Teksten", Binding.Text),
        [nameof(LegendSettings.LabelTijdelijk)] = new("Teksten", Binding.Text),
        [nameof(LegendSettings.LabelRevisie)] = new("Teksten", Binding.Text),
        [nameof(LegendSettings.IncludeFooter)] = new("Teksten", Binding.CheckBox),
        [nameof(LegendSettings.IncludeDate)] = new("Teksten", Binding.CheckBox),
        [nameof(LegendSettings.ScaleFormat)] = new("Teksten", Binding.Text),
        [nameof(LegendSettings.DateFormat)] = new("Teksten", Binding.Text),
        [nameof(LegendSettings.IncludeRemarks)] = new("Teksten", Binding.CheckBox),
        [nameof(LegendSettings.RemarksTitle)] = new("Teksten > Opmerkingen", Binding.Subdialog),
        [nameof(LegendSettings.RemarksText)] = new("Teksten > Opmerkingen", Binding.Subdialog),

        [nameof(LegendSettings.UnitCount)] = new("Hoeveelheden", Binding.Text),
        [nameof(LegendSettings.UnitLength)] = new("Hoeveelheden", Binding.Text),
        [nameof(LegendSettings.UnitArea)] = new("Hoeveelheden", Binding.Text),
        [nameof(LegendSettings.QuantityDecimals)] = new("Hoeveelheden", Binding.Numeric),
        [nameof(LegendSettings.IncludeTotalsRow)] = new("Hoeveelheden", Binding.CheckBox),
        [nameof(LegendSettings.TotalsPrefix)] = new("Hoeveelheden", Binding.Text),
        [nameof(LegendSettings.QuantityColumnWidthMm)] = new("Hoeveelheden/Geavanceerd", Binding.Numeric),

        [nameof(LegendSettings.ScaleBarSegments)] = new("Schaalbalk", Binding.Numeric),
        [nameof(LegendSettings.ScaleBarSegmentMeters)] = new("Schaalbalk", Binding.Numeric),
        [nameof(LegendSettings.ScaleBarHeightMm)] = new("Schaalbalk", Binding.Numeric),
        [nameof(LegendSettings.ViewportManual)] = new("Schaalbalk", Binding.CheckBox),
        [nameof(LegendSettings.SwatchWidthMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.SwatchHeightMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.RowPitchMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.LineSpacingFactor)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.RemarksWidthMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.TextGapMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.TextHeightMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.HeaderTextHeightMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.TitleTextHeightMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.HeaderSpacingMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.ColumnWidthMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.ColumnGapMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),
        [nameof(LegendSettings.BorderMarginMm)] = new("Schaalbalk > Geavanceerd", Binding.Numeric),

        // Collecties met een eigen (sub)venster.
        [nameof(LegendSettings.IncludedStatuses)] = new("Inhoud > Statussen", Binding.CheckBox),
        [nameof(LegendSettings.IncludedDrawTypes)] = new("Inhoud > Elementsoorten", Binding.CheckBox),
        [nameof(LegendSettings.MergedDimensions)] = new("Inhoud > KLIC-groepering", Binding.CheckBox),
        [nameof(LegendSettings.ExcludedEntries)] = new("Inhoud > Samenstellen", Binding.Subdialog),
        [nameof(LegendSettings.ManualEntries)] = new("Inhoud > Samenstellen", Binding.Subdialog),
        [nameof(LegendSettings.CustomStatuses)] = new("Inhoud > Eigen statussen", Binding.Subdialog),
        [nameof(LegendSettings.CustomLayerRules)] = new("Inhoud > Eigen lagen", Binding.Subdialog),
        [nameof(LegendSettings.XrefInclusion)] = new("Inhoud > Xrefs", Binding.Subdialog),
        [nameof(LegendSettings.DescriptionOverrides)] = new("Teksten > Omschrijvingen", Binding.Subdialog),

        // Interne filters met een vaste, zinvolle standaard; bewust niet in de UI (XX = onbekend,
        // AL = algemeen zijn vrijwel altijd ruis in een legenda). Testcommando's maken ze leeg.
        [nameof(LegendSettings.ExcludedDisciplines)] = new("(intern filter)", Binding.Intern),
        [nameof(LegendSettings.ExcludedHoofdgroepen)] = new("(intern filter)", Binding.Intern),
    };

    private static bool IsScalar(Type t) =>
        t == typeof(bool) || t == typeof(int) || t == typeof(double) || t == typeof(string) || t.IsEnum;

    private static bool IsCollection(Type t) =>
        t != typeof(string) && (t.IsGenericType || t == typeof(DescriptionCatalog));

    private static PropertyInfo[] PersistentProperties() =>
        typeof(LegendSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true }
                        && p.GetCustomAttribute<JsonIgnoreAttribute>() is null
                        && (IsScalar(p.PropertyType) || IsCollection(p.PropertyType)))
            .ToArray();

    [Fact]
    public void EveryPersistentSetting_HasUiSurface()
    {
        var missing = new List<string>();
        foreach (var p in PersistentProperties())
        {
            bool known = Map.TryGetValue(p.Name, out var s);
            _out.WriteLine($"{p.Name,-26} {(known ? $"{s!.Kind,-9} {s.Tab}" : "ONTBREEKT")}");
            if (!known) missing.Add(p.Name);
        }
        Assert.True(missing.Count == 0, "Settings zonder UI-plek: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoStaleMapEntries()
    {
        var names = PersistentProperties().Select(p => p.Name).ToHashSet();
        var stale = Map.Keys.Where(k => !names.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Mapping voor verdwenen settings: " + string.Join(", ", stale));
    }

    [Fact]
    public void IntentionalDuplicates_AreMarkedOnTwoTabs()
    {
        // Deze schakelaars staan bewust op twee tabbladen.
        foreach (var name in new[] { nameof(LegendSettings.IncludeText),
                 nameof(LegendSettings.DrawBorder), nameof(LegendSettings.IncludeScaleBar),
                 nameof(LegendSettings.IncludeQuantities) })
            Assert.Contains("/", Map[name].Tab);
    }
}
