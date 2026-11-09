using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal static class OfflineHelp
{

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Topics = new(() =>
    {
        using var stream = typeof(OfflineHelp).Assembly.GetManifestResourceStream("NlcsLegenda.HelpTopics")
            ?? throw new InvalidOperationException("De verwijzingen naar de handleiding ontbreken.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
    });

    internal static IEnumerable<string> Commands => Topics.Value["commands"].Keys;

    public static string Topic(string group, string key) =>
        Topics.Value.TryGetValue(group, out var topics) && topics.TryGetValue(key, out var topic)
            ? topic : "handleiding";

    internal static Uri Address(string assemblyDirectory, string topic)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(topic, "^[a-zA-Z0-9_-]+$"))
            throw new ArgumentException("Ongeldig helponderwerp.", nameof(topic));
        // losse bundel en gezamenlijke installer
        string[] directories = { Path.Join(assemblyDirectory, "Help"), Path.Join(assemblyDirectory, "..", "Help") };
        foreach (var directory in directories)
        {
            string path = Path.GetFullPath(Path.Join(directory, "help-" + topic + ".html"));
            if (File.Exists(path))
                return new Uri(path);
        }
        throw new FileNotFoundException("De offline handleiding ontbreekt. Installeer de plug-in opnieuw met de volledige installer of bundel.");
    }

    public static Uri? Find(string topic)
    {
        try { return Address(Path.GetDirectoryName(typeof(OfflineHelp).Assembly.Location)!, topic); }
        catch (FileNotFoundException) { return null; }
    }

    public static void Open(string topic = "handleiding")
    {
        var directory = Path.GetDirectoryName(typeof(OfflineHelp).Assembly.Location)!;
        Process.Start(new ProcessStartInfo(Address(directory, topic).AbsoluteUri) { UseShellExecute = true });
    }

    public static void Show(IWin32Window owner, string topic)
    {
        try { Open(topic); }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            MessageBox.Show(owner, "De handleiding kon niet worden geopend.\n\n" + ex.Message,
                "NLCS Legenda – hulp", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
