using System.Text.Json;

namespace NlcsLegenda.Update;

public sealed class UpdateState
{
    public int Schema { get; set; } = 1;
    public bool Automatic { get; set; } = true;
    public string SkippedVersion { get; set; } = "";
    public DateTimeOffset SnoozeUntil { get; set; }
    public string ReportedResult { get; set; } = "";
    public DateTimeOffset RetryAt { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public string ETag { get; set; } = "";
    public string ReleaseJson { get; set; } = "";
    public string ManifestJson { get; set; } = "";
    public string LastResult { get; set; } = "";
}

public sealed class UpdateStore(string? directory = null)
{
    public string DirectoryPath { get; } = directory ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NlcsLegenda", "Updates");

    public FileStream? TryLease(string name)
    {
        Directory.CreateDirectory(DirectoryPath);
        try { return new FileStream(Path.Join(DirectoryPath, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    public UpdateState Load()
    {
        var path = Path.Join(DirectoryPath, "settings.json");
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("De update-instellingen zijn te groot.");
        var state = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(path), ReleasePolicy.JsonOptions);
        if (state is null || state.Schema != 1) throw new InvalidDataException("De update-instellingen hebben een onbekend formaat.");
        state.ETag ??= ""; state.ReleaseJson ??= ""; state.ManifestJson ??= "";
        state.SkippedVersion ??= ""; state.LastResult ??= ""; state.ReportedResult ??= "";
        return state;
    }

    public void Change(Action<UpdateState> change)
    {
        using var lease = TryLease("settings") ?? throw new IOException("De update-instellingen worden elders gewijzigd. Probeer het opnieuw.");
        var state = Load();
        change(state);
        WriteJson(Path.Join(DirectoryPath, "settings.json"), state);
    }

    public static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, ReleasePolicy.JsonOptions)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Join(DirectoryPath, "updates.log");
            if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024) File.WriteAllText(path, "");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {text}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
    }
}

public sealed class UpdateService(UpdateStore store, ReleaseClient client)
{
    public async Task<UpdateOffer?> CheckAsync(ReleaseVersion current, int acadYear, bool manual, CancellationToken cancellation)
    {
        var state = store.Load();
        var now = DateTimeOffset.UtcNow;
        if (!manual && (!state.Automatic || now < state.SnoozeUntil)) return null;
        if (now < state.RetryAt)
        {
            if (manual) throw new ReleaseRateLimitException(state.RetryAt);
            return null;
        }
        var cached = state.ReleaseJson.Length > 0 && now - state.CheckedAt < TimeSpan.FromDays(1) && now >= state.CheckedAt;
        if (manual || !cached)
        {
            if (!manual && now >= state.AttemptedAt && now - state.AttemptedAt < TimeSpan.FromHours(1)) return null;
            store.Change(s => s.AttemptedAt = now);
            ReleaseResponse response;
            try { response = await client.LatestAsync(state.ReleaseJson, state.ETag, cancellation).ConfigureAwait(false); }
            catch (ReleaseRateLimitException ex) { store.Change(s => s.RetryAt = ex.RetryAt); throw; }
            var release = ReleasePolicy.Parse(response.Json, current);
            var manifest = release?.Manifest is { } asset ? await client.ManifestAsync(asset, cancellation).ConfigureAwait(false) : "";
            if (release is not null) ReleasePolicy.CheckManifest(release, manifest.Length == 0 ? null : manifest, acadYear);
            cancellation.ThrowIfCancellationRequested();
            store.Change(s => { s.CheckedAt = now; s.ETag = response.ETag; s.ReleaseJson = response.Json; s.ManifestJson = manifest; s.LastResult = "Controle voltooid."; });
            state = store.Load();
        }
        var available = ReleasePolicy.Parse(state.ReleaseJson, current);
        if (available is null || !manual && state.SkippedVersion == available.Version) return null;
        return ReleasePolicy.CheckManifest(available, state.ManifestJson.Length == 0 ? null : state.ManifestJson, acadYear);
    }
}
