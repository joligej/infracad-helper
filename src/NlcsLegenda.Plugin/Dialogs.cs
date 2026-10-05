using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class ButtonBar : FlowLayoutPanel
{
    public Button Ok { get; }
    public Button Cancel { get; }
    public Button? Apply { get; }

    public ButtonBar(bool withApply)
    {
        Dock = DockStyle.Bottom;
        FlowDirection = FlowDirection.RightToLeft;
        WrapContents = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8, 6, 8, 8);

        Ok = MakeButton("Opslaan", DialogResult.OK);
        Cancel = MakeButton("Annuleren", DialogResult.Cancel);
        Controls.Add(Ok);
        Controls.Add(Cancel);
        if (withApply)
        {
            Apply = MakeButton("Toepassen", DialogResult.None);
            Controls.Add(Apply);
        }
    }

    public Button AddExtra(string text)
    {
        var button = MakeButton(text, DialogResult.None);
        Controls.Add(button);
        return button;
    }

    // Knoppen groeien mee met hun tekst (DPI-/taal-onafhankelijk) met een nette ondergrens,
    // zodat langere labels als "Opmaak → template" niet worden afgekapt.
    private static Button MakeButton(string text, DialogResult result) => new()
    {
        Text = text,
        DialogResult = result,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(92, 28),
        Padding = new Padding(10, 2, 10, 2),
        Margin = new Padding(6, 3, 0, 0),
        UseVisualStyleBackColor = true
    };
}

internal sealed class SettingsDialog : Form
{
    private readonly List<Action> _resync = new();
    private readonly LegendSettings _settings;
    private readonly IReadOnlyList<EntryCheckItem>? _composition;
    private readonly IReadOnlyList<string> _xrefNames;
    private bool _syncing;

    public event EventHandler? ApplyRequested;

    // Bewerkt precies één instellingenobject (een werkkopie). De aanroeper bepaalt de scope
    // (globale standaard of één specifieke legenda) en past het resultaat toe. Geef je de
    // geanalyseerde entries mee, dan kan de gebruiker vanuit dit venster ook samenstellen
    // (uitsluitingen + eigen regels) en eigen statussen leden toewijzen; de xref-namen voeden
    // de per-xref keuze.
    public SettingsDialog(LegendSettings settings, string contextLabel,
        IReadOnlyList<EntryCheckItem>? composition = null, IReadOnlyList<string>? xrefNames = null)
    {
        _composition = composition;
        _xrefNames = xrefNames ?? Array.Empty<string>();
        _settings = settings;

        Text = "NLCS Legenda \u2013 instellingen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(600, 640);
        MinimumSize = new Size(560, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var header = new Label
        {
            Dock = DockStyle.Top,
            Text = contextLabel,
            AutoSize = false,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold)
        };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildAlgemeenTab());
        tabs.TabPages.Add(BuildInhoudTab());
        tabs.TabPages.Add(BuildOpmaakTab());
        tabs.TabPages.Add(BuildTekstenTab());
        tabs.TabPages.Add(BuildHoeveelhedenTab());
        tabs.TabPages.Add(BuildSchaalbalkTab());

        var buttons = new ButtonBar(withApply: true);
        buttons.Apply!.Click += (_, _) =>
        {
            if (ValidateScale())
                ApplyRequested?.Invoke(this, EventArgs.Empty);
        };
        var remarks = buttons.AddExtra("Opmerkingen\u2026");
        remarks.Click += (_, _) => EditRemarks();
        if (_composition is not null)
        {
            var samenstellen = buttons.AddExtra("Samenstellen\u2026");
            samenstellen.Click += (_, _) => EditComposition();
        }
        var resetFormat = buttons.AddExtra("Opmaak \u2192 template");
        resetFormat.Click += (_, _) => { _settings.ResetFormattingToTemplate(); RefreshGrids(); };
        var reset = buttons.AddExtra("Standaardwaarden");
        reset.Click += (_, _) => { ResetToDefaults(_settings); RefreshGrids(); };
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        FormClosing += OnFormClosing;

        Controls.Add(tabs);
        Controls.Add(buttons);
        Controls.Add(header);
    }

    public LegendSettings Settings => _settings;

    // De guard voorkomt dat terugschrijven naar een control zijn eigen change-handler weer laat syncen.
    private void SyncAll()
    {
        if (_syncing) return;
        _syncing = true;
        try { foreach (var s in _resync) s(); }
        finally { _syncing = false; }
    }

    private Panel NumRow(string label, Func<double> get, Action<double> set,
        decimal min, decimal max, int decimals = 1, decimal increment = 0.5M, int width = 90)
    {
        decimal start = ClampDecimal((decimal)get(), min, max);
        // Buiten bereik opgeslagen waarde meteen gelijktrekken, anders tonen UI en opslag iets anders.
        if ((double)start != get()) set((double)start);
        var n = new NumericUpDown
        {
            Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = increment,
            Width = width, Value = start
        };
        n.ValueChanged += (_, _) => { if (!_syncing) set((double)n.Value); };
        _resync.Add(() => n.Value = ClampDecimal((decimal)get(), min, max));
        return LabeledRow(label, n);
    }

    private Panel IntRow(string label, Func<int> get, Action<int> set, int min, int max, int width = 70)
    {
        int start = Math.Clamp(get(), min, max);
        if (start != get()) set(start);
        var n = new NumericUpDown
        {
            Minimum = min, Maximum = max, DecimalPlaces = 0, Increment = 1,
            Width = width, Value = start
        };
        n.ValueChanged += (_, _) => { if (!_syncing) set((int)n.Value); };
        _resync.Add(() => n.Value = Math.Clamp(get(), min, max));
        return LabeledRow(label, n);
    }

    private Panel TextRow(string label, Func<string> get, Action<string> set, int width = 220)
    {
        var t = new TextBox { Text = get(), Width = width };
        t.TextChanged += (_, _) => { if (!_syncing) set(t.Text); };
        _resync.Add(() => { if (t.Text != get()) t.Text = get(); });
        return LabeledRow(label, t);
    }

    private Panel ComboRow<TEnum>(string label, Func<TEnum> get, Action<TEnum> set) where TEnum : struct, Enum
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        combo.Items.AddRange(Enum.GetNames<TEnum>());
        combo.SelectedItem = get().ToString();
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (!_syncing && combo.SelectedItem is string s && Enum.TryParse<TEnum>(s, out var v))
                set(v);
        };
        _resync.Add(() => combo.SelectedItem = get().ToString());
        return LabeledRow(label, combo);
    }

    private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        => value < min ? min : value > max ? max : value;

    // SyncAll houdt een tweede vakje voor dezelfde setting (bijv. op Algemeen) gelijk.
    private CheckBox BoundCheck(string text, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { Text = text, Checked = get(), AutoSize = true, Margin = new Padding(3, 3, 12, 3) };
        cb.CheckedChanged += (_, _) => { if (_syncing) return; set(cb.Checked); SyncAll(); };
        _resync.Add(() => cb.Checked = get());
        return cb;
    }

    private static GroupBox Group(string title, params Control[] controls)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(6) };
        flow.Controls.AddRange(controls);
        var box = new GroupBox { Text = title, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        box.Controls.Add(flow);
        return box;
    }

    private TabPage BuildAlgemeenTab()
    {
        var page = new TabPage("Algemeen") { Padding = new Padding(10), AutoScroll = true };
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        var scale = new NumericUpDown { Minimum = 1, Maximum = 1_000_000, Value = (decimal)Math.Clamp(_settings.Scale, 1, 1_000_000), Width = 100 };
        scale.ValueChanged += (_, _) => _settings.Scale = (double)scale.Value;
        _resync.Add(() => scale.Value = (decimal)Math.Clamp(_settings.Scale, 1, 1_000_000));
        var title = new TextBox { Text = _settings.Title, Width = 260 };
        title.TextChanged += (_, _) => _settings.Title = title.Text;
        _resync.Add(() => title.Text = _settings.Title);
        var margin = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 0.5M, Value = (decimal)Math.Clamp(_settings.ViewportMarginMm, 0, 100), Width = 80 };
        margin.ValueChanged += (_, _) => _settings.ViewportMarginMm = (double)margin.Value;
        _resync.Add(() => margin.Value = (decimal)Math.Clamp(_settings.ViewportMarginMm, 0, 100));

        root.Controls.Add(LabeledRow("Schaal 1:", scale));
        root.Controls.Add(LabeledRow("Titel:", title));
        root.Controls.Add(LabeledRow("Viewportmarge (mm):", margin));
        root.Controls.Add(Group("Weergave",
            BoundCheck("Titel tonen", () => _settings.IncludeTitle, v => _settings.IncludeTitle = v),
            BoundCheck("Tekst naast de vakjes tonen", () => _settings.IncludeText, v => _settings.IncludeText = v),
            BoundCheck("Kader rond legenda", () => _settings.DrawBorder, v => _settings.DrawBorder = v),
            BoundCheck("Schaalbalk tonen", () => _settings.IncludeScaleBar, v => _settings.IncludeScaleBar = v),
            BoundCheck("Hoeveelheden tonen", () => _settings.IncludeQuantities, v => _settings.IncludeQuantities = v)));
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildInhoudTab()
    {
        var page = new TabPage("Inhoud") { Padding = new Padding(10), AutoScroll = true };
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        root.Controls.Add(Group("Elementsoorten",
            BoundCheck("Geometrie / lijnen", () => _settings.ToonGeometrie, v => _settings.ToonGeometrie = v),
            BoundCheck("Vlakken", () => _settings.ToonVlakken, v => _settings.ToonVlakken = v),
            BoundCheck("Arceringen", () => _settings.ToonArceringen, v => _settings.ToonArceringen = v),
            BoundCheck("Vlakvullingen", () => _settings.ToonVlakvullingen, v => _settings.ToonVlakvullingen = v),
            BoundCheck("Symbolen", () => _settings.ToonSymbolen, v => _settings.ToonSymbolen = v)));
        root.Controls.Add(Group("Statussen",
            BoundCheck("Nieuw", () => _settings.ToonNieuw, v => _settings.ToonNieuw = v),
            BoundCheck("Bestaand", () => _settings.ToonBestaand, v => _settings.ToonBestaand = v),
            BoundCheck("Vervallen", () => _settings.ToonVervallen, v => _settings.ToonVervallen = v),
            BoundCheck("Tijdelijk", () => _settings.ToonTijdelijk, v => _settings.ToonTijdelijk = v),
            BoundCheck("Revisie", () => _settings.ToonRevisie, v => _settings.ToonRevisie = v),
            BoundCheck("Gelijke statussen samenvoegen (alleen bij identieke weergave)", () => _settings.MergeIdenticalStatuses, v => _settings.MergeIdenticalStatuses = v)));
        root.Controls.Add(Group("KLIC-groepering",
            BoundCheck("Soort samenvoegen", () => _settings.SamenvoegenSoort, v => _settings.SamenvoegenSoort = v),
            BoundCheck("Spanning/druk samenvoegen", () => _settings.SamenvoegenSpecificatie, v => _settings.SamenvoegenSpecificatie = v),
            BoundCheck("Uitvoering samenvoegen", () => _settings.SamenvoegenUitvoering, v => _settings.SamenvoegenUitvoering = v),
            BoundCheck("Volgnummer samenvoegen", () => _settings.SamenvoegenNummer, v => _settings.SamenvoegenNummer = v),
            BoundCheck("KLIC-placeholdertekst weglaten", () => _settings.SuppressKlicPlaceholders, v => _settings.SuppressKlicPlaceholders = v)));
        root.Controls.Add(Group("Lagen",
            BoundCheck("Onzichtbare (bevroren/uit) lagen meenemen", () => _settings.IncludeInvisibleLayers, v => _settings.IncludeInvisibleLayers = v),
            BoundCheck("Xref-lagen meenemen", () => _settings.IncludeXrefLayers, v => _settings.IncludeXrefLayers = v)));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Padding = new Padding(3) };
        if (_composition is not null)
        {
            var samen = new Button { Text = "Samenstellen\u2026", AutoSize = true };
            samen.Click += (_, _) => EditComposition();
            actions.Controls.Add(samen);
        }
        var statussen = new Button { Text = "Eigen statussen\u2026", AutoSize = true };
        statussen.Click += (_, _) => EditStatusMembers();
        actions.Controls.Add(statussen);
        var xrefs = new Button { Text = "Xrefs\u2026", AutoSize = true };
        xrefs.Click += (_, _) => EditXrefInclusion();
        actions.Controls.Add(xrefs);
        var eigenLagen = new Button { Text = "Eigen lagen\u2026", AutoSize = true };
        eigenLagen.Click += (_, _) => EditCustomLayers();
        actions.Controls.Add(eigenLagen);
        root.Controls.Add(actions);
        page.Controls.Add(root);
        return page;
    }

    private static Panel LabeledRow(string label, Control control)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(3) };
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) });
        panel.Controls.Add(control);
        return panel;
    }

    private TabPage BuildOpmaakTab()
    {
        var page = new TabPage("Opmaak") { Padding = new Padding(10), AutoScroll = true };
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        root.Controls.Add(Group("Weergave",
            BoundCheck("Kader rond legenda", () => _settings.DrawBorder, v => _settings.DrawBorder = v),
            BoundCheck("Kader per swatch", () => _settings.DrawSwatchFrame, v => _settings.DrawSwatchFrame = v),
            BoundCheck("Symboolblokken invoegen", () => _settings.InsertSymbolBlocks, v => _settings.InsertSymbolBlocks = v),
            BoundCheck("Exploderen bij plaatsen", () => _settings.ExplodeOnPlace, v => _settings.ExplodeOnPlace = v)));
        root.Controls.Add(ComboRow("Sortering:", () => _settings.SortMode, v => _settings.SortMode = v));
        root.Controls.Add(NumRow("Arceerschaal-factor:", () => _settings.HatchScaleFactor, v => _settings.HatchScaleFactor = v, 0.01M, 100M, 2, 0.1M));
        root.Controls.Add(Group("Koppen",
            BoundCheck("Statuskoppen tonen", () => _settings.IncludeGroupHeaders, v => _settings.IncludeGroupHeaders = v),
            BoundCheck("Hoofdgroep-subkoppen tonen", () => _settings.IncludeHoofdgroepHeaders, v => _settings.IncludeHoofdgroepHeaders = v)));
        root.Controls.Add(Group("Kolommen",
            IntRow("Vast aantal (0 = auto):", () => _settings.Columns, v => _settings.Columns = v, 0, 20),
            IntRow("Max. regels per kolom:", () => _settings.MaxRowsPerColumn, v => _settings.MaxRowsPerColumn = v, 1, 1000, 80),
            BoundCheck("Kolommen balanceren", () => _settings.BalanceColumns, v => _settings.BalanceColumns = v),
            NumRow("Max. hoogte (mm, 0 = uit):", () => _settings.MaxLegendHeightMm, v => _settings.MaxLegendHeightMm = v, 0, 10000, 1, 5)));
        root.Controls.Add(Group("Stijl en lagen",
            TextRow("Tekststijl (leeg = huidige):", () => _settings.TextStyle, v => _settings.TextStyle = v, 180),
            TextRow("Kaderlaag:", () => _settings.FrameLayer, v => _settings.FrameLayer = v, 180),
            TextRow("Tekstlaag omschrijvingen:", () => _settings.TextLayer, v => _settings.TextLayer = v, 180),
            TextRow("Tekstlaag koppen/titel:", () => _settings.HeaderTextLayer, v => _settings.HeaderTextLayer = v, 180)));
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildTekstenTab()
    {
        var page = new TabPage("Teksten") { Padding = new Padding(10), AutoScroll = true };
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        root.Controls.Add(Group("Omschrijvingen",
            BoundCheck("Omschrijvingen tonen", () => _settings.IncludeText, v => _settings.IncludeText = v),
            BoundCheck("Algemeen deel tonen", () => _settings.IncludeGeneralDescription, v => _settings.IncludeGeneralDescription = v)));
        root.Controls.Add(TextRow("Scheidingsteken alg./spec.:", () => _settings.GeneralSeparator, v => _settings.GeneralSeparator = v, 120));
        root.Controls.Add(Group("Statuslabels",
            TextRow("Nieuw:", () => _settings.LabelNieuw, v => _settings.LabelNieuw = v, 160),
            TextRow("Bestaand:", () => _settings.LabelBestaand, v => _settings.LabelBestaand = v, 160),
            TextRow("Vervallen:", () => _settings.LabelVervallen, v => _settings.LabelVervallen = v, 160),
            TextRow("Tijdelijk:", () => _settings.LabelTijdelijk, v => _settings.LabelTijdelijk = v, 160),
            TextRow("Revisie:", () => _settings.LabelRevisie, v => _settings.LabelRevisie = v, 160)));
        root.Controls.Add(Group("Voet- en datumregel",
            BoundCheck("Voetregel tonen", () => _settings.IncludeFooter, v => _settings.IncludeFooter = v),
            BoundCheck("Datum tonen", () => _settings.IncludeDate, v => _settings.IncludeDate = v)));
        root.Controls.Add(TextRow("Schaal-formaat:", () => _settings.ScaleFormat, v => _settings.ScaleFormat = v, 160));
        root.Controls.Add(TextRow("Datum-formaat:", () => _settings.DateFormat, v => _settings.DateFormat = v, 160));
        root.Controls.Add(Group("Opmerkingen",
            BoundCheck("Opmerkingen tonen", () => _settings.IncludeRemarks, v => _settings.IncludeRemarks = v)));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Padding = new Padding(3) };
        var omschr = new Button { Text = "Omschrijvingen\u2026", AutoSize = true };
        omschr.Click += (_, _) => EditDescriptions();
        actions.Controls.Add(omschr);
        var opm = new Button { Text = "Opmerkingen\u2026", AutoSize = true };
        opm.Click += (_, _) => EditRemarks();
        actions.Controls.Add(opm);
        root.Controls.Add(actions);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildHoeveelhedenTab()
    {
        var page = new TabPage("Hoeveelheden") { Padding = new Padding(10), AutoScroll = true };
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        root.Controls.Add(BoundCheck("Hoeveelheden tonen", () => _settings.IncludeQuantities, v => _settings.IncludeQuantities = v));
        root.Controls.Add(Group("Eenheden",
            TextRow("Aantal:", () => _settings.UnitCount, v => _settings.UnitCount = v, 100),
            TextRow("Lengte:", () => _settings.UnitLength, v => _settings.UnitLength = v, 100),
            TextRow("Oppervlak:", () => _settings.UnitArea, v => _settings.UnitArea = v, 100)));
        root.Controls.Add(IntRow("Decimalen:", () => _settings.QuantityDecimals, v => _settings.QuantityDecimals = v, 0, 6));
        root.Controls.Add(Group("Totaalregel",
            BoundCheck("Totaalregel tonen", () => _settings.IncludeTotalsRow, v => _settings.IncludeTotalsRow = v),
            TextRow("Voorvoegsel:", () => _settings.TotalsPrefix, v => _settings.TotalsPrefix = v, 160)));
        root.Controls.Add(NumRow("Kolombreedte hoeveelheden (mm):", () => _settings.QuantityColumnWidthMm, v => _settings.QuantityColumnWidthMm = v, 1, 200, 1, 0.5M));
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildSchaalbalkTab()
    {
        var page = new TabPage("Schaalbalk / Extra") { Padding = new Padding(10), AutoScroll = true };
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        root.Controls.Add(Group("Schaalbalk",
            BoundCheck("Schaalbalk tonen", () => _settings.IncludeScaleBar, v => _settings.IncludeScaleBar = v),
            IntRow("Aantal segmenten:", () => _settings.ScaleBarSegments, v => _settings.ScaleBarSegments = v, 1, 50),
            NumRow("Meters per segment (0 = auto):", () => _settings.ScaleBarSegmentMeters, v => _settings.ScaleBarSegmentMeters = v, 0, 10000, 2, 1),
            NumRow("Hoogte (mm):", () => _settings.ScaleBarHeightMm, v => _settings.ScaleBarHeightMm = v, 0.5M, 50, 1, 0.5M)));
        root.Controls.Add(Group("Viewport",
            BoundCheck("Viewport zelf tekenen", () => _settings.ViewportManual, v => _settings.ViewportManual = v)));
        root.Controls.Add(Group("Geavanceerd \u2013 maatvoering (mm)",
            NumRow("Swatch breedte:", () => _settings.SwatchWidthMm, v => _settings.SwatchWidthMm = v, 1, 200, 1, 0.5M),
            NumRow("Swatch hoogte:", () => _settings.SwatchHeightMm, v => _settings.SwatchHeightMm = v, 1, 200, 1, 0.5M),
            NumRow("Regelafstand:", () => _settings.RowPitchMm, v => _settings.RowPitchMm = v, 1, 100, 1, 0.1M),
            NumRow("Regelhoogte-factor tekst:", () => _settings.LineSpacingFactor, v => _settings.LineSpacingFactor = v, 0.5M, 5, 2, 0.05M),
            NumRow("Breedte opmerkingen:", () => _settings.RemarksWidthMm, v => _settings.RemarksWidthMm = v, 1, 500, 1, 1),
            NumRow("Ruimte swatch-tekst:", () => _settings.TextGapMm, v => _settings.TextGapMm = v, 0, 50, 1, 0.5M),
            NumRow("Teksthoogte omschrijving:", () => _settings.TextHeightMm, v => _settings.TextHeightMm = v, 0.5M, 50, 2, 0.1M),
            NumRow("Teksthoogte kopregel:", () => _settings.HeaderTextHeightMm, v => _settings.HeaderTextHeightMm = v, 0.5M, 50, 2, 0.1M),
            NumRow("Teksthoogte titel:", () => _settings.TitleTextHeightMm, v => _settings.TitleTextHeightMm = v, 0.5M, 50, 2, 0.1M),
            NumRow("Witruimte boven kopregel:", () => _settings.HeaderSpacingMm, v => _settings.HeaderSpacingMm = v, 0, 50, 1, 0.5M),
            NumRow("Kolombreedte:", () => _settings.ColumnWidthMm, v => _settings.ColumnWidthMm = v, 1, 500, 1, 1),
            NumRow("Kolomtussenruimte:", () => _settings.ColumnGapMm, v => _settings.ColumnGapMm = v, 0, 100, 1, 0.5M),
            NumRow("Hoeveelheidkolom breedte:", () => _settings.QuantityColumnWidthMm, v => _settings.QuantityColumnWidthMm = v, 1, 200, 1, 0.5M),
            NumRow("Kadermarge:", () => _settings.BorderMarginMm, v => _settings.BorderMarginMm = v, 0, 50, 1, 0.5M)));
        page.Controls.Add(root);
        return page;
    }

    // Dezelfde CustomStatus.Members (entry-keys) als NLCSLEGENDASTATUS.
    private void EditStatusMembers()
    {
        var available = new List<(string Key, string Label)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_composition is not null)
            foreach (var item in _composition)
                if (seen.Add(item.Key))
                    available.Add((item.Key, item.Label));
        foreach (var manual in _settings.ManualEntries)
        {
            if (!manual.IsValid) continue;
            var key = LegendSettings.EntryKey(manual.ToLegendEntry());
            if (seen.Add(key))
                available.Add((key, $"[eigen] {manual.Description}"));
        }

        using var dlg = new CustomStatusDialog(_settings.CustomStatuses, available);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.CustomStatuses = dlg.Result;
            SyncAll();
        }
    }

    // Dezelfde XrefInclusion als NLCSLEGENDAXREFS.
    private void EditXrefInclusion()
    {
        if (_xrefNames.Count == 0)
        {
            MessageBox.Show(this, "Deze tekening heeft geen gekoppelde xrefs.", "NLCS Legenda",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new XrefInclusionDialog(_xrefNames, _settings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.XrefInclusion = dlg.Result;
            SyncAll();
        }
    }

    // Hetzelfde DescriptionOverrides-model als het losse commando.
    private void EditDescriptions()
    {
        using var dlg = new DescriptionsDialog(_settings.DescriptionOverrides.Clone());
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.DescriptionOverrides = dlg.ToCatalog();
            SyncAll();
        }
    }

    // Eigen (niet-NLCS) bronlagen koppelen. Werkt op een kopie; alleen OK schrijft terug.
    private void EditCustomLayers()
    {
        using var dlg = new CustomLayerDialog(_settings.CustomLayerRules);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.CustomLayerRules = dlg.Result;
            SyncAll();
        }
    }

    private void RefreshGrids() => SyncAll();

    private void EditRemarks()
    {
        using var dlg = new RemarksEditDialog(_settings.RemarksTitle, _settings.RemarksText);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.RemarksTitle = dlg.RemarksTitle;
            _settings.RemarksText = dlg.RemarksText;
            RefreshGrids();
        }
    }

    // Dezelfde boom-editor als het losse commando, op de werkkopie.
    private void EditComposition()
    {
        if (_composition is null)
            return;
        using var dlg = new LegendManageDialog(_composition, _settings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.ExcludedEntries = dlg.ExcludedKeys;
            _settings.ManualEntries = dlg.ManualEntries;
            RefreshGrids();
        }
    }

    private bool ValidateScale()
    {
        if (_settings.Scale > 0)
            return true;
        MessageBox.Show(this, "De schaal moet groter dan 0 zijn (bijvoorbeeld 200 voor 1:200).",
            "NLCS Legenda", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && !ValidateScale())
            e.Cancel = true;
    }

    private static void ResetToDefaults(LegendSettings target)
    {
        var defaults = new LegendSettings();
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(target))
        {
            if (property.IsReadOnly || !property.IsBrowsable)
                continue;
            property.SetValue(target, property.GetValue(defaults));
        }
    }
}

// Begint bij de effectieve keuze (IsXrefIncluded, inclusief default-fallback) en geeft bij OK een
// expliciete map terug.
internal sealed class XrefInclusionDialog : Form
{
    private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, CheckOnClick = true };
    private readonly IReadOnlyList<string> _names;

    public XrefInclusionDialog(IReadOnlyList<string> names, LegendSettings settings)
    {
        _names = names;
        Text = "NLCS Legenda \u2013 xrefs";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(380, 340);
        MinimumSize = new Size(320, 240);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        foreach (var name in names)
            _list.Items.Add(name, settings.IsXrefIncluded(name));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        var all = new Button { Text = "Alles aan", AutoSize = true };
        all.Click += (_, _) => SetAll(true);
        var none = new Button { Text = "Alles uit", AutoSize = true };
        none.Click += (_, _) => SetAll(false);
        bar.Controls.AddRange(new Control[] { all, none });

        var buttons = new ButtonBar(withApply: false);
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        Controls.Add(_list);
        Controls.Add(bar);
        Controls.Add(buttons);
    }

    public Dictionary<string, bool> Result
    {
        get
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _names.Count; i++)
                map[_names[i]] = _list.GetItemChecked(i);
            return map;
        }
    }

    private void SetAll(bool value)
    {
        for (int i = 0; i < _list.Items.Count; i++)
            _list.SetItemChecked(i, value);
    }
}

// Werkt op een diepe kopie; leden zijn entry-keys (zelfde model als NLCSLEGENDASTATUS).
internal sealed class CustomStatusDialog : Form
{
    private readonly List<CustomStatus> _statuses;
    private readonly IReadOnlyList<(string Key, string Label)> _available;
    private readonly ListBox _statusList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly CheckedListBox _members = new() { Dock = DockStyle.Fill, IntegralHeight = false, CheckOnClick = true };
    private bool _loading;

    public CustomStatusDialog(IReadOnlyList<CustomStatus> initial, IReadOnlyList<(string Key, string Label)> available)
    {
        _statuses = initial.Select(c => c.Clone()).ToList();
        _available = available;

        Text = "NLCS Legenda \u2013 eigen statussen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(640, 440);
        MinimumSize = new Size(520, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var split = new SplitContainer { Dock = DockStyle.Fill };

        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        var add = new Button { Text = "Nieuw", AutoSize = true };
        add.Click += (_, _) =>
        {
            var n = Prompt("Naam van de status:");
            if (string.IsNullOrWhiteSpace(n)) return;
            if (NameExists(n.Trim(), -1)) { WarnDuplicate(n.Trim()); return; }
            _statuses.Add(new CustomStatus { Name = n.Trim() });
            ReloadStatuses(_statuses.Count - 1);
        };
        var rename = new Button { Text = "Hernoemen", AutoSize = true };
        rename.Click += (_, _) =>
        {
            int i = _statusList.SelectedIndex;
            if (i < 0) return;
            var n = Prompt("Nieuwe naam:", _statuses[i].Name);
            if (string.IsNullOrWhiteSpace(n)) return;
            if (NameExists(n.Trim(), i)) { WarnDuplicate(n.Trim()); return; }
            _statuses[i].Name = n.Trim();
            ReloadStatuses(i);
        };
        var remove = new Button { Text = "Verwijderen", AutoSize = true };
        remove.Click += (_, _) =>
        {
            int i = _statusList.SelectedIndex;
            if (i < 0) return;
            _statuses.RemoveAt(i);
            ReloadStatuses(Math.Min(i, _statuses.Count - 1));
        };
        leftButtons.Controls.AddRange(new Control[] { add, rename, remove });
        split.Panel1.Controls.Add(_statusList);
        split.Panel1.Controls.Add(leftButtons);

        var memberLabel = new Label { Dock = DockStyle.Top, Text = "Toegewezen regels:", Height = 22, TextAlign = ContentAlignment.MiddleLeft };
        split.Panel2.Controls.Add(_members);
        split.Panel2.Controls.Add(memberLabel);

        _statusList.SelectedIndexChanged += (_, _) => LoadMembers();
        _members.ItemCheck += OnMemberCheck;

        var buttons = new ButtonBar(withApply: false);
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        Controls.Add(split);
        Controls.Add(buttons);

        // De splitter pas instellen als het venster een breedte heeft (anders gooit WinForms).
        Load += (_, _) => { try { split.SplitterDistance = 220; } catch { /* standaardverdeling */ } };

        ReloadStatuses(_statuses.Count > 0 ? 0 : -1);
    }

    public List<CustomStatus> Result => _statuses;

    private bool NameExists(string name, int excludeIndex)
    {
        for (int i = 0; i < _statuses.Count; i++)
            if (i != excludeIndex && string.Equals(_statuses[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private void WarnDuplicate(string name) =>
        MessageBox.Show(this, $"Er bestaat al een status met de naam \u201c{name}\u201d.", "NLCS Legenda",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void ReloadStatuses(int select)
    {
        _statusList.BeginUpdate();
        _statusList.Items.Clear();
        foreach (var s in _statuses)
            _statusList.Items.Add($"{s.Name} ({s.Members.Count})");
        _statusList.EndUpdate();
        if (select >= 0 && select < _statusList.Items.Count)
            _statusList.SelectedIndex = select;
        else
            LoadMembers();
    }

    private void LoadMembers()
    {
        _loading = true;
        _members.BeginUpdate();
        _members.Items.Clear();
        int sel = _statusList.SelectedIndex;
        var members = sel >= 0 ? _statuses[sel].Members : null;
        foreach (var (key, label) in _available)
            _members.Items.Add(label, members is not null && members.Contains(key, StringComparer.OrdinalIgnoreCase));
        _members.EndUpdate();
        _members.Enabled = sel >= 0;
        _loading = false;
    }

    private void OnMemberCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_loading) return;
        int sel = _statusList.SelectedIndex;
        if (sel < 0) return;
        var key = _available[e.Index].Key;
        var members = _statuses[sel].Members;
        if (e.NewValue == CheckState.Checked)
        {
            if (!members.Contains(key, StringComparer.OrdinalIgnoreCase))
                members.Add(key);
        }
        else
        {
            members.RemoveAll(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase));
        }
        _statusList.Items[sel] = $"{_statuses[sel].Name} ({members.Count})";
    }

    private string? Prompt(string label, string initial = "")
    {
        using var dlg = new Form
        {
            Text = label,
            Font = SystemFonts.MessageBoxFont,
            ClientSize = new Size(320, 110),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ShowInTaskbar = false,
            ShowIcon = false,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var tb = new TextBox { Text = initial, Dock = DockStyle.Top, Margin = new Padding(8) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dlg.AcceptButton = ok;
        dlg.Controls.Add(tb);
        dlg.Controls.Add(ok);
        return dlg.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
    }
}

// Meerregelige editor voor de opmerkingen: Enter maakt een nieuwe regel, met scrollbar en
// gewone klembord-ondersteuning. Regeleindes en lege regels blijven exact behouden.
internal sealed class RemarksEditDialog : Form
{
    private readonly TextBox _title;
    private readonly TextBox _body;

    public RemarksEditDialog(string title, string body)
    {
        Text = "NLCS Legenda \u2013 opmerkingen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(560, 420);
        MinimumSize = new Size(420, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8, 6, 8, 6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Kop", AutoSize = true, Margin = new Padding(0, 2, 0, 2) }, 0, 0);
        _title = new TextBox { Dock = DockStyle.Fill, Text = title };
        layout.Controls.Add(_title, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = "Tekst (Enter = nieuwe regel; gebruik \u2022 voor opsommingen)",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 2)
        }, 0, 2);
        _body = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Text = body
        };
        layout.Controls.Add(_body, 0, 3);

        var buttons = new ButtonBar(withApply: false);
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;
        // Enter in het meerregelige veld hoort een regeleinde te maken, niet OK te drukken.
        buttons.Ok.Click += (_, _) => DialogResult = DialogResult.OK;

        Controls.Add(layout);
        Controls.Add(buttons);
    }

    public string RemarksTitle => _title.Text.Trim();

    // Body niet trimmen op regelniveau: bewuste lege regels en inspringing blijven staan.
    public string RemarksText => _body.Text.Replace("\r\n", "\n").Trim('\n');
}

public sealed class DescriptionRow
{
    public string Sleutel { get; set; } = string.Empty;

    public string Algemeen { get; set; } = string.Empty;

    public string Specifiek { get; set; } = string.Empty;
}

internal sealed class DescriptionsDialog : Form
{
    private readonly DataGridView _grid;
    private BindingList<DescriptionRow> _rows = new();

    public event EventHandler? ApplyRequested;

    public DescriptionsDialog(DescriptionCatalog initial)
    {
        Text = "NLCS Legenda \u2013 omschrijvingen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(760, 620);
        MinimumSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AllowUserToResizeRows = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DescriptionRow.Sleutel),
            HeaderText = "Element (hoofdgroep|element)",
            FillWeight = 38
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DescriptionRow.Algemeen),
            HeaderText = "Algemeen",
            FillWeight = 24,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DescriptionRow.Specifiek),
            HeaderText = "Specifiek",
            FillWeight = 38,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
        });
        _grid.EditingControlShowing += OnEditingControlShowing;

        var buttons = new ButtonBar(withApply: true);
        buttons.Apply!.Click += (_, _) => ApplyRequested?.Invoke(this, EventArgs.Empty);
        var reset = buttons.AddExtra("Standaardwaarden");
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Alle omschrijvingen terugzetten naar de standaard?",
                    "NLCS Legenda", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                SetRows(DescriptionCatalog.Default());
        };
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        Controls.Add(_grid);
        Controls.Add(buttons);

        SetRows(initial);
    }

    private void SetRows(DescriptionCatalog catalog)
    {
        _rows = new BindingList<DescriptionRow>(
            catalog.Elementen
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new DescriptionRow
                {
                    Sleutel = kv.Key,
                    Algemeen = kv.Value.Algemeen ?? string.Empty,
                    Specifiek = kv.Value.Specifiek
                })
                .ToList());
        _grid.DataSource = _rows;
    }

    private static void OnEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is not TextBox tb)
            return;
        tb.Multiline = true;
        tb.AcceptsReturn = false;
        tb.WordWrap = true;
        tb.KeyDown -= MultilineKeyDown;
        tb.KeyDown += MultilineKeyDown;
    }

    private static void MultilineKeyDown(object? sender, KeyEventArgs e)
    {
        // Shift+Enter voegt een regeleinde toe; gewone Enter bevestigt de cel.
        if (e.KeyCode == Keys.Enter && e.Shift && sender is TextBox tb)
        {
            int pos = tb.SelectionStart;
            tb.Text = tb.Text.Insert(pos, Environment.NewLine);
            tb.SelectionStart = pos + Environment.NewLine.Length;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    public DescriptionCatalog ToCatalog()
    {
        _grid.EndEdit();
        var catalog = new DescriptionCatalog();
        foreach (var row in _rows)
        {
            var key = row.Sleutel?.Trim();
            if (string.IsNullOrEmpty(key))
                continue;
            catalog.Elementen[key] = new DescriptionEntry
            {
                Algemeen = string.IsNullOrWhiteSpace(row.Algemeen) ? null : row.Algemeen.Trim(),
                Specifiek = (row.Specifiek ?? string.Empty).Trim()
            };
        }
        return catalog;
    }
}

internal sealed class TextEditDialog : Form
{
    private readonly TextBox _algemeen;
    private readonly TextBox _specifiek;
    private readonly DescriptionEntry _default;

    public event EventHandler? ApplyRequested;

    public TextEditDialog(string elementKey, DescriptionEntry current, DescriptionEntry defaults)
    {
        _default = defaults;

        Text = "NLCS Legenda \u2013 elementtekst";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(460, 360);
        MinimumSize = new Size(400, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8, 6, 8, 6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = $"Element: {elementKey}", AutoSize = true, Margin = new Padding(0, 2, 0, 6) }, 0, 0);
        layout.Controls.Add(new Label { Text = "Algemeen", AutoSize = true, Margin = new Padding(0, 2, 0, 2) }, 0, 1);
        _algemeen = MakeTextBox();
        layout.Controls.Add(_algemeen, 0, 2);
        layout.Controls.Add(new Label
        {
            Text = "Specifiek (Shift+Enter voor een nieuwe regel)",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 2)
        }, 0, 3);
        _specifiek = MakeTextBox();
        layout.Controls.Add(_specifiek, 0, 4);

        var buttons = new ButtonBar(withApply: true);
        buttons.Apply!.Click += (_, _) => ApplyRequested?.Invoke(this, EventArgs.Empty);
        var reset = buttons.AddExtra("Standaard");
        reset.Click += (_, _) =>
        {
            _algemeen.Text = _default.Algemeen ?? string.Empty;
            _specifiek.Text = _default.Specifiek;
        };
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        Controls.Add(layout);
        Controls.Add(buttons);

        _algemeen.Text = current.Algemeen ?? string.Empty;
        _specifiek.Text = current.Specifiek;
    }

    public string Algemeen => _algemeen.Text.Trim();

    public string Specifiek => _specifiek.Text.Trim();

    private static TextBox MakeTextBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        WordWrap = true,
        ScrollBars = ScrollBars.Vertical
    };
}

// Beheert de eigen-laagkoppelingen: een lijst met toevoegen/bewerken/verwijderen. Werkt op een
// diepe kopie; de aanroeper neemt het resultaat alleen bij OK over.
internal sealed class CustomLayerDialog : Form
{
    private readonly List<CustomLayerRule> _rules;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public CustomLayerDialog(IReadOnlyList<CustomLayerRule> initial)
    {
        _rules = initial.Select(r => r.Clone()).ToList();

        Text = "NLCS Legenda \u2013 eigen lagen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(560, 420);
        MinimumSize = new Size(460, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            Text = "Koppel een eigen (niet-NLCS) laag als bron. Dit is iets anders dan een handmatige "
                 + "regel: een eigen laag telt alleen mee als er objecten op die laag staan.",
            Padding = new Padding(4, 2, 4, 2)
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        var add = new Button { Text = "Toevoegen\u2026", AutoSize = true };
        add.Click += (_, _) => { var r = EditRule(new CustomLayerRule()); if (r is not null) { _rules.Add(r); Reload(_rules.Count - 1); } };
        var edit = new Button { Text = "Bewerken\u2026", AutoSize = true };
        edit.Click += (_, _) => { int i = _list.SelectedIndex; if (i < 0) return; var r = EditRule(_rules[i].Clone()); if (r is not null) { _rules[i] = r; Reload(i); } };
        var remove = new Button { Text = "Verwijderen", AutoSize = true };
        remove.Click += (_, _) => { int i = _list.SelectedIndex; if (i < 0) return; _rules.RemoveAt(i); Reload(Math.Min(i, _rules.Count - 1)); };
        bar.Controls.AddRange(new Control[] { add, edit, remove });

        var buttons = new ButtonBar(withApply: false);
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        Controls.Add(_list);
        Controls.Add(bar);
        Controls.Add(hint);
        Controls.Add(buttons);
        Reload(_rules.Count > 0 ? 0 : -1);
    }

    public List<CustomLayerRule> Result => _rules;

    private void Reload(int select)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in _rules)
            _list.Items.Add($"{r.Layer}  \u2192  {r.Element} ({r.Type}, {r.QuantityMode})");
        _list.EndUpdate();
        if (select >= 0 && select < _list.Items.Count)
            _list.SelectedIndex = select;
    }

    // Bewerkt één regel in een subvenster. Geeft de regel terug bij OK, anders null.
    private CustomLayerRule? EditRule(CustomLayerRule rule)
    {
        using var dlg = new Form
        {
            Text = "Eigen laag",
            Font = SystemFonts.MessageBoxFont,
            ClientSize = new Size(420, 420),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ShowInTaskbar = false, ShowIcon = false, MinimizeBox = false, MaximizeBox = false
        };
        var layer = new TextBox { Text = rule.Layer, Width = 240 };
        var element = new TextBox { Text = rule.Element, Width = 240 };
        var desc = new TextBox { Text = rule.Description, Width = 240 };
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        type.Items.AddRange(new object[] { NlcsDrawType.Geometrie, NlcsDrawType.Vlak, NlcsDrawType.Arcering, NlcsDrawType.Vlakvulling, NlcsDrawType.Symbool });
        type.SelectedItem = rule.Type;
        var status = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        status.Items.AddRange(Enum.GetNames<NlcsStatus>());
        status.SelectedItem = rule.Status.ToString();
        var qmode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        qmode.Items.AddRange(Enum.GetNames<CustomQuantityMode>());
        qmode.SelectedItem = rule.QuantityMode.ToString();
        var scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        scope.Items.AddRange(Enum.GetNames<CustomSourceScope>());
        scope.SelectedItem = rule.Scope.ToString();
        var xref = new TextBox { Text = rule.XrefName, Width = 160 };
        var block = new TextBox { Text = rule.BlockName ?? string.Empty, Width = 160 };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8) };
        FlowLayoutPanel Row(string label, Control c)
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            p.Controls.Add(new Label { Text = label, AutoSize = true, Width = 150, Padding = new Padding(0, 6, 0, 0) });
            p.Controls.Add(c);
            return p;
        }
        flow.Controls.Add(Row("Laagnaam:", layer));
        flow.Controls.Add(Row("Logisch element:", element));
        flow.Controls.Add(Row("Omschrijving:", desc));
        flow.Controls.Add(Row("Type:", type));
        flow.Controls.Add(Row("Status:", status));
        flow.Controls.Add(Row("Hoeveelheid:", qmode));
        flow.Controls.Add(Row("Bron:", scope));
        flow.Controls.Add(Row("Xref-naam (bij xref):", xref));
        flow.Controls.Add(Row("Alleen blok (symbool):", block));

        var ok = new Button { Text = "OK", DialogResult = DialogResult.None, Width = 90 };
        var cancel = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, Width = 90 };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
        bar.Controls.Add(cancel); bar.Controls.Add(ok);
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        dlg.Controls.Add(flow); dlg.Controls.Add(bar);

        ok.Click += (_, _) =>
        {
            rule.Layer = layer.Text.Trim();
            rule.Element = element.Text.Trim();
            rule.Description = desc.Text.Trim();
            rule.Type = (NlcsDrawType)type.SelectedItem!;
            rule.Status = Enum.Parse<NlcsStatus>((string)status.SelectedItem!);
            rule.QuantityMode = Enum.Parse<CustomQuantityMode>((string)qmode.SelectedItem!);
            rule.Scope = Enum.Parse<CustomSourceScope>((string)scope.SelectedItem!);
            rule.XrefName = xref.Text.Trim();
            rule.BlockName = string.IsNullOrWhiteSpace(block.Text) ? null : block.Text.Trim();
            if (!LayerNaming.IsValid(rule.Layer) || string.IsNullOrWhiteSpace(rule.Layer))
            { Warn("Geef een geldige laagnaam op."); return; }
            if (string.IsNullOrWhiteSpace(rule.Element))
            { Warn("Geef een logisch element op."); return; }
            if (_rules.Any(o => !ReferenceEquals(o, rule) && SameMapping(o, rule)))
            { Warn("Er bestaat al een koppeling voor deze laag en bron."); return; }
            dlg.DialogResult = DialogResult.OK;
        };

        return dlg.ShowDialog(this) == DialogResult.OK ? rule : null;
    }

    private static bool SameMapping(CustomLayerRule a, CustomLayerRule b) =>
        string.Equals(a.Layer, b.Layer, StringComparison.OrdinalIgnoreCase)
        && a.Scope == b.Scope
        && string.Equals(a.XrefName, b.XrefName, StringComparison.OrdinalIgnoreCase)
        && a.Type == b.Type;

    private void Warn(string message) =>
        MessageBox.Show(this, message, "NLCS Legenda", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

