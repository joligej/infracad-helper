using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class RowChoiceDialog : LegendDialog
{
    private readonly TextBox _description = new() { Dock = DockStyle.Top };
    private readonly ComboBox _scope = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _exclude = new() { Text = "Uit de legenda houden", AutoSize = true, Dock = DockStyle.Top };
    public string Description => _description.Text.Trim();
    public bool Excluded => _exclude.Checked;
    public ChoiceScope Scope => (ChoiceScope)_scope.SelectedIndex;
    public RowChoiceDialog(LegendEntry entry)
    {
        Text = "Legendaregel aanpassen"; ClientSize = new Size(650, 420); MinimumSize = new Size(440, 310);
        StartPosition = FormStartPosition.CenterParent; ShowIcon = false; ShowInTaskbar = false;
        var details = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, TabStop = false,
            Text = LegendUpdatePlan.Describe(entry) + Environment.NewLine + entry.VariantDetails + Environment.NewLine + Environment.NewLine + VariantExplanation(entry) };
        _description.Text = entry.Description;
        _scope.Items.AddRange(new object[] { "Deze variant", "Deze bronlaag en objectsoort", "Dit bronsymbool op deze laag" });
        _scope.SelectedIndex = 0;
        _scope.Enabled = !entry.IsManual && !entry.Retained;
        _exclude.Checked = entry.SourceExcluded == true;
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), ColumnCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.Controls.Add(new Label { Text = "Eigen omschrijving", AutoSize = true }); top.Controls.Add(_description);
        top.Controls.Add(new Label { Text = "Toepassen op", AutoSize = true }); top.Controls.Add(_scope); top.Controls.Add(_exclude);
        var buttons = new ButtonBar(withApply: false); AcceptButton = buttons.Ok; CancelButton = buttons.Cancel;
        Controls.Add(details); Controls.Add(top); Controls.Add(buttons);
        ActiveControl = _description;
    }

    internal static string VariantExplanation(LegendEntry entry)
    {
        string symbols = string.Join(", ", entry.Sources.Select(s => s.Symbol).Where(s => s.Length > 0).Distinct());
        return (symbols.Length > 0 ? "Bronblok: " + symbols + Environment.NewLine : "")
            + "Verschillen in kleur, lijnpatroon, lijndikte en transparantie blijven afzonderlijk. "
            + (entry.SymbolLayer is not null ? "Voor symbolen staan grootte, draairichting en attribuuttekst apart in Instellingen > Samenvoegen. Een grootteklasse of puttype in de bloknaam blijft apart; kies Overzicht > Samenvoegen om zulke klassen met een hoofdvoorbeeld te combineren. " : "")
            + "Soort, specificatie, uitvoering, nummer, beheerder, diameter en materiaal zijn daar per voorbeeldtype instelbaar. "
            + "Een aangevinkte samenvoegoptie negeert dat verschil."
            + (entry.Sources.Select(s => s.Status).Distinct().Count() > 1 ? " Deze regel combineert meerdere statussen." : "");
    }
}

internal sealed class UpdatePreviewDialog : LegendDialog
{
    public UpdatePreviewDialog(string name, IReadOnlyList<LegendChange> changes, bool inspectionOnly = false)
    {
        Text = "Bijwerken – " + name; ClientSize = new Size(950, 580); MinimumSize = new Size(540, 330);
        StartPosition = FormStartPosition.CenterParent; ShowIcon = false; ShowInTaskbar = false;
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } };
        grid.Columns.Add("change", "Wijziging"); grid.Columns.Add("description", "Regel"); grid.Columns.Add("details", "Bron / voorbeeld / status");
        grid.Columns[2].FillWeight = 250;
        foreach (var change in changes) grid.Rows.Add(change.Change, change.Description, change.Details);
        var summary = new Label { Text = changes.Count == 0 ? "Geen wijzigingen gevonden." : string.Join(" · ", changes.GroupBy(c => c.Change).Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}")),
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), MaximumSize = new Size(880, 0) };
        var buttons = new ButtonBar(withApply: false); buttons.Ok.Text = inspectionOnly ? "Sluiten" : "Bijwerken"; AcceptButton = buttons.Ok; CancelButton = buttons.Cancel;
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Zoek omschrijving, bronlaag, objectsoort of reden" };
        search.TextChanged += (_, _) =>
        {
            grid.CurrentCell = null;
            foreach (DataGridViewRow row in grid.Rows)
                row.Visible = LayerSearch.Matches(string.Join(" ", row.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString())), search.Text);
        };
        Controls.Add(grid); Controls.Add(search); Controls.Add(summary); Controls.Add(buttons);
    }
}

internal sealed class SourceMatchDialog : LegendDialog
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly CheckBox _keep = new() { Text = "Bestaand voorbeeld behouden", AutoSize = true, Dock = DockStyle.Top, Checked = true };
    private sealed record Candidate(LegendEntry Entry) { public override string ToString() => Entry.DisplayDescription + " · " + LegendUpdatePlan.Describe(Entry); }
    public LegendEntry? Selected => (_list.SelectedItem as Candidate)?.Entry;
    public bool KeepExample => _keep.Checked;
    public SourceMatchDialog(LegendEntry previous, IEnumerable<LegendEntry> candidates, Action<Form, LegendEntry>? preview = null)
    {
        Text = "Bron koppelen – " + previous.Description; ClientSize = new Size(900, 480); MinimumSize = new Size(500, 320);
        StartPosition = FormStartPosition.CenterParent; ShowIcon = false; ShowInTaskbar = false;
        var all = candidates.Where(e => !e.IsManual && e.Sources.Count > 0).ToArray();
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Zoek omschrijving, laag, symbool of status" };
        void Fill() { _list.Items.Clear(); foreach (var e in all.Where(e => LayerSearch.Matches(e.DisplayDescription + " " + LegendUpdatePlan.Describe(e), search.Text))) _list.Items.Add(new Candidate(e)); }
        search.TextChanged += (_, _) => Fill(); Fill();
        _keep.Enabled = previous.SampleBlock.Length > 0; _keep.Checked = _keep.Enabled;
        var buttons = new ButtonBar(withApply: false); buttons.Ok.Text = "Koppelen"; AcceptButton = buttons.Ok; CancelButton = buttons.Cancel;
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && Selected is null) { e.Cancel = true; DialogResult = DialogResult.None; } };
        var previews = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
        void Show(LegendEntry? entry)
        {
            if (entry is null || preview is null) return;
            try { preview(this, entry); }
            catch (Exception ex) when (!ExceptionSafety.IsFatal(ex)) { MessageBox.Show(this, ex.Message, "Voorbeeld", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        var oldSample = new Button { Text = "Bestaand voorbeeld bekijken", AutoSize = true, Enabled = preview is not null && previous.SampleBlock.Length > 0 };
        var candidate = new Button { Text = "Gekozen bronvoorbeeld bekijken", AutoSize = true, Enabled = preview is not null };
        oldSample.Click += (_, _) => Show(previous); candidate.Click += (_, _) => Show(Selected);
        previews.Controls.Add(oldSample); previews.Controls.Add(candidate);
        Controls.Add(_list); Controls.Add(_keep); Controls.Add(search); Controls.Add(previews); Controls.Add(buttons);
    }
}
