using System.Windows.Input;
using Autodesk.Windows;
using NlcsLegenda.Core;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

// Alleen in de GUI: in headless AutoCAD is er geen ribbon.
internal static class RibbonBuilder
{
    private const string TabId = "NLCSLEGENDA_TAB";
    private static bool _idleHooked;
    private static bool _sysvarHooked;
    private static string? _lastError;

    public static void Initialize()
    {
        if (HostEnvironment.IsCoreConsole)
            return;
        // Een werkruimte-wissel of CUI-herlaad bouwt het lint opnieuw op en gooit runtime-tabs weg.
        // Daarom luisteren we naar WSCURRENT en zetten de tab zo nodig terug.
        if (!_sysvarHooked)
        {
            AcWindows.SystemVariableChanged += OnSystemVariableChanged;
            _sysvarHooked = true;
        }
        if (TryBuild())
            return;
        // Ribbon nog niet beschikbaar bij het laden: één keer op de Idle-lus wachten tot hij er is.
        AcWindows.Idle += OnIdle;
        _idleHooked = true;
    }

    // Afmelden bij terminate zodat er geen handlers blijven hangen.
    public static void Shutdown()
    {
        if (_idleHooked)
        {
            AcWindows.Idle -= OnIdle;
            _idleHooked = false;
        }
        if (_sysvarHooked)
        {
            AcWindows.SystemVariableChanged -= OnSystemVariableChanged;
            _sysvarHooked = false;
        }
    }

    // Na een werkruimte-wissel (WSCURRENT) is onze tab weg; opnieuw opbouwen als hij ontbreekt.
    private static void OnSystemVariableChanged(object? sender,
        Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
    {
        if (!string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            if (!TryBuild() && !_idleHooked)
            {
                AcWindows.Idle += OnIdle;
                _idleHooked = true;
            }
        }
        catch (System.Exception ex)
        {
            _lastError = ex.Message;
        }
    }

    // Structurele beschrijving voor de GUI-diagnostic: is de tab aanwezig en met hoeveel knoppen?
    public static (bool Tab, int Panels, int Buttons, string Note) Describe()
    {
        if (HostEnvironment.IsCoreConsole)
            return (false, 0, 0, "core console: geen ribbon");
        var ribbon = ComponentManager.Ribbon;
        if (ribbon is null)
            return (false, 0, 0, "ribbon nog niet beschikbaar");
        var tab = ribbon.FindTab(TabId);
        if (tab is null)
            return (false, 0, 0, _lastError ?? "tab niet gevonden");
        int buttons = tab.Panels.Sum(p => p.Source?.Items.Count ?? 0);
        return (true, tab.Panels.Count, buttons, "ok");
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        try
        {
            if (TryBuild())
            {
                AcWindows.Idle -= OnIdle;
                _idleHooked = false;
            }
        }
        catch (System.Exception ex)
        {
            // Nooit de Idle-lus laten crashen; zonder ribbon werkt de plugin via de commando's.
            _lastError = ex.Message;
            AcWindows.Idle -= OnIdle;
            _idleHooked = false;
        }
    }

    private static bool TryBuild()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon is null)
            return false;
        if (ribbon.FindTab(TabId) is null)
            Build(ribbon);
        return true;
    }

    private static void Build(RibbonControl ribbon)
    {
        var tab = new RibbonTab { Title = "NLCS Legenda", Id = TabId };
        ribbon.Tabs.Add(tab);

        // Hoofdworkflow: groot en vooraan.
        var legenda = Panel(tab, "Legenda");
        legenda.Items.Add(Button("Genereren", "NLCSLEGENDA",
            "Maak een legenda van de tekening of selectie."));
        legenda.Items.Add(Button("Bijwerken", "NLCSLEGENDAUPDATE",
            "Teken een bestaande legenda opnieuw."));
        legenda.Items.Add(Button("Legenda's", "NLCSLEGENDABEHEER",
            "Legenda's bekijken, bijwerken, hernoemen en verwijderen."));

        // Controleren en uitvoeren.
        var uitvoer = Panel(tab, "Uitvoer");
        uitvoer.Items.Add(Button("Overzicht", "NLCSLEGENDAINFO",
            "Laat zien wat erin komt, zonder te tekenen.", large: false));
        uitvoer.Items.Add(Button("Controleren", "NLCSLEGENDAELEMENT",
            "Klik een object aan en zie of het in de legenda komt.", large: false));
        uitvoer.Items.Add(Button("Exporteren", "NLCSLEGENDAEXPORT",
            "Schrijf de regels weg als CSV en JSON.", large: false));
        uitvoer.Items.Add(Button("Batch", "NLCSLEGENDABATCH",
            "Alle tekeningen in een map in één uittrekstaat.", large: false));
        uitvoer.Items.Add(Button("Viewport", "NLCSLEGENDAVIEWPORT",
            "Maak een viewport rond de legenda.", large: false));

        // Inhoud van de legenda aanpassen.
        var inhoud = Panel(tab, "Inhoud");
        inhoud.Items.Add(Button("Instellingen", "NLCSLEGENDAOPTIES",
            "Schaal, opmaak en inhoud instellen.", large: false));
        inhoud.Items.Add(Button("Samenstellen", "NLCSLEGENDASAMENSTELLEN",
            "Regels uitvinken en eigen regels toevoegen.", large: false));
        inhoud.Items.Add(Button("Omschrijvingen", "NLCSLEGENDAOMSCHRIJVINGEN",
            "De tekst per element bewerken.", large: false));
        inhoud.Items.Add(Button("Elementtekst", "NLCSLEGENDATEKST",
            "Eén aangeklikte regel aanpassen.", large: false));

        // Bronnen en profielen.
        var bronnen = Panel(tab, "Bronnen");
        bronnen.Items.Add(Button("Statussen", "NLCSLEGENDASTATUS",
            "Eigen statussen maken en regels toewijzen.", large: false));
        bronnen.Items.Add(Button("Xrefs", "NLCSLEGENDAXREFS",
            "Kiezen welke xrefs meetellen.", large: false));
        bronnen.Items.Add(Button("Profielen", "NLCSLEGENDAPRESET",
            "Instellingen bewaren en opnieuw gebruiken.", large: false));
        bronnen.Items.Add(Button("Laagnaam", "NLCSLEGENDALAAGNAAM",
            "Een NLCS-laagnaam bewerken en de laag hernoemen.", large: false));
        bronnen.Items.Add(Button("Bestanden", "NLCSLEGENDACONFIG",
            "De instellingenbestanden en hun paden tonen.", large: false));

        // Help apart, aan de rechterkant.
        var help = Panel(tab, "Help");
        help.Items.Add(Button("Help", "NLCSLEGENDAHELP",
            "Korte uitleg over de belangrijkste functies."));
    }

    private static RibbonPanelSource Panel(RibbonTab tab, string title)
    {
        var source = new RibbonPanelSource { Title = title };
        tab.Panels.Add(new RibbonPanel { Source = source });
        return source;
    }

    private static RibbonButton Button(string text, string command, string tooltip, bool large = true)
    {
        return new RibbonButton
        {
            Text = text,
            ShowText = true,
            ShowImage = true,
            Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
            Orientation = large
                ? System.Windows.Controls.Orientation.Vertical
                : System.Windows.Controls.Orientation.Horizontal,
            IsToolTipEnabled = true,
            LargeImage = RibbonIcons.Get(command, 32),
            Image = RibbonIcons.Get(command, 16),
            CommandParameter = command,
            CommandHandler = CommandRunner.Instance,
            ToolTip = new RibbonToolTip { Title = text, Content = tooltip, Command = command }
        };
    }
}

internal sealed class CommandRunner : ICommand
{
    public static readonly CommandRunner Instance = new();

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        // Bij een klik geeft AutoCAD de RibbonButton door; de commandotekst staat in
        // CommandParameter. (Een rechtstreekse string ondersteunen we ook.)
        var command = parameter switch
        {
            RibbonButton button => button.CommandParameter as string,
            string text => text,
            _ => null
        };
        if (string.IsNullOrEmpty(command))
            return;
        AcWindows.DocumentManager.MdiActiveDocument?.SendStringToExecute(command + " ", true, false, true);
    }
}
