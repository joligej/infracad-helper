using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class OverviewLegend
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required LegendSettings Settings { get; init; }
    public required IReadOnlyList<LegendEntry> Entries { get; init; }
    public bool Changed { get; set; }
    public override string ToString() => Name;
}

internal sealed class OverviewDialog : LegendDialog
{
    private readonly IReadOnlyList<OverviewLegend> _legends;
    private readonly Func<IReadOnlyList<OverviewLegend>, string?> _save;
    private readonly ComboBox _legend = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly ComboBox _sort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
    };
    private readonly Label _summary = new() { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8) };
    private readonly FlowLayoutPanel _moves = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
    private List<LegendEntry> _entries = new();
    private bool _loading;
    private OverviewLegend Current => _legends[_legend.SelectedIndex];

    public OverviewDialog(IReadOnlyList<OverviewLegend> legends, Func<IReadOnlyList<OverviewLegend>, string?> save)
    {
        _legends = legends;
        _save = save;
        Text = "NLCS Legenda – overzicht";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(960, 620);
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        top.Controls.Add(new Label { Text = "Legenda:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        top.Controls.Add(_legend);
        top.Controls.Add(new Label { Text = "Volgorde:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        top.Controls.Add(_sort);
        _sort.Items.AddRange(Enum.GetNames<LegendSortMode>());
        foreach (var (name, width) in new[] { ("Status", 100), ("Omschrijving", 260), ("Netbeheerder", 180), ("Soort", 100), ("Hoeveelheid", 100), ("Bronlagen", 230), ("Samengevoegd", 170) })
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = width, MinimumWidth = Math.Min(width, 100), SortMode = DataGridViewColumnSortMode.NotSortable, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
        AddMoveEntry("Naar boven", -1);
        AddMoveEntry("Naar beneden", 1);
        AddMoveEntry("Naar begin", int.MinValue);
        AddMoveEntry("Naar einde", int.MaxValue);
        _moves.Controls.Add(new Label { Text = "Ook met Alt+↑ / Alt+↓. Kies Handmatig om te verplaatsen.", AutoSize = true, Padding = new Padding(6) });
        _grid.KeyDown += (_, e) =>
        {
            if (!e.Alt || e.KeyCode is not (Keys.Up or Keys.Down)) return;
            MoveEntry(e.KeyCode == Keys.Up ? -1 : 1);
            e.Handled = e.SuppressKeyPress = true;
        };
        var buttons = new ButtonBar(withApply: true);
        buttons.Apply!.Click += (_, _) => SaveChanges();
        AcceptButton = buttons.Ok;
        CancelButton = buttons.Cancel;
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && !SaveChanges()) e.Cancel = true; };
        Controls.Add(_grid);
        Controls.Add(_summary);
        Controls.Add(top);
        Controls.Add(_moves);
        Controls.Add(buttons);
        _legend.SelectedIndexChanged += (_, _) => LoadLegend();
        _sort.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || Current.Id.Length == 0) return;
            Current.Settings.SortMode = Enum.Parse<LegendSortMode>((string)_sort.SelectedItem!);
            Current.Changed = true;
            LoadEntries();
        };
        foreach (var legend in legends) _legend.Items.Add(legend);
        _legend.SelectedIndex = 0;
    }

    private void AddMoveEntry(string text, int offset)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        button.Click += (_, _) => MoveEntry(offset);
        _moves.Controls.Add(button);
    }

    private void LoadLegend()
    {
        _loading = true;
        _sort.SelectedItem = Current.Settings.SortMode.ToString();
        _sort.Enabled = Current.Id.Length > 0;
        _loading = false;
        LoadEntries();
    }

    private void LoadEntries()
    {
        _entries = LegendGrouping.Sort(Current.Entries, Current.Settings);
        _moves.Enabled = Current.Id.Length > 0 && Current.Settings.SortMode == LegendSortMode.Handmatig;
        _summary.Text = Current.Id.Length == 0
            ? $"{_entries.Count} regels in de tekening. Plaats eerst een legenda om de volgorde op te slaan."
            : $"{_entries.Count} regels · schaal 1:{Current.Settings.Scale:g} · wijzigingen gelden alleen voor deze legenda.";
        FillGrid(0);
    }

    private void FillGrid(int selected)
    {
        _grid.Rows.Clear();
        foreach (var entry in _entries)
            _grid.Rows.Add(entry.CustomStatusName ?? Current.Settings.StatusLabel(entry.Status), entry.Description, string.Join(", ", entry.Netbeheerders),
                string.Join(", ", entry.LayersByType.Keys), entry.QuantityText(Current.Settings.QuantityDecimals, Current.Settings.UnitArea, Current.Settings.UnitLength, Current.Settings.UnitCount),
                string.Join(Environment.NewLine, entry.SourceLayers.Select(s => s.SourceLayerName).Distinct()), string.Join(", ", entry.MergedMembers));
        if (_grid.Rows.Count > 0) _grid.CurrentCell = _grid.Rows[Math.Clamp(selected, 0, _grid.Rows.Count - 1)].Cells[1];
    }

    private void MoveEntry(int offset)
    {
        if (!_moves.Enabled || _grid.CurrentRow is null) return;
        int from = _grid.CurrentRow.Index;
        int to = offset == int.MinValue ? 0 : offset == int.MaxValue ? _entries.Count - 1 : Math.Clamp(from + offset, 0, _entries.Count - 1);
        if (to == from) return;
        var entry = _entries[from];
        _entries.RemoveAt(from);
        _entries.Insert(to, entry);
        Current.Settings.ManualEntryOrder = LegendOrder.Remember(Current.Settings.ManualEntryOrder, _entries);
        Current.Changed = true;
        FillGrid(to);
    }

    private bool SaveChanges()
    {
        var changed = _legends.Where(l => l.Changed).ToList();
        if (changed.Count == 0) return true;
        var error = _save(changed);
        if (error is not null)
        {
            MessageBox.Show(this, error, "Overzicht", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        foreach (var legend in changed) legend.Changed = false;
        return true;
    }
}
