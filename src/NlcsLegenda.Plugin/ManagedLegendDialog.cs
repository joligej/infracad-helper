using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class CompositionDialog : LegendDialog
{
    private readonly CheckBox _locked = new() { Text = "Goedgekeurde samenstelling vastzetten", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = SystemColors.Window, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } };
    public bool Locked => _locked.Checked;
    public IReadOnlyList<LegendEntry> Approved => _grid.Rows.Cast<DataGridViewRow>().Where(r => Equals(r.Cells[0].Value, true)).Select(r => (LegendEntry)r.Tag!).ToArray();
    public CompositionDialog(LegendDefinition definition, IReadOnlyList<LegendEntry> proposals, Func<LegendEntry, string>? approvalProblem = null)
    {
        Text = "Samenstelling – " + definition.Name; ClientSize = new Size(880, 530); MinimumSize = new Size(520, 350);
        StartPosition = FormStartPosition.CenterParent; ShowIcon = false; ShowInTaskbar = false;
        _locked.Checked = definition.CompositionLocked;
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Goedkeuren", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells });
        _grid.Columns.Add("description", "Voorstel"); _grid.Columns.Add("source", "Bron en status");
        _grid.Columns[1].ReadOnly = _grid.Columns[2].ReadOnly = true; _grid.Columns[2].FillWeight = 220;
        foreach (var entry in proposals)
        {
            string problem = approvalProblem?.Invoke(entry) ?? "";
            var row = _grid.Rows[_grid.Rows.Add(false, entry.DisplayDescription, problem.Length > 0 ? problem : LegendUpdatePlan.Describe(entry))];
            row.Tag = entry; row.Cells[0].ReadOnly = problem.Length > 0;
            if (problem.Length > 0) row.Cells[0].Style.BackColor = SystemColors.Control;
        }
        var help = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), MaximumSize = new Size(830, 0),
            Text = $"{definition.Rows.Count} bestaande regels. Bij vastzetten volgen brongekoppelde voorbeelden de bron; nieuwe onderdelen worden pas na goedkeuring opgenomen. Eigen regels en uitsluitingen blijven behouden. De lijst hieronder bevat voorstellen." };
        SizeChanged += (_, _) => help.MaximumSize = new Size(Math.Max(100, ClientSize.Width - 20), 0);
        var buttons = new ButtonBar(withApply: false); buttons.Ok.Text = "Opslaan"; AcceptButton = buttons.Ok; CancelButton = buttons.Cancel;
        buttons.Ok.Click += (_, _) => _grid.EndEdit();
        Controls.Add(_grid); Controls.Add(help); Controls.Add(_locked); Controls.Add(buttons);
    }
}

internal sealed class SheetComparisonDialog : LegendDialog
{
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, BackgroundColor = SystemColors.Window, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } };
    public IReadOnlyList<SheetComparisonException> Exceptions => _grid.Rows.Cast<DataGridViewRow>()
        .Where(r => Equals(r.Cells[0].Value, true)).Select(r => (SheetDifference)r.Tag!).Select(d => new SheetComparisonException(d.Key, d.Fingerprint)).ToArray();
    public SheetComparisonDialog(IReadOnlyList<SheetDifference> differences)
    {
        Text = "Legenda's tussen bladen vergelijken"; ClientSize = new Size(1000, 600); MinimumSize = new Size(540, 350);
        StartPosition = FormStartPosition.CenterParent; ShowIcon = false; ShowInTaskbar = false;
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Uitzondering", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells });
        _grid.Columns.Add("description", "Onderdeel"); _grid.Columns.Add("details", "Verschillen");
        _grid.Columns[1].ReadOnly = _grid.Columns[2].ReadOnly = true; _grid.Columns[2].FillWeight = 250;
        foreach (var d in differences) _grid.Rows[_grid.Rows.Add(d.Accepted, d.Description, d.Details)].Tag = d;
        var help = new Label { Text = differences.Count == 0 ? "Geen verschillen tussen de opgeslagen legendaregels gevonden."
            : "Een uitzondering geldt alleen voor deze teksten en weergaven. Een latere wijziging wordt opnieuw gemeld. Andere tekeningen blijven ongewijzigd.", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), MaximumSize = new Size(940, 0) };
        SizeChanged += (_, _) => help.MaximumSize = new Size(Math.Max(100, ClientSize.Width - 20), 0);
        var buttons = new ButtonBar(withApply: false); buttons.Ok.Text = "Keuzes opslaan"; AcceptButton = buttons.Ok; CancelButton = buttons.Cancel;
        buttons.Ok.Click += (_, _) => _grid.EndEdit();
        Controls.Add(_grid); Controls.Add(help); Controls.Add(buttons);
    }
}
