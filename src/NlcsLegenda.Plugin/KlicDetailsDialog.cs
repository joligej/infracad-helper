using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class KlicDetailsDialog : LegendDialog
{
    internal static string ResolveAttachmentPath(string path, string drawing)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            if (Path.IsPathRooted(path)) throw new InvalidOperationException("Gebruik een volledig bijlagepad of een pad relatief aan de brontekening.");
            if (!Path.IsPathFullyQualified(drawing)) throw new InvalidOperationException("Sla de brontekening eerst op om een relatieve bijlage te openen.");
            path = Path.Join(Path.GetDirectoryName(drawing)!, path);
        }
        path = Path.GetFullPath(path);
        if (path.IndexOf(':', 2) >= 0 || !new[] { ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            throw new InvalidOperationException("Alleen PDF- en afbeeldingsbestanden kunnen hier worden geopend.");
        return path;
    }

    public KlicDetailsDialog(LegendEntry entry)
    {
        Text = "KLIC-details – " + entry.Description; Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(1000, 640); MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; ShowIcon = false;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        DataGridView Grid(string title, params string[] columns)
        {
            var page = new TabPage(title);
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            foreach (var column in columns) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column, MinimumWidth = 120,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
            page.Controls.Add(grid); tabs.TabPages.Add(page); return grid;
        }
        var data = Grid("Veldwaarden", "Eigenschap", "Waarde", "Bron", "Tabel", "Gebruikt", "Bronobjecten");
        var values = entry.KlicObjects.SelectMany(o => o.Records.SelectMany(r => r.Values.Select(v => new
        {
            Object = o.Identity, r.Source, r.Table, Key = v.Key, Value = v.Value,
            Selected = o.Selected(v.Key).Any(selected => selected.Source == r.Source && selected.Table.Equals(r.Table, StringComparison.OrdinalIgnoreCase))
        })));
        foreach (var group in values.GroupBy(v => new { v.Source, v.Table, v.Key, v.Value, v.Selected }).OrderBy(g => g.Key.Key).ThenBy(g => g.Key.Source))
            data.Rows.Add(KlicFields.Find(group.Key.Key)?.Label ?? group.Key.Key, KlicFields.Format(group.Key.Key, group.Key.Value),
                KlicFields.SourceLabel(group.Key.Source), group.Key.Table, group.Key.Selected ? "Ja" : "Nee", group.Select(v => v.Object).Distinct().Count());
        var sources = Grid("Bronobjecten", "Tekening", "Xref", "Objecthandle", "Broncontrole");
        sources.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        sources.VirtualMode = true;
        sources.CellValueNeeded += (_, e) =>
        {
            var obj = entry.KlicObjects[e.RowIndex];
            e.Value = e.ColumnIndex switch { 0 => obj.Drawing, 1 => obj.Xref, 2 => obj.Handle, _ => KlicSummary.Issues(new[] { obj }) };
        };
        sources.RowCount = entry.KlicObjects.Count;
        var attachments = Grid("Bijlagen", "Bestand", "Tekening", "Bron");
        foreach (var item in entry.KlicObjects.SelectMany(o => o.Records.Where(r => r.Values.TryGetValue("Bijlage", out var path) && !string.IsNullOrWhiteSpace(path))
            .Select(r => new { Path = r.Values["Bijlage"], o.Drawing, r.Source })).Distinct())
            attachments.Rows.Add(item.Path, item.Drawing, KlicFields.SourceLabel(item.Source));
        var open = new Button { Text = "Geselecteerde bijlage openen", Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        open.Click += (_, _) =>
        {
            if (attachments.CurrentRow is not { } row) return;
            try
            {
                string path = ResolveAttachmentPath(row.Cells[0].Value as string ?? "", row.Cells[1].Value as string ?? "");
                if (!File.Exists(path)) throw new FileNotFoundException("De bijlage is niet gevonden. Controleer het pad in de brontekening.", path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Bijlage", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        attachments.Parent!.Controls.Add(open);
        var issues = KlicSummary.Issues(entry.KlicObjects);
        var summary = new TextBox { Dock = DockStyle.Top, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 80,
            Text = $"{entry.KlicObjects.Count} unieke bronobjecten. Lege of nulwaarden zijn geen gemeten diepte.\r\n" + (issues.Length == 0 ? "Geen verschillen tussen de gelezen bronnen gevonden." : issues) };
        var buttons = new ButtonBar(withApply: false); buttons.Cancel.Text = "Sluiten"; buttons.Ok.Visible = false; CancelButton = buttons.Cancel;
        Controls.Add(tabs); Controls.Add(summary); Controls.Add(buttons);
    }
}
