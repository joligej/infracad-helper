using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(NlcsLegenda.Plugin.Plugin))]

namespace NlcsLegenda.Plugin;

public sealed class Plugin : IExtensionApplication
{
    public void Initialize()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        doc?.Editor.WriteMessage(
            "\nNLCS Legenda geladen. Gebruik het commando NLCSLEGENDA.\n");

        // Headless (accoreconsole) is er geen ribbon; de Idle-route en ribbon-API's zijn dan
        // onnodig en vergroten alleen het risico.
        if (HostEnvironment.IsCoreConsole)
            return;

        try
        {
            RibbonBuilder.Initialize();
        }
        catch
        {
            // Zonder ribbon werkt de plugin gewoon via de commando's.
        }
    }

    public void Terminate()
    {
        RibbonBuilder.Shutdown();
    }
}
