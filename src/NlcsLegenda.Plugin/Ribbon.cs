using System.Windows.Input;
using Autodesk.Windows;
using NlcsLegenda.Core;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

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
        // lint herstellen na werkruimtewissel of herladen
        if (!_sysvarHooked)
        {
            AcWindows.SystemVariableChanged += OnSystemVariableChanged;
            _sysvarHooked = true;
        }
        if (TryBuild())
            return;
        AcWindows.Idle += OnIdle;
        _idleHooked = true;
    }

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
        int buttons = tab.Panels.Sum(p => p.Source?.Items.OfType<RibbonRowPanel>().Sum(row => row.Items.OfType<RibbonButton>().Count()) ?? 0);
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
        AddPanel(tab, "Legenda",
            ("Genereren", "NLCSLEGENDA", "Maak een legenda van de tekening of selectie."),
            ("Bijwerken", "NLCSLEGENDAUPDATE", "Teken een bestaande legenda opnieuw."),
            ("Overzicht", "NLCSLEGENDAINFO", "Bekijk alle regels en pas de volgorde aan."));
        AddPanel(tab, "Inhoud",
            ("Samenstellen", "NLCSLEGENDASAMENSTELLEN", "Regels uitvinken en eigen regels toevoegen."),
            ("Omschrijvingen", "NLCSLEGENDAOMSCHRIJVINGEN", "De tekst per element bewerken."),
            ("Elementtekst", "NLCSLEGENDATEKST", "Eén aangeklikte regel aanpassen."),
            ("Statussen", "NLCSLEGENDASTATUS", "Eigen statussen maken en regels toewijzen."),
            ("Xrefs", "NLCSLEGENDAXREFS", "Kiezen welke xrefs meetellen."),
            ("Laagnaam", "NLCSLEGENDALAAGNAAM", "Een NLCS-laagnaam bewerken."));
        AddPanel(tab, "Uitvoer",
            ("Viewport", "NLCSLEGENDAVIEWPORT", "Maak een viewport rond de legenda."),
            ("Exporteren", "NLCSLEGENDAEXPORT", "Schrijf de regels weg als CSV en JSON."),
            ("Batch", "NLCSLEGENDABATCH", "Alle tekeningen in een map in één uittrekstaat."));
        AddPanel(tab, "Instellingen",
            ("Instellingen", "NLCSLEGENDAOPTIES", "Schaal, opmaak en inhoud van een legenda."),
            ("Standaard", "NLCSLEGENDASTANDAARD", "Instellingen voor nieuwe legenda's."),
            ("Profielen", "NLCSLEGENDAPRESET", "Instellingen bewaren en opnieuw gebruiken."));
        AddPanel(tab, "Beheer en hulp",
            ("Legenda's", "NLCSLEGENDABEHEER", "Legenda's hernoemen, opzoeken en verwijderen."),
            ("Controleren", "NLCSLEGENDAELEMENT", "Zie of een aangeklikt object in de legenda komt."),
            ("Bestanden", "NLCSLEGENDACONFIG", "De instellingenbestanden en hun paden tonen."),
            ("Help", "NLCSLEGENDAHELP", "Uitleg over de functies."));
        ribbon.Tabs.Add(tab);
    }

    private static void AddPanel(RibbonTab tab, string title, params (string Text, string Command, string Tip)[] buttons)
    {
        var source = new RibbonPanelSource { Title = title };
        tab.Panels.Add(new RibbonPanel { Source = source });
        for (int start = 0; start < buttons.Length; start += 3)
        {
            var column = new RibbonRowPanel();
            for (int i = start; i < Math.Min(start + 3, buttons.Length); i++)
            {
                if (i > start) column.Items.Add(new RibbonRowBreak());
                var (text, command, tip) = buttons[i];
                column.Items.Add(new RibbonButton
                {
                    Text = text, ShowText = true, ShowImage = true, Size = RibbonItemSize.Standard,
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    IsToolTipEnabled = true, Image = RibbonIcons.Get(command, 16), LargeImage = RibbonIcons.Get(command, 32),
                    CommandParameter = command, CommandHandler = CommandRunner.Instance,
                    ToolTip = new RibbonToolTip { Title = text, Content = tip, Command = command }
                });
            }
            source.Items.Add(column);
        }
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
