using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NlcsLegenda.Update;
using Xunit;

namespace NlcsLegenda.Update.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "nlcs-updates-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("voorbeeldinstaller");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Installer));
    private static readonly string Manifest = JsonSerializer.Serialize(new UpdateManifest(1, "1.42.0", new[] { 2025, 2026, 2027 }, 1, "NlcsLegenda-1.42.0.msi", Hash), ReleasePolicy.JsonOptions);
    private static readonly ReleaseVersion Current = new(1, 41, 0);

    private static object Asset(string name, byte[] bytes) => new { name, size = bytes.Length, state = "uploaded",
        digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)),
        browser_download_url = "https://github.com/joligej/infracad-helper/releases/download/v1.42.0/" + name };
    private static string Json(string version = "v1.42.0", bool preview = false) => JsonSerializer.Serialize(new {
        tag_name = version, draft = false, prerelease = preview, body = "Wijzigingen", assets = new[] { Asset("NlcsLegenda-1.42.0.msi", Installer), Asset("update.json", Encoding.UTF8.GetBytes(Manifest)) }
    });
    private static AvailableRelease Release() => ReleasePolicy.Parse(Json(), Current)!;

    [Theory]
    [InlineData("v1.42.0", true)] [InlineData("1.42.0", true)] [InlineData("1.142.0", true)]
    [InlineData("1.42.0-beta", false)] [InlineData("1.42.0.1", false)] [InlineData("1.42", false)]
    [InlineData("../1.42.0", false)] [InlineData("01.42.0", false)] [InlineData("256.1.1", false)]
    [InlineData("1.1.65536", false)] [InlineData("1.2.-1", false)] [InlineData("1.2.3 ", false)]
    public void VersionsAreStrict(string value, bool valid) => Assert.Equal(valid, ReleaseVersion.TryParse(value, out _));

    [Fact] public void NumericComparisonHandlesMultiDigitParts() => Assert.True(new ReleaseVersion(1, 100, 0).CompareTo(new(1, 99, 9)) > 0);
    [Theory] [InlineData("v1.41.0")] [InlineData("v1.40.9")]
    public void NeverOfferAnOlderOrSameVersion(string version) => Assert.Null(ReleasePolicy.Parse(Json(version), Current));
    [Fact] public void PreviewIsNeverOffered() => Assert.Null(ReleasePolicy.Parse(Json(preview: true), Current));
    [Fact] public void ReleaseNamesAndUrlsMustMatch() => Assert.Throws<InvalidDataException>(() => ReleasePolicy.Parse(Json().Replace("releases/download/v1.42.0", "releases/download/v1.41.0"), Current));
    [Fact] public void MissingDigestIsRejected() => Assert.Throws<InvalidDataException>(() => ReleasePolicy.Parse(Json().Replace("sha256:", "sha1:"), Current));
    [Fact] public void IncompleteAssetsAreRejected() => Assert.Throws<InvalidDataException>(() => ReleasePolicy.Parse(Json().Replace("uploaded", "starter"), Current));
    [Fact] public void MatchingHostAndManifestAreAccepted() => Assert.True(ReleasePolicy.CheckManifest(Release(), Manifest, 2027).Compatible);
    [Fact] public void UnsupportedHostGetsManualRoute() => Assert.False(ReleasePolicy.CheckManifest(Release(), Manifest, 2028).Compatible);
    [Fact] public void MissingManifestGetsManualRoute() => Assert.False(ReleasePolicy.CheckManifest(Release(), null, 2027).Compatible);
    [Fact] public void ManifestCannotChangeInstaller() => Assert.Throws<InvalidDataException>(() => ReleasePolicy.CheckManifest(Release(), Manifest.Replace(Hash, new string('0', 64)), 2027));
    [Fact] public void FutureProtocolGetsManualRoute() => Assert.False(ReleasePolicy.CheckManifest(Release(), Manifest.Replace("\"minimumUpdater\": 1", "\"minimumUpdater\": 2"), 2027).Compatible);

    [Theory]
    [InlineData("https://github.com/joligej/infracad-helper", true)]
    [InlineData("https://release-assets.githubusercontent.com/file?token=value", true)]
    [InlineData("http://github.com/file", false)] [InlineData("https://github.com.evil.test/file", false)]
    [InlineData("https://user@github.com/file", false)] [InlineData("https://github.com:8080/file", false)]
    [InlineData("https://other.test/file", false)]
    public void RedirectsStayOnGitHub(string address, bool accepted) => Assert.Equal(accepted, ReleaseClient.IsDownloadAddress(new Uri(address)));

    [Fact]
    public async Task DownloadChecksContentAndReplacesOnlyAfterSuccess()
    {
        Directory.CreateDirectory(_directory); var target = Path.Join(_directory, "installer.msi"); File.WriteAllText(target, "oud");
        using (var client = Client(_ => Reply("fout")))
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release().Installer, target, null, default));
        Assert.Equal("oud", File.ReadAllText(target)); Assert.Empty(Directory.GetFiles(_directory, "*.part"));
        using (var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }))
            await client.DownloadAsync(Release().Installer, target, null, default);
        Assert.Equal(Installer, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task WrongDigestDoesNotReplaceFile()
    {
        Directory.CreateDirectory(_directory); var target = Path.Join(_directory, "installer.msi"); File.WriteAllText(target, "oud");
        var bytes = Installer.ToArray(); bytes[0] ^= 1;
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release().Installer, target, null, default));
        Assert.Equal("oud", File.ReadAllText(target)); Assert.Empty(Directory.GetFiles(_directory, "*.part"));
    }

    [Fact]
    public async Task RedirectToAnotherSiteIsRejected()
    {
        using var client = Client(_ => { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://other.test/file"); return response; });
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release().Installer, Path.Join(_directory, "installer.msi"), null, default));
    }

    [Fact]
    public async Task CacheUsesConditionalRequest()
    {
        using var client = Client(request => { Assert.Equal("\"same\"", request.Headers.IfNoneMatch.Single().ToString()); return new HttpResponseMessage(HttpStatusCode.NotModified); });
        var response = await client.LatestAsync(Json(), "\"same\"", default); Assert.Equal(Json(), response.Json);
    }

    [Fact]
    public async Task OversizedResponseIsRejected()
    {
        using var client = Client(_ => Reply(new string('x', 1024 * 1024 + 1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.LatestAsync("", "", default));
    }

    [Fact]
    public async Task CancelledDownloadLeavesNoPartialFile()
    {
        using var client = Client(_ => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(Release().Installer, Path.Join(_directory, "installer.msi"), null, default));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task DailyCacheAndPreferencesSurviveNewService()
    {
        var store = new UpdateStore(_directory); var calls = 0;
        using var client = Client(request => { calls++; return Reply(request.RequestUri!.Host == "api.github.com" ? Json() : Manifest); });
        var offer = await new UpdateService(store, client).CheckAsync(Current, 2027, false, default);
        Assert.True(offer!.Compatible); Assert.Equal(2, calls);
        Assert.NotNull(await new UpdateService(new(_directory), client).CheckAsync(Current, 2027, false, default)); Assert.Equal(2, calls);
        store.Change(s => s.SkippedVersion = "1.42.0");
        Assert.Null(await new UpdateService(store, client).CheckAsync(Current, 2027, false, default));
        Assert.NotNull(await new UpdateService(store, client).CheckAsync(Current, 2027, true, default));
        Assert.Equal("1.42.0", store.Load().SkippedVersion);
    }

    [Fact]
    public async Task DisabledOrSnoozedAutomaticChecksDoNotUseNetwork()
    {
        var store = new UpdateStore(_directory);
        using var client = Client(_ => throw new InvalidOperationException("Netwerk niet verwacht"));
        var service = new UpdateService(store, client);
        store.Change(s => s.Automatic = false); Assert.Null(await service.CheckAsync(Current, 2027, false, default));
        store.Change(s => { s.Automatic = true; s.SnoozeUntil = DateTimeOffset.UtcNow.AddHours(1); });
        Assert.Null(await service.CheckAsync(Current, 2027, false, default));
    }

    [Fact]
    public async Task FailedAutomaticCheckDoesNotRetryAtEveryStartup()
    {
        var store = new UpdateStore(_directory); var calls = 0;
        using var client = Client(_ => { calls++; return new(HttpStatusCode.ServiceUnavailable); });
        var service = new UpdateService(store, client);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckAsync(Current, 2027, false, default));
        Assert.Null(await service.CheckAsync(Current, 2027, false, default)); Assert.Equal(1, calls);
    }

    [Fact]
    public void OnlyOneSessionOwnsNotification()
    {
        var store = new UpdateStore(_directory);
        using (var lease = store.TryLease("notification")) { Assert.NotNull(lease); Assert.Null(new UpdateStore(_directory).TryLease("notification")); }
        using var next = store.TryLease("notification"); Assert.NotNull(next);
    }

    [Fact]
    public void CorruptPreferencesAreNotOverwritten()
    {
        Directory.CreateDirectory(_directory); var path = Path.Join(_directory, "settings.json"); File.WriteAllText(path, "kapot");
        Assert.Throws<JsonException>(() => new UpdateStore(_directory).Change(s => s.Automatic = false));
        Assert.Equal("kapot", File.ReadAllText(path));
    }

    [Fact]
    public async Task ServerRetryPeriodAppliesToManualChecksToo()
    {
        var calls = 0; var store = new UpdateStore(_directory);
        using var client = Client(_ => { calls++; var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(30)); return response; });
        var service = new UpdateService(store, client);
        await Assert.ThrowsAsync<ReleaseRateLimitException>(() => service.CheckAsync(Current, 2027, true, default));
        await Assert.ThrowsAsync<ReleaseRateLimitException>(() => service.CheckAsync(Current, 2027, true, default));
        Assert.Null(await service.CheckAsync(Current, 2027, false, default)); Assert.Equal(1, calls);
    }

    [Theory] [InlineData("1", "2025,2027", true)] [InlineData("2", "2027", false)] [InlineData("1", "2028", false)]
    public void SignedCompatibilityMustAlsoMatch(string protocol, string years, bool accepted)
    {
        var properties = new Dictionary<string, string> { ["NLCSUPDATEPROTOCOL"] = protocol, ["NLCSACADYEARS"] = years };
        if (accepted) ReleasePolicy.RequireSignedSupport(properties, 2027);
        else Assert.Throws<InvalidDataException>(() => ReleasePolicy.RequireSignedSupport(properties, 2027));
    }
    [Fact]
    public void MissingHashIsRejected() => Assert.False(ReleasePolicy.IsSha256(null));

    [Fact]
    public void CacheCleanupPreservesPendingUpdatesAndUnknownFiles()
    {
        var store = new UpdateStore(_directory);
        string Package(string version)
        {
            var folder = Path.Join(_directory, "Packages", version);
            Directory.CreateDirectory(folder);
            var file = Path.Join(folder, $"NlcsLegenda-{version}.msi");
            File.WriteAllText(file, "bestand");
            return file;
        }
        var old = Package("1.40.0"); var current = Package("1.41.0"); var next = Package("1.42.0");
        var foreign = Package("1.39.0"); File.WriteAllText(Path.Join(Path.GetDirectoryName(foreign)!, "eigen.txt"), "bewaren");
        UpdateCache.Clean(store, Current);
        Assert.False(File.Exists(old)); Assert.False(File.Exists(current));
        Assert.True(File.Exists(next)); Assert.True(File.Exists(foreign));
    }

    [Fact]
    public void CacheCleanupWaitsForInstallerAndLeavesRecentRunner()
    {
        var store = new UpdateStore(_directory);
        var runner = Path.Join(_directory, "Runner", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runner);
        var file = Path.Join(runner, "request.json"); File.WriteAllText(file, "opdracht");
        UpdateCache.Clean(store, Current); Assert.True(File.Exists(file));
        Directory.SetLastWriteTimeUtc(runner, DateTime.UtcNow.AddDays(-3));
        using (store.TryLease("installation")) { UpdateCache.Clean(store, Current); Assert.True(File.Exists(file)); }
        UpdateCache.Clean(store, Current); Assert.False(Directory.Exists(runner));
    }

    [Fact]
    public void FileValidationUsesOnlySupportedKnownYears()
    {
        Assert.Equal(new[] { 2025, 2027 }, ReleasePolicy.SupportedYears(new Dictionary<string, string> { ["NLCSACADYEARS"] = "2025,2027,2028" }));
    }

    [Fact]
    public void PendingReplacementIsCheckedAfterWindowsRestart()
    {
        var time = DateTimeOffset.UtcNow;
        var result = new UpdateResult("1.42.0", true, true, "herstart", time, new[] { 2027 });
        Assert.True(ReleasePolicy.RequiresRestart(result, time.AddHours(-3)));
        Assert.False(ReleasePolicy.RequiresRestart(result, time.AddMinutes(1)));
        Assert.False(ReleasePolicy.RequiresRestart(result with { RestartRequired = false }, time.AddHours(-3)));
    }

    private static ReleaseClient Client(Func<HttpRequestMessage, HttpResponseMessage> reply) => new(new HttpClient(new Handler(reply)));
    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => Task.FromResult(reply(request));
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
