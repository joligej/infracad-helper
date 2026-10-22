using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using NlcsLegenda.Core;
using NlcsLegenda.Plugin;

internal static class DocumentationImages
{
    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var settings = new LegendSettings { Scale = 500, IncludeQuantities = true, SortMode = LegendSortMode.Handmatig };
        settings.Klic.Columns.Clear();
        settings.ManualEntryOrder = new();
        var entries = new[] {
            Entry("B-WE-KL-WATER-G", "Waterleiding", NlcsDrawType.Geometrie, 164, "KLIC"),
            Entry("B-WE-KL-DATA-G", "Datakabel", NlcsDrawType.Geometrie, 208, "KLIC"),
            Entry("B-WE-KL-GAS-G", "Gasleiding", NlcsDrawType.Geometrie, 92, "KLIC"),
            Entry("N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A", "Betonstraatsteen", NlcsDrawType.Arcering, 846),
            Entry("N-WE-VH-OPENVERHARDING_TEGEL-A", "Betontegel", NlcsDrawType.Arcering, 324),
            Entry("N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G", "Trottoirband", NlcsDrawType.Geometrie, 156),
            Entry("N-WE-GR-GRAS-V", "Gras", NlcsDrawType.Vlakvulling, 610),
            Entry("N-WE-GR-BOOM-S", "Boom", NlcsDrawType.Symbool, 12),
            Entry("N-WE-IE-MEUBILAIR_AFVALBAK-S", "Afvalbak", NlcsDrawType.Symbool, 4),
            Entry("N-WE-OV-LICHTMAST-S", "Lichtmast", NlcsDrawType.Symbool, 8)
        };
        settings.ManualEntryOrder = LegendOrder.Remember(Array.Empty<string>(), entries);
        var legend = new OverviewLegend { Id = "demo", Name = "Inrichting – demonstratie", Settings = settings, Entries = entries };
        using (var form = new OverviewDialog(new[] { legend }, _ => null, _ => entries))
            Capture(form, directory, "overzicht.png", new Size(1180, 650));
        using (var form = new SettingsDialog(settings.Clone(), "Legenda: Inrichting – demonstratie", xrefNames: new[] { "KLIC", "ONDERGROND" }))
            Capture(form, directory, "instellingen.png", new Size(840, 650));
        using (var form = new SettingsDialog(settings.Clone(), "Legenda: Inrichting – demonstratie", xrefNames: new[] { "KLIC", "ONDERGROND" }))
            Capture(form, directory, "samenvoegen.png", new Size(840, 650), f => {
                var tabs = Descendants(f).OfType<TabControl>().Single();
                tabs.SelectedIndex = 1;
                Application.DoEvents();
                var group = Descendants(tabs.SelectedTab!).OfType<GroupBox>().Single(g => g.Text.StartsWith("KLIC —"));
                tabs.SelectedTab!.AutoScrollPosition = new Point(0, group.Top);
            });
        var checks = entries.Select(e => new EntryCheckItem(LegendSettings.EntryKey(e), e.Description,
            e.Status.DisplayName(), string.Join(" ", e.SourceLayers.Select(s => s.SourceLayerName)))).ToArray();
        using (var form = new LegendManageDialog(checks, new LegendSettings(), new[] {
            new ManualEntry { Layer = "PROJECT_GRENS", Description = "Werkgrens", Type = NlcsDrawType.Geometrie } }, allowPicking: true))
            Capture(form, directory, "samenstellen.png", new Size(840, 600), f =>
                Descendants(f).OfType<TextBox>().Single().Text = "-kl-");
        Console.WriteLine("Vier documentatiebeelden geschreven met synthetische gegevens.");
    }

    private static LegendEntry Entry(string layer, string text, NlcsDrawType type, double quantity, string xref = "")
    {
        if (!NlcsLayerParser.TryParse(layer, out var parsed) || parsed is null) throw new InvalidOperationException(layer);
        return new LegendEntry { Status = parsed.Status, Discipline = parsed.Discipline, Hoofdgroep = parsed.Hoofdgroep,
            Element = parsed.Element, Description = text, LayersByType = new() { [type] = layer },
            SourceLayers = new[] { new NlcsLayerSource(layer, xref.Length == 0 ? layer : xref + "|" + layer, xref) },
            Metric = type == NlcsDrawType.Symbool ? new((int)quantity, 0, 0)
                : type == NlcsDrawType.Geometrie ? new(1, quantity, 0) : new(1, 0, quantity) };
    }

    private static void Capture(Form form, string directory, string name, Size size, Action<Form>? configure = null)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.ClientSize = size;
        form.Show();
        configure?.Invoke(form);
        Application.DoEvents();
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(Path.Join(directory, name), ImageFormat.Png);
        form.DialogResult = DialogResult.Cancel;
        form.Close();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
