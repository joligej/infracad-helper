using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;
using NlcsLegenda.Update;

namespace NlcsLegenda.Plugin;

internal sealed class SoftwareUpdateDialog : LegendDialog
{
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly TextBox _notes = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Button _download = new() { Text = "Downloaden en bijwerken", AutoSize = true, Enabled = false };
    private readonly Button _check = new() { Text = "Opnieuw controleren", AutoSize = true };
    private readonly Button _skip = new() { Text = "Deze versie overslaan", AutoSize = true, Enabled = false };
    private readonly Button _page = new() { Text = "Releasepagina", AutoSize = true };
    private readonly Button _laterButton = new() { Text = "Later", AutoSize = true };
    private readonly CheckBox _automatic = new() { Text = "Bij opstarten controleren", AutoSize = true, Dock = DockStyle.Fill };
    private readonly CancellationTokenSource _cancel = new();
    private readonly Func<CancellationToken, Task<UpdateOffer?>> _checkRelease;
    private readonly Func<UpdateOffer, IProgress<int>, CancellationToken, Task> _downloadRelease;
    private readonly Action<string> _skipVersion;
    private readonly Action _later;
    private readonly string? _installationProblem;
    private UpdateOffer? _offer;
    private bool _busy;
    private volatile bool _closing;
    private int _downloadAttempt;

    public SoftwareUpdateDialog(string current, bool automatic, UpdateOffer? initial,
        Func<CancellationToken, Task<UpdateOffer?>> check,
        Func<UpdateOffer, IProgress<int>, CancellationToken, Task> download,
        Action<bool> preference, Action<string> skip, Action later, string? installationProblem = null)
    {
        Text = "NLCS Legenda bijwerken";
        MinimumSize = new Size(480, 350); ClientSize = new Size(660, 470);
        StartPosition = FormStartPosition.CenterParent;
        _checkRelease = check; _downloadRelease = download; _skipVersion = skip; _later = later;
        _installationProblem = installationProblem;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++) body.RowStyles.Add(new RowStyle(i == 2 ? SizeType.Percent : SizeType.AutoSize, i == 2 ? 100 : 0));
        body.Controls.Add(new Label { Text = $"Geïnstalleerd: {current}", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) }, 0, 0);
        body.Controls.Add(_status, 0, 1); body.Controls.Add(_notes, 0, 2); body.Controls.Add(_progress, 0, 3);
        _automatic.Checked = automatic; _automatic.Margin = new Padding(0, 12, 0, 12);
        var savedAutomatic = automatic;
        _automatic.CheckedChanged += (_, _) =>
        {
            if (_automatic.Checked == savedAutomatic) return;
            try { preference(_automatic.Checked); savedAutomatic = _automatic.Checked; }
            catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
            { _automatic.Checked = savedAutomatic; _status.Text = "Voorkeur niet opgeslagen. " + ex.Message; }
        };
        body.Controls.Add(_automatic, 0, 4);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange(new Control[] { _download, _check, _page, _skip, _laterButton });
        var help = new Button { Text = "Hulp (F1)", Name = "HelpButton", AutoSize = true };
        help.Click += (_, _) => OpenHelp(CurrentHelpTopic);
        buttons.Controls.Add(help);
        body.Controls.Add(buttons, 0, 5); Controls.Add(body);
        body.SizeChanged += (_, _) =>
        {
            var width = Math.Max(1, body.ClientSize.Width - body.Padding.Horizontal - 8);
            buttons.MaximumSize = new Size(width, 0);
            _status.MaximumSize = new Size(width, 0);
            _automatic.MaximumSize = new Size(width, 0);
        };
        _laterButton.Click += (_, _) => TryAction(() =>
        {
            if (!_busy && _offer is not null) _later();
            Close();
        });
        _skip.Click += (_, _) => TryAction(() =>
        {
            if (_offer is null) return;
            _skipVersion(_offer.Release.Version);
            Close();
        });
        _page.Click += (_, _) => TryAction(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ReleasePolicy.ReleasesPage) { UseShellExecute = true }));
        _check.Click += async (_, _) => await CheckAsync();
        _download.Click += async (_, _) => await DownloadAsync();
        Shown += async (_, _) => { if (initial is null) await CheckAsync(); else Present(initial); };
        FormClosing += (_, _) => { _closing = true; _cancel.Cancel(); };

    }

    private async Task CheckAsync()
    {
        if (_busy) return;
        _offer = null; _notes.Clear(); Busy(true); _status.Text = "Controleren op updates…";
        try { var offer = await _checkRelease(_cancel.Token); UpdateUi(() => Present(offer)); }
        catch (OperationCanceledException) { UpdateUi(() => _status.Text = "De controle is afgebroken of de server reageert niet op tijd."); }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex)) { UpdateUi(() => _status.Text = "Controleren is niet gelukt. " + ex.Message); }
        finally { UpdateUi(() => Busy(false)); }
    }

    private void Present(UpdateOffer? offer)
    {
        if (IsDisposed) return;
        _offer = offer;
        _status.Text = offer is null ? "Je gebruikt de nieuwste beschikbare stabiele versie." : $"Versie {offer.Release.Version} is beschikbaar.";
        var reason = _installationProblem ?? offer?.Reason ?? "";
        if (reason.Length > 0) _status.Text += "\n" + reason;
        _notes.Text = offer?.Release.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n") ?? "";
        _download.Enabled = offer?.Compatible == true && _installationProblem is null && !_busy;
        _skip.Enabled = offer is not null && !_busy;
        _laterButton.Text = _busy ? "Annuleren" : offer is null ? "Sluiten" : "Later";
    }

    private async Task DownloadAsync()
    {
        if (_busy || _offer is null) return;
        var attempt = ++_downloadAttempt;
        Busy(true); _progress.Value = 0; _progress.Visible = true; _status.Text = "Downloaden en controleren…";
        try
        {
            await _downloadRelease(_offer, new Progress<int>(value => UpdateUi(() => { if (attempt == _downloadAttempt) _progress.Value = Math.Clamp(value, 0, 100); })), _cancel.Token);
            UpdateUi(() => { TryAction(_later); Close(); });
        }
        catch (OperationCanceledException) { UpdateUi(() => _status.Text = "Download afgebroken. Je huidige installatie blijft behouden."); }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex)) { UpdateUi(() => _status.Text = "Bijwerken is niet gestart. " + ex.Message); }
        finally { UpdateUi(() => { _progress.Visible = false; Busy(false); }); }
    }

    private void UpdateUi(Action update)
    {
        if (_closing || IsDisposed) return;
        if (!InvokeRequired) { update(); return; }
        try { BeginInvoke(() => { if (!_closing && !IsDisposed) update(); }); }
        catch (InvalidOperationException) when (_closing || IsDisposed) { return; }
    }

    private void Busy(bool busy)
    {
        _busy = busy; _check.Enabled = !busy; _automatic.Enabled = !busy;
        _laterButton.Text = busy ? "Annuleren" : _offer is null ? "Sluiten" : "Later";
        _download.Enabled = !busy && _offer?.Compatible == true && _installationProblem is null;
        _skip.Enabled = !busy && _offer is not null;
    }

    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex)) { _status.Text = ex.Message; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { _closing = true; _cancel.Cancel(); _cancel.Dispose(); }
        base.Dispose(disposing);
    }
}
