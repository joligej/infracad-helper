using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class OverviewLegend
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required LegendSettings Settings { get; init; }
    public required IReadOnlyList<LegendEntry> Entries { get; set; }
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
    private List<LegendEntry> _visible = new();
    private readonly Func<OverviewLegend, IReadOnlyList<LegendEntry>>? _analyze;
    private readonly TextBox _filter = new() { Width = 210, PlaceholderText = "Zoek waarde of tekst" };
    private readonly ComboBox _field = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _issuesOnly = new() { Text = "Alleen bronmeldingen", AutoSize = true };
    private bool _loading;
    private OverviewLegend Current => _legends[_legend.SelectedIndex];

    public OverviewDialog(IReadOnlyList<OverviewLegend> legends, Func<IReadOnlyList<OverviewLegend>, string?> save,
        Func<OverviewLegend, IReadOnlyList<LegendEntry>>? analyze = null)
    {
        _legends = legends;
        _save = save;
        _analyze = analyze;
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
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        void ActionButton(string title, Action action)
        {
            var button = new Button { Text = title, AutoSize = true, Padding = new Padding(4) };
            button.Click += (_, _) => action(); tools.Controls.Add(button);
        }
        ActionButton("KLIC-instellingen…", EditKlic);
        ActionButton("KLIC-details…", ShowKlicDetails);
        ActionButton("Exporteer lijst…", ExportVisible);
        _field.Items.Add("Alle zichtbare velden");
        _field.Items.AddRange(KlicFields.All.Cast<object>().ToArray());
        _field.SelectedIndex = 0;
        tools.Controls.Add(_field); tools.Controls.Add(_filter); tools.Controls.Add(_issuesOnly);
        _filter.TextChanged += (_, _) => { if (!_loading) FillGrid(0); };
        _field.SelectedIndexChanged += (_, _) => { if (!_loading) FillGrid(0); };
        _issuesOnly.CheckedChanged += (_, _) => { if (!_loading) FillGrid(0); };
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowKlicDetails(); };
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
        SizeChanged += (_, _) => _summary.MaximumSize = new Size(Math.Max(100, ClientSize.Width), 0);
        Controls.Add(_grid);
        Controls.Add(_summary);
        Controls.Add(tools);
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
        ConfigureColumns();
        LoadEntries();
    }

    private void ConfigureColumns()
    {
        _grid.Columns.Clear();
        foreach (var name in new[] { "Status", "Omschrijving", "Netbeheerder", "Soort", "Hoeveelheid", "Bronlagen", "Samengevoegd" }
            .Concat(Current.Settings.Klic.Columns.Select(k => KlicFields.Find(k)!.Label)).Concat(new[] { "Broncontrole" }))
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, MinimumWidth = 100, FillWeight = name == "Omschrijving" ? 240 : 120,
                SortMode = DataGridViewColumnSortMode.NotSortable, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
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
        _visible.Clear();
        _moves.Enabled = Current.Id.Length > 0 && Current.Settings.SortMode == LegendSortMode.Handmatig && _filter.Text.Length == 0 && !_issuesOnly.Checked;
        foreach (var entry in _entries)
        {
            var issues = KlicSummary.Issues(entry.KlicObjects);
            if (_issuesOnly.Checked && issues.Length == 0) continue;
            var values = new List<string> { entry.CustomStatusName ?? Current.Settings.StatusLabel(entry.Status), entry.DisplayDescription, string.Join(", ", entry.Netbeheerders),
                string.Join(", ", entry.LayersByType.Keys), entry.QuantityText(Current.Settings.QuantityDecimals, Current.Settings.UnitArea, Current.Settings.UnitLength, Current.Settings.UnitCount),
                string.Join(Environment.NewLine, entry.SourceLayers.Select(s => s.SourceLayerName).Distinct()), string.Join(", ", entry.MergedMembers) };
            values.AddRange(Current.Settings.Klic.Columns.Select(k => KlicSummary.Field(entry.KlicObjects, k)));
            values.Add(issues);
            string search = _field.SelectedItem is KlicField field ? KlicSummary.Field(entry.KlicObjects, field.Key) : string.Join(" ", values);
            if (_filter.Text.Trim().Length > 0 && !search.Contains(_filter.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)) continue;
            _visible.Add(entry);
            _grid.Rows.Add(values.Cast<object>().ToArray());
        }
        _summary.Text = $"{_visible.Count} van {_entries.Count} regels · voorkeursbron: {KlicFields.SourceLabel(Current.Settings.Klic.PreferredSource)}. "
            + (Current.Id.Length == 0 ? "Voorbeeld; instellingen worden niet opgeslagen." : "Wijzigingen gelden alleen voor deze legenda.")
            + (_filter.Text.Length > 0 || _issuesOnly.Checked ? " Filter geldt voor deze lijst en export; wis het filter om regels te verplaatsen." : "");
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

    private void EditKlic()
    {
        using var dialog = new KlicOptionsDialog(Current.Settings.Klic);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var previous = Current.Settings.Klic;
        try
        {
            Current.Settings.Klic = dialog.Result;
            if (_analyze is not null) Current.Entries = _analyze(Current);
            Current.Changed = Current.Id.Length > 0;
            ConfigureColumns(); LoadEntries();
        }
        catch (Exception ex)
        {
            Current.Settings.Klic = previous;
            MessageBox.Show(this, "De KLIC-instellingen zijn niet toegepast: " + ex.Message, "KLIC-gegevens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowKlicDetails()
    {
        if (_grid.CurrentRow is not { } row) return;
        var entry = _visible[row.Index];
        if (entry.KlicObjects.Count == 0) { MessageBox.Show(this, "Geen KLIC-gegevens bij deze regel gevonden.", "KLIC-details"); return; }
        using var dialog = new KlicDetailsDialog(entry);
        dialog.ShowDialog(this);
    }

    private void ExportVisible()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV-bestand (*.csv)|*.csv|JSON-bestand (*.json)|*.json", FileName = "NLCS-overzicht.csv", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var s = Current.Settings;
            string text = dialog.FilterIndex == 2 ? LegendExport.ToJson(_visible, s.QuantityDecimals, s.UnitArea, s.UnitLength, s.UnitCount, s.Klic.Columns)
                : LegendExport.ToCsv(_visible, s.QuantityDecimals, s.UnitArea, s.UnitLength, s.UnitCount, s.Klic.Columns);
            File.WriteAllText(dialog.FileName, text, new UTF8Encoding(dialog.FilterIndex == 1));
        }
        catch (Exception ex) { MessageBox.Show(this, "Exporteren is niet gelukt: " + ex.Message, "Overzicht", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
