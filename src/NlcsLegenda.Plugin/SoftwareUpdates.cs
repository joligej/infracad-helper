using System.IO;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using NlcsLegenda.Update;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

internal static class SoftwareUpdates
{
    private static readonly UpdateStore Store = new();
    private static readonly ReleaseVersion Version = ReleaseVersion.FromAssembly(typeof(Plugin).Assembly.GetName().Version!);
#if ACAD2027
    private const int AcadYear = 2027;
#elif ACAD2026
    private const int AcadYear = 2026;
#else
    private const int AcadYear = 2025;
#endif
    private static CancellationTokenSource? _cancel;
    private static Task<UpdateOffer?>? _check;
    private static FileStream? _lease;
    private static SoftwareUpdateDialog? _dialog;
    private static bool _idleHooked;
    private static bool _confirmed;

    public static void Initialize()
    {
        if (HostEnvironment.IsCoreConsole || _idleHooked) return;
        _cancel = new(); _check = null;
        AcApp.Idle += OnIdle; _idleHooked = true;
    }

    private static void OnIdle(object? sender, EventArgs args)
    {
        var document = AcApp.DocumentManager.MdiActiveDocument;
        if (document is null || document.CommandInProgress.Length > 0) return;
        try
        {
            if (!_confirmed)
            {
                _confirmed = true;
                var message = UpdateInstallation.ConfirmLoaded(Store, Version);
                if (message.Length > 0) document.Editor.WriteMessage("\n" + message + "\n");
            }
            if (_check is null)
            {
                _lease = Store.TryLease("notification");
                if (_lease is null) { Unhook(); return; }
                var token = _cancel!.Token;
                _check = Task.Run(() => CheckAsync(false, token), token);
                return;
            }
            if (!_check.IsCompleted) return;
            Unhook();
            var offer = _check.GetAwaiter().GetResult();
            if (offer is null) ReleaseLease();
            else ShowDialog(offer);
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex))
        { Store.Log("Automatische controle: " + ex.Message); Unhook(); ReleaseLease(); }
    }

    public static void Show()
    {
        if (HostEnvironment.IsCoreConsole) return;
        if (_dialog is not null && !_dialog.IsDisposed) { _dialog.Activate(); return; }
        if (_lease is null && (_lease = Store.TryLease("notification")) is null)
        { AcApp.ShowAlertDialog("Er loopt al een updatecontrole of er staat een updatevenster open in een andere CAD-sessie."); return; }
        Unhook();
        _cancel?.Cancel();
        try { ShowDialog(null, _check); }
        catch { ReleaseLease(); throw; }
    }

    private static void ShowDialog(UpdateOffer? offer, Task<UpdateOffer?>? previousCheck = null)
    {
        var problem = UpdateInstallation.Eligibility(typeof(Plugin).Assembly.Location, Version, AcadYear);
        var dialog = new SoftwareUpdateDialog(Version.ToString(), Store.Load().Automatic, offer,
            token => CheckForDialogAsync(previousCheck, token), DownloadAsync,
            automatic => Store.Change(s => s.Automatic = automatic),
            version => Store.Change(s => s.SkippedVersion = version),
            () => Store.Change(s => s.SnoozeUntil = DateTimeOffset.UtcNow.AddDays(1)), problem);
        _dialog = dialog;
        dialog.FormClosed += (_, _) => { _dialog = null; ReleaseLease(); dialog.Dispose(); };
        AcApp.ShowModelessDialog(dialog);
    }

    private static async Task<UpdateOffer?> CheckForDialogAsync(Task<UpdateOffer?>? previousCheck, CancellationToken token)
    {
        if (previousCheck is not null)
        {
            try { await previousCheck.WaitAsync(token).ConfigureAwait(false); }
            catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex) && !token.IsCancellationRequested)
            {
                if (ex is not OperationCanceledException) Store.Log("Vorige updatecontrole: " + ex.Message);
            }
        }
        token.ThrowIfCancellationRequested();
        return await CheckAsync(true, token).ConfigureAwait(false);
    }

    private static async Task<UpdateOffer?> CheckAsync(bool manual, CancellationToken token)
    {
        UpdateCache.Clean(Store, Version);
        using var client = new ReleaseClient();
        return await new UpdateService(Store, client).CheckAsync(Version, AcadYear, manual, token).ConfigureAwait(false);
    }

    private static async Task DownloadAsync(UpdateOffer offer, IProgress<int> progress, CancellationToken token)
    {
        using var client = new ReleaseClient();
        var request = new UpdateRequest(1, offer.Release.Version, offer.Release.Installer.Sha256, offer.Release.Installer.Size, AcadYear);
        var path = UpdateInstallation.PackagePath(Store, request.Version);
        await client.DownloadAsync(offer.Release.Installer, path, progress, token).ConfigureAwait(false);
        await Task.Run(() => UpdateInstallation.VerifyDownload(Store, request), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        UpdateInstallation.Launch(Store, request);
    }

    private static void Unhook()
    {
        if (_idleHooked) AcApp.Idle -= OnIdle;
        _idleHooked = false;
    }

    private static void ReleaseLease() { _lease?.Dispose(); _lease = null; }

    public static void Shutdown()
    {
        Unhook(); _cancel?.Cancel();
        _dialog?.Close(); _dialog = null;
        // lopende controle buiten de cad-afsluiting afronden
        if (_check is { IsCompleted: false } task)
            _ = task.ContinueWith(t => { _ = t.Exception; ReleaseLease(); }, TaskScheduler.Default);
        else ReleaseLease();
        _cancel?.Dispose(); _cancel = null;
    }
}

public partial class Commands
{
    [CommandMethod("NLCS_LEGENDA", "NLCSLEGENDAUPDATES", null, CommandFlags.Modal, null, "help-NLCSLEGENDAUPDATES.html", "")]
    public void NlcsLegendaUpdates()
    {
        if (HostEnvironment.IsCoreConsole) return;
        try { SoftwareUpdates.Show(); }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex))
        { AcApp.ShowAlertDialog("Updates konden niet worden geopend. " + ex.Message); }
    }
}
