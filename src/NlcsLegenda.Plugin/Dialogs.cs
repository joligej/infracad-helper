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
    private readonly List<PropertyGrid> _grids = new();
    private readonly List<Action> _resync = new();
    private readonly LegendSettings _settings;
    private readonly IReadOnlyList<EntryCheckItem>? _composition;

    public event EventHandler? ApplyRequested;

    // Quantity-properties krijgen een eigen tabblad; ze zitten qua categorie verspreid.
    private static readonly HashSet<string> QuantityProps = new(StringComparer.Ordinal)
    {
        "IncludeQuantities", "UnitCount", "UnitLength", "UnitArea", "QuantityDecimals",
        "IncludeTotalsRow", "TotalsPrefix", "QuantityColumnWidthMm"
    };

    // Bewerkt precies één instellingenobject (een werkkopie). De aanroeper bepaalt de scope
    // (globale standaard of één specifieke legenda) en past het resultaat toe. Geef je de
    // geanalyseerde entries mee, dan kan de gebruiker vanuit dit venster ook samenstellen
    // (uitsluitingen + eigen regels) via dezelfde boom-editor als het losse commando.
    public SettingsDialog(LegendSettings settings, string contextLabel,
        IReadOnlyList<EntryCheckItem>? composition = null)
    {
        _composition = composition;
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
        tabs.TabPages.Add(Tab("Opmaak", p => InCategory(p, "Weergave", "Kolommen", "Koppen")));
        tabs.TabPages.Add(Tab("Teksten", p => InCategory(p, "Teksten", "Opmerkingen") && !QuantityProps.Contains(p.Name)));
        tabs.TabPages.Add(Tab("Hoeveelheden", p => QuantityProps.Contains(p.Name)));
        tabs.TabPages.Add(Tab("Schaalbalk / Extra", p => InCategory(p, "Schaalbalk", "Maatvoering (mm)")));

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

    private static bool InCategory(PropertyDescriptor p, params string[] categories)
        => categories.Contains(p.Category, StringComparer.Ordinal);

    private TabPage Tab(string title, Func<PropertyDescriptor, bool> include)
    {
        var grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = new FilteredSettings(_settings, include),
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = true
        };
        _grids.Add(grid);
        var page = new TabPage(title) { Padding = new Padding(4) };
        page.Controls.Add(grid);
        return page;
    }

    // Een gebonden aankruisvakje op de werkkopie: wijzigen werkt direct op _settings, en bij een
    // reset van buitenaf wordt het vakje opnieuw gesynchroniseerd (_resync).
    private CheckBox BoundCheck(string text, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { Text = text, Checked = get(), AutoSize = true, Margin = new Padding(3, 3, 12, 3) };
        cb.CheckedChanged += (_, _) => set(cb.Checked);
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

    // Tab Algemeen met normale controls i.p.v. een PropertyGrid.
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
            BoundCheck("Kader om de legenda", () => _settings.DrawBorder, v => _settings.DrawBorder = v),
            BoundCheck("Schaalbalk tonen", () => _settings.IncludeScaleBar, v => _settings.IncludeScaleBar = v),
            BoundCheck("Hoeveelheden tonen", () => _settings.IncludeQuantities, v => _settings.IncludeQuantities = v)));
        page.Controls.Add(root);
        return page;
    }

    // Tab Inhoud met normale controls; subdialogs voor samenstellen en eigen statussen.
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
        statussen.Click += (_, _) => EditCustomStatusNames();
        actions.Controls.Add(statussen);
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

    // Eenvoudige editor voor de namen van eigen statussen op de werkkopie. Leden toewijzen blijft
    // via NLCSLEGENDASTATUS (dat de tekening nodig heeft); dit venster beheert alleen de namen.
    private void EditCustomStatusNames()
    {
        using var dlg = new StringListEditDialog("Eigen statussen",
            _settings.CustomStatuses.Select(c => c.Name));
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        var byName = _settings.CustomStatuses.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var result = new List<CustomStatus>();
        foreach (var name in dlg.Values)
            result.Add(byName.TryGetValue(name, out var existing) ? existing : new CustomStatus { Name = name });
        _settings.CustomStatuses = result;
    }

    private void RefreshGrids()
    {
        foreach (var grid in _grids)
            grid.Refresh();
        // Normale controls op Algemeen/Inhoud opnieuw gelijkzetten met de werkkopie (na reset).
        foreach (var sync in _resync)
            sync();
    }

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

    // Samenstellen vanuit het instellingenvenster: dezelfde boom-editor als het losse commando,
    // maar op dezelfde werkkopie. Annuleren laat de werkkopie ongemoeid (Cancel = geen mutatie).
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

// Eenvoudige lijsteditor voor namen (bijv. eigen statussen): toevoegen, hernoemen, verwijderen.
// Geeft bij OK de bewerkte lijst terug; de aanroeper mapt die op de werkkopie.
internal sealed class StringListEditDialog : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public StringListEditDialog(string title, IEnumerable<string> values)
    {
        Text = title;
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(360, 320);
        MinimumSize = new Size(320, 240);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                _list.Items.Add(v.Trim());

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        var add = new Button { Text = "Toevoegen", AutoSize = true };
        add.Click += (_, _) => { var n = Prompt("Nieuwe naam:"); if (!string.IsNullOrWhiteSpace(n)) _list.Items.Add(n.Trim()); };
        var rename = new Button { Text = "Hernoemen", AutoSize = true };
        rename.Click += (_, _) =>
        {
            if (_list.SelectedIndex < 0) return;
            var n = Prompt("Nieuwe naam:", (string)_list.SelectedItem!);
            if (!string.IsNullOrWhiteSpace(n)) _list.Items[_list.SelectedIndex] = n.Trim();
        };
        var remove = new Button { Text = "Verwijderen", AutoSize = true };
        remove.Click += (_, _) => { if (_list.SelectedIndex >= 0) _list.Items.RemoveAt(_list.SelectedIndex); };
        bar.Controls.AddRange(new Control[] { add, rename, remove });

        var buttons = new ButtonBar(withApply: false);
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        Controls.Add(_list);
        Controls.Add(bar);
        Controls.Add(buttons);
    }

    public IReadOnlyList<string> Values => _list.Items.Cast<string>().ToList();

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
