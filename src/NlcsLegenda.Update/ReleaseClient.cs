using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace NlcsLegenda.Update;

public sealed class ReleaseRateLimitException(DateTimeOffset retryAt) : HttpRequestException("GitHub laat tijdelijk geen nieuwe controle toe. Probeer het na " + retryAt.ToLocalTime().ToString("HH:mm") + " opnieuw, of open de releasepagina.")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}

public sealed record ReleaseResponse(string Json, string ETag);

public sealed class ReleaseClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public ReleaseClient(HttpClient? client = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<ReleaseResponse> LatestAsync(string cachedJson, string etag, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = Request(new Uri(ReleasePolicy.LatestUrl));
        if (cachedJson.Length > 0 && EntityTagHeaderValue.TryParse(etag, out var parsed)) request.Headers.IfNoneMatch.Add(parsed);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && cachedJson.Length > 0) return new(cachedJson, etag);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var now = DateTimeOffset.UtcNow;
            var retryAt = response.Headers.RetryAfter?.Date ?? now.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1));
            if (response.Headers.TryGetValues("x-ratelimit-reset", out var values) && long.TryParse(values.FirstOrDefault(), out var seconds) && seconds is > 0 and < 253402300799)
                retryAt = DateTimeOffset.FromUnixTimeSeconds(seconds) > retryAt ? DateTimeOffset.FromUnixTimeSeconds(seconds) : retryAt;
            throw new ReleaseRateLimitException(retryAt > now ? retryAt : now.AddMinutes(1));
        }
        response.EnsureSuccessStatusCode();
        var bytes = await ReadLimitedAsync(response, 1024 * 1024, timeout.Token).ConfigureAwait(false);
        return new(Encoding.UTF8.GetString(bytes), response.Headers.ETag?.ToString() ?? "");
    }

    public async Task<string> ManifestAsync(ReleaseAsset asset, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await DownloadResponseAsync(asset.Url, timeout.Token).ConfigureAwait(false);
        var bytes = await ReadLimitedAsync(response, 16384, timeout.Token).ConfigureAwait(false);
        CheckBytes(asset, bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    public async Task DownloadAsync(ReleaseAsset asset, string destination, IProgress<int>? progress, CancellationToken cancellation)
    {
        if (asset.Size <= 0 || asset.Size > ReleasePolicy.MaximumInstallerBytes || !ReleasePolicy.IsSha256(asset.Sha256))
            throw new InvalidDataException("Ongeldige downloadgegevens.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            using var response = await DownloadResponseAsync(asset.Url, timeout.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
                throw new InvalidDataException("De downloadgrootte wijkt af van de release.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > asset.Size) throw new InvalidDataException("De download is groter dan het releasebestand.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    progress?.Report((int)(100 * total / asset.Size));
                }
                if (total != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("De download is onvolledig of de SHA-256-controle wijkt af.");
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(partial, destination, true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    private async Task<HttpResponseMessage> DownloadResponseAsync(Uri uri, CancellationToken cancellation)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!IsDownloadAddress(uri)) throw new InvalidDataException("Het downloadadres is niet toegestaan.");
            using var request = Request(uri);
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException("Het downloadadres ontbreekt.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            if (!response.IsSuccessStatusCode) { try { response.EnsureSuccessStatusCode(); } finally { response.Dispose(); } }
            return response;
        }
        throw new InvalidDataException("Te veel doorverwijzingen bij het downloaden.");
    }

    internal static bool IsDownloadAddress(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.Host is "github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com";

    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("NlcsLegenda-Updates/1");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maximum, CancellationToken cancellation)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("De serverreactie is te groot.");
        using var output = new MemoryStream();
        await using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        var buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("De serverreactie is te groot.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static void CheckBytes(ReleaseAsset asset, byte[] bytes)
    {
        if (bytes.LongLength != asset.Size || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("De updategegevens wijken af van de release.");
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
