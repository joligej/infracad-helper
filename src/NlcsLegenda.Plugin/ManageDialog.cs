using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class EntryCheckItem
{
    public EntryCheckItem(string key, string label, string group = "", string searchText = "")
    {
        Key = key;
        Label = label;
        Group = group;
        SearchText = searchText;
    }

    public string Key { get; }

    public string Label { get; }

    public string Group { get; }

    public string SearchText { get; }

    public override string ToString() => Label;
}

public sealed class ManualRow
{
    public string? Id { get; set; }
    public string SampleBlock { get; set; } = "";
    public double SampleScale { get; set; } = 1000;
    public string Laag { get; set; } = string.Empty;

    public string Type { get; set; } = "Lijn";

    public string Omschrijving { get; set; } = string.Empty;

    public string Status { get; set; } = "Nieuw";

    public string Patroon { get; set; } = string.Empty;

    public string Blok { get; set; } = string.Empty;

    public string Hoofdgroep { get; set; } = "HM";
}

internal sealed class LegendManageDialog : LegendDialog
{
    private static readonly string[] TypeNames = { "Lijn", "Vlak", "Arcering", "Vulling", "Symbool" };
    private static readonly string[] StatusNames = { "Nieuw", "Bestaand", "Vervallen", "Tijdelijk", "Revisie", "Overig" };

    private readonly TriStateTree _tree;
    private readonly TextBox _filter;
    private readonly DataGridView _grid;
    private BindingList<ManualRow> _rows = new();
    private readonly IReadOnlyList<EntryCheckItem> _allEntries;
    private readonly DialogSaveState _saveState;

    public bool PickObjectsRequested { get; private set; }

    public event EventHandler? ApplyRequested;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string?>? SaveChanges { get; set; }

    public LegendManageDialog(IReadOnlyList<EntryCheckItem> allEntries, LegendSettings initial,
        IReadOnlyList<ManualEntry>? layerChoices = null, bool addLayer = false, bool allowPicking = false)
    {
        _allEntries = allEntries;

        Text = "NLCS Legenda \u2013 samenstellen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(720, 640);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var drawingTab = new TabPage("Tekening");
        var customTab = new TabPage("Eigen regels");
        tabs.TabPages.Add(drawingTab);
        tabs.TabPages.Add(customTab);

        var topGroup = new GroupBox { Text = "Regels in de legenda", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _tree = new TriStateTree { Dock = DockStyle.Fill };
        _filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Zoek woorden in omschrijving of laagnaam; * voor tussenliggende tekens…" };
        _filter.TextChanged += (_, _) => _tree.SetModel(_tree.Model, _filter.Text);
        var selectionBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        foreach (var (caption, included) in new[] { ("Resultaten opnemen", true), ("Resultaten uitsluiten", false) })
        {
            var button = new Button { Text = caption, AutoSize = true };
            button.Click += (_, _) =>
            {
                foreach (var group in _tree.Model.Filter(_filter.Text))
                    foreach (var leaf in _tree.Model.VisibleLeaves(group, _filter.Text)) leaf.Included = included;
                _tree.SetModel(_tree.Model, _filter.Text);
            };
            selectionBar.Controls.Add(button);
        }
        topGroup.Controls.Add(_tree);
        topGroup.Controls.Add(selectionBar);
        topGroup.Controls.Add(_filter);
        drawingTab.Controls.Add(topGroup);

        var bottomGroup = new GroupBox { Text = "Eigen regels", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _grid = BuildGrid();
        var removeBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var remove = new Button { Text = "Verwijderen", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        remove.Click += (_, _) => RemoveSelectedRows();
        removeBar.Controls.Add(remove);
        var add = new Button { Text = "Laag toevoegen…", AutoSize = true, Margin = new Padding(0, 4, 8, 0), Enabled = layerChoices is { Count: > 0 } };
        add.Click += (_, _) =>
        {
            tabs.SelectedTab = customTab;
            using var picker = new LayerChoiceDialog(layerChoices!);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is not { } selected) return;
            _grid.EndEdit();
            _rows.Add(ToRow(selected));
            _grid.CurrentCell = _grid.Rows[_rows.Count - 1].Cells[2];
        };
        removeBar.Controls.Add(add);
        if (addLayer) Shown += (_, _) => BeginInvoke(new Action(add.PerformClick));
        bottomGroup.Controls.Add(_grid);
        bottomGroup.Controls.Add(removeBar);
        customTab.Controls.Add(bottomGroup);

        var buttons = new ButtonBar(withApply: true);
        if (allowPicking) buttons.AddExtra("In tekening uitsluiten…").Click += (_, _) =>
        {
            PickObjectsRequested = true;
            DialogResult = DialogResult.OK;
        };
        buttons.Apply!.Click += (_, _) =>
        {
            if (ValidationError() is { } err)
            {
                Warn(err);
                return;
            }
            if (Save()) ApplyRequested?.Invoke(this, EventArgs.Empty);
        };
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK)
                return;
            if (ValidationError() is { } err)
            {
                Warn(err);
                e.Cancel = true;
                return;
            }
            e.Cancel = !Save();
        };

        Controls.Add(tabs);
        Controls.Add(buttons);

        LoadFrom(initial);
        _saveState = Own(new DialogSaveState(buttons, () => System.Text.Json.JsonSerializer.Serialize(new {
            excluded = ExcludedKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), rows = _rows.ToArray(), edit = DialogSaveState.GridEdit(_grid) })));
        _tree.SelectionChanged += (_, _) => _saveState.Refresh();
        _rows.ListChanged += (_, _) => _saveState.Refresh();
        _grid.CellValueChanged += (_, _) => _saveState.Refresh();
        _grid.CurrentCellDirtyStateChanged += (_, _) => _saveState.Refresh();
    }

    private bool Save()
    {
        try
        {
            if (!_saveState.HasChanges) return true;
            if (SaveChanges?.Invoke() is not { } error) { _saveState.Saved(); return true; }
            Warn(error);
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex)) { Warn(ex.Message); }
        return false;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "NLCS Legenda", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private string? ValidationError()
    {
        _grid.EndEdit();
        foreach (var row in _rows)
        {
            bool hasLayer = !string.IsNullOrWhiteSpace(row.Laag);
            bool hasText = !string.IsNullOrWhiteSpace(row.Omschrijving);
            if (!hasLayer && !hasText)
                continue;
            if (!hasLayer || !hasText)
                return "Vul bij elke eigen regel zowel een laag als een omschrijving in.";
            if (row.Type == "Symbool" && string.IsNullOrWhiteSpace(row.Blok))
                return "Vul bij elke eigen symboolregel een bloknaam in.";
            if (!LayerNaming.IsValidReference(row.Laag))
                return $"\"{row.Laag.Trim()}\" is geen geldige laagnaam.";
        }
        return null;
    }

    public HashSet<string> ExcludedKeys => _tree.Model.ExcludedKeys();

    public void ApplyTo(LegendSettings settings)
    {
        var excluded = ExcludedKeys;
        foreach (var item in _allEntries)
        {
            bool before = settings.IsEntryExcluded(item.Key), after = excluded.Contains(item.Key);
            if (before == after) continue;
            var source = settings.SourceRows.LastOrDefault(row => row.SelectionKey == item.Key);
            if (source is not null) settings.RowChoices.Add(new RowChoice { Sources = new(source.Sources), Scope = ChoiceScope.Variant, Excluded = after });
        }
        settings.ExcludedEntries = excluded;
        settings.ManualEntries = ManualEntries;
    }

    public List<ManualEntry> ManualEntries
    {
        get
        {
            _grid.EndEdit();
            var list = new List<ManualEntry>();
            foreach (var row in _rows.Where(row => !string.IsNullOrWhiteSpace(row.Laag) && !string.IsNullOrWhiteSpace(row.Omschrijving)))
            {
                list.Add(new ManualEntry
                {
                    Id = row.Id, SampleBlock = row.Blok == row.SampleBlock && row.Type == "Symbool" ? row.SampleBlock : "", SampleScale = row.SampleScale,
                    Layer = row.Laag.Trim(),
                    Type = ToDrawType(row.Type),
                    Description = row.Omschrijving.Trim(),
                    Status = ToStatus(row.Status),
                    Hoofdgroep = row.Hoofdgroep,
                    SymbolBlock = string.IsNullOrWhiteSpace(row.Blok) ? null : row.Blok.Trim(),
                    HatchPattern = string.IsNullOrWhiteSpace(row.Patroon) ? null : row.Patroon.Trim()
                });
            }
            return list;
        }
    }

    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        { DataPropertyName = nameof(ManualRow.Laag), HeaderText = "Laag", FillWeight = 28 });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        { DataPropertyName = nameof(ManualRow.Type), HeaderText = "Type", FillWeight = 16, DataSource = TypeNames.Clone(), FlatStyle = FlatStyle.Flat });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        { DataPropertyName = nameof(ManualRow.Omschrijving), HeaderText = "Omschrijving", FillWeight = 30 });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        { DataPropertyName = nameof(ManualRow.Status), HeaderText = "Status", FillWeight = 16, DataSource = StatusNames.Clone(), FlatStyle = FlatStyle.Flat });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        { DataPropertyName = nameof(ManualRow.Patroon), HeaderText = "Patroon", FillWeight = 14 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        { DataPropertyName = nameof(ManualRow.Blok), HeaderText = "Blok", FillWeight = 18 });
        foreach (DataGridViewColumn column in grid.Columns)
            column.MinimumWidth = column.DataPropertyName == nameof(ManualRow.Omschrijving) ? 150 : 90;
        grid.DataError += (_, e) => e.ThrowException = false;
        return grid;
    }

    private void LoadFrom(LegendSettings settings)
    {
        var model = CompositionTree.Build(
            _allEntries.Select(i => (i.Key, i.Label, i.Group, i.SearchText)), settings.ExcludedEntries);
        _tree.SetModel(model, _filter.Text);

        _rows = new BindingList<ManualRow>(settings.ManualEntries.Select(ToRow).ToList());
        _grid.DataSource = _rows;
    }

    private static ManualRow ToRow(ManualEntry m) => new()
        {
            Id = m.Id, SampleBlock = m.SampleBlock, SampleScale = m.SampleScale,
            Laag = m.Layer,
            Type = FromDrawType(m.Type),
            Omschrijving = m.Description,
            Status = m.Status.DisplayName(),
            Patroon = m.HatchPattern ?? string.Empty,
            Blok = m.SymbolBlock ?? string.Empty,
            Hoofdgroep = m.Hoofdgroep
        };

    private void RemoveSelectedRows()
    {
        var toRemove = _grid.SelectedCells.Cast<DataGridViewCell>()
            .Select(c => c.RowIndex).Distinct()
            .Where(i => i >= 0 && i < _rows.Count)
            .OrderByDescending(i => i).ToList();
        foreach (var i in toRemove)
            _rows.RemoveAt(i);
    }

    private static NlcsDrawType ToDrawType(string name) => name switch
    {
        "Vlak" => NlcsDrawType.Vlak,
        "Arcering" => NlcsDrawType.Arcering,
        "Vulling" => NlcsDrawType.Vlakvulling,
        "Symbool" => NlcsDrawType.Symbool,
        _ => NlcsDrawType.Geometrie
    };

    private static string FromDrawType(NlcsDrawType type) => type switch
    {
        NlcsDrawType.Vlak => "Vlak",
        NlcsDrawType.Arcering => "Arcering",
        NlcsDrawType.Vlakvulling => "Vulling",
        NlcsDrawType.Symbool => "Symbool",
        _ => "Lijn"
    };

    private static NlcsStatus ToStatus(string name) => name switch
    {
        "Bestaand" => NlcsStatus.Bestaand,
        "Vervallen" => NlcsStatus.Vervallen,
        "Tijdelijk" => NlcsStatus.Tijdelijk,
        "Revisie" => NlcsStatus.Revisie,
        "Overig" => NlcsStatus.Overig,
        _ => NlcsStatus.Nieuw
    };
}

internal sealed class LayerChoiceDialog : LegendDialog
{
    private readonly IReadOnlyList<ManualEntry> _choices;
    private readonly DataGridView _list;
    public ManualEntry? Selected => _list.CurrentRow?.Tag is ManualEntry entry ? entry.Clone() : null;

    public LayerChoiceDialog(IReadOnlyList<ManualEntry> choices)
    {
        _choices = choices;
        Text = "Laag voor eigen regel";
        ClientSize = new Size(760, 480);
        MinimumSize = new Size(520, 340);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Zoek in bron, laag, omschrijving of voorbeeld…", AccessibleName = "Laag zoeken" };
        _list = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        _list.Columns.Add("source", "Bron");
        _list.Columns.Add("layer", "Laag");
        _list.Columns.Add("description", "Omschrijving");
        _list.Columns.Add("example", "Blok / patroon");
        _list.Columns[0].FillWeight = 25;
        _list.Columns[1].FillWeight = 30;
        _list.Columns[2].FillWeight = 25;
        _list.Columns[3].FillWeight = 20;
        foreach (DataGridViewColumn column in _list.Columns) column.MinimumWidth = 100;
        var buttons = new ButtonBar(false);
        buttons.Ok.Text = "Toevoegen";
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;
        void Filter()
        {
            _list.Rows.Clear();
            foreach (var choice in _choices.Where(c => LayerSearch.Matches(c.Layer + " " + c.Description + " " + c.SymbolBlock + " " + c.HatchPattern, search.Text)))
                _list.Rows[_list.Rows.Add(choice.Layer.Contains('|') ? LayerNaming.SourceName(choice.Layer) : "Deze tekening", LayerNaming.LocalName(choice.Layer), choice.Description, LayerNaming.LocalName(choice.SymbolBlock ?? choice.HatchPattern))].Tag = choice;
            buttons.Ok.Enabled = _list.Rows.Count > 0;
        }
        search.TextChanged += (_, _) => Filter();
        _list.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) { DialogResult = DialogResult.OK; Close(); } };
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && Selected is null) e.Cancel = true; };
        Controls.Add(_list);
        Controls.Add(search);
        Controls.Add(buttons);
        Filter();
    }
}
