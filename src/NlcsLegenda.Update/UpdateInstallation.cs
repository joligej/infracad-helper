using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace NlcsLegenda.Update;

public sealed record UpdateRequest(int Schema, string Version, string Sha256, long Size, int AcadYear);
public sealed record UpdateResult(string Version, bool Success, bool RestartRequired, string Message, DateTimeOffset Time, int[]? AcadYears = null);

[SupportedOSPlatform("windows")]
public static class UpdateInstallation
{
    public static string BundlePath => Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins", "NlcsLegenda.bundle");
    public static string UpdaterPath => Path.Join(BundlePath, "Contents", "Updater", "NlcsLegenda.Updater.exe");
    public static string PluginPath(int year) => Path.Join(BundlePath, "Contents", year == 2027 ? "net10" : $"net8-{year}", "NlcsLegenda.dll");
    public static string PackagePath(UpdateStore store, string version)
    {
        if (!ReleaseVersion.TryParse(version, out _)) throw new InvalidDataException("Ongeldige updateversie.");
        return Path.Join(store.DirectoryPath, "Packages", version, $"NlcsLegenda-{version}.msi");
    }

    public static string? Eligibility(string loadedPath, ReleaseVersion loaded, int year)
    {
        if (year is not (2025 or 2026 or 2027) || !Path.GetFullPath(loadedPath).Equals(Path.GetFullPath(PluginPath(year)), StringComparison.OrdinalIgnoreCase))
            return "Deze plug-in komt uit een losse of afwijkende installatie. Gebruik de handmatige installer.";
        var registered = WindowsPackage.InstalledVersion();
        if (registered != loaded.ToString())
            return "De geladen versie wijkt af van de geregistreerde installatie. Herstart Civil 3D of gebruik de handmatige installer.";
        if (!File.Exists(UpdaterPath)) return "Het updateprogramma ontbreekt. Gebruik de handmatige installer.";
        return null;
    }

    public static void ValidateRequest(UpdateRequest request)
    {
        if (request.Schema != 1 || !ReleaseVersion.TryParse(request.Version, out _) || !ReleasePolicy.IsSha256(request.Sha256) ||
            request.Size <= 0 || request.Size > ReleasePolicy.MaximumInstallerBytes || request.AcadYear is not (2025 or 2026 or 2027))
            throw new InvalidDataException("De updateopdracht is ongeldig.");
    }

    public static void VerifyDownload(UpdateStore store, UpdateRequest request)
    {
        ValidateRequest(request);
        var path = PackagePath(store, request.Version);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != request.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(request.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Het gedownloade bestand is gewijzigd of onvolledig.");
        ReleaseVersion.TryParse(request.Version, out var expected);
        WindowsPackage.VerifyInstaller(path, expected, request.AcadYear);
    }

    public static void Launch(UpdateStore store, UpdateRequest request)
    {
        ValidateRequest(request);
        WindowsPackage.VerifySignature(UpdaterPath);
        var runner = Path.Join(store.DirectoryPath, "Runner", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runner);
        var executable = Path.Join(runner, "NlcsLegenda.Updater.exe");
        File.Copy(UpdaterPath, executable);
        WindowsPackage.VerifySignature(executable);
        var requestPath = Path.Join(runner, "request.json");
        UpdateStore.WriteJson(requestPath, request);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = runner };
        start.ArgumentList.Add(requestPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Het updateprogramma kon niet worden gestart.");
    }

    public static string[] ActiveCadSessions()
    {
        var names = new List<string>();
        foreach (var name in new[] { "acad", "accoreconsole" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try { if (!process.HasExited) names.Add($"{(name == "acad" ? "AutoCAD / Civil 3D" : "Core Console")} (proces {process.Id})"); }
                    catch (InvalidOperationException) { continue; }
                    catch (Win32Exception) { names.Add("Een CAD-sessie waarvan de toestand niet kon worden gelezen"); }
                }
            }
        }
        return names.ToArray();
    }

    public static void RequireNewerVersion(UpdateRequest request)
    {
        var installed = WindowsPackage.InstalledVersion();
        if (!ReleaseVersion.TryParse(installed, out var current) || !ReleaseVersion.TryParse(request.Version, out var target))
            throw new InvalidDataException("De bestaande gebruikersinstallatie kon niet worden vastgesteld.");
        if (target.CompareTo(current) <= 0) throw new InvalidOperationException("Deze versie is al geïnstalleerd of is ouder dan de huidige installatie. Herstart Civil 3D.");
    }

    public static void VerifyInstalledFiles(string expected, IEnumerable<int> years)
    {
        foreach (var year in years)
        {
            if (year is not (2025 or 2026 or 2027)) throw new InvalidDataException("Onbekende CAD-versie in de update-uitslag.");
            foreach (var file in new[] { "NlcsLegenda.dll", "NlcsLegenda.Core.dll", "NlcsLegenda.Update.dll" })
            {
                var path = Path.Join(Path.GetDirectoryName(PluginPath(year)), file);
                RequireFileVersion(path, expected);
                WindowsPackage.VerifySignature(path);
            }
        }
        WindowsPackage.VerifySignature(UpdaterPath);
        RequireFileVersion(UpdaterPath, expected);
    }

    private static void RequireFileVersion(string path, string expected)
    {
        var version = FileVersionInfo.GetVersionInfo(path);
        if ($"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}" != expected)
            throw new InvalidDataException("Een geïnstalleerd bestand heeft niet de verwachte versie: " + Path.GetFileName(path));
    }

    public static string ConfirmLoaded(UpdateStore store, ReleaseVersion loaded)
    {
        var path = Path.Join(store.DirectoryPath, "last-result.json");
        if (!File.Exists(path)) return "";
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("De update-uitslag is te groot.");
        var result = JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(path), ReleasePolicy.JsonOptions);
        if (result is null) return "";
        var bootedAt = DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64);
        var pendingRestart = ReleasePolicy.RequiresRestart(result, bootedAt);
        var key = result.Version + "|" + result.Time.ToString("O") + "|" + pendingRestart;
        if (store.Load().ReportedResult == key) return "";
        string message;
        if (pendingRestart)
            message = "NLCS Legenda: Windows moet nog opnieuw worden opgestart om de update af te ronden. Sla je werk eerst op.";
        else if (result.Success && result.Version == loaded.ToString())
        {
            try
            {
                VerifyInstalledFiles(result.Version, result.AcadYears ?? new[] { 2025, 2026, 2027 });
                message = $"NLCS Legenda {loaded}: de update is geladen en gecontroleerd.";
            }
            catch (Exception ex) when (IsExpected(ex)) { message = "NLCS Legenda: controleer de installatie via Updates controleren. " + ex.Message; }
        }
        else message = "NLCS Legenda: controleer de laatste update via Updates controleren. " + result.Message;
        store.Change(s => s.ReportedResult = key);
        return message;
    }
    public static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException
        or HttpRequestException or OperationCanceledException or CryptographicException or Win32Exception or InvalidOperationException
        or ArgumentException or System.Security.SecurityException;
}
