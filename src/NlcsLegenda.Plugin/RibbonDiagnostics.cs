using System.IO;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDARIBBONTEST", CommandFlags.Modal)]
    public void NlcsLegendaRibbonTest()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        var (tab, panels, buttons, note) = RibbonBuilder.Describe();
        bool ok = tab && panels >= 2 && buttons >= 10;
        string line = $"RIBBON: tab={tab} panels={panels} knoppen={buttons} ({note}) -> {(ok ? "OK" : "FAIL")}";
        ed?.WriteMessage("\n" + line);

        var outPath = System.Environment.GetEnvironmentVariable("NLCS_RIBBON_OUT");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            try { File.WriteAllText(outPath, line + "\n"); }
            catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { ed?.WriteMessage($"\nRIBBON FAIL: controleresultaat niet opgeslagen: {ex.Message}"); }
        }
    }
}
