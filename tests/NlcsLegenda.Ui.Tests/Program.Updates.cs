using System.Net.Http;
using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Plugin;
using NlcsLegenda.Update;

internal static partial class Program
{
    internal static UpdateOffer DemonstrationUpdate() => new(new AvailableRelease("1.43.0",
        new Uri("https://github.com/joligej/infracad-helper/releases/tag/v1.43.0"),
        "Voorbeeld van een updatemelding\n\nVerbeteringen aan bronselectie en bediening.\n\nNa downloaden en controleren kun je je werk opslaan. Installatie wacht totdat Civil 3D gesloten is.",
        new ReleaseAsset("NlcsLegenda-1.43.0.msi", new Uri("https://github.com/joligej/infracad-helper/releases/download/v1.43.0/NlcsLegenda-1.43.0.msi"), 100, new string('A', 64)), null), true, "");

    internal static SoftwareUpdateDialog SoftwareDialog() => new("1.42.0", true, DemonstrationUpdate(),
        _ => Task.FromResult<UpdateOffer?>(DemonstrationUpdate()), (_, _, _) => Task.CompletedTask, _ => { }, _ => { }, () => { });

    private static void CheckSoftwareUpdates()
    {
        bool? automatic = null; string? skipped = null; var downloads = 0;
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, DemonstrationUpdate(),
            _ => Task.FromResult<UpdateOffer?>(null), (_, _, _) => { downloads++; return Task.CompletedTask; },
            value => automatic = value, value => skipped = value, () => { }))
        {
            OpenOffscreen(dialog);
            var checkbox = Descendants(dialog).OfType<CheckBox>().Single(); checkbox.Checked = false;
            Check(automatic == false, "updatevoorkeur direct opgeslagen");
            var download = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Downloaden en bijwerken");
            Check(download.Enabled, "geschikte nieuwe update kan worden gekozen");
            Descendants(dialog).OfType<Button>().Single(b => b.Text == "Deze versie overslaan").PerformClick();
            Check(skipped == "1.43.0" && downloads == 0, "overslaan onthoudt versie zonder download");
        }
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, DemonstrationUpdate() with { Compatible = false, Reason = "Niet geschikt voor deze CAD-versie." },
            _ => Task.FromResult<UpdateOffer?>(null), (_, _, _) => Task.CompletedTask, _ => { }, _ => { }, () => { }))
        {
            OpenOffscreen(dialog);
            Check(!Descendants(dialog).OfType<Button>().Single(b => b.Text == "Downloaden en bijwerken").Enabled, "ongeschikte release niet installeerbaar");
        }
        var pending = new TaskCompletionSource<UpdateOffer?>(); CancellationToken cancel = default;
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, null,
            token => { cancel = token; return pending.Task; }, (_, _, _) => Task.CompletedTask, _ => { }, _ => { }, () => { }))
        {
            OpenOffscreen(dialog); dialog.Close(); pending.SetResult(null); Application.DoEvents();
            Check(cancel.IsCancellationRequested, "sluiten annuleert lopende controle zonder late melding");
        }
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, null,
            _ => throw new HttpRequestException("Offline"), (_, _, _) => Task.CompletedTask, _ => { }, _ => { }, () => { }))
        {
            OpenOffscreen(dialog);
            Check(Descendants(dialog).OfType<Label>().Any(l => l.Text.Contains("Offline")), "handmatige netwerkfout zichtbaar in updatevenster");
            Check(!Descendants(dialog).OfType<Button>().Single(b => b.Text == "Downloaden en bijwerken").Enabled, "netwerkfout start geen installatie");
        }
    }
}
