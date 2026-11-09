using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using NlcsLegenda.Core;
using NlcsLegenda.Plugin;

internal static partial class Program
{
    private static void CheckOfflineHelp()
    {
        using var dialog = new SettingsDialog(new LegendSettings(), "Lokale instellingen");
        var opened = new List<string>();
        dialog.OpenHelp = opened.Add;
        OpenOffscreen(dialog);
        var tabs = Descendants(dialog).OfType<TabControl>().Single();
        string[] expected = { "handleiding--instellingen", "handleiding--bronnen-kiezen", "handleiding--regels-samenvoegen",
            "handleiding--schaal-en-opmaak", "handleiding--teksten-en-statussen", "handleiding--hoeveelheden-en-export",
            "handleiding--opmerkingen-en-schaalbalk", "template-maatvoering" };
        for (int index = 0; index < tabs.TabPages.Count; index++)
        {
            tabs.SelectedIndex = index;
            Application.DoEvents();
            var help = Descendants(dialog).OfType<Button>().Single(button => button.Name == "HelpButton");
            help.PerformClick();
            Check(opened.Last() == expected[index], "hulpknop volgt het gekozen instellingenonderdeel");
            var request = new HelpEventArgs(Point.Empty);
            typeof(LegendDialog).GetMethod("OnHelpRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, new object[] { request });
            Check(request.Handled && opened.Last() == expected[index], "F1 opent dezelfde uitleg zonder AutoCAD-hulp ernaast");
            Check(dialog.Visible && dialog.DialogResult == DialogResult.None, "hulp laat de instellingen open");
        }
        string root = Path.Join(Path.GetTempPath(), "NlcsHelp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var contents = Path.Join(root, "spatie & teken #", "Contents");
            Directory.CreateDirectory(Path.Join(contents, "Help"));
            File.WriteAllText(Path.Join(contents, "Help", "help-NLCSLEGENDA.html"), "help");
            var standalone = OfflineHelp.Address(contents, "NLCSLEGENDA");
            var combined = OfflineHelp.Address(Path.Join(contents, "net10"), "NLCSLEGENDA");
            Check(standalone == combined && standalone.Fragment.Length == 0 && standalone.LocalPath.EndsWith("help-NLCSLEGENDA.html"), "ZIP en MSI vinden dezelfde handleiding");
            Check(File.Exists(standalone.LocalPath), "spaties, ampersand en hekje blijven deel van het bestandspad");
            bool missing = false;
            try { OfflineHelp.Address(root, "handleiding"); } catch (FileNotFoundException) { missing = true; }
            Check(missing, "ontbrekende handleiding geeft een herstelbare fout");
        }
        finally { Directory.Delete(root, true); }
    }
}
