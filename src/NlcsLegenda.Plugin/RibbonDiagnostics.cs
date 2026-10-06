using System.IO;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    // Structurele controle van de werkbalk in een GUI-host: bestaat de tab NLCS Legenda en bevat
    // hij de verwachte panels/knoppen? Schrijft het resultaat naar NLCS_RIBBON_OUT als die gezet
    // is, zodat een GUI-start zonder NETLOAD automatisch te verifiëren valt. In accoreconsole is er
    // geen ribbon; dan meldt het commando dat eerlijk.
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
            try { File.WriteAllText(outPath, line + "\n"); } catch { /* verificatiehaak */ }
        }
    }
}
