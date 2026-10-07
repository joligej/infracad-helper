using System.Drawing;
using System.Windows.Forms;

namespace NlcsLegenda.Plugin;

// Eenvoudig helpvenster: links de onderwerpen, rechts de tekst. Werkt zonder internet; de tekst
// staat in de plugin zelf. Alleen basisuitleg voor dagelijks gebruik, geen volledige handleiding.
internal sealed class HelpDialog : Form
{
    private static readonly (string Title, string Body)[] Topics =
    {
        ("Beginnen",
            "NLCS Legenda maakt een legenda van de NLCS-lagen die al in je tekening zitten.\n\n" +
            "Je vindt de knoppen in het lint onder de tab NLCS Legenda. Typen kan ook: elk\n" +
            "commando begint met NLCSLEGENDA."),
        ("Legenda maken",
            "Klik op Genereren.\n\n" +
            "Kies de hele tekening of een selectie, en zet de legenda op een plek naast de\n" +
            "tekening. De legenda komt op de eigen NLCS-lagen, dus kleur en lijntype kloppen."),
        ("Bijwerken",
            "Is het ontwerp veranderd? Klik op Bijwerken.\n\n" +
            "De legenda wordt opnieuw getekend op dezelfde plek. Wijzig de tekst via de\n" +
            "instellingen of Elementtekst, niet met de hand in de legenda zelf."),
        ("Meerdere legenda's",
            "Je kunt meerdere legenda's naast elkaar hebben, elk met een eigen bron en\n" +
            "instellingen.\n\n" +
            "Open Legenda's om ze te bekijken, bij te werken, te hernoemen, op te zoeken of te\n" +
            "verwijderen."),
        ("Instellingen",
            "Open Instellingen voor schaal, inhoud, opmaak, teksten en hoeveelheden.\n\n" +
            "Dit is de standaard voor nieuwe legenda's. Een bestaande legenda pas je aan via\n" +
            "Legenda's. Die houdt zijn eigen instellingen."),
        ("Eigen lagen",
            "Heb je eigen lagen die geen NLCS zijn maar wel mee moeten tellen? Koppel ze onder\n" +
            "Eigen lagen in de instellingen.\n\n" +
            "Je kiest de laag, het type en de hoeveelheid. Daarna werken ze net als NLCS-lagen."),
        ("Blanco regels",
            "Een blanco regel is een lege regel die je zelf later invult.\n\n" +
            "Voeg ze toe onder Blanco regels in de instellingen. Ze blijven staan bij bijwerken,\n" +
            "opslaan en opnieuw openen."),
        ("Xrefs",
            "Werk je met externe referenties? Onder Xrefs kies je per xref of die meetelt.\n\n" +
            "Zit dezelfde laagnaam in de tekening en in een xref, dan houdt de bron ze uit\n" +
            "elkaar."),
        ("Hoeveelheden",
            "De legenda telt lengtes, aantallen en oppervlakten uit de tekening.\n\n" +
            "Zet hoeveelheden aan of uit in de instellingen. Overzicht laat de totalen zien\n" +
            "zonder te tekenen."),
        ("Export",
            "Exporteren schrijft de regels weg als CSV en JSON, met de hoeveelheden.\n\n" +
            "Batch doet dat voor alle tekeningen in een map tegelijk, in één uittrekstaat."),
        ("Commando's",
            "NLCSLEGENDA — legenda maken\n" +
            "NLCSLEGENDAUPDATE — een legenda bijwerken\n" +
            "NLCSLEGENDABEHEER — legenda's beheren\n" +
            "NLCSLEGENDAINFO — overzicht zonder tekenen\n" +
            "NLCSLEGENDAEXPORT — wegschrijven als CSV en JSON"),
    };

    public HelpDialog(string version)
    {
        Text = "NLCS Legenda \u2013 Help";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(640, 440);
        MinimumSize = new Size(480, 340);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 170,
            Panel1MinSize = 120,
            Panel2MinSize = 200,
            Padding = new Padding(8)
        };

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, TabIndex = 0 };
        foreach (var t in Topics) list.Items.Add(t.Title);
        split.Panel1.Controls.Add(list);

        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            TabStop = false
        };
        split.Panel2.Controls.Add(text);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0) text.Text = Topics[list.SelectedIndex].Body;
        };

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 40, ColumnCount = 2, Padding = new Padding(8, 4, 8, 4) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var versionLabel = new Label { Text = $"NLCS Legenda {version}", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText };
        var close = new Button { Text = "Sluiten", AutoSize = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        bottom.Controls.Add(versionLabel, 0, 0);
        bottom.Controls.Add(close, 1, 0);

        Controls.Add(split);
        Controls.Add(bottom);
        AcceptButton = close;
        CancelButton = close;
        Load += (_, _) => list.SelectedIndex = 0;
    }
}
