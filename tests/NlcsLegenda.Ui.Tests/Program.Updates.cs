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
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, DemonstrationUpdate(),
            _ => Task.FromResult<UpdateOffer?>(null), (_, _, _) => Task.CompletedTask,
            _ => throw new IOException("Bestand in gebruik"), _ => { }, () => { }))
        {
            OpenOffscreen(dialog);
            var checkbox = Descendants(dialog).OfType<CheckBox>().Single(); checkbox.Checked = false;
            Check(checkbox.Checked && Descendants(dialog).OfType<Label>().Any(l => l.Text.Contains("Voorkeur niet opgeslagen")), "mislukte opslag toont de werkelijk bewaarde updatevoorkeur");
        }
        var started = 0;
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, DemonstrationUpdate(),
            _ => Task.FromResult<UpdateOffer?>(null), (_, _, _) => { started++; return Task.CompletedTask; },
            _ => { }, _ => { }, () => throw new IOException("Uitstellen niet opgeslagen")))
        {
            OpenOffscreen(dialog);
            Check(started == 0, "tonen van update start geen download");
            Descendants(dialog).OfType<Button>().Single(b => b.Text == "Downloaden en bijwerken").PerformClick();
            Check(started == 1 && !dialog.Visible, "gestarte updater blijft gestart bij fout in uitstelvoorkeur");
        }
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, DemonstrationUpdate(),
            _ => Task.FromResult<UpdateOffer?>(null), (_, _, _) => throw new IOException("Hash klopt niet"),
            _ => { }, _ => { }, () => { }))
        {
            OpenOffscreen(dialog);
            var download = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Downloaden en bijwerken"); download.PerformClick();
            Check(dialog.Visible && download.Enabled && Descendants(dialog).OfType<Label>().Any(l => l.Text.Contains("Hash klopt niet")), "downloadfout houdt venster open voor herstel");
        }
        var refresh = new TaskCompletionSource<UpdateOffer?>(); var refreshes = 0; var staleDownloads = 0;
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, DemonstrationUpdate(),
            _ => ++refreshes == 1 ? refresh.Task : Task.FromResult<UpdateOffer?>(DemonstrationUpdate()),
            (_, _, _) => { staleDownloads++; return Task.CompletedTask; }, _ => { }, _ => { }, () => { }))
        {
            OpenOffscreen(dialog);
            var check = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Opnieuw controleren");
            var download = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Downloaden en bijwerken");
            var skip = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Deze versie overslaan");
            check.PerformClick();
            Check(!download.Enabled && !skip.Enabled, "hercontrole blokkeert de eerdere releasekeuze");
            refresh.SetException(new HttpRequestException("Offline")); Application.DoEvents();
            download.PerformClick();
            Check(!download.Enabled && !skip.Enabled && staleDownloads == 0, "mislukte hercontrole laat geen oude release downloaden of overslaan");
            Check(Descendants(dialog).OfType<TextBox>().Single().Text.Length == 0, "mislukte hercontrole toont geen oude release-informatie");
            check.PerformClick();
            Check(download.Enabled && skip.Enabled, "geslaagde hercontrole maakt de actuele release weer beschikbaar");
        }
        var snoozed = 0; var checking = new TaskCompletionSource<UpdateOffer?>(); CancellationToken checkingToken = default;
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, null,
            token => { checkingToken = token; return checking.Task; }, (_, _, _) => Task.CompletedTask,
            _ => { }, _ => { }, () => snoozed++))
        {
            OpenOffscreen(dialog);
            Descendants(dialog).OfType<Button>().Single(b => b.Text == "Annuleren").PerformClick();
            checking.SetResult(null); Application.DoEvents();
            Check(checkingToken.IsCancellationRequested && snoozed == 0 && !dialog.Visible, "annuleren stopt de controle zonder een dag uitstel op te slaan");
        }
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, null,
            _ => Task.FromResult<UpdateOffer?>(null), (_, _, _) => Task.CompletedTask,
            _ => { }, _ => { }, () => snoozed++))
        {
            OpenOffscreen(dialog);
            Descendants(dialog).OfType<Button>().Single(b => b.Text == "Sluiten").PerformClick();
            Check(snoozed == 0 && !dialog.Visible, "sluiten zonder update wijzigt de meldingsvoorkeur niet");
        }
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, DemonstrationUpdate(),
            _ => Task.FromResult<UpdateOffer?>(null), (_, _, _) => Task.CompletedTask,
            _ => { }, _ => { }, () => snoozed++))
        {
            OpenOffscreen(dialog);
            Descendants(dialog).OfType<Button>().Single(b => b.Text == "Later").PerformClick();
            Check(snoozed == 1 && !dialog.Visible, "later stelt alleen een daadwerkelijk aangeboden update uit");
        }
        var attempts = 0; IProgress<int>? oldProgress = null;
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, DemonstrationUpdate(),
            _ => Task.FromResult<UpdateOffer?>(null), (_, progress, token) =>
            {
                if (++attempts == 1) { oldProgress = progress; progress.Report(80); throw new IOException("Verbinding verbroken"); }
                return Task.Delay(Timeout.Infinite, token);
            }, _ => { }, _ => { }, () => { }))
        {
            OpenOffscreen(dialog);
            var download = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Downloaden en bijwerken");
            download.PerformClick(); Application.DoEvents();
            var progress = Descendants(dialog).OfType<ProgressBar>().Single();
            Check(progress.Value == 80, "downloadfout na gedeeltelijke voortgang getest");
            download.PerformClick();
            Check(progress.Visible && progress.Value == 0, "nieuwe download begint zonder de voortgang van de vorige poging");
            oldProgress!.Report(95); Application.DoEvents();
            Check(progress.Value == 0, "late voortgang van een mislukte poging wordt genegeerd");
            dialog.Close(); Application.DoEvents();
        }
        var background = new TaskCompletionSource<UpdateOffer?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var dialog = new SoftwareUpdateDialog("1.42.0", true, DemonstrationUpdate(),
            _ => background.Task, (_, _, _) => Task.CompletedTask, _ => { }, _ => { }, () => { }))
        {
            OpenOffscreen(dialog);
            var context = SynchronizationContext.Current;
            var crossThread = Control.CheckForIllegalCrossThreadCalls;
            try
            {
                Control.CheckForIllegalCrossThreadCalls = true;
                SynchronizationContext.SetSynchronizationContext(null);
                var check = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Opnieuw controleren");
                check.PerformClick();
                Task.Run(() => background.SetResult(DemonstrationUpdate())).GetAwaiter().GetResult();
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (!check.Enabled && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
                Check(check.Enabled && Descendants(dialog).OfType<Button>().Single(b => b.Text == "Downloaden en bijwerken").Enabled,
                    "achtergrondresultaat werkt op de vensterthread zonder synchronisatiecontext van de host");
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); Control.CheckForIllegalCrossThreadCalls = crossThread; }
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
