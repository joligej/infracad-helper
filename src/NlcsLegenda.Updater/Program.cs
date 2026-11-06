using System.Text.Json;
using NlcsLegenda.Update;

namespace NlcsLegenda.Updater;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var store = new UpdateStore();
        try
        {
            using var lease = store.TryLease("installation");
            if (lease is null) { MessageBox.Show("Er loopt al een NLCS Legenda-update.", "NLCS Legenda"); return; }
            if (args.Length != 1) throw new InvalidDataException("Open de updater via Updates controleren in NLCS Legenda.");
            var path = Path.GetFullPath(args[0]);
            var allowed = Path.GetFullPath(Path.Join(store.DirectoryPath, "Runner")) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path) != "request.json" || new FileInfo(path).Length > 8192)
                throw new InvalidDataException("De updateopdracht staat op een onverwachte locatie.");
            var request = JsonSerializer.Deserialize<UpdateRequest>(File.ReadAllText(path), ReleasePolicy.JsonOptions)
                ?? throw new InvalidDataException("De updateopdracht ontbreekt.");
            UpdateInstallation.ValidateRequest(request);
            Application.Run(new UpdaterForm(store, request));
        }
        catch (Exception ex) when (UpdateInstallation.IsExpected(ex))
        { store.Log("Updateprogramma: " + ex); MessageBox.Show(ex.Message, "NLCS Legenda bijwerken", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
