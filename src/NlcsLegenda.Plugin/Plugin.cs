using Autodesk.AutoCAD.Runtime;
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

        try
        {
            RibbonBuilder.Initialize();
        }
        catch
        {
            // Zonder ribbon (bijv. headless) werkt de plugin gewoon via de commando's.
        }
    }

    public void Terminate()
    {
    }
}
