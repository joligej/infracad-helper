using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAHELP", CommandFlags.Modal)]
    public void NlcsLegendaHelp()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        if (ed is null)
            return;
        if (HostEnvironment.IsCoreConsole)
        {
            ed.WriteMessage("\nNLCSLEGENDAHELP werkt alleen in AutoCAD met venster.");
            return;
        }
        try
        {
            ShowHelp();
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nHelp kon niet worden geopend: {ex.Message}");
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ShowHelp()
    {
        using var dialog = new HelpDialog(PluginVersion);
        AcWindows.ShowModalDialog(dialog);
    }
}
