using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal enum ConfigScope
{
    Global,

    Drawing
}

internal sealed class ScopeBar : FlowLayoutPanel
{
    private readonly ComboBox _scope;

    public event EventHandler? ScopeChanged;

    public ScopeBar(ConfigScope initial)
    {
        Dock = DockStyle.Top;
        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = false;
        AutoSize = true;
        Padding = new Padding(8, 6, 8, 6);

        Controls.Add(new Label { Text = "Bewaren in:", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
        _scope = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            Margin = new Padding(0, 2, 0, 2)
        };
        _scope.Items.Add("Alle tekeningen");
        _scope.Items.Add("Alleen deze tekening");
        _scope.SelectedIndex = initial == ConfigScope.Drawing ? 1 : 0;
        _scope.SelectedIndexChanged += (_, _) => ScopeChanged?.Invoke(this, EventArgs.Empty);
        Controls.Add(_scope);
    }

    public ConfigScope Scope => _scope.SelectedIndex == 1 ? ConfigScope.Drawing : ConfigScope.Global;
}

internal sealed class ButtonBar : FlowLayoutPanel
{
    public Button Ok { get; }
    public Button Cancel { get; }
    public Button? Apply { get; }

    public ButtonBar(bool withApply)
    {
        Dock = DockStyle.Bottom;
        FlowDirection = FlowDirection.RightToLeft;
        WrapContents = false;
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

    private static Button MakeButton(string text, DialogResult result) => new()
    {
        Text = text,
        DialogResult = result,
        Size = new Size(92, 26),
        Margin = new Padding(6, 0, 0, 0),
        UseVisualStyleBackColor = true
    };
}

internal sealed class SettingsDialog : Form
{
    private readonly PropertyGrid _grid;
    private readonly ScopeBar _scopeBar;
    private readonly Func<ConfigScope, LegendSettings> _loader;
    private LegendSettings _settings;

    public event EventHandler? ApplyRequested;

    public SettingsDialog(ConfigScope initialScope, Func<ConfigScope, LegendSettings> loader)
    {
        _loader = loader;
        _settings = loader(initialScope);

        Text = "NLCS Legenda \u2013 instellingen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(520, 640);
        MinimumSize = new Size(460, 460);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        _grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = _settings,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = true
        };

        _scopeBar = new ScopeBar(initialScope);
        _scopeBar.ScopeChanged += (_, _) =>
        {
            _settings = _loader(_scopeBar.Scope);
            _grid.SelectedObject = _settings;
            ScrollToTop();
        };

        var buttons = new ButtonBar(withApply: true);
        buttons.Apply!.Click += (_, _) =>
        {
            if (ValidateScale())
                ApplyRequested?.Invoke(this, EventArgs.Empty);
        };
        var reset = buttons.AddExtra("Standaardwaarden");
        reset.Click += (_, _) => { ResetToDefaults(_settings); _grid.Refresh(); ScrollToTop(); };
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        FormClosing += OnFormClosing;

        Controls.Add(_grid);
        Controls.Add(buttons);
        Controls.Add(_scopeBar);
    }

    public LegendSettings Settings => _settings;

    public ConfigScope Scope => _scopeBar.Scope;

    private void ScrollToTop()
    {
        var item = _grid.SelectedGridItem;
        if (item is null)
            return;
        while (item.Parent is not null)
            item = item.Parent;
        foreach (GridItem child in item.GridItems)
        {
            _grid.SelectedGridItem = child;
            break;
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

public sealed class DescriptionRow
{
    public string Sleutel { get; set; } = string.Empty;

    public string Algemeen { get; set; } = string.Empty;

    public string Specifiek { get; set; } = string.Empty;
}

internal sealed class DescriptionsDialog : Form
{
    private readonly DataGridView _grid;
    private readonly ScopeBar _scopeBar;
    private readonly Func<ConfigScope, DescriptionCatalog> _loader;
    private BindingList<DescriptionRow> _rows = new();

    public event EventHandler? ApplyRequested;

    public DescriptionsDialog(ConfigScope initialScope, Func<ConfigScope, DescriptionCatalog> loader)
    {
        _loader = loader;

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

        _scopeBar = new ScopeBar(initialScope);
        _scopeBar.ScopeChanged += (_, _) => LoadRows(_scopeBar.Scope);

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
        Controls.Add(_scopeBar);

        LoadRows(initialScope);
    }

    public ConfigScope Scope => _scopeBar.Scope;

    private void LoadRows(ConfigScope scope) => SetRows(_loader(scope));

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
    private readonly ScopeBar _scopeBar;
    private readonly Func<ConfigScope, DescriptionEntry> _loader;
    private readonly DescriptionEntry _default;

    public event EventHandler? ApplyRequested;

    public TextEditDialog(
        string elementKey, ConfigScope initialScope,
        Func<ConfigScope, DescriptionEntry> loader, DescriptionEntry defaults)
    {
        _loader = loader;
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

        _scopeBar = new ScopeBar(initialScope);
        _scopeBar.ScopeChanged += (_, _) => LoadFromScope(_scopeBar.Scope);

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
        Controls.Add(_scopeBar);

        LoadFromScope(initialScope);
    }

    public string Algemeen => _algemeen.Text.Trim();

    public string Specifiek => _specifiek.Text.Trim();

    public ConfigScope Scope => _scopeBar.Scope;

    private void LoadFromScope(ConfigScope scope)
    {
        var entry = _loader(scope);
        _algemeen.Text = entry.Algemeen ?? string.Empty;
        _specifiek.Text = entry.Specifiek;
    }

    private static TextBox MakeTextBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        WordWrap = true,
        ScrollBars = ScrollBars.Vertical
    };
}
