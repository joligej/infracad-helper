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

        if (HostEnvironment.IsCoreConsole)
            return;

        try
        {
            RibbonBuilder.Initialize();
        }
        catch (System.Exception ex)
        {
            ed?.WriteMessage($"\nNLCS Legenda: de werkbalk kon niet worden opgebouwd ({ex.Message}). De commando's werken wel.\n");
        }
    }

    public void Terminate()
    {
        RibbonBuilder.Shutdown();
    }
}
