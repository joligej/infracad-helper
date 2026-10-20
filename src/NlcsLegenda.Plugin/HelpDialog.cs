using System.Drawing;
using System.Windows.Forms;

namespace NlcsLegenda.Plugin;

internal sealed class HelpDialog : LegendDialog
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
            "Open Lokale instellingen voor de gekozen legenda: schaal, inhoud, opmaak en teksten.\n\n" +
            "Bij één aanwezige legenda open je die direct. Bij meerdere kies je welke.\n" +
            "Globale standaard bewerkt alleen de instellingen voor nieuwe legenda's.\n\n" +
            "Legenda → Templateopmaak herstellen zet de maten terug; eigen teksten blijven behouden.\n\nToepassen werkt de legenda direct bij. Opslaan doet dat ook en sluit het venster."),
        ("Overzicht en volgorde",
            "Overzicht leest alleen de gekozen legenda. Vernieuwen leest de bron opnieuw.\n\n" +
            "Kies Handmatig en verplaats regels met de knoppen of Alt+↑ / Alt+↓.\n" +
            "Toepassen werkt de legenda bij; Opslaan sluit het venster. Elke legenda\n" +
            "onthoudt haar eigen volgorde, ook na opnieuw openen.\n\n" +
            "Selecteer meerdere regels met Ctrl of Shift en kies Samenvoegen.\n" +
            "Het hoofdobject bepaalt de titel en weergave. Samenvoeging opheffen draait dit terug."),
        ("KLIC-groepering",
            "Een aangevinkt kenmerk wordt niet vergeleken. Soorten samenvoegen staat uit.\n" +
            "Spanning/druk, uitvoering, volgnummer en netbeheerder staan voor KLIC aan.\n" +
            "Kleur, lijn en symbool moeten gelijk blijven. Overige lagen hebben eigen keuzes.\n\n" +
            "Netbeheerders worden uit de InfraCAD-metadata van de bron gelezen.\n" +
            "Samenstellen toont de afzonderlijke bronregels om aan of uit te zetten."),
        ("KLIC-gegevens",
            "Kies in Overzicht → KLIC-instellingen welke gegevens je wilt tonen en exporteren.\n\n" +
            "Legenda-omschrijving voegt gekozen velden aan de tekst toe. Afzonderlijke regels\n" +
            "splitst bijvoorbeeld verschillende materialen en diameters. De keuzes gelden\n" +
            "voor één legenda. Wis het zoekfilter voordat je regels handmatig verplaatst.\n\n" +
            "KLIC-details toont waarden per bron en tabel, bronverschillen en bijlagen.\n" +
            "Gegevensbron bepaalt welke opslagvorm voorrang krijgt. Object Data vereist\n" +
            "een geopende bron-DWG in Civil 3D/Map; xrefs gebruiken de overige leesbare\n" +
            "gegevens. Niet-leesbare bronnen worden gemeld.\n\n" +
            "Een nulwaarde is geen gemeten diepte. Een standaarddiepte is apart benoemd."),
        ("Samenstellen en zoeken",
            "Samenstellen heeft aparte tabbladen voor bronregels en eigen regels. Zoek op woorden of een deel van de laagnaam\n" +
            "of omschrijving; * kan meerdere delen combineren. Een groepsvinkje verandert\n" +
            "tijdens zoeken alleen de zichtbare resultaten.\n\n" +
            "In tekening uitsluiten: klik meerdere objecten, ook binnen xrefs.\n" +
            "Enter past toe; Escape annuleert de aanklikronde. De bronobjecten blijven staan.\n\n" +
            "Laag toevoegen opent een doorzoekbare lijst. Een eigen regel heeft geen gemeten\n" +
            "hoeveelheid. Koppel een Eigen laag als de brongeometrie wel moet meetellen."),
        ("Voorbeelden",
            "Overzicht → Voorbeeld kiest een volledig voorbeeld uit een DWG-bibliotheek.\n\n" +
            "Gebruiken koppelt het aan deze regel; Als nieuwe regel voegt het los toe.\n" +
            "Uit tekening gebruikt de brongeometrie. Automatisch kiest alleen bij een\n" +
            "eenduidige match. Gebruikte voorbeelden blijven in de DWG bewaard.\n\n" +
            "Neem de bibliotheek mee naar een andere computer voor nog niet gebruikte voorbeelden."),
        ("Profielen",
            "Profielen bewaart een complete opzet, met instellingen en eigen omschrijvingen.\n\n" +
            "Kies bij Opslaan of Laden expliciet Legenda of Standaard. Standaard werkt ook\n" +
            "zonder legenda en verandert bestaande legenda's niet.\n\n" +
            "Importeren en Exporteren delen een profiel als JSON; een voorbeeldenbibliotheek\n" +
            "en het lettertype moeten afzonderlijk worden meegenomen."),
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
            "Lagen met dezelfde inhoud en weergave worden samengevoegd. Verschillende\n" +
            "kleuren of voorbeelden krijgen aparte regels."),
        ("Hoeveelheden",
            "De legenda telt lengtes, aantallen en oppervlakten uit de tekening.\n\n" +
            "Zet hoeveelheden aan of uit in de instellingen. Overzicht laat de totalen zien\n" +
            "zonder te tekenen.\n\nEen onmeetbaar oppervlak krijgt ? m². Overzicht → Broncontrole noemt de bronlaag\nen het object; herstel de broncontour en werk de legenda bij."),
        ("Export",
            "Exporteren schrijft CSV en JSON met hoeveelheden, omschrijvingen en bronlagen.\n\n" +
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
            Size = new Size(640, 380),
            Panel1MinSize = 120,
            Panel2MinSize = 200,
            SplitterDistance = 170,
            Padding = new Padding(8)
        };

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, TabIndex = 0 };
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
