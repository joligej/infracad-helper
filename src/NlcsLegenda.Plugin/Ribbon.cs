using System.Windows.Input;
using Autodesk.Windows;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

// Alleen in de GUI: in headless AutoCAD is er geen ribbon.
internal static class RibbonBuilder
{
    private const string TabId = "NLCSLEGENDA_TAB";

    public static void Initialize()
    {
        if (TryBuild())
            return;
        AcWindows.Idle += OnIdle;
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        try
        {
            if (TryBuild())
                AcWindows.Idle -= OnIdle;
        }
        catch
        {
            // Nooit de Idle-lus laten crashen; zonder ribbon werkt de plugin via commando's.
            AcWindows.Idle -= OnIdle;
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

        var maken = new RibbonPanelSource { Title = "Legenda" };
        tab.Panels.Add(new RibbonPanel { Source = maken });
        maken.Items.Add(Button("Genereren", "NLCSLEGENDA",
            "Analyseer de tekening en plaats de legenda met de muis."));
        maken.Items.Add(Button("Bijwerken", "NLCSLEGENDAUPDATE",
            "Teken de legenda opnieuw op exact dezelfde plek."));
        maken.Items.Add(Button("Overzicht", "NLCSLEGENDAINFO",
            "Toon wat er in de legenda zou komen, zonder te tekenen."));
        maken.Items.Add(Button("Exporteren", "NLCSLEGENDAEXPORT",
            "Schrijf de regels weg als CSV en JSON."));
        maken.Items.Add(Button("Batch", "NLCSLEGENDABATCH",
            "Alle DWG's in een map samen in één uittrekstaat."));
        maken.Items.Add(Button("Viewport", "NLCSLEGENDAVIEWPORT",
            "Maak in de huidige layout een viewport rond de legenda."));

        var beheer = new RibbonPanelSource { Title = "Instellingen" };
        tab.Panels.Add(new RibbonPanel { Source = beheer });
        beheer.Items.Add(Button("Instellingen", "NLCSLEGENDAOPTIES",
            "Schaal, teksten en opmaak aanpassen in een venster."));
        beheer.Items.Add(Button("Samenstellen", "NLCSLEGENDABEHEER",
            "Regels uitvinken en eigen regels toevoegen."));
        beheer.Items.Add(Button("Omschrijvingen", "NLCSLEGENDAOMSCHRIJVINGEN",
            "De tekst per element bewerken in een tabel."));
        beheer.Items.Add(Button("Elementtekst", "NLCSLEGENDATEKST",
            "Klik een element aan en pas alleen die tekst aan."));
        beheer.Items.Add(Button("Statussen", "NLCSLEGENDASTATUS",
            "Eigen statussen maken en er regels aan toewijzen."));
        beheer.Items.Add(Button("Xrefs", "NLCSLEGENDAXREFS",
            "Per gekoppelde xref instellen of die wordt meegenomen."));
        beheer.Items.Add(Button("Profielen", "NLCSLEGENDAPRESET",
            "Legenda-instellingen als profiel opslaan en later opnieuw gebruiken."));
        beheer.Items.Add(Button("Bestanden", "NLCSLEGENDACONFIG",
            "De configuratiebestanden aanmaken en de paden tonen."));
    }

    private static RibbonButton Button(string text, string command, string tooltip)
    {
        return new RibbonButton
        {
            Text = text,
            ShowText = true,
            ShowImage = true,
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
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
