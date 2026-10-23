using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class ButtonBar : TableLayoutPanel
{
    private readonly FlowLayoutPanel _extras = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
    public Button Ok { get; }
    public Button Cancel { get; }
    public Button? Apply { get; }

    public ButtonBar(bool withApply)
    {
        Dock = DockStyle.Bottom;
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        Controls.Add(_extras, 0, 0);
        Controls.Add(actions, 0, 1);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8, 6, 8, 8);

        Ok = MakeButton("Opslaan", DialogResult.OK);
        Cancel = MakeButton("Annuleren", DialogResult.Cancel);
        actions.Controls.Add(Ok);
        actions.Controls.Add(Cancel);
        if (withApply)
        {
            Apply = MakeButton("Toepassen", DialogResult.None);
            actions.Controls.Add(Apply);
        }
    }

    public Button AddExtra(string text)
    {
        var button = MakeButton(text, DialogResult.None);
        _extras.Controls.Add(button);
        return button;
    }

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

internal sealed class SettingsDialog : LegendDialog
{
    private readonly List<Action> _resync = new();
    private readonly LegendSettings _settings;
    private readonly IReadOnlyList<EntryCheckItem>? _composition;
    private readonly IReadOnlyList<string> _xrefNames;
    private readonly Func<LegendSettings, IReadOnlyList<EntryCheckItem>>? _compositionSource;
    private bool _syncing;
    private readonly DialogSaveState? _saveState;
    private readonly IReadOnlyList<ManualEntry> _layerChoices;

    public event EventHandler? ApplyRequested;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string?>? SaveChanges { get; set; }

    public SettingsDialog(LegendSettings settings, string contextLabel,
        IReadOnlyList<EntryCheckItem>? composition = null, IReadOnlyList<string>? xrefNames = null,
        Func<LegendSettings, IReadOnlyList<EntryCheckItem>>? compositionSource = null,
        IReadOnlyList<ManualEntry>? layerChoices = null)
    {
        _composition = composition;
        _layerChoices = layerChoices ?? Array.Empty<ManualEntry>();
        _compositionSource = compositionSource;
        _xrefNames = xrefNames ?? Array.Empty<string>();
        _settings = settings;

        Text = "NLCS Legenda \u2013 instellingen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(720, 680);
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var header = new Label
        {
            Dock = DockStyle.Top,
            Text = contextLabel,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Font = Own(new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold))
        };

        var tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons,
            SizeMode = TabSizeMode.Fixed, ItemSize = new Size(0, 1), TabStop = false };
        tabs.TabPages.Add(BuildAlgemeenTab());
        tabs.TabPages.Add(BuildInhoudTab());
        tabs.TabPages.Add(BuildSamenvoegenTab());
        tabs.TabPages.Add(BuildOpmaakTab());
        tabs.TabPages.Add(BuildTekstenTab());
        tabs.TabPages.Add(BuildHoeveelhedenTab());
        tabs.TabPages.Add(BuildSchaalbalkTab());
        tabs.TabPages.Add(BuildMatenTab());
        var navigation = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 32, AccessibleName = "Onderdeel" };
        navigation.Items.AddRange(tabs.TabPages.Cast<TabPage>().Select(p => (object)p.Text).ToArray());
        navigation.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            TextRenderer.DrawText(e.Graphics, navigation.Items[e.Index].ToString(), e.Font,
                Rectangle.Inflate(e.Bounds, -10, 0), e.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
        navigation.SelectedIndexChanged += (_, _) => tabs.SelectedIndex = navigation.SelectedIndex;
        tabs.SelectedIndexChanged += (_, _) => navigation.SelectedIndex = tabs.SelectedIndex;
        navigation.SelectedIndex = 0;
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(navigation, 0, 0);
        content.Controls.Add(tabs, 1, 0);

        var buttons = new ButtonBar(withApply: true);
        buttons.Apply!.Click += (_, _) =>
        {
            if (ValidateScale() && Save())
                ApplyRequested?.Invoke(this, EventArgs.Empty);
        };
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        FormClosing += OnFormClosing;

        Controls.Add(content);
        Controls.Add(buttons);
        Controls.Add(header);
        _saveState = Own(new DialogSaveState(buttons, () => _settings.ToJson()));
    }

    public LegendSettings Settings => _settings;

    // terugkoppeling tussen gekoppelde invoervelden voorkomen
    private void SyncAll()
    {
        if (_syncing) return;
        _syncing = true;
        try { foreach (var s in _resync) s(); }
        finally { _syncing = false; }
        _saveState?.Refresh();
    }

    private Panel NumRow(string label, Func<double> get, Action<double> set,
        decimal min, decimal max, int decimals = 1, decimal increment = 0.5M, int width = 90)
    {
        decimal start = ClampDecimal((decimal)get(), min, max);
        // weergave en opgeslagen waarde gelijk houden
        if (start != (decimal)get()) set((double)start);
        var n = new NumericUpDown
        {
            Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = increment,
            Width = width, Value = start
        };
        n.ValueChanged += (_, _) => { if (!_syncing) { set((double)n.Value); SyncAll(); } };
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
        n.ValueChanged += (_, _) => { if (!_syncing) { set((int)n.Value); SyncAll(); } };
        _resync.Add(() => n.Value = Math.Clamp(get(), min, max));
        return LabeledRow(label, n);
    }

    private Panel TextRow(string label, Func<string> get, Action<string> set, int width = 220)
    {
        var t = new TextBox { Text = get(), Width = width };
        t.TextChanged += (_, _) => { if (!_syncing) { set(t.Text); SyncAll(); } };
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
            { set(v); SyncAll(); }
        };
        _resync.Add(() => combo.SelectedItem = get().ToString());
        return LabeledRow(label, combo);
    }

    private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        => value < min ? min : value > max ? max : value;

    private CheckBox BoundCheck(string text, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { Text = text, Checked = get(), AutoSize = true, Margin = new Padding(3, 3, 12, 3) };
        cb.CheckedChanged += (_, _) => { if (_syncing) return; set(cb.Checked); SyncAll(); };
        _resync.Add(() => cb.Checked = get());
        return cb;
    }

    private CheckBox XrefChoice()
    {
        var control = new CheckBox { Text = "Xrefs meenemen", AutoSize = true, ThreeState = true, AutoCheck = false };
        void Refresh()
        {
            var choices = _xrefNames.Count > 0
                ? _xrefNames.Select(_settings.IsXrefIncluded).ToArray()
                : new[] { _settings.IncludeXrefLayers }.Concat(_settings.XrefInclusion.Values).ToArray();
            control.CheckState = choices.All(v => v) ? CheckState.Checked
                : choices.All(v => !v) ? CheckState.Unchecked : CheckState.Indeterminate;
            control.Text = control.CheckState == CheckState.Indeterminate ? "Xrefs meenemen (gedeeltelijk)" : "Xrefs meenemen";
        }
        control.Click += (_, _) => { _settings.SetAllXrefs(control.CheckState != CheckState.Checked); SyncAll(); };
        _resync.Add(Refresh);
        Refresh();
        return control;
    }

    private static GroupBox Group(string title, params Control[] controls)
    {
        var flow = new SettingsStack { Padding = new Padding(6) };
        flow.Controls.AddRange(controls);
        var box = new GroupBox { Text = title, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        box.Controls.Add(flow);
        return box;
    }

    private TabPage BuildAlgemeenTab()
    {
        var page = new TabPage("Legenda") { Padding = new Padding(10), AutoScroll = true };
        var root = new SettingsStack();

        var scale = new NumericUpDown { Minimum = 1, Maximum = 1_000_000, Value = (decimal)Math.Clamp(_settings.Scale, 1, 1_000_000), Width = 100 };
        scale.ValueChanged += (_, _) => { _settings.Scale = (double)scale.Value; _saveState?.Refresh(); };
        _resync.Add(() => scale.Value = (decimal)Math.Clamp(_settings.Scale, 1, 1_000_000));
        var title = new TextBox { Text = _settings.Title, Width = 260 };
        title.TextChanged += (_, _) => { _settings.Title = title.Text; _saveState?.Refresh(); };
        _resync.Add(() => title.Text = _settings.Title);
        var margin = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 0.5M, Value = (decimal)Math.Clamp(_settings.ViewportMarginMm, 0, 100), Width = 80 };
        margin.ValueChanged += (_, _) => { _settings.ViewportMarginMm = (double)margin.Value; _saveState?.Refresh(); };
        _resync.Add(() => margin.Value = (decimal)Math.Clamp(_settings.ViewportMarginMm, 0, 100));

        root.Controls.Add(LabeledRow("Schaal 1:", scale));
        root.Controls.Add(LabeledRow("Titel:", title));
        root.Controls.Add(BoundCheck("Titel tonen", () => _settings.IncludeTitle, v => _settings.IncludeTitle = v));
        root.Controls.Add(Group("Viewport",
            BoundCheck("Viewport zelf tekenen", () => _settings.ViewportManual, v => _settings.ViewportManual = v),
            LabeledRow("Marge (mm):", margin)));
        var resetFormat = new Button { Text = "Templateopmaak herstellen", AutoSize = true };
        resetFormat.Click += (_, _) => { _settings.ResetFormattingToTemplate(); RefreshGrids(); };
        var resetAll = new Button { Text = "Alle instellingen herstellen", AutoSize = true };
        resetAll.Click += (_, _) => { ResetToDefaults(_settings); RefreshGrids(); };
        var defaults = Group("Standaardinstellingen");
        defaults.Controls[0].Controls.Add(resetFormat);
        defaults.Controls[0].Controls.Add(resetAll);
        root.Controls.Add(defaults);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildInhoudTab()
    {
        var page = new TabPage("Bronnen en inhoud") { Padding = new Padding(10), AutoScroll = true };
        var root = new SettingsStack();

        var xrefs = new Button { Text = "Xrefs\u2026", AutoSize = true };
        xrefs.Click += (_, _) => EditXrefInclusion();
        var sources = Group("Bronnen",
            BoundCheck("Onzichtbare (bevroren/uit) lagen meenemen", () => _settings.IncludeInvisibleLayers, v => _settings.IncludeInvisibleLayers = v),
            XrefChoice());
        sources.Controls[0].Controls.Add(xrefs);
        root.Controls.Add(sources);

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
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Padding = new Padding(3) };
        if (_composition is not null || _compositionSource is not null)
        {
            var samen = new Button { Text = "Samenstellen\u2026", AutoSize = true };
            samen.Click += (_, _) => EditComposition();
            actions.Controls.Add(samen);
        }
        var statussen = new Button { Text = "Eigen statussen\u2026", AutoSize = true };
        statussen.Click += (_, _) => EditStatusMembers();
        actions.Controls.Add(statussen);
        var eigenLagen = new Button { Text = "Eigen lagen\u2026", AutoSize = true };
        eigenLagen.Click += (_, _) => EditCustomLayers();
        actions.Controls.Add(eigenLagen);
        var blanco = new Button { Text = "Blanco regels\u2026", AutoSize = true };
        blanco.Click += (_, _) => EditBlankEntries();
        actions.Controls.Add(blanco);
        root.Controls.Add(actions);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildSamenvoegenTab()
    {
        var page = new TabPage("Samenvoegen") { Padding = new Padding(10), AutoScroll = true };
        var root = new SettingsStack();
        root.Controls.Add(new Label { Text = "Vink aan welke verschillen je wilt negeren. Kleur, lijnpatroon en symbool blijven apart.", AutoSize = true });
        var choices = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top, Padding = new Padding(6) };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        choices.Controls.Add(new Label { Text = "Kenmerk", AutoSize = true }, 0, 0);
        choices.Controls.Add(new Label { Text = "KLIC", AutoSize = true }, 1, 0);
        choices.Controls.Add(new Label { Text = "Overige lagen", AutoSize = true }, 2, 0);
        int row = 1;
        foreach (var (dimension, label) in new[] {
            (GroupDimension.Soort, "Soorten"), (GroupDimension.Specificatie, "Spanning/druk of specificatie"),
            (GroupDimension.Uitvoering, "Uitvoeringen"), (GroupDimension.Nummer, "Volgnummers"),
            (GroupDimension.Diameter, "Diameters"), (GroupDimension.Netbeheerder, "Netbeheerders") })
        {
            choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            choices.Controls.Add(new Label { Text = label, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 5) }, 0, row);
            CheckBox Choice(Func<ICollection<GroupDimension>> dimensions, string category)
            {
                var check = BoundCheck("", () => dimensions().Contains(dimension), value => {
                    if (value) dimensions().Add(dimension); else dimensions().Remove(dimension);
                });
                check.AccessibleName = category + ": " + label + " samenvoegen";
                check.MinimumSize = new Size(32, 24);
                check.Anchor = AnchorStyles.Left;
                return check;
            }
            choices.Controls.Add(Choice(() => _settings.MergedDimensions, "KLIC"), 1, row);
            if (dimension != GroupDimension.Netbeheerder) choices.Controls.Add(Choice(() => _settings.NonKlicMergedDimensions, "Overige lagen"), 2, row);
            else choices.Controls.Add(new Label { Text = "—", AutoSize = true }, 2, row);
            row++;
        }
        var comparisons = new GroupBox { Text = "Samenvoegen", AutoSize = true, Padding = new Padding(6) };
        comparisons.Controls.Add(choices);
        root.Controls.Add(comparisons);
        root.Controls.Add(new Label { Text = "KLIC: kabel- en leidinglagen, objecten met KLIC-gegevens en bronnen met KLIC in de naam. Overige lagen: alle andere objecten.", AutoSize = true, Margin = new Padding(3, 6, 3, 6) });
        root.Controls.Add(Group("Tekst",
            BoundCheck("Samengevoegde waarden tonen", () => _settings.ShowMergedValues, v => _settings.ShowMergedValues = v),
            Own(new Label { Text = "Aan: Leiding (200, 300). Uit: Leiding.", AutoSize = true }),
            BoundCheck("Lege KLIC-teksten weglaten", () => _settings.SuppressKlicPlaceholders, v => _settings.SuppressKlicPlaceholders = v)));

        var data = new Button { Text = "Extra KLIC-velden…", AutoSize = true };
        data.Click += (_, _) =>
        {
            using var dialog = new KlicOptionsDialog(_settings.Klic);
            if (dialog.ShowDialog(this) == DialogResult.OK) { _settings.Klic = dialog.Result; SyncAll(); }
        };
        root.Controls.Add(data);
        page.Controls.Add(root);
        return page;
    }

    internal static Panel LabeledRow(string label, Control control)
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(3) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 4, 6, 4) }, 0, 0);
        control.Anchor = control is TextBox or ComboBox ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
        panel.Controls.Add(control, 1, 0);
        return panel;
    }

    private TabPage BuildOpmaakTab()
    {
        var page = new TabPage("Opmaak") { Padding = new Padding(10), AutoScroll = true };
        var root = new SettingsStack();

        root.Controls.Add(Group("Weergave",
            BoundCheck("Kader rond legenda", () => _settings.DrawBorder, v => _settings.DrawBorder = v),
            BoundCheck("Kader per voorbeeldvak", () => _settings.DrawSwatchFrame, v => _settings.DrawSwatchFrame = v),
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
            TextRow("Tekststijl (leeg = NLCS-ISO):", () => _settings.TextStyle, v => _settings.TextStyle = v, 180),
            TextRow("Kaderlaag:", () => _settings.FrameLayer, v => _settings.FrameLayer = v, 180),
            TextRow("Hulpkaders (niet plotten):", () => _settings.SwatchFrameLayer, v => _settings.SwatchFrameLayer = v, 180),
            TextRow("Tekstlaag omschrijvingen:", () => _settings.TextLayer, v => _settings.TextLayer = v, 180),
            TextRow("Tekstlaag koppen/titel:", () => _settings.HeaderTextLayer, v => _settings.HeaderTextLayer = v, 180)));
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildTekstenTab()
    {
        var page = new TabPage("Teksten") { Padding = new Padding(10), AutoScroll = true };
        var root = new SettingsStack();

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
        var root = new SettingsStack();

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
        var page = new TabPage("Schaalbalk") { Padding = new Padding(10), AutoScroll = true };
        var root = new SettingsStack();

        root.Controls.Add(Group("Schaalbalk",
            BoundCheck("Schaalbalk tonen", () => _settings.IncludeScaleBar, v => _settings.IncludeScaleBar = v),
            IntRow("Aantal segmenten:", () => _settings.ScaleBarSegments, v => _settings.ScaleBarSegments = v, 1, 50),
            NumRow("Meters per segment (0 = auto):", () => _settings.ScaleBarSegmentMeters, v => _settings.ScaleBarSegmentMeters = v, 0, 10000, 2, 1),
            NumRow("Hoogte (mm):", () => _settings.ScaleBarHeightMm, v => _settings.ScaleBarHeightMm = v, 0.5M, 50, 1, 0.5M)));
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildMatenTab()
    {
        var page = new TabPage("Maten") { Padding = new Padding(10), AutoScroll = true };
        var root = new SettingsStack();
        root.Controls.Add(Group("Afmetingen op papier (mm)",
            NumRow("Breedte voorbeeldvak:", () => _settings.SwatchWidthMm, v => _settings.SwatchWidthMm = v, 1, 200, 1, 0.5M),
            NumRow("Hoogte voorbeeldvak:", () => _settings.SwatchHeightMm, v => _settings.SwatchHeightMm = v, 1, 200, 1, 0.5M),
            NumRow("Regelafstand:", () => _settings.RowPitchMm, v => _settings.RowPitchMm = v, 1, 100, 4, 0.1M),
            NumRow("Regelhoogte-factor tekst:", () => _settings.LineSpacingFactor, v => _settings.LineSpacingFactor = v, 0.25M, 4, 3, 0.01M),
            NumRow("Breedte opmerkingen:", () => _settings.RemarksWidthMm, v => _settings.RemarksWidthMm = v, 1, 500, 4, 1),
            NumRow("Afstand voorbeeld-tekst:", () => _settings.TextGapMm, v => _settings.TextGapMm = v, 0, 50, 4, 0.5M),
            NumRow("Teksthoogte omschrijving:", () => _settings.TextHeightMm, v => _settings.TextHeightMm = v, 0.5M, 50, 2, 0.1M),
            NumRow("Teksthoogte kopregel:", () => _settings.HeaderTextHeightMm, v => _settings.HeaderTextHeightMm = v, 0.5M, 50, 2, 0.1M),
            NumRow("Teksthoogte titel:", () => _settings.TitleTextHeightMm, v => _settings.TitleTextHeightMm = v, 0.5M, 50, 2, 0.1M),
            NumRow("Titel-kop (basislijnen):", () => _settings.HeaderSpacingMm, v => _settings.HeaderSpacingMm = v, 0, 50, 4, 0.5M),
            NumRow("Kolombreedte:", () => _settings.ColumnWidthMm, v => _settings.ColumnWidthMm = v, 1, 500, 1, 1),
            NumRow("Kolomtussenruimte:", () => _settings.ColumnGapMm, v => _settings.ColumnGapMm = v, 0, 100, 1, 0.5M),
            NumRow("Hoeveelheidkolom breedte:", () => _settings.QuantityColumnWidthMm, v => _settings.QuantityColumnWidthMm = v, 1, 200, 1, 0.5M),
            NumRow("Kadermarge:", () => _settings.BorderMarginMm, v => _settings.BorderMarginMm = v, 0, 50, 1, 0.5M)));
        page.Controls.Add(root);
        return page;
    }

    private void EditStatusMembers()
    {
        var available = new List<(string Key, string Label)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var composition = CompositionItems();
        if (composition is null) return;
        if (_composition is not null || _compositionSource is not null)
            foreach (var item in composition.Where(item => seen.Add(item.Key)))
                available.Add((item.Key, item.Label));
        foreach (var manual in _settings.ManualEntries.Where(manual => manual.IsValid))
        {
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

    private void EditDescriptions()
    {
        var effective = DescriptionCatalog.Default();
        effective.MergeFrom(_settings.DescriptionOverrides);
        using var dlg = new DescriptionsDialog(effective);
        void Apply()
        {
            _settings.DescriptionOverrides = dlg.ToCatalog().Diff(DescriptionCatalog.Default());
            SyncAll();
        }
        dlg.SaveChanges = () => { Apply(); return null; };
        dlg.ShowDialog(this);
    }

    private void EditCustomLayers()
    {
        using var dlg = new CustomLayerDialog(_settings.CustomLayerRules);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.CustomLayerRules = dlg.Result;
            SyncAll();
        }
    }

    private void EditBlankEntries()
    {
        using var dlg = new BlankEntryDialog(_settings.BlankEntries);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.BlankEntries = dlg.Result;
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

    private IReadOnlyList<EntryCheckItem>? CompositionItems()
    {
        try { return _compositionSource?.Invoke(_settings) ?? _composition ?? Array.Empty<EntryCheckItem>(); }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            MessageBox.Show(this, "De bronregels konden niet worden gelezen.\n\n" + ex.Message,
                "NLCS Legenda", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
    }

    private void EditComposition()
    {
        if (_composition is null && _compositionSource is null) return;
        var composition = CompositionItems();
        if (composition is null) return;
        using var dlg = new LegendManageDialog(composition, _settings, _layerChoices);
        void Apply()
        {
            _settings.ExcludedEntries = dlg.ExcludedKeys;
            _settings.ManualEntries = dlg.ManualEntries;
            RefreshGrids();
        }
        dlg.ApplyRequested += (_, _) => Apply();
        if (dlg.ShowDialog(this) == DialogResult.OK) Apply();
    }

    private bool ValidateScale()
    {
        if (_settings.Scale > 0)
            return true;
        MessageBox.Show(this, "De schaal moet groter dan 0 zijn (bijvoorbeeld 200 voor 1:200).",
            "NLCS Legenda", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private bool Save()
    {
        try
        {
            if (_saveState?.HasChanges == false) return true;
            if (SaveChanges?.Invoke() is not { } error) { _saveState?.Saved(); return true; }
            MessageBox.Show(this, error, "NLCS Legenda", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            MessageBox.Show(this, ex.Message, "NLCS Legenda", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return false;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && (!ValidateScale() || !Save()))
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
        target.MergedDimensions = LegendSettings.DefaultKlicMergeDimensions();
        target.NonKlicMergedDimensions.Clear();
    }
}

internal sealed class XrefInclusionDialog : LegendDialog
{
    private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, CheckOnClick = true };
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

internal sealed class CustomStatusDialog : LegendDialog
{
    private readonly List<CustomStatus> _statuses;
    private readonly IReadOnlyList<(string Key, string Label)> _available;
    private readonly ListBox _statusList = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly CheckedListBox _members = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, CheckOnClick = true };
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

        var memberLabel = new Label { Dock = DockStyle.Top, Text = "Toegewezen regels:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        split.Panel2.Controls.Add(_members);
        split.Panel2.Controls.Add(memberLabel);

        _statusList.SelectedIndexChanged += (_, _) => { if (!_loading) LoadMembers(); };
        _members.ItemCheck += OnMemberCheck;

        var buttons = new ButtonBar(withApply: false);
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        Controls.Add(split);
        Controls.Add(buttons);

        Load += (_, _) =>
        {
            try { split.SplitterDistance = 220; }
            catch (ArgumentException) { /* standaardverdeling bij te weinig ruimte */ }
        };

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
            CustomStatus.Assign(_statuses, _statuses[sel], key);
        }
        else
        {
            members.RemoveAll(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase));
        }
        _loading = true;
        for (int i = 0; i < _statuses.Count; i++)
            _statusList.Items[i] = $"{_statuses[i].Name} ({_statuses[i].Members.Count})";
        _loading = false;
    }

    private string? Prompt(string label, string initial = "")
    {
        using var dlg = new LegendDialog
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

internal sealed class RemarksEditDialog : LegendDialog
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
            Text = "Tekst (Enter = nieuwe regel, \u2022 voor opsommingen)",
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
        var presets = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        choice.Items.AddRange(new object[] { "Tekeningnotities (TAUW)", "KLIC-waarschuwing (TAUW)", "Tekeningnotities + KLIC", "Eigen tekst" });
        choice.SelectedIndex = 0;
        var use = new Button { Text = "Tekst overnemen", AutoSize = true };
        use.Click += (_, _) =>
        {
            _body.Text = (choice.SelectedIndex switch {
                0 => StandardTexts.DrawingRemarks,
                1 => StandardTexts.KlicDisclaimer,
                2 => StandardTexts.DrawingRemarks + "\n\n" + StandardTexts.KlicDisclaimer,
                _ => ""
            }).Replace("\n", Environment.NewLine);
            _body.Focus();
        };
        presets.Controls.Add(choice); presets.Controls.Add(use);
        Controls.Add(presets);
        // enter voor een nieuwe regel, niet voor opslaan
        buttons.Ok.Click += (_, _) => DialogResult = DialogResult.OK;

        Controls.Add(layout);
        Controls.SetChildIndex(layout, 0);
        Controls.Add(buttons);
    }

    public string RemarksTitle => _title.Text.Trim();

    // lege regels en inspringing behouden
    public string RemarksText => _body.Text.Replace("\r\n", "\n");
}

public sealed class DescriptionRow
{
    public string Sleutel { get; set; } = string.Empty;

    public string Algemeen { get; set; } = string.Empty;

    public string Specifiek { get; set; } = string.Empty;
}

internal sealed class DescriptionsDialog : LegendDialog
{
    private readonly DataGridView _grid;
    private BindingList<DescriptionRow> _rows = new();

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string?>? SaveChanges { get; set; }

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
        var state = TrackSaving(buttons, () => System.Text.Json.JsonSerializer.Serialize(new {
            rows = _rows.ToArray(), edit = DialogSaveState.GridEdit(_grid) }), () => { _grid.EndEdit(); return SaveChanges?.Invoke(); });
        _grid.CellValueChanged += (_, _) => state.Refresh();
        _grid.CurrentCellDirtyStateChanged += (_, _) => state.Refresh();
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

internal sealed class TextEditDialog : LegendDialog
{
    private readonly TextBox _algemeen;
    private readonly TextBox _specifiek;
    private readonly DescriptionEntry _default;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string?>? SaveChanges { get; set; }

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
        var state = TrackSaving(buttons, () => System.Text.Json.JsonSerializer.Serialize(new { Algemeen, Specifiek }), () => SaveChanges?.Invoke());
        _algemeen.TextChanged += (_, _) => state.Refresh();
        _specifiek.TextChanged += (_, _) => state.Refresh();
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

internal sealed class BlankEntryDialog : LegendDialog
{
    private readonly List<BlankEntry> _items;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };

    public BlankEntryDialog(IReadOnlyList<BlankEntry> initial)
    {
        _items = initial.Select(b => b.Clone()).ToList();

        Text = "NLCS Legenda \u2013 blanco regels";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(520, 360);
        MinimumSize = new Size(420, 280);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = "Een blanco regel is een lege regel die je zelf later invult. "
                 + "Standaardtekst: \u201c[blanco]\u201d.",
            Padding = new Padding(4, 2, 4, 2)
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        var add = new Button { Text = "Toevoegen", AutoSize = true };
        add.Click += (_, _) => { _items.Add(new BlankEntry()); Reload(_items.Count - 1); };
        var edit = new Button { Text = "Bewerken\u2026", AutoSize = true };
        edit.Click += (_, _) => { int i = _list.SelectedIndex; if (i < 0) return; if (EditItem(_items[i])) Reload(i); };
        var dup = new Button { Text = "Dupliceren", AutoSize = true };
        dup.Click += (_, _) => { int i = _list.SelectedIndex; if (i < 0) return; _items.Insert(i + 1, _items[i].CloneWithNewId()); Reload(i + 1); };
        var remove = new Button { Text = "Verwijderen", AutoSize = true };
        remove.Click += (_, _) => { int i = _list.SelectedIndex; if (i < 0) return; _items.RemoveAt(i); Reload(Math.Min(i, _items.Count - 1)); };
        bar.Controls.AddRange(new Control[] { add, edit, dup, remove });

        var buttons = new ButtonBar(withApply: false);
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        _list.DoubleClick += (_, _) => { int i = _list.SelectedIndex; if (i >= 0 && EditItem(_items[i])) Reload(i); };

        Controls.Add(_list);
        Controls.Add(bar);
        Controls.Add(hint);
        Controls.Add(buttons);
        Reload(_items.Count > 0 ? 0 : -1);
    }

    public List<BlankEntry> Result => _items;

    private void Reload(int select)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var b in _items)
            _list.Items.Add($"[blanco]  \u2013  \u201c{b.EffectiveText}\u201d ({b.Status.DisplayName()})");
        _list.EndUpdate();
        if (select >= 0 && select < _list.Items.Count)
            _list.SelectedIndex = select;
    }

    private bool EditItem(BlankEntry item)
    {
        using var dlg = new LegendDialog
        {
            Text = "Blanco regel",
            Font = SystemFonts.MessageBoxFont,
            ClientSize = new Size(360, 150),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ShowInTaskbar = false, ShowIcon = false, MinimizeBox = false, MaximizeBox = false
        };
        var text = new TextBox { Text = item.Text, Width = 220 };
        var status = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        status.Items.AddRange(Enum.GetNames<NlcsStatus>());
        status.SelectedItem = item.Status.ToString();

        var flow = new SettingsStack { Padding = new Padding(8) };
        Panel Row(string label, Control c) => SettingsDialog.LabeledRow(label, c);

        flow.Controls.Add(Row("Tekst:", text));
        flow.Controls.Add(Row("Status:", status));
        flow.Controls.Add(new Label { Text = "Leeg laten = \u201c[blanco]\u201d.", AutoSize = true, ForeColor = SystemColors.GrayText });

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, Width = 90 };
        var barBtn = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
        barBtn.Controls.Add(cancel); barBtn.Controls.Add(ok);
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(flow);
        dlg.Controls.Add(scroll); dlg.Controls.Add(barBtn);

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return false;
        item.Text = text.Text;
        item.Status = Enum.Parse<NlcsStatus>((string)status.SelectedItem!);
        return true;
    }
}

internal sealed class CustomLayerDialog : LegendDialog
{
    private readonly List<CustomLayerRule> _rules;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };

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
            AutoSize = true,
            Text = "Koppel een eigen (niet-NLCS) laag als bron. Dit is iets anders dan een handmatige "
                 + "regel: een eigen laag telt alleen mee als er objecten op die laag staan.",
            Padding = new Padding(4, 2, 4, 2)
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        var add = new Button { Text = "Toevoegen\u2026", AutoSize = true };
        add.Click += (_, _) => { var r = EditRule(new CustomLayerRule()); if (r is not null) { _rules.Add(r); Reload(_rules.Count - 1); } };
        var edit = new Button { Text = "Bewerken\u2026", AutoSize = true };
        edit.Click += (_, _) => { int i = _list.SelectedIndex; if (i < 0) return; var r = EditRule(_rules[i].Clone(), i); if (r is not null) { _rules[i] = r; Reload(i); } };
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

    private CustomLayerRule? EditRule(CustomLayerRule rule, int editingIndex = -1)
    {
        using var dlg = new LegendDialog
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
        var qmode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        var qmodeValues = new[] { CustomQuantityMode.Auto, CustomQuantityMode.Geen, CustomQuantityMode.Aantal, CustomQuantityMode.Lengte, CustomQuantityMode.Oppervlak };
        var qmodeLabels = new[] { "Automatisch", "Geen", "Aantal", "Lengte", "Oppervlak" };
        qmode.Items.AddRange(qmodeLabels);
        qmode.SelectedIndex = Math.Max(0, Array.IndexOf(qmodeValues, rule.QuantityMode));
        var scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        var scopeValues = new[] { CustomSourceScope.Local, CustomSourceScope.AnySource, CustomSourceScope.SpecificXref };
        var scopeLabels = new[] { "Lokaal (hoofdtekening)", "Elke bron", "Specifieke xref" };
        scope.Items.AddRange(scopeLabels);
        scope.SelectedIndex = Math.Max(0, Array.IndexOf(scopeValues, rule.Scope));
        var xref = new TextBox { Text = rule.XrefName, Width = 160 };
        var block = new TextBox { Text = rule.BlockName ?? string.Empty, Width = 160 };

        var flow = new SettingsStack { Padding = new Padding(8) };
        Panel Row(string label, Control c) => SettingsDialog.LabeledRow(label, c);

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
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(flow);
        dlg.Controls.Add(scroll); dlg.Controls.Add(bar);

        ok.Click += (_, _) =>
        {
            rule.Layer = layer.Text.Trim();
            rule.Element = element.Text.Trim();
            rule.Description = desc.Text.Trim();
            rule.Type = (NlcsDrawType)type.SelectedItem!;
            rule.Status = Enum.Parse<NlcsStatus>((string)status.SelectedItem!);
            rule.QuantityMode = qmodeValues[Math.Max(0, qmode.SelectedIndex)];
            rule.Scope = scopeValues[Math.Max(0, scope.SelectedIndex)];
            rule.XrefName = xref.Text.Trim();
            rule.BlockName = string.IsNullOrWhiteSpace(block.Text) ? null : block.Text.Trim();
            if (!LayerNaming.IsValid(rule.Layer) || string.IsNullOrWhiteSpace(rule.Layer))
            { Warn("Geef een geldige laagnaam op."); return; }
            if (string.IsNullOrWhiteSpace(rule.Element))
            { Warn("Geef een logisch element op."); return; }
            if (_rules.Where((_, index) => index != editingIndex).Any(o => SameMapping(o, rule)))
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

