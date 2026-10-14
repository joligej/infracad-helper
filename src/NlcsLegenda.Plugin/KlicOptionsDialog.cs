using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class KlicOptionsDialog : LegendDialog
{
    public KlicOptions Result { get; private set; }
    public KlicOptionsDialog(KlicOptions options)
    {
        Result = options.Clone();
        Text = "KLIC-gegevens – velden en groepering";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(680, 570); MinimumSize = new Size(480, 340);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; ShowIcon = false;
        var tabs = new TabControl { Dock = DockStyle.Fill, Multiline = true };
        CheckedListBox AddFields(string title, string explanation, IReadOnlyList<string> selected)
        {
            var page = new TabPage(title) { Padding = new Padding(10) };
            var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true };
            foreach (var field in KlicFields.All) list.Items.Add(field, selected.Contains(field.Key));
            var hint = new Label { Text = explanation, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 10) };
            page.SizeChanged += (_, _) => hint.MaximumSize = new Size(Math.Max(100, page.ClientSize.Width - 20), 0);
            page.Controls.Add(list); page.Controls.Add(hint); tabs.TabPages.Add(page); return list;
        }
        var columns = AddFields("Overzicht en export", "Deze velden verschijnen als kolommen in Overzicht en in CSV/JSON-export.", Result.Columns);
        var description = AddFields("Legenda-omschrijving", "Voeg gekozen eigenschappen toe achter de omschrijving. Bij meerdere waarden blijven alle varianten zichtbaar. Zonder selectie blijft de bestaande tekst behouden.", Result.DescriptionFields);
        var groups = AddFields("Afzonderlijke regels", "Objecten met verschillende waarden voor de aangevinkte eigenschappen krijgen afzonderlijke legendaregels. Dit geldt ook wanneer andere KLIC-kenmerken worden samengevoegd.", Result.GroupFields);
        var sourcePage = new TabPage("Gegevensbron") { Padding = new Padding(10), AutoScroll = true };
        var source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        foreach (var value in Enum.GetValues<KlicSource>()) source.Items.Add(KlicFields.SourceLabel(value));
        source.SelectedIndex = (int)Result.PreferredSource;
        var stack = new SettingsStack();
        stack.Controls.Add(new Label { Text = "Voorkeursbron bij meerdere opslagvormen:", AutoSize = true });
        stack.Controls.Add(source);
        stack.Controls.Add(new Label { Text = "Verschillen blijven zichtbaar onder KLIC-details. Een aanwezige lege waarde in de voorkeursbron blijft leeg. Ontbreekt het veld daar, dan wordt een beschikbare bron gebruikt.\n\nObject Data vereist een geopende bron-DWG in Civil 3D/Map. Xrefs gebruiken de overige leesbare gegevens. Overzicht vermeldt welke aanwezige bronnen niet gelezen konden worden.\n\nEen nul bij diepte, diameter, druk of spanning wordt als niet opgegeven weergegeven. Een standaarddiepte is geen gemeten diepte.", AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 12, 3, 3) });
        sourcePage.Controls.Add(stack); tabs.TabPages.Add(sourcePage);
        var buttons = new ButtonBar(withApply: false); AcceptButton = buttons.Ok; CancelButton = buttons.Cancel;
        FormClosing += (_, _) =>
        {
            if (DialogResult != DialogResult.OK) return;
            List<string> Keys(CheckedListBox list) => list.CheckedItems.Cast<KlicField>().Select(f => f.Key).ToList();
            Result = new KlicOptions { Columns = Keys(columns), DescriptionFields = Keys(description), GroupFields = Keys(groups), PreferredSource = (KlicSource)source.SelectedIndex };
        };
        Controls.Add(tabs); Controls.Add(buttons);
    }
}
