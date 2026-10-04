using System.Reflection;
using System.Text.Json.Serialization;
using NlcsLegenda.Core;
using Xunit;
using Xunit.Abstractions;

namespace NlcsLegenda.Core.Tests;

// Dode-settings-audit: elke persistente user-facing scalar-instelling moet een benoemde consumer
// hebben. De reflectie-check faalt zodra er een instelling bijkomt die hier niet geregistreerd is,
// of als een registratie verwijst naar een verdwenen property. Het echte uitvoergedrag per
// semantische groep wordt bewezen door de layout-, grouping-, quantity-, description- en
// geometrie-tests; deze audit bewaakt de volledigheid van de bedrading.
public class ConsumerAuditTests
{
    private readonly ITestOutputHelper _out;
    public ConsumerAuditTests(ITestOutputHelper output) => _out = output;

    // Instelling -> waar de waarde wordt verbruikt (consumer).
    private static readonly Dictionary<string, string> Consumers = new()
    {
        [nameof(LegendSettings.SchemaVersion)] = "LegendSettings.FromJson migratie",
        [nameof(LegendSettings.Scale)] = "LegendSettings.ToModel / LegendLayoutEngine",
        [nameof(LegendSettings.IncludeTitle)] = "LegendBuilder titel",
        [nameof(LegendSettings.Title)] = "LegendBuilder titeltekst",
        [nameof(LegendSettings.IncludeText)] = "LegendLayoutEngine tekstkolom",
        [nameof(LegendSettings.IncludeGeneralDescription)] = "DescriptionResolver algemeen deel",
        [nameof(LegendSettings.GeneralSeparator)] = "DescriptionResolver scheidingsteken",
        [nameof(LegendSettings.LabelNieuw)] = "LegendBuilder statuslabel",
        [nameof(LegendSettings.LabelBestaand)] = "LegendBuilder statuslabel",
        [nameof(LegendSettings.LabelVervallen)] = "LegendBuilder statuslabel",
        [nameof(LegendSettings.LabelTijdelijk)] = "LegendBuilder statuslabel",
        [nameof(LegendSettings.LabelRevisie)] = "LegendBuilder statuslabel",
        [nameof(LegendSettings.IncludeFooter)] = "LegendBuilder voetregel",
        [nameof(LegendSettings.ScaleFormat)] = "LegendBuilder schaaltekst",
        [nameof(LegendSettings.IncludeDate)] = "LegendBuilder datumregel",
        [nameof(LegendSettings.DateFormat)] = "LegendBuilder datumtekst",
        [nameof(LegendSettings.IncludeTotalsRow)] = "LegendQuantities totaalregel",
        [nameof(LegendSettings.TotalsPrefix)] = "LegendQuantities totaallabel",
        [nameof(LegendSettings.UnitCount)] = "LegendQuantities eenheid",
        [nameof(LegendSettings.UnitLength)] = "LegendQuantities eenheid",
        [nameof(LegendSettings.UnitArea)] = "LegendQuantities eenheid",
        [nameof(LegendSettings.QuantityDecimals)] = "LegendQuantities afronding",
        [nameof(LegendSettings.TextStyle)] = "LegendBuilder tekststijl",
        [nameof(LegendSettings.IncludeGroupHeaders)] = "LegendLayoutEngine statuskoppen",
        [nameof(LegendSettings.IncludeHoofdgroepHeaders)] = "LegendLayoutEngine subkoppen",
        [nameof(LegendSettings.IncludeRemarks)] = "LegendBuilder opmerkingen",
        [nameof(LegendSettings.RemarksTitle)] = "LegendBuilder opmerkingenkop",
        [nameof(LegendSettings.RemarksText)] = "LegendBuilder opmerkingentekst",
        [nameof(LegendSettings.DrawBorder)] = "LegendBuilder kader",
        [nameof(LegendSettings.DrawSwatchFrame)] = "LegendBuilder swatchkader",
        [nameof(LegendSettings.IncludeQuantities)] = "LegendLayoutEngine/LegendQuantities hoeveelheidkolom",
        [nameof(LegendSettings.InsertSymbolBlocks)] = "LegendBuilder symboolinvoeging",
        [nameof(LegendSettings.HatchScaleFactor)] = "LegendBuilder arceerschaal",
        [nameof(LegendSettings.ExplodeOnPlace)] = "Plaatsing exploderen",
        [nameof(LegendSettings.IncludeInvisibleLayers)] = "DrawingAnalyzer bevroren/uit lagen",
        [nameof(LegendSettings.IncludeXrefLayers)] = "DrawingAnalyzer xref-default",
        [nameof(LegendSettings.MergeIdenticalStatuses)] = "LegendGrouping statussamenvoeging",
        [nameof(LegendSettings.SuppressKlicPlaceholders)] = "PlaceholderText filter",
        [nameof(LegendSettings.IncludeScaleBar)] = "LegendBuilder schaalbalk",
        [nameof(LegendSettings.ScaleBarSegments)] = "LegendBuilder schaalbalk-segmenten",
        [nameof(LegendSettings.ScaleBarSegmentMeters)] = "LegendBuilder/ScaleBarMath segmentlengte",
        [nameof(LegendSettings.ScaleBarHeightMm)] = "LegendBuilder schaalbalk-hoogte",
        [nameof(LegendSettings.ViewportManual)] = "NLCSLEGENDAVIEWPORT",
        [nameof(LegendSettings.ViewportMarginMm)] = "ViewportMath marge",
        [nameof(LegendSettings.Columns)] = "LegendLayoutEngine kolommen",
        [nameof(LegendSettings.MaxRowsPerColumn)] = "LegendLayoutEngine kolomhoogte",
        [nameof(LegendSettings.BalanceColumns)] = "LegendLayoutEngine balanceren",
        [nameof(LegendSettings.MaxLegendHeightMm)] = "LegendLayoutEngine maximale hoogte",
        [nameof(LegendSettings.SwatchWidthMm)] = "LegendLayoutEngine/LegendBuilder swatch",
        [nameof(LegendSettings.SwatchHeightMm)] = "LegendLayoutEngine/LegendBuilder swatch",
        [nameof(LegendSettings.RowPitchMm)] = "LegendLayoutEngine regelafstand",
        [nameof(LegendSettings.LineSpacingFactor)] = "LegendLayoutEngine regelhoogte",
        [nameof(LegendSettings.RemarksWidthMm)] = "LegendBuilder opmerkingenbreedte",
        [nameof(LegendSettings.TextGapMm)] = "LegendLayoutEngine swatch-tekst ruimte",
        [nameof(LegendSettings.TextHeightMm)] = "LegendLayoutEngine/LegendBuilder teksthoogte",
        [nameof(LegendSettings.HeaderTextHeightMm)] = "LegendBuilder kopteksthoogte",
        [nameof(LegendSettings.TitleTextHeightMm)] = "LegendBuilder titelhoogte",
        [nameof(LegendSettings.HeaderSpacingMm)] = "LegendLayoutEngine witruimte kop",
        [nameof(LegendSettings.ColumnWidthMm)] = "LegendLayoutEngine kolombreedte",
        [nameof(LegendSettings.ColumnGapMm)] = "LegendLayoutEngine kolomtussenruimte",
        [nameof(LegendSettings.QuantityColumnWidthMm)] = "LegendLayoutEngine hoeveelheidkolom",
        [nameof(LegendSettings.BorderMarginMm)] = "LegendBuilder/ViewportMath kadermarge",
        [nameof(LegendSettings.FrameLayer)] = "LegendBuilder kaderlaag",
        [nameof(LegendSettings.TextLayer)] = "LegendBuilder tekstlaag",
        [nameof(LegendSettings.HeaderTextLayer)] = "LegendBuilder koplaag",
        [nameof(LegendSettings.SortMode)] = "LegendGrouping sortering",
    };

    private static bool IsScalar(Type t) =>
        t == typeof(bool) || t == typeof(int) || t == typeof(double) || t == typeof(string) || t.IsEnum;

    private static PropertyInfo[] SerializedScalars() =>
        typeof(LegendSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true }
                        && p.GetCustomAttribute<JsonIgnoreAttribute>() is null
                        && IsScalar(p.PropertyType))
            .ToArray();

    [Fact]
    public void EveryPersistentScalarSetting_HasNamedConsumer()
    {
        var missing = new List<string>();
        foreach (var p in SerializedScalars())
        {
            bool known = Consumers.TryGetValue(p.Name, out var consumer) && !string.IsNullOrWhiteSpace(consumer);
            _out.WriteLine($"{p.Name,-28} -> {(known ? consumer : "GEEN CONSUMER")}");
            if (!known) missing.Add(p.Name);
        }
        Assert.True(missing.Count == 0, "Settings zonder consumer: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoStaleConsumerEntries()
    {
        var names = SerializedScalars().Select(p => p.Name).ToHashSet();
        var stale = Consumers.Keys.Where(k => !names.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Consumers voor verdwenen settings: " + string.Join(", ", stale));
    }

    // De UI-wrappers voor statussen/elementsoorten/groepering schrijven naar gereserveerde
    // collecties die wél geserialiseerd en verbruikt worden.
    [Fact]
    public void StatusAndDrawTypeToggles_WriteToConsumedCollections()
    {
        var s = new LegendSettings();
        s.ToonNieuw = false;
        Assert.DoesNotContain(NlcsStatus.Nieuw, s.IncludedStatuses);
        s.ToonGeometrie = false;
        Assert.DoesNotContain(NlcsDrawType.Geometrie, s.IncludedDrawTypes);
        s.SamenvoegenSoort = true;
        Assert.Contains(GroupDimension.Soort, s.MergedDimensions);
    }
}
