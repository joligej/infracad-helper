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
    public IReadOnlyList<LegendEntry> SourceCandidates { get; set; } = Array.Empty<LegendEntry>();
    public bool Changed { get; set; }
    public bool NeedsAnalysis { get; set; }
    public override string ToString() => Name;
}

internal sealed class OverviewDialog : LegendDialog
{
    private readonly IReadOnlyList<OverviewLegend> _legends;
    private readonly DialogSaveState? _saveState;
    private readonly Func<IReadOnlyList<OverviewLegend>, string?> _save;
    private readonly ComboBox _legend = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly ComboBox _sort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        BackgroundColor = SystemColors.Window,
        GridColor = SystemColors.ControlLight,
        BorderStyle = BorderStyle.FixedSingle,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        DefaultCellStyle = { Padding = new Padding(4, 3, 4, 3) },
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
    };
    private readonly Label _summary = new() { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8) };
    private readonly FlowLayoutPanel _moves = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
    private List<LegendEntry> _entries = new();
    private readonly List<LegendEntry> _visible = new();
    private readonly Func<OverviewLegend, IReadOnlyList<LegendEntry>>? _analyze;
    private readonly TextBox _filter = new() { Width = 210, PlaceholderText = "Zoek waarde of tekst" };
    private readonly ComboBox _field = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _issuesOnly = new() { Text = "Alleen bronmeldingen", AutoSize = true };
    private readonly Dictionary<LegendEntry, string[]> _rowValues = new();
    private readonly Dictionary<(LegendEntry, string), string> _fieldValues = new();
    private readonly Func<string, IReadOnlyList<LegendExample>>? _loadExamples;
    private readonly Action<Form, OverviewLegend, LegendEntry>? _showExample;
    private readonly Func<OverviewLegend, IReadOnlyList<LegendEntry>>? _sourceCandidates;
    private readonly Action<OverviewLegend, ManualEntry, LegendEntry>? _linkSource;
    private bool _loading;
    private string? _analysisError;
    private readonly List<Button> _actions = new();
    private readonly Button _example;
    private OverviewLegend Current => _legends[_legend.SelectedIndex];

    public OverviewDialog(IReadOnlyList<OverviewLegend> legends, Func<IReadOnlyList<OverviewLegend>, string?> save,
        Func<OverviewLegend, IReadOnlyList<LegendEntry>>? analyze = null,
        Func<string, IReadOnlyList<LegendExample>>? loadExamples = null,
        Action<Form, OverviewLegend, LegendEntry>? showExample = null,
        Func<OverviewLegend, IReadOnlyList<LegendEntry>>? sourceCandidates = null,
        Action<OverviewLegend, ManualEntry, LegendEntry>? linkSource = null)
    {
        _legends = legends;
        _save = save;
        _analyze = analyze;
        _loadExamples = loadExamples;
        _showExample = showExample; _sourceCandidates = sourceCandidates; _linkSource = linkSource;
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
        Button ActionButton(string title, Action action)
        {
            var button = new Button { Text = title, AutoSize = true, Padding = new Padding(4) };
            button.Click += (_, _) => action(); tools.Controls.Add(button);
            return button;
        }
        ActionButton("Vernieuwen", () => { Current.NeedsAnalysis = true; LoadLegend(); }).Enabled = _analyze is not null;
        _actions.Add(ActionButton("Regel aanpassen…", EditRow));
        _actions.Add(ActionButton("Bron koppelen…", MatchSource));
        _example = ActionButton("Voorbeeld…", EditExample);
        _actions.Add(_example);
        _actions.Add(ActionButton("KLIC-instellingen…", EditKlic));
        _actions.Add(ActionButton("KLIC-details…", ShowKlicDetails));
        _actions.Add(ActionButton("Exporteer lijst…", ExportVisible));
        _actions.Add(ActionButton("Samenvoegen…", MergeSelected));
        _actions.Add(ActionButton("Samenvoeging opheffen", UnmergeSelected));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 4) };
        _field.Items.Add("Alle zichtbare velden");
        _field.Items.AddRange(KlicFields.All.Cast<object>().ToArray());
        _field.SelectedIndex = 0;
        filters.Controls.Add(_filter); filters.Controls.Add(_field); filters.Controls.Add(_issuesOnly);
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
        Controls.Add(filters);
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
            LoadEntries(updateColumns: false);
        };
        foreach (var legend in legends) _legend.Items.Add(legend);
        _legend.SelectedIndex = 0;
        _saveState = Own(new DialogSaveState(buttons, () => System.Text.Json.JsonSerializer.Serialize(
            _legends.Select(l => new { l.Id, settings = l.Settings.ToJson() }))));
    }

    private void AddMoveEntry(string text, int offset)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        button.Click += (_, _) => MoveEntry(offset);
        _moves.Controls.Add(button);
    }

    private void LoadLegend()
    {
        _analysisError = null;
        if (Current.NeedsAnalysis && _analyze is not null)
        {
            UseWaitCursor = true;
            try
            {
                Current.Entries = _analyze(Current);
                Current.NeedsAnalysis = false;
            }
            catch (Exception ex) when (!ExceptionSafety.IsFatal(ex)) { _analysisError = "Bronnen niet gelezen: " + ex.Message + " Kies Vernieuwen om opnieuw te proberen."; }
            finally { UseWaitCursor = false; }
        }
        _loading = true;
        _sort.SelectedItem = Current.Settings.SortMode.ToString();
        _sort.Enabled = Current.Id.Length > 0 && _analysisError is null;
        _loading = false;
        foreach (var action in _actions) action.Enabled = _analysisError is null;
        _example.Enabled &= Current.Id.Length > 0;
        LoadEntries();
    }

    private void ConfigureColumns()
    {
        _grid.Columns.Clear();
        foreach (var name in new[] { "Status", "Omschrijving", "Weergave", "Variant", "Netbeheerder", "Soort", "Hoeveelheid", "Bronlagen", "Samengevoegd" }
            .Concat(Current.Settings.Klic.Columns.Select(k => KlicFields.Find(k)!.Label)).Concat(new[] { "Broncontrole" }))
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = name,
                MinimumWidth = 100,
                FillWeight = name is "Omschrijving" or "Bronlagen" ? 240 : 120,
                Visible = name switch
                {
                    "Weergave" => _entries.Any(e => e.AppearanceLabel.Length > 0),
                    "Variant" => _entries.Any(e => e.VariantDetails.Length > 0),
                    "Netbeheerder" => _entries.Any(e => e.Netbeheerders.Count > 0),
                    "Samengevoegd" => _entries.Any(e => e.MergedMembers.Count > 0),
                    "Broncontrole" => _entries.Any(e => RowValues(e)[^1].Length > 0),
                    _ => true
                },
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
            });
    }

    private void LoadEntries(bool updateColumns = true)
    {
        _entries = _analysisError is null ? LegendGrouping.Sort(Current.Entries, Current.Settings) : new();
        _rowValues.Clear();
        _fieldValues.Clear();
        if (updateColumns) ConfigureColumns();
        _moves.Visible = Current.Id.Length > 0 && Current.Settings.SortMode == LegendSortMode.Handmatig;
        _moves.Enabled = Current.Id.Length > 0 && Current.Settings.SortMode == LegendSortMode.Handmatig;
        _summary.Text = Current.Id.Length == 0
            ? $"{_entries.Count} regels in de tekening. Plaats eerst een legenda om de volgorde op te slaan."
            : $"{_entries.Count} regels · schaal 1:{Current.Settings.Scale:g} · wijzigingen gelden alleen voor deze legenda.";
        FillGrid(0);
        _saveState?.Refresh();
    }

    private void FillGrid(int selected)
    {
        _grid.Rows.Clear();
        _visible.Clear();
        _moves.Enabled = _analysisError is null && Current.Id.Length > 0 && Current.Settings.SortMode == LegendSortMode.Handmatig && _filter.Text.Length == 0 && !_issuesOnly.Checked;
        var rows = new List<DataGridViewRow>();
        foreach (var entry in _entries)
        {
            var values = RowValues(entry);
            if (_issuesOnly.Checked && values[^1].Length == 0) continue;
            string search = _field.SelectedItem is KlicField field ? FieldValue(entry, field.Key) : string.Join(" ", values);
            if (!LayerSearch.Matches(search, _filter.Text)) continue;
            _visible.Add(entry);
            var row = new DataGridViewRow();
            row.CreateCells(_grid, values.Cast<object>().ToArray());
            rows.Add(row);
        }
        _grid.Rows.AddRange(rows.ToArray());
        _summary.Text = $"{_visible.Count} van {_entries.Count} regels · voorkeursbron: {KlicFields.SourceLabel(Current.Settings.Klic.PreferredSource)}. "
            + (Current.Id.Length == 0 ? "Voorbeeld; instellingen worden niet opgeslagen." : "Wijzigingen gelden alleen voor deze legenda.")
            + (_filter.Text.Length > 0 || _issuesOnly.Checked ? " Filter geldt voor deze lijst en export; wis het filter om regels te verplaatsen." : "");
        if (_analysisError is not null) _summary.Text = _analysisError;
        if (_grid.Rows.Count > 0) _grid.CurrentCell = _grid.Rows[Math.Clamp(selected, 0, _grid.Rows.Count - 1)].Cells[1];
    }

    private string[] RowValues(LegendEntry entry)
    {
        if (_rowValues.TryGetValue(entry, out var cached)) return cached;
        var issues = LayerMetric.MergeIssues(KlicSummary.Issues(entry.KlicObjects), entry.Metric.AreaIssue) ?? string.Empty;
        if (entry.SourceNotice.Length > 0) issues += " " + entry.SourceNotice;
        var values = new List<string> { entry.CustomStatusName ?? Current.Settings.StatusLabel(entry.Status), entry.DisplayDescription, (entry.ExampleMode == ExampleMode.Fixed ? "Vast voorbeeld" : "Bron volgen") + (entry.AppearanceLabel.Length > 0 ? " · " + entry.AppearanceLabel : ""), entry.VariantDetails, string.Join(", ", entry.Netbeheerders),
                string.Join(", ", entry.LayersByType.Keys), entry.QuantityText(Current.Settings.QuantityDecimals, Current.Settings.UnitArea, Current.Settings.UnitLength, Current.Settings.UnitCount),
                string.Join(Environment.NewLine, entry.SourceLayers.Select(s => s.SourceLayerName).Distinct()), string.Join(", ", entry.MergedMembers) };
        values.AddRange(Current.Settings.Klic.Columns.Select(k => KlicSummary.Field(entry.KlicObjects, k)));
        values.Add(issues);
        var result = values.ToArray();
        _rowValues.Add(entry, result);
        return result;
    }

    private string FieldValue(LegendEntry entry, string key)
    {
        if (!_fieldValues.TryGetValue((entry, key), out var value))
            _fieldValues.Add((entry, key), value = KlicSummary.Field(entry.KlicObjects, key));
        return value;
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
        _saveState?.Refresh();
    }

    private List<LegendEntry> SelectedEntries() => _grid.SelectedRows.Cast<DataGridViewRow>()
        .OrderBy(r => r.Index).Select(r => _visible[r.Index]).ToList();

    private void MergeSelected()
    {
        if (Current.Id.Length == 0 || _analyze is null) return;
        var selected = SelectedEntries();
        if (selected.Count < 2)
        {
            MessageBox.Show(this, "Selecteer minstens twee regels met Ctrl of Shift.", "Samenvoegen");
            return;
        }
        using var picker = new MainEntryDialog(selected);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is not { } main) return;
        ChangeMerges(() => Current.Settings.ManualMergeGroups.Add(ManualMergeGroup.Create(selected, main)));
    }

    private void UnmergeSelected()
    {
        if (Current.Id.Length == 0 || _analyze is null) return;
        var ids = SelectedEntries().Select(e => e.ManualMergeId).Where(id => id.Length > 0).ToHashSet();
        if (ids.Count == 0)
        {
            MessageBox.Show(this, "Selecteer een handmatig samengevoegde regel. Automatisch samenvoegen pas je aan in Instellingen > Samenvoegen.", "Samenvoeging opheffen");
            return;
        }
        ChangeMerges(() => Current.Settings.ManualMergeGroups.RemoveAll(g => ids.Contains(g.Id)));
    }

    private void ChangeMerges(Action change)
    {
        var previous = Current.Settings.Clone();
        try
        {
            change();
            Current.Entries = _analyze!(Current);
            Current.Changed = true;
            LoadEntries();
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            Current.Settings.CopyFrom(previous);
            MessageBox.Show(this, ex.Message, "Samenvoegen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void MatchSource()
    {
        if (Current.Id.Length == 0 || _grid.CurrentRow is not { } row) return;
        var previous = _visible[row.Index];
        try { if (_sourceCandidates is not null) Current.SourceCandidates = _sourceCandidates(Current); }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex)) { MessageBox.Show(this, ex.Message, "Bronnen", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        using var dialog = new SourceMatchDialog(previous, Current.SourceCandidates.Concat(_entries).Distinct().Where(e => e != previous),
            _showExample is null ? null : (form, entry) => _showExample(form, Current, entry));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Selected is not { } source) return;
        ChangeMerges(() => {
            if (previous.IsManual && !previous.Retained)
            {
                var manual = Current.Settings.ManualEntries.Single(m => m.ToLegendEntry().Element == previous.Element);
                if (_linkSource is null) throw new InvalidOperationException("Gebruik Gekoppeld voorbeeld om deze eigen regel aan een bronobject te koppelen.");
                _linkSource(Current, manual, source);
                manual.ExampleMode = dialog.KeepExample ? ExampleMode.Fixed : ExampleMode.FollowSource;
                return;
            }
            LegendContinuity.LinkRow(Current.Settings, previous, source, dialog.KeepExample);
        });
    }

    private void EditRow()
    {
        if (Current.Id.Length == 0 || _grid.CurrentRow is not { } row) return;
        var entry = _visible[row.Index];
        using var dialog = new RowChoiceDialog(entry);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ChangeMerges(() => {
            if (entry.IsManual && !entry.Retained)
            {
                var manual = Current.Settings.ManualEntries.FirstOrDefault(m => m.ToLegendEntry().Element == entry.Element);
                if (manual is not null) { manual.Description = dialog.Description; manual.ExampleMode = dialog.ExampleMode; manual.ComparisonKey = dialog.ComparisonKey; }
                if (dialog.Excluded) Current.Settings.ExcludedEntries.UnionWith(entry.ExclusionKeys);
            }
            else
            {
                var choice = LegendContinuity.SetChoice(Current.Settings, entry, dialog.Scope, dialog.Description, dialog.Excluded);
                choice.ComparisonKey = dialog.ComparisonKey;
                if (entry.CanFollowSource && dialog.ExampleMode != entry.ExampleMode)
                {
                    bool follow = dialog.ExampleMode == ExampleMode.FollowSource;
                    choice = LegendContinuity.SetChoice(Current.Settings, entry, dialog.Scope, example: follow ? null : entry.CapturedSampleBlock, automaticExample: follow);
                    choice.ExampleScale = entry.CapturedSampleScale;
                }
            }
        });
    }

    private void EditExample()
    {
        if (Current.Id.Length == 0 || _grid.CurrentRow is not { } row) return;
        var entry = _visible[row.Index];
        using var dialog = new ExampleDialog(Current.Settings.ExampleLibraryPath, _loadExamples ?? (_ => Array.Empty<LegendExample>()));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var previous = Current.Settings.Clone();
        try
        {
            Current.Settings.ExampleLibraryPath = dialog.LibraryPath;
            Current.Settings.ExampleOverrides = LegendOrder.AlignChoices(Current.Settings.ExampleOverrides, Current.Entries);
            if (dialog.AddEntry && dialog.SelectedExample is { } example)
            {
                var manual = new ManualEntry { Layer = "X-XX-AL-LEGENDA_VOORBEELD-S", Type = NlcsDrawType.Symbool,
                    Description = example.Name, SymbolBlock = example.BlockName, Status = entry.Status };
                Current.Settings.ManualEntries.Add(manual);
                Current.Settings.ExampleOverrides[LegendOrder.Key(manual.ToLegendEntry())] = example.BlockName;
            }
            else
            {
                string key = LegendOrder.Key(entry);
                if (dialog.SelectedBlock is null) Current.Settings.ExampleOverrides.Remove(key);
                else Current.Settings.ExampleOverrides[key] = dialog.SelectedBlock;
                if (entry.Sources.Count > 0)
                    LegendContinuity.SetChoice(Current.Settings, entry, ChoiceScope.Variant, example: dialog.SelectedBlock, automaticExample: dialog.SelectedBlock is null);
            }
            if (_analyze is not null) Current.Entries = _analyze(Current);
            Current.Changed = true; LoadEntries();
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            Current.Settings.CopyFrom(previous);
            MessageBox.Show(this, ex.Message, "Voorbeeld", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
            LoadEntries();
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { MessageBox.Show(this, "Exporteren is niet gelukt: " + ex.Message, "Overzicht", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
        _saveState?.Saved();
        return true;
    }
}


internal sealed class MainEntryDialog : LegendDialog
{
    private readonly DataGridView _list;
    public LegendEntry? Selected => _list.CurrentRow?.Tag as LegendEntry;

    public MainEntryDialog(IReadOnlyList<LegendEntry> entries, bool excluding = false)
    {
        Text = excluding ? "Regel uitsluiten" : "Hoofdobject kiezen";
        ClientSize = new Size(820, 420);
        MinimumSize = new Size(520, 320);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        var help = new Label { Text = excluding ? "Deze bron wordt op meerdere manieren weergegeven. Kies welke variant je wilt uitsluiten."
            : "Het hoofdobject bepaalt de titel, kleur, lijn en het symbool. Hoeveelheden worden opgeteld.",
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), MaximumSize = new Size(760, 0) };
        _list = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, MultiSelect = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        _list.Columns.Add("title", "Omschrijving");
        _list.Columns.Add("source", "Bronlaag");
        _list.Columns.Add("appearance", "Weergave");
        foreach (var e in entries)
            _list.Rows[_list.Rows.Add(e.DisplayDescription, string.Join(", ", e.SourceLayers.Select(l => l.SourceLayerName)),
                e.AppearanceLabel)].Tag = e;
        var buttons = new ButtonBar(false);
        buttons.Ok.Text = excluding ? "Uitsluiten" : "Samenvoegen";
        AcceptButton = buttons.Ok; CancelButton = buttons.Cancel;
        SizeChanged += (_, _) => help.MaximumSize = new Size(Math.Max(100, ClientSize.Width - 20), 0);
        Controls.Add(_list); Controls.Add(help); Controls.Add(buttons);
    }
}
