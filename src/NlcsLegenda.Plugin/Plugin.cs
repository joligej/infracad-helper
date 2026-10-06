using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(NlcsLegenda.Plugin.Plugin))]

namespace NlcsLegenda.Plugin;

public sealed class Plugin : IExtensionApplication
{
    public void Initialize()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        ed?.WriteMessage("\nNLCS Legenda geladen. Gebruik het commando NLCSLEGENDA.\n");

        // In headless AutoCAD (accoreconsole) is er geen ribbon.
        if (HostEnvironment.IsCoreConsole)
            return;

        try
        {
            RibbonBuilder.Initialize();
        }
        catch (System.Exception ex)
        {
            // Een ribbonfout mag de plugin niet blokkeren: de commando's blijven werken. Niet stil
            // inslikken, maar op de opdrachtregel melden zodat de oorzaak traceerbaar is.
            ed?.WriteMessage($"\nNLCS Legenda: de werkbalk kon niet worden opgebouwd ({ex.Message}). De commando's werken wel.\n");
        }
    }

    public void Terminate()
    {
        RibbonBuilder.Shutdown();
    }
}
