using System.Text.Json;

namespace NlcsLegenda.Update;

public sealed record ReleaseAsset(string Name, Uri Url, long Size, string Sha256);
public sealed record AvailableRelease(string Version, Uri Page, string Notes, ReleaseAsset Installer, ReleaseAsset? Manifest);
public sealed record UpdateManifest(int Schema, string Version, int[] AutoCadYears, int MinimumUpdater, string Installer, string Sha256);
public sealed record UpdateOffer(AvailableRelease Release, bool Compatible, string Reason);

public static class ReleasePolicy
{
    public const string Repository = "joligej/infracad-helper";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";
    public const int UpdaterProtocol = 1;
    public const long MaximumInstallerBytes = 64 * 1024 * 1024;

    public static AvailableRelease? Parse(string json, ReleaseVersion current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString();
        if (!ReleaseVersion.TryParse(tag, out var version)) throw new InvalidDataException("De release heeft geen geldig versienummer.");
        if (version.CompareTo(current) <= 0) return null;
        var page = new Uri($"https://github.com/{Repository}/releases/tag/{tag}");
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        ReleaseAsset? Find(string name, long maximum)
        {
            var matches = assets.Where(a => a.GetProperty("name").GetString() == name).ToArray();
            if (matches.Length == 0) return null;
            if (matches.Length != 1) throw new InvalidDataException("De release bevat dubbele bestanden.");
            var asset = matches[0];
            var size = asset.GetProperty("size").GetInt64();
            if (asset.GetProperty("state").GetString() != "uploaded" || size <= 0 || size > maximum)
                throw new InvalidDataException("Het releasebestand is nog niet compleet of heeft een ongeldige grootte.");
            var url = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "");
            if (url.AbsoluteUri != $"https://github.com/{Repository}/releases/download/{tag}/{name}")
                throw new InvalidDataException("Het downloadadres hoort niet bij deze release.");
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || !IsSha256(digest[7..]))
                throw new InvalidDataException("De SHA-256-controle van de release ontbreekt.");
            return new(name, url, size, digest[7..].ToUpperInvariant());
        }
        var installer = Find($"NlcsLegenda-{version}.msi", MaximumInstallerBytes)
            ?? throw new InvalidDataException("De release bevat nog geen complete installer.");
        var manifest = Find("update.json", 16384);
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        if (notes.Length > 16000) notes = notes[..16000] + "\nMeer informatie op de releasepagina.";
        return new(version.ToString(), page, notes, installer, manifest);
    }

    public static UpdateOffer CheckManifest(AvailableRelease release, string? json, int acadYear)
    {
        if (json is null) return new(release, false, "Deze release ondersteunt nog geen update vanuit de plug-in. Open de releasepagina voor handmatige installatie.");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
        if (manifest is null || manifest.Schema != 1 || manifest.Version != release.Version ||
            manifest.Installer != release.Installer.Name || !string.Equals(manifest.Sha256, release.Installer.Sha256, StringComparison.OrdinalIgnoreCase) ||
            manifest.AutoCadYears is null || manifest.MinimumUpdater < 1)
            throw new InvalidDataException("De updategegevens komen niet overeen met de release.");
        if (manifest.MinimumUpdater > UpdaterProtocol)
            return new(release, false, "Voor deze release is een nieuwere updater nodig. Gebruik de handmatige installer.");
        if (!manifest.AutoCadYears.Contains(acadYear))
            return new(release, false, $"Deze release ondersteunt AutoCAD/Civil 3D {acadYear} niet. Je huidige versie blijft beschikbaar.");
        return new(release, true, "");
    }

    public static void RequireSignedSupport(IReadOnlyDictionary<string, string> properties, int acadYear)
    {
        if (!properties.TryGetValue("NLCSUPDATEPROTOCOL", out var protocol) || protocol != UpdaterProtocol.ToString() ||
            !properties.TryGetValue("NLCSACADYEARS", out var years) || !years.Split(',').Contains(acadYear.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            throw new InvalidDataException("De ondertekende installer ondersteunt deze CAD-versie of updater niet. Gebruik de handmatige installatie-instructies.");
    }
    public static int[] SupportedYears(IReadOnlyDictionary<string, string> properties)
    {
        if (!properties.TryGetValue("NLCSACADYEARS", out var years)) return Array.Empty<int>();
        var declared = years.Split(',');
        return new[] { 2025, 2026, 2027 }.Where(year => declared.Contains(year.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
    }

    public static bool RequiresRestart(UpdateResult result, DateTimeOffset bootedAt) => result.RestartRequired && bootedAt <= result.Time;

    public static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}
