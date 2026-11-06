using System.Diagnostics;
using System.Security.Cryptography;
using NlcsLegenda.Update;

namespace NlcsLegenda.Updater;

internal sealed class UpdaterForm : Form
{
    private readonly UpdateStore _store;
    private readonly UpdateRequest _request;
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly TextBox _details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button _close = new() { Text = "Annuleren", AutoSize = true };
    private readonly Button _retry = new() { Text = "Opnieuw proberen", AutoSize = true, Visible = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private readonly CancellationTokenSource _cancel = new();
    private bool _installing;
    private bool _restartRequired;
    private int _clearChecks;
    private DateTimeOffset _waitUntil = DateTimeOffset.UtcNow.AddHours(2);

    public UpdaterForm(UpdateStore store, UpdateRequest request, bool startAutomatically = true)
    {
        _store = store; _request = request;
        _status.Text = $"Update naar versie {request.Version}";
        _details.Text = "Sla je werk op en sluit AutoCAD, Civil 3D en Core Console. De installatie wacht totdat deze programma’s gesloten zijn. Start Civil 3D daarna opnieuw.";
        Text = "NLCS Legenda bijwerken"; AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96); ClientSize = new Size(590, 310); MinimumSize = new Size(430, 260);
        StartPosition = FormStartPosition.CenterScreen;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3, ColumnCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(_status, 0, 0); body.Controls.Add(_details, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(_close); buttons.Controls.Add(_retry); body.Controls.Add(buttons, 0, 2); Controls.Add(body);
        body.SizeChanged += (_, _) =>
        {
            var width = Math.Max(1, body.ClientSize.Width - body.Padding.Horizontal - 8);
            buttons.MaximumSize = new Size(width, 0);
            _status.MaximumSize = new Size(width, 0);
        };
        _close.Click += (_, _) => Close();
        _retry.Click += async (_, _) => { _retry.Visible = false; _close.Text = "Annuleren"; _clearChecks = 0; _waitUntil = DateTimeOffset.UtcNow.AddHours(2); await PrepareAsync(); };
        if (startAutomatically) Shown += async (_, _) => await PrepareAsync();
        _timer.Tick += async (_, _) => await WaitForCadAsync();
        FormClosing += (_, e) => { if (_installing) e.Cancel = true; else _cancel.Cancel(); };
    }

    private async Task PrepareAsync()
    {
        _restartRequired = false;
        _status.Text = $"Versie {_request.Version} controleren…";
        try
        {
            await Task.Run(() => { UpdateInstallation.RequireNewerVersion(_request); UpdateInstallation.VerifyDownload(_store, _request); }, _cancel.Token);
            if (!IsDisposed) { _timer.Start(); await WaitForCadAsync(); }
        }
        catch (Exception ex) when (UpdateInstallation.IsExpected(ex)) { if (!IsDisposed) Failed(ex); }
    }

    private async Task WaitForCadAsync()
    {
        if (_installing || IsDisposed) return;
        try
        {
            if (DateTimeOffset.UtcNow > _waitUntil) throw new InvalidOperationException("Er is nog niet geïnstalleerd. Start de update opnieuw wanneer je klaar bent met werken.");
            var sessions = UpdateInstallation.ActiveCadSessions();
            if (sessions.Length > 0)
            {
                _clearChecks = 0;
                _status.Text = "Update klaar. Sla je werk op en sluit de CAD-sessies.";
                _details.Text = "De huidige plug-in is nog in gebruik. Installatie begint pas na afsluiten.\r\n\r\n" + string.Join("\r\n", sessions) + "\r\n\r\nStart Civil 3D opnieuw nadat de installatie is voltooid.";
                return;
            }
            if (++_clearChecks < 2) return;
            _timer.Stop(); _installing = true; _close.Enabled = false; ControlBox = false;
            _status.Text = $"Versie {_request.Version} installeren…";
            _details.Text = "Laat AutoCAD en Civil 3D gesloten totdat de installatie klaar is. Je computer wordt niet opnieuw opgestart.";
            var result = await Task.Run(InstallAsync);
            try { UpdateStore.WriteJson(Path.Join(_store.DirectoryPath, "last-result.json"), result); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _store.Log("Uitslag bewaren: " + ex.Message); }
            _status.Text = result.Success ? "Bijwerken voltooid." : "Bijwerken niet voltooid.";
            _details.Text = result.Message; _close.Text = "Sluiten";
        }
        catch (Exception ex) when (UpdateInstallation.IsExpected(ex)) { Failed(ex); }
        finally { if (_installing) { _installing = false; _close.Enabled = true; ControlBox = true; } }
    }

    private async Task<UpdateResult> InstallAsync()
    {
        UpdateInstallation.RequireNewerVersion(_request);
        using var packageLock = new FileStream(UpdateInstallation.PackagePath(_store, _request.Version), FileMode.Open, FileAccess.Read, FileShare.Read);
        UpdateInstallation.VerifyDownload(_store, _request);
        var years = ReleasePolicy.SupportedYears(WindowsPackage.ReadProperties(UpdateInstallation.PackagePath(_store, _request.Version)));
        var snapshot = SettingsSnapshot.Capture(_store, _request.Version);
        if (UpdateInstallation.ActiveCadSessions().Length > 0) throw new InvalidOperationException("Er is opnieuw een CAD-sessie geopend. Sluit CAD en start de update opnieuw.");
        var log = Path.Join(_store.DirectoryPath, "install-" + _request.Version + ".log");
        var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "msiexec.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "/i", UpdateInstallation.PackagePath(_store, _request.Version), "/qn", "/norestart", "MSIRESTARTMANAGERCONTROL=Disable", "REBOOT=ReallySuppress", "/l*v", log }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows Installer kon niet worden gestart.");
        await process.WaitForExitAsync();
        _restartRequired = process.ExitCode == 3010;
        if (process.ExitCode is not (0 or 3010))
        {
            var installed = WindowsPackage.InstalledVersion();
            throw new InvalidOperationException($"Windows Installer heeft de update niet voltooid (code {process.ExitCode}). Geregistreerde versie: {installed ?? "niet gevonden"}.\r\nDetails: {log}");
        }
        if (WindowsPackage.InstalledVersion() != _request.Version) throw new InvalidDataException("De installatie meldt succes, maar de geregistreerde versie wijkt af.");
        if (process.ExitCode == 0) UpdateInstallation.VerifyInstalledFiles(_request.Version, years);
        snapshot.Verify();
        return new(_request.Version, true, process.ExitCode == 3010,
            process.ExitCode == 3010 ? "De update is geïnstalleerd. Windows meldt dat opnieuw opstarten nodig is; sla je werk op en herstart de computer op een geschikt moment."
                : $"NLCS Legenda {_request.Version} is geïnstalleerd. Je instellingen zijn behouden. Start Civil 3D opnieuw om de nieuwe versie te gebruiken.", DateTimeOffset.UtcNow, years);
    }

    private void Failed(Exception exception)
    {
        var message = (_restartRequired ? "Windows moet opnieuw worden opgestart om de installatie af te ronden.\r\n\r\n" : "") + exception.Message;
        _timer.Stop(); _retry.Visible = !_restartRequired; _status.Text = "Bijwerken niet voltooid."; _details.Text = message; _close.Text = "Sluiten";
        _store.Log("Installatie: " + exception);
        try { UpdateStore.WriteJson(Path.Join(_store.DirectoryPath, "last-result.json"), new UpdateResult(_request.Version, false, _restartRequired, message, DateTimeOffset.UtcNow)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _store.Log("Uitslag bewaren: " + ex.Message); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { _timer.Dispose(); _cancel.Cancel(); _cancel.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class SettingsSnapshot(Dictionary<string, string> hashes)
{
    public static SettingsSnapshot Capture(UpdateStore store, string version)
    {
        var directory = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NlcsLegenda");
        var backup = Path.Join(store.DirectoryPath, "SettingsBackup", version, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                var target = Path.Join(backup, Path.GetRelativePath(directory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var bytes = File.ReadAllBytes(file);
                File.WriteAllBytes(target, bytes); hashes.Add(file, Convert.ToHexString(SHA256.HashData(bytes)));
            }
        }
        return new(hashes);
    }

    public void Verify()
    {
        foreach (var pair in hashes)
        {
            using var stream = File.OpenRead(pair.Key);
            if (Convert.ToHexString(SHA256.HashData(stream)) != pair.Value)
                throw new InvalidDataException("Een persoonlijk instellingenbestand is tussentijds gewijzigd. De kopie vóór installatie staat onder Updates/SettingsBackup. Er is niets overheen geschreven.");
        }
    }
}
