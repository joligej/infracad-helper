using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class EntryCheckItem
{
    public EntryCheckItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

    public override string ToString() => Label;
}

public sealed class ManualRow
{
    public string Laag { get; set; } = string.Empty;

    public string Type { get; set; } = "Lijn";

    public string Omschrijving { get; set; } = string.Empty;

    public string Status { get; set; } = "Nieuw";

    public string Patroon { get; set; } = string.Empty;
}

internal sealed class LegendManageDialog : Form
{
    private static readonly string[] TypeNames = { "Lijn", "Vlak", "Arcering", "Vulling", "Symbool" };
    private static readonly string[] StatusNames = { "Nieuw", "Bestaand", "Vervallen", "Tijdelijk", "Revisie" };

    private readonly CheckedListBox _entries;
    private readonly DataGridView _grid;
    private BindingList<ManualRow> _rows = new();
    private readonly ScopeBar _scopeBar;
    private readonly IReadOnlyList<EntryCheckItem> _allEntries;
    private readonly Func<ConfigScope, LegendSettings> _loader;

    public event EventHandler? ApplyRequested;

    public LegendManageDialog(
        IReadOnlyList<EntryCheckItem> allEntries, ConfigScope initialScope,
        Func<ConfigScope, LegendSettings> loader)
    {
        _allEntries = allEntries;
        _loader = loader;

        Text = "NLCS Legenda \u2013 samenstellen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(720, 640);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 120,
            Panel2MinSize = 120
        };
        // De verdeling pas zetten als de werkelijke hoogte bekend is; in de constructor
        // is het control nog klein, waardoor SplitterDistance zou worden bijgeknepen.
        Load += (_, _) =>
        {
            try
            {
                int wanted = (int)(split.Height * 0.5);
                split.SplitterDistance = Math.Min(
                    Math.Max(wanted, split.Panel1MinSize),
                    Math.Max(split.Height - split.Panel2MinSize - split.SplitterWidth, split.Panel1MinSize));
            }
            catch { /* bij een extreem klein venster de standaardverdeling houden */ }
        };

        var topGroup = new GroupBox { Text = "NLCS-regels (vink uit wat je niet in de legenda wilt)", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _entries = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        foreach (var item in _allEntries)
            _entries.Items.Add(item, true);
        topGroup.Controls.Add(_entries);
        split.Panel1.Controls.Add(topGroup);

        var bottomGroup = new GroupBox { Text = "Eigen regels", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _grid = BuildGrid();
        var removeBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var remove = new Button { Text = "Verwijderen", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        remove.Click += (_, _) => RemoveSelectedRows();
        removeBar.Controls.Add(remove);
        bottomGroup.Controls.Add(_grid);
        bottomGroup.Controls.Add(removeBar);
        split.Panel2.Controls.Add(bottomGroup);

        _scopeBar = new ScopeBar(initialScope);
        _scopeBar.ScopeChanged += (_, _) => LoadFromScope(_scopeBar.Scope);

        var buttons = new ButtonBar(withApply: true);
        buttons.Apply!.Click += (_, _) =>
        {
            if (ValidationError() is { } err)
            {
                Warn(err);
                return;
            }
            ApplyRequested?.Invoke(this, EventArgs.Empty);
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
            }
        };

        Controls.Add(split);
        Controls.Add(buttons);
        Controls.Add(_scopeBar);

        LoadFromScope(initialScope);
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
            if (!LayerNaming.IsValid(row.Laag))
                return $"\"{row.Laag.Trim()}\" is geen geldige laagnaam.";
        }
        return null;
    }

    public ConfigScope Scope => _scopeBar.Scope;

    public HashSet<string> ExcludedKeys
    {
        get
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _entries.Items.Count; i++)
                if (!_entries.GetItemChecked(i) && _entries.Items[i] is EntryCheckItem item)
                    set.Add(item.Key);
            return set;
        }
    }

    public List<ManualEntry> ManualEntries
    {
        get
        {
            _grid.EndEdit();
            var list = new List<ManualEntry>();
            foreach (var row in _rows)
            {
                if (string.IsNullOrWhiteSpace(row.Laag) || string.IsNullOrWhiteSpace(row.Omschrijving))
                    continue;
                list.Add(new ManualEntry
                {
                    Layer = row.Laag.Trim(),
                    Type = ToDrawType(row.Type),
                    Description = row.Omschrijving.Trim(),
                    Status = ToStatus(row.Status),
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
        // Voorkom rode fouticoontjes bij lege combo-waarden in de invoerregel.
        grid.DataError += (_, e) => e.ThrowException = false;
        return grid;
    }

    private void LoadFromScope(ConfigScope scope)
    {
        var settings = _loader(scope);

        for (int i = 0; i < _entries.Items.Count; i++)
            if (_entries.Items[i] is EntryCheckItem item)
                _entries.SetItemChecked(i, !settings.ExcludedEntries.Contains(item.Key));

        _rows = new BindingList<ManualRow>(settings.ManualEntries.Select(m => new ManualRow
        {
            Laag = m.Layer,
            Type = FromDrawType(m.Type),
            Omschrijving = m.Description,
            Status = m.Status.DisplayName(),
            Patroon = m.HatchPattern ?? string.Empty
        }).ToList());
        _grid.DataSource = _rows;
    }

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
        _ => NlcsStatus.Nieuw
    };
}
