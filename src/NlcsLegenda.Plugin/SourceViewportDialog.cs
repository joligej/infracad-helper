using System.Drawing;
using System.Windows.Forms;

namespace NlcsLegenda.Plugin;

internal sealed record SourceViewOption(string Handle, string Layout, int Number, double Scale,
    double X, double Y, double Width, double Height, string State, bool Available = true)
{
    public string Caption => Number > 0 ? $"Venster {Number} · {Handle}" : Handle;
}

internal sealed class SourceViewportDialog : LegendDialog
{
    private readonly IReadOnlyList<SourceViewOption> _views;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
        BackgroundColor = SystemColors.Window, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } };
    private readonly RadioButton _whole = new() { Text = "Hele tekening / objectselectie", AutoSize = true };
    private readonly RadioButton _chosen = new() { Text = "Alleen aangevinkte kaartvensters", AutoSize = true };
    private readonly Label _notice = new() { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8) };
    private readonly Panel _preview = new() { Dock = DockStyle.Bottom, Height = 180, BackColor = SystemColors.Window };
    private readonly ButtonBar _buttons = new(false);
    private readonly bool _loading = true;

    public List<string> SelectedHandles => _whole.Checked ? new() : CheckedRows().Select(v => v.Handle).ToList();

    public SourceViewportDialog(IReadOnlyList<SourceViewOption> views, IReadOnlyCollection<string> selected)
    {
        Text = "Kaartvensters kiezen"; ClientSize = new Size(1000, 640); MinimumSize = new Size(640, 440);
        StartPosition = FormStartPosition.CenterParent; ShowIcon = false; ShowInTaskbar = false;
        var selectedSet = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _views = views.Concat(selected.Where(h => !views.Any(v => v.Handle.Equals(h, StringComparison.OrdinalIgnoreCase)))
            .Select(h => new SourceViewOption(h, "Ontbrekende bron", 0, 0, 0, 0, 0, 0, "Venster ontbreekt; kies opnieuw", false))).ToArray();
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Opnemen", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells });
        foreach (var (name, title) in new[] { ("layout", "Tekenblad"), ("view", "Kaartvenster"), ("scale", "Schaal"), ("size", "Papiermaat"), ("state", "Toestand") })
        { _grid.Columns.Add(name, title); _grid.Columns[name]!.ReadOnly = true; }
        foreach (DataGridViewColumn column in _grid.Columns)
            column.MinimumWidth = column.Index == 0 ? 65 : column.Name == "scale" ? 85 : 125;
        _grid.FontChanged += (_, _) =>
        {
            foreach (DataGridViewColumn column in _grid.Columns)
                column.MinimumWidth = Math.Max(column.Index == 0 ? 65 : 85, TextRenderer.MeasureText(column.HeaderText, _grid.Font).Width + 24);
        };
        _grid.Columns["layout"]!.FillWeight = 160; _grid.Columns["scale"]!.FillWeight = 65;
        foreach (var view in _views)
        {
            var row = _grid.Rows[_grid.Rows.Add(selectedSet.Contains(view.Handle), view.Layout, view.Caption,
                view.Scale > 0 ? $"1:{view.Scale:0.###}" : "—", view.Width > 0 ? $"{view.Width:0.#} × {view.Height:0.#} mm" : "—", view.State)];
            row.Tag = view;
            row.Cells[0].ReadOnly = !view.Available && !selectedSet.Contains(view.Handle);
            if (!view.Available) row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }
        _whole.Checked = selected.Count == 0; _chosen.Checked = selected.Count > 0;
        var modes = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        modes.Controls.AddRange(new Control[] { _whole, _chosen });
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Zoek op bladnaam of venster", Margin = new Padding(8) };
        search.TextChanged += (_, _) =>
        {
            _grid.CurrentCell = null;
            string query = search.Text.Replace("*", "").Trim();
            foreach (DataGridViewRow row in _grid.Rows)
            { var view = (SourceViewOption)row.Tag!; row.Visible = $"{view.Layout} {view.Caption}".Contains(query, StringComparison.CurrentCultureIgnoreCase); }
            if (_grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Visible) is { } first) _grid.CurrentCell = first.Cells[1];
            _preview.Invalidate();
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellValueChanged += (_, e) =>
        {
            if (_loading || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            _chosen.Checked = true;
            var row = _grid.Rows[e.RowIndex];
            if (row.Tag is SourceViewOption { Available: false }) row.Cells[0].ReadOnly = !Equals(row.Cells[0].Value, true);
            RefreshChoice();
        };
        _whole.CheckedChanged += (_, _) => RefreshChoice();
        _chosen.CheckedChanged += (_, _) => RefreshChoice();
        _grid.SelectionChanged += (_, _) => _preview.Invalidate();
        _preview.Paint += DrawPreview;
        _buttons.Ok.Text = "Bronkeuze gebruiken"; AcceptButton = _buttons.Ok; CancelButton = _buttons.Cancel;
        _buttons.Ok.Click += (_, _) => _grid.EndEdit();
        Controls.Add(_grid); Controls.Add(_preview); Controls.Add(search); Controls.Add(modes); Controls.Add(_notice); Controls.Add(_buttons);
        Layout += (_, _) =>
        {
            int free = ClientSize.Height - _buttons.Height - _notice.Height - modes.Height - search.Height;
            _preview.Visible = free > Math.Max(300, Font.Height * 14);
            if (_preview.Visible) _preview.Height = Math.Min(180, free / 3);
        };
        SizeChanged += (_, _) => { _notice.MaximumSize = new Size(Math.Max(100, ClientSize.Width - 20), 0); _preview.Invalidate(); };
        _notice.MaximumSize = new Size(ClientSize.Width - 20, 0);
        _loading = false; RefreshChoice();
    }

    private SourceViewOption[] CheckedRows() => _grid.Rows.Cast<DataGridViewRow>()
        .Where(r => Equals(r.Cells[0].Value, true)).Select(r => (SourceViewOption)r.Tag!).ToArray();

    private void RefreshChoice()
    {
        if (_loading) return;
        var chosen = CheckedRows();
        string error = !_whole.Checked && chosen.Any(v => !v.Available) ? "Een gekozen venster is niet beschikbaar. Vink het uit of kies een ander venster."
            : !_whole.Checked && chosen.Length == 0 ? "Vink minimaal één kaartvenster aan, of kies de hele tekening." : "";
        _buttons.Ok.Enabled = error.Length == 0;
        _notice.Text = error.Length > 0 ? error : _whole.Checked ? "Geen beperking tot kaartvensters. De xref-keuzes en uitsluitingen blijven gelden."
            : $"{chosen.Length} kaartvenster(s) op {chosen.Select(v => v.Layout).Distinct().Count()} blad(en). Laat overzichtskaartjes en legendavensters buiten de keuze."
                + (chosen.Select(v => Math.Round(v.Scale, 4)).Distinct().Count() > 1 ? " Verschillende kaartschalen; de ingestelde legendaschaal blijft behouden." : "");
        _notice.ForeColor = error.Length > 0 ? Color.Firebrick : SystemColors.ControlText;
        _preview.Invalidate();
    }

    private void DrawPreview(object? sender, PaintEventArgs e)
    {
        if (_grid.CurrentRow?.Tag is not SourceViewOption active) return;
        var views = _views.Where(v => v.Layout == active.Layout && v.Width > 0 && v.Height > 0).ToArray();
        if (views.Length == 0) return;
        double minX = views.Min(v => v.X - v.Width / 2), minY = views.Min(v => v.Y - v.Height / 2);
        double maxX = views.Max(v => v.X + v.Width / 2), maxY = views.Max(v => v.Y + v.Height / 2);
        double scale = Math.Min((_preview.ClientSize.Width - 30) / Math.Max(1, maxX - minX), (_preview.ClientSize.Height - 45) / Math.Max(1, maxY - minY));
        if (scale <= 0) return;
        var chosen = SelectedHandles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        e.Graphics.DrawString("Positie op " + active.Layout + " (schematisch)", Font, SystemBrushes.ControlText, new RectangleF(8, 4, _preview.Width - 16, 22));
        using var fill = new SolidBrush(Color.FromArgb(218, 235, 244));
        using var border = new Pen(Color.SteelBlue, 2);
        using var focus = new Pen(SystemColors.ControlText, 2);
        foreach (var view in views)
        {
            var box = new RectangleF((float)(15 + (view.X - view.Width / 2 - minX) * scale), (float)(32 + (maxY - view.Y - view.Height / 2) * scale),
                (float)(view.Width * scale), (float)(view.Height * scale));
            if (chosen.Contains(view.Handle)) e.Graphics.FillRectangle(fill, box);
            e.Graphics.DrawRectangle(view.Handle == active.Handle ? focus : border, box.X, box.Y, box.Width, box.Height);
            e.Graphics.DrawString(view.Number.ToString(), Font, SystemBrushes.ControlText, box);
        }
    }
}