using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NlcsLegenda.Core;

// Maatvoering staat in papier-mm; ToModel rekent met de plotschaal naar modelmeters.
public sealed class LegendSettings
{
    public const int CurrentSchemaVersion = 1;

    [Browsable(false)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // ---- Algemeen ----

    [Category("Algemeen"), DisplayName("Schaal (1:)"), Description("Plotschaal, bijvoorbeeld 200 voor 1:200.")]
    public double Scale { get; set; } = 200.0;

    // ---- Teksten ----

    [Category("Teksten"), DisplayName("Titel tonen")]
    public bool IncludeTitle { get; set; } = true;

    [Category("Teksten"), DisplayName("Titeltekst")]
    public string Title { get; set; } = "LEGENDA";

    [Category("Teksten"), DisplayName("Omschrijvingen tonen")]
    public bool IncludeText { get; set; } = true;

    [Category("Teksten"), DisplayName("Algemeen deel tonen"), Description("Toont bijv. 'Verharding - Betonstraatsteen' in plaats van alleen 'Betonstraatsteen'.")]
    public bool IncludeGeneralDescription { get; set; } = false;

    [Category("Teksten"), DisplayName("Scheidingsteken algemeen/specifiek")]
    public string GeneralSeparator { get; set; } = " - ";

    [Category("Teksten"), DisplayName("Label Nieuw")]
    public string LabelNieuw { get; set; } = "Nieuw";

    [Category("Teksten"), DisplayName("Label Bestaand")]
    public string LabelBestaand { get; set; } = "Bestaand";

    [Category("Teksten"), DisplayName("Label Vervallen")]
    public string LabelVervallen { get; set; } = "Vervallen";

    [Category("Teksten"), DisplayName("Label Tijdelijk")]
    public string LabelTijdelijk { get; set; } = "Tijdelijk";

    [Category("Teksten"), DisplayName("Label Revisie")]
    public string LabelRevisie { get; set; } = "Revisie";

    [Category("Teksten"), DisplayName("Voetregel tonen")]
    public bool IncludeFooter { get; set; } = true;

    [Category("Teksten"), DisplayName("Schaal-formaat"), Description("{0} = schaal, bijv. 'Schaal 1:{0:0}'.")]
    public string ScaleFormat { get; set; } = "Schaal 1:{0:0}";

    [Category("Teksten"), DisplayName("Datum tonen")]
    public bool IncludeDate { get; set; } = false;

    [Category("Teksten"), DisplayName("Datum-formaat"), Description(".NET datumformaat, bijv. d-M-yyyy.")]
    public string DateFormat { get; set; } = "d-M-yyyy";

    [Category("Teksten"), DisplayName("Totaalregel tonen")]
    public bool IncludeTotalsRow { get; set; } = false;

    [Category("Teksten"), DisplayName("Totaal-voorvoegsel")]
    public string TotalsPrefix { get; set; } = "Totaal:";

    [Category("Teksten"), DisplayName("Eenheid aantal")]
    public string UnitCount { get; set; } = "st";

    [Category("Teksten"), DisplayName("Eenheid lengte")]
    public string UnitLength { get; set; } = "m";

    [Category("Teksten"), DisplayName("Eenheid oppervlak")]
    public string UnitArea { get; set; } = "m\u00B2";

    [Category("Teksten"), DisplayName("Decimalen hoeveelheden")]
    public int QuantityDecimals { get; set; } = 0;

    [Category("Teksten"), DisplayName("Tekststijl"), Description("Naam van de AutoCAD-tekststijl (leeg = NLCS/huidige).")]
    public string TextStyle { get; set; } = string.Empty;

    // ---- Koppen ----

    [Category("Koppen"), DisplayName("Statuskoppen tonen")]
    public bool IncludeGroupHeaders { get; set; } = true;

    [Category("Koppen"), DisplayName("Hoofdgroep-subkoppen tonen")]
    public bool IncludeHoofdgroepHeaders { get; set; } = false;

    // ---- Opmerkingen ----

    [Category("Opmerkingen"), DisplayName("Opmerkingen tonen")]
    public bool IncludeRemarks { get; set; } = true;

    [Category("Opmerkingen"), DisplayName("Kop")]
    public string RemarksTitle { get; set; } = "OPMERKINGEN";

    [Category("Opmerkingen"), DisplayName("Tekst")]
    public string RemarksText { get; set; } =
        "De tekeningen zijn globaal en uitsluitend gebaseerd op de leggingsgegevens zoals deze " +
        "bekend zijn bij het KLIC. De exacte ligging, zowel horizontaal als verticaal, kan door " +
        "tal van oorzaken afwijken. Deze dient daarom in het veld te worden vastgesteld d.m.v. " +
        "proefgaten of -sleuven. Praktische richtlijnen om zorgvuldig te graven zijn te verkrijgen " +
        "bij het CROW (www.crow.nl, brochure 'Graafschade voorkomen aan kabels en leidingen'). " +
        "Mofslagen en huisaansluitingen zijn sporadisch aangeduid; de diepteligging is nooit " +
        "aangegeven. Van een 'gebruikelijke' of constante diepteligging kan nooit worden uitgegaan.";

    // ---- Weergave ----

    [Category("Weergave"), DisplayName("Kader rond legenda")]
    public bool DrawBorder { get; set; } = true;

    [Category("Weergave"), DisplayName("Kader per swatch")]
    public bool DrawSwatchFrame { get; set; } = true;

    [Category("Weergave"), DisplayName("Hoeveelheden tonen")]
    public bool IncludeQuantities { get; set; } = false;

    [Category("Weergave"), DisplayName("Symboolblokken invoegen")]
    public bool InsertSymbolBlocks { get; set; } = true;

    [Category("Weergave"), DisplayName("Arceerschaal-factor")]
    public double HatchScaleFactor { get; set; } = 1.0;

    [Category("Weergave"), DisplayName("Sortering")]
    public LegendSortMode SortMode { get; set; } = LegendSortMode.Status;

    [Category("Weergave"), DisplayName("Exploderen bij plaatsen"), Description("Losse entiteiten; anders blijft het één blok.")]
    public bool ExplodeOnPlace { get; set; } = true;

    [Category("Weergave"), DisplayName("Onzichtbare lagen meenemen")]
    public bool IncludeInvisibleLayers { get; set; } = false;

    [Category("Weergave"), DisplayName("Xref-lagen meenemen")]
    public bool IncludeXrefLayers { get; set; } = false;

    // ---- Schaalbalk ----

    [Category("Schaalbalk"), DisplayName("Schaalbalk tonen")]
    public bool IncludeScaleBar { get; set; } = true;

    [Category("Schaalbalk"), DisplayName("Aantal segmenten")]
    public int ScaleBarSegments { get; set; } = 4;

    [Category("Schaalbalk"), DisplayName("Meters per segment (0 = auto)")]
    public double ScaleBarSegmentMeters { get; set; } = 0.0;

    [Category("Schaalbalk"), DisplayName("Hoogte (mm)")]
    public double ScaleBarHeightMm { get; set; } = 2.5;

    // ---- Viewport ----

    [Category("Viewport"), DisplayName("Viewport zelf tekenen")]
    public bool ViewportManual { get; set; } = false;

    [Category("Viewport"), DisplayName("Marge (mm)")]
    public double ViewportMarginMm { get; set; } = 5.0;

    // ---- Kolommen ----

    [Category("Kolommen"), DisplayName("Vast aantal kolommen (0 = auto)")]
    public int Columns { get; set; } = 0;

    [Category("Kolommen"), DisplayName("Max. regels per kolom")]
    public int MaxRowsPerColumn { get; set; } = 30;

    [Category("Kolommen"), DisplayName("Kolommen balanceren")]
    public bool BalanceColumns { get; set; } = true;

    [Category("Kolommen"), DisplayName("Max. hoogte (mm, 0 = uit)")]
    public double MaxLegendHeightMm { get; set; } = 0.0;

    // ---- Maatvoering (papier-mm) ----

    [Category("Maatvoering (mm)"), DisplayName("Swatch breedte")]
    public double SwatchWidthMm { get; set; } = 24.0;

    [Category("Maatvoering (mm)"), DisplayName("Swatch hoogte")]
    public double SwatchHeightMm { get; set; } = 5.0;

    [Category("Maatvoering (mm)"), DisplayName("Regelafstand")]
    public double RowPitchMm { get; set; } = 6.3;

    [Category("Maatvoering (mm)"), DisplayName("Regelhoogte-factor tekst")]
    public double LineSpacingFactor { get; set; } = 1.35;

    [Category("Maatvoering (mm)"), DisplayName("Breedte opmerkingen")]
    public double RemarksWidthMm { get; set; } = 90.0;

    [Category("Maatvoering (mm)"), DisplayName("Ruimte swatch-tekst")]
    public double TextGapMm { get; set; } = 8.0;

    [Category("Maatvoering (mm)"), DisplayName("Teksthoogte omschrijving")]
    public double TextHeightMm { get; set; } = 2.5;

    [Category("Maatvoering (mm)"), DisplayName("Teksthoogte kopregel")]
    public double HeaderTextHeightMm { get; set; } = 4.0;

    [Category("Maatvoering (mm)"), DisplayName("Teksthoogte titel")]
    public double TitleTextHeightMm { get; set; } = 6.0;

    [Category("Maatvoering (mm)"), DisplayName("Witruimte boven kopregel")]
    public double HeaderSpacingMm { get; set; } = 6.0;

    [Category("Maatvoering (mm)"), DisplayName("Kolombreedte")]
    public double ColumnWidthMm { get; set; } = 67.0;

    [Category("Maatvoering (mm)"), DisplayName("Kolomtussenruimte")]
    public double ColumnGapMm { get; set; } = 10.0;

    [Category("Maatvoering (mm)"), DisplayName("Hoeveelheidkolom breedte")]
    public double QuantityColumnWidthMm { get; set; } = 18.0;

    [Category("Maatvoering (mm)"), DisplayName("Kadermarge")]
    public double BorderMarginMm { get; set; } = 5.0;

    // ---- Lagen ----

    [Category("Lagen"), DisplayName("Kaderlaag")]
    public string FrameLayer { get; set; } = "X-XX-AL-LEGENDA_KADER-G";

    [Category("Lagen"), DisplayName("Swatch-vakjeslaag (niet plotten)")]
    public string SwatchFrameLayer { get; set; } = "X-XX-AL-HULPLIJN-G";

    [Category("Lagen"), DisplayName("Tekstlaag")]
    public string TextLayer { get; set; } = "X-XX-AL-LEGENDA_TEKST-T";

    // ---- Filters (niet in de GUI-grid; via commando's/JSON) ----

    // ---- Statussen (in de GUI als aankruisvakjes; opgeslagen als IncludedStatuses) ----

    [Category("Statussen"), DisplayName("Nieuw"), JsonIgnore]
    public bool ToonNieuw { get => IncludedStatuses.Contains(NlcsStatus.Nieuw); set => SetStatus(NlcsStatus.Nieuw, value); }

    [Category("Statussen"), DisplayName("Bestaand"), JsonIgnore]
    public bool ToonBestaand { get => IncludedStatuses.Contains(NlcsStatus.Bestaand); set => SetStatus(NlcsStatus.Bestaand, value); }

    [Category("Statussen"), DisplayName("Vervallen"), JsonIgnore]
    public bool ToonVervallen { get => IncludedStatuses.Contains(NlcsStatus.Vervallen); set => SetStatus(NlcsStatus.Vervallen, value); }

    [Category("Statussen"), DisplayName("Tijdelijk"), JsonIgnore]
    public bool ToonTijdelijk { get => IncludedStatuses.Contains(NlcsStatus.Tijdelijk); set => SetStatus(NlcsStatus.Tijdelijk, value); }

    [Category("Statussen"), DisplayName("Revisie"), JsonIgnore]
    public bool ToonRevisie { get => IncludedStatuses.Contains(NlcsStatus.Revisie); set => SetStatus(NlcsStatus.Revisie, value); }

    private void SetStatus(NlcsStatus status, bool include)
    {
        if (include)
            IncludedStatuses.Add(status);
        else
            IncludedStatuses.Remove(status);
    }

    [Browsable(false)]
    public HashSet<NlcsStatus> IncludedStatuses { get; set; } = new()
    {
        NlcsStatus.Nieuw, NlcsStatus.Bestaand, NlcsStatus.Vervallen,
        NlcsStatus.Tijdelijk, NlcsStatus.Revisie
    };

    // ---- Elementsoorten (in de GUI als aankruisvakjes; opgeslagen als IncludedDrawTypes) ----

    [Category("Elementsoorten"), DisplayName("Geometrie / lijnen"), JsonIgnore]
    public bool ToonGeometrie
    {
        get => IncludedDrawTypes.Contains(NlcsDrawType.Geometrie);
        set => SetDrawType(NlcsDrawType.Geometrie, value);
    }

    [Category("Elementsoorten"), DisplayName("Vlakken"), JsonIgnore]
    public bool ToonVlakken
    {
        get => IncludedDrawTypes.Contains(NlcsDrawType.Vlak);
        set => SetDrawType(NlcsDrawType.Vlak, value);
    }

    [Category("Elementsoorten"), DisplayName("Arceringen"), JsonIgnore]
    public bool ToonArceringen
    {
        get => IncludedDrawTypes.Contains(NlcsDrawType.Arcering);
        set => SetDrawType(NlcsDrawType.Arcering, value);
    }

    [Category("Elementsoorten"), DisplayName("Vlakvullingen"), JsonIgnore]
    public bool ToonVlakvullingen
    {
        get => IncludedDrawTypes.Contains(NlcsDrawType.Vlakvulling);
        set => SetDrawType(NlcsDrawType.Vlakvulling, value);
    }

    [Category("Elementsoorten"), DisplayName("Symbolen"), JsonIgnore]
    public bool ToonSymbolen
    {
        get => IncludedDrawTypes.Contains(NlcsDrawType.Symbool);
        set => SetDrawType(NlcsDrawType.Symbool, value);
    }

    private void SetDrawType(NlcsDrawType type, bool include)
    {
        if (include)
            IncludedDrawTypes.Add(type);
        else
            IncludedDrawTypes.Remove(type);
    }

    // Standaard alle swatch-soorten aan, zodat oude instellingen hetzelfde blijven.
    [Browsable(false)]
    public HashSet<NlcsDrawType> IncludedDrawTypes { get; set; } = new()
    {
        NlcsDrawType.Geometrie, NlcsDrawType.Vlak, NlcsDrawType.Arcering,
        NlcsDrawType.Vlakvulling, NlcsDrawType.Symbool
    };

    public bool IsDrawTypeIncluded(NlcsDrawType type) => IncludedDrawTypes.Contains(type);

    public IReadOnlyList<NlcsDrawType> DisabledDrawTypes()
    {
        var all = new[]
        {
            NlcsDrawType.Geometrie, NlcsDrawType.Vlak, NlcsDrawType.Arcering,
            NlcsDrawType.Vlakvulling, NlcsDrawType.Symbool
        };
        return all.Where(t => !IncludedDrawTypes.Contains(t)).ToList();
    }

    [Browsable(false)]
    public HashSet<string> ExcludedDisciplines { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "XX" };

    [Browsable(false)]
    public HashSet<string> ExcludedHoofdgroepen { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "AL" };

    [Browsable(false)]
    public Dictionary<string, string> TextOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [Browsable(false)]
    public HashSet<string> ExcludedEntries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Browsable(false)]
    public List<ManualEntry> ManualEntries { get; set; } = new();

    [Browsable(false)]
    public Dictionary<string, bool> XrefInclusion { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [Browsable(false)]
    public List<CustomStatus> CustomStatuses { get; set; } = new();

    public CustomStatus? FindCustomStatus(string entryKey)
    {
        if (string.IsNullOrEmpty(entryKey))
            return null;
        foreach (var cs in CustomStatuses)
            if (cs.IsValid && cs.Members.Contains(entryKey, StringComparer.OrdinalIgnoreCase))
                return cs;
        return null;
    }

    public static string EntryKey(string statusCode, string discipline, string hoofdgroep, string element) =>
        $"{statusCode}|{discipline}|{hoofdgroep}|{element}".ToUpperInvariant();

    public static string EntryKey(LegendEntry entry) =>
        EntryKey(entry.Status.Code(), entry.Discipline, entry.Hoofdgroep, entry.Element);

    public static string EntryKey(NlcsLayerName layer) =>
        EntryKey(layer.Status.Code(), layer.Discipline, layer.Hoofdgroep, layer.Element);

    public string StatusLabel(NlcsStatus status) => status switch
    {
        NlcsStatus.Nieuw => LabelNieuw,
        NlcsStatus.Bestaand => LabelBestaand,
        NlcsStatus.Vervallen => LabelVervallen,
        NlcsStatus.Tijdelijk => LabelTijdelijk,
        NlcsStatus.Revisie => LabelRevisie,
        _ => status.DisplayName()
    };

    public string FormatScale()
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, ScaleFormat, Scale);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, "Schaal 1:{0:0}", Scale);
        }
    }

    public string FormatDate(DateTime date)
    {
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        try
        {
            var text = date.ToString(DateFormat, nl);
            return string.IsNullOrEmpty(text) ? date.ToString("d-M-yyyy", nl) : text;
        }
        catch (FormatException)
        {
            return date.ToString("d-M-yyyy", nl);
        }
    }

    public double ToModel(double paperMm) => paperMm * Scale / 1000.0;

    public void CopyFrom(LegendSettings other)
    {
        // Scalars via reflectie; mutable collecties expliciet deep-copyen zodat een
        // legenda nooit een collectie-instance deelt met de globale defaults of een
        // andere legenda.
        foreach (var p in typeof(LegendSettings).GetProperties())
            if (p is { CanRead: true, CanWrite: true } && !IsCollectionProperty(p))
                p.SetValue(this, p.GetValue(other));

        IncludedStatuses = new HashSet<NlcsStatus>(other.IncludedStatuses);
        IncludedDrawTypes = new HashSet<NlcsDrawType>(other.IncludedDrawTypes);
        ExcludedDisciplines = new HashSet<string>(other.ExcludedDisciplines, StringComparer.OrdinalIgnoreCase);
        ExcludedHoofdgroepen = new HashSet<string>(other.ExcludedHoofdgroepen, StringComparer.OrdinalIgnoreCase);
        ExcludedEntries = new HashSet<string>(other.ExcludedEntries, StringComparer.OrdinalIgnoreCase);
        TextOverrides = new Dictionary<string, string>(other.TextOverrides, StringComparer.OrdinalIgnoreCase);
        XrefInclusion = new Dictionary<string, bool>(other.XrefInclusion, StringComparer.OrdinalIgnoreCase);
        ManualEntries = other.ManualEntries.Select(m => m.Clone()).ToList();
        CustomStatuses = other.CustomStatuses.Select(cs => cs.Clone()).ToList();
    }

    // Een volledig onafhankelijke kopie: geen enkele collectie-instance wordt gedeeld.
    public LegendSettings Clone()
    {
        var copy = new LegendSettings();
        copy.CopyFrom(this);
        return copy;
    }

    private static bool IsCollectionProperty(System.Reflection.PropertyInfo p) =>
        p.Name is nameof(IncludedStatuses) or nameof(IncludedDrawTypes) or nameof(ExcludedDisciplines)
            or nameof(ExcludedHoofdgroepen) or nameof(ExcludedEntries) or nameof(TextOverrides)
            or nameof(XrefInclusion) or nameof(ManualEntries) or nameof(CustomStatuses);

    [JsonIgnore, Browsable(false)]
    public double ModelUnitsPerPaperMm => Scale / 1000.0;

    public bool IsIncluded(NlcsLayerName layer)
    {
        var key = EntryKey(layer);
        // Uitvinken en een uitgeschakelde xref winnen altijd, ook van een eigen status.
        if (ExcludedEntries.Contains(key)) return false;
        if (layer.IsXref && !IsXrefIncluded(layer.XrefName)) return false;
        // Een handmatige toewijzing aan een eigen status haalt de laag door de
        // status-/discipline-/hoofdgroepfilters heen: de gebruiker koos die regel bewust.
        if (FindCustomStatus(key) is not null) return true;
        if (!IncludedStatuses.Contains(layer.Status)) return false;
        if (ExcludedDisciplines.Contains(layer.Discipline)) return false;
        if (ExcludedHoofdgroepen.Contains(layer.Hoofdgroep)) return false;
        return true;
    }

    // Bij geneste xrefs bepaalt de bovenste xref de keuze uit de instellingen.
    public bool IsXrefIncluded(string xrefName)
    {
        if (string.IsNullOrEmpty(xrefName))
            return IncludeXrefLayers;
        if (XrefInclusion.TryGetValue(xrefName, out var exact))
            return exact;
        var topLevel = xrefName.Split('|')[0];
        if (!string.Equals(topLevel, xrefName, StringComparison.Ordinal)
            && XrefInclusion.TryGetValue(topLevel, out var top))
            return top;
        return IncludeXrefLayers;
    }

    [JsonIgnore, Browsable(false)]
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    // System.Text.Json maakt bij deserialisatie ordinale HashSets/Dictionaries; de
    // filters horen case-insensitive te blijven. Herstelt ook null-collecties uit
    // handmatig bewerkte of oude JSON.
    public LegendSettings Normalize()
    {
        IncludedStatuses ??= new HashSet<NlcsStatus>();
        IncludedDrawTypes ??= new HashSet<NlcsDrawType>();
        ManualEntries ??= new List<ManualEntry>();
        CustomStatuses ??= new List<CustomStatus>();
        ExcludedDisciplines = ToCi(ExcludedDisciplines);
        ExcludedHoofdgroepen = ToCi(ExcludedHoofdgroepen);
        ExcludedEntries = ToCi(ExcludedEntries);
        TextOverrides = ToCi(TextOverrides);
        XrefInclusion = ToCi(XrefInclusion);
        return this;
    }

    private static HashSet<string> ToCi(HashSet<string>? source) =>
        new(source ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, T> ToCi<T>(Dictionary<string, T>? source)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (source is not null)
            foreach (var kv in source)
                result[kv.Key] = kv.Value;
        return result;
    }

    public static LegendSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new LegendSettings();
        try
        {
            return (JsonSerializer.Deserialize<LegendSettings>(json, JsonOptions) ?? new LegendSettings()).Normalize();
        }
        catch
        {
            return new LegendSettings();
        }
    }

    public static bool TryParse(string? json, out LegendSettings settings)
    {
        settings = new LegendSettings();
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<LegendSettings>(json, JsonOptions);
            if (parsed is null)
                return false;
            settings = parsed.Normalize();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    public static LegendSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return (JsonSerializer.Deserialize<LegendSettings>(File.ReadAllText(path), JsonOptions)
                       ?? new LegendSettings()).Normalize();
        }
        catch
        {
            // Ongeldige configuratie: val terug op de standaardinstellingen.
        }
        return new LegendSettings();
    }
}
