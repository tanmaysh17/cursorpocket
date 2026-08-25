using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CursorPocket.Core.Updates;

namespace CursorPocket.Tests;

public sealed class ApplicationUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CursorPocket.Update.Tests", Guid.NewGuid().ToString("N"));
    private static readonly Uri ManifestUri = new("https://tanmaysh17.github.io/cursorpocket/update.json");

    [Theory]
    [InlineData("0.4.0.1")]
    [InlineData("0.4")]
    [InlineData("not-a-version")]
    public void Release_versions_require_semantic_version_numbers(string value)
    {
        Assert.False(ReleaseVersion.TryParse(value, out _));
    }

    [Theory]
    [InlineData("0.4.0-preview", "0.4.0", true)]
    [InlineData("0.4.0", "0.4.0", false)]
    [InlineData("0.4.1", "0.4.0", false)]
    [InlineData("v1.2.3", "1.3.0-beta", true)]
    public async Task Check_compares_release_versions(string installed, string available, bool expected)
    {
        var handler = new StubHandler(_ => JsonResponse(Manifest(available)));
        using var service = Service(handler);

        var result = await service.CheckAsync(installed, true, null, force: true);

        Assert.Equal(expected ? UpdateCheckStatus.Available : UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal(expected, result.Update is not null);
    }

    [Fact]
    public async Task Disabled_and_recent_checks_do_not_contact_GitHub()
    {
        var handler = new StubHandler(_ => JsonResponse(Manifest("1.0.0")));
        var now = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        using var service = Service(handler, () => now);

        var disabled = await service.CheckAsync("0.4.0", false, null);
        var throttled = await service.CheckAsync("0.4.0", true, now.AddHours(-2));

        Assert.Equal(UpdateCheckStatus.Disabled, disabled.Status);
        Assert.Equal(UpdateCheckStatus.Throttled, throttled.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Concurrent_checks_share_one_request()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await gate.Task.WaitAsync(cancellationToken);
            return JsonResponse(Manifest("0.5.0"));
        });
        using var service = Service(handler);

        var first = service.CheckAsync("0.4.0", true, null, force: true);
        var second = service.CheckAsync("0.4.0", true, null, force: true);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(UpdateCheckStatus.Available, first.Result.Status);
        Assert.Equal(UpdateCheckStatus.Available, second.Result.Status);
    }

    [Fact]
    public async Task Offline_and_timeout_checks_are_non_destructive_results()
    {
        using var offline = Service(new StubHandler(_ => throw new HttpRequestException("offline")));
        var offlineResult = await offline.CheckAsync("0.4.0", true, null, force: true);

        using var timeout = Service(
            new StubHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return JsonResponse(Manifest("0.5.0"));
            }),
            checkTimeout: TimeSpan.FromMilliseconds(25));
        var timeoutResult = await timeout.CheckAsync("0.4.0", true, null, force: true);

        Assert.Equal(UpdateCheckStatus.Unavailable, offlineResult.Status);
        Assert.Equal(UpdateCheckStatus.Unavailable, timeoutResult.Status);
    }

    [Theory]
    [InlineData("http://github.com/tanmaysh17/cursorpocket/releases/download/v1/app.exe", "Inno")]
    [InlineData("https://example.com/app.exe", "GitHub")]
    [InlineData("https://github.com/someone-else/cursorpocket/releases/download/v1/app.exe", "GitHub")]
    [InlineData("https://github.com/tanmaysh17/cursorpocket/releases/download/v1/app.exe", "XYZ")]
    public async Task Invalid_manifests_are_rejected(string installerUrl, string sha)
    {
        var manifest = Manifest("0.5.0") with { InstallerUrl = installerUrl, Sha256 = sha };
        using var service = Service(new StubHandler(_ => JsonResponse(manifest)));

        var result = await service.CheckAsync("0.4.0", true, null, force: true);

        Assert.Equal(UpdateCheckStatus.InvalidManifest, result.Status);
    }

    [Fact]
    public async Task Download_requires_exact_hash_size_and_publisher()
    {
        var payload = Encoding.UTF8.GetBytes("signed installer fixture");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
        var update = Update(payload.Length, sha);

        using var valid = Service(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }),
            verifier: new StubVerifier(true, "Tanmay Sharma"));
        var downloaded = await valid.DownloadAndVerifyAsync(update, "Tanmay Sharma");
        Assert.Equal(payload, await File.ReadAllBytesAsync(downloaded.InstallerPath));

        using var wrongPublisher = Service(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }),
            verifier: new StubVerifier(false, "Someone Else"));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            wrongPublisher.DownloadAndVerifyAsync(update, "Tanmay Sharma"));

        using var corrupt = Service(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }),
            verifier: new StubVerifier(true, "Tanmay Sharma"));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            corrupt.DownloadAndVerifyAsync(update with { Sha256 = new string('0', 64) }, "Tanmay Sharma"));

        using var interrupted = Service(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload[..^1]) }),
            verifier: new StubVerifier(true, "Tanmay Sharma"));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            interrupted.DownloadAndVerifyAsync(update, "Tanmay Sharma"));
    }

    [Fact]
    public void Stable_release_is_newer_than_preview_and_downgrades_are_detectable()
    {
        Assert.True(ReleaseVersion.TryParse("0.4.0-preview", out var preview));
        Assert.True(ReleaseVersion.TryParse("0.4.0", out var stable));
        Assert.True(ReleaseVersion.TryParse("0.3.9", out var older));

        Assert.True(stable > preview);
        Assert.True(older < stable);
    }

    [Fact]
    public void Main_build_queues_a_stable_signed_release_for_the_declared_version()
    {
        var workflow = ReadRepositoryFile(".github", "workflows", "native-windows.yml");
        var versionProps = ReadRepositoryFile("native", "Version.props");
        var changelog = ReadRepositoryFile("CHANGELOG.md");

        Assert.Contains("validate-release-version:", workflow, StringComparison.Ordinal);
        Assert.Contains("Version $version is not newer than $baseVersion", workflow, StringComparison.Ordinal);
        Assert.Contains("git tag --list $tag", workflow, StringComparison.Ordinal);
        Assert.Contains("queue-signed-release:", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: [validate-release-version, build-test-package]", workflow, StringComparison.Ordinal);
        Assert.Contains("actions: write", workflow, StringComparison.Ordinal);
        Assert.Contains("gh workflow run native-windows.yml --ref", workflow, StringComparison.Ordinal);
        Assert.Contains("refs/tags/$tag^{commit}", workflow, StringComparison.Ordinal);
        Assert.Contains("git rev-parse origin/main", workflow, StringComparison.Ordinal);
        Assert.Contains("should-dispatch == 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release upload $env:GITHUB_REF_NAME @assets --clobber", workflow, StringComparison.Ordinal);
        Assert.Equal(2, workflow.Split("overwrite: true", StringSplitOptions.None).Length - 1);

        var version = System.Xml.Linq.XDocument.Parse(versionProps)
            .Descendants("CursorPocketVersion")
            .Single()
            .Value;
        Assert.True(ReleaseVersion.TryParse(version, out var release));
        Assert.Null(release.Prerelease);
        Assert.Contains($"## {version} ", changelog, StringComparison.Ordinal);
    }

    private ApplicationUpdateService Service(
        HttpMessageHandler handler,
        Func<DateTimeOffset>? clock = null,
        IInstallerSignatureVerifier? verifier = null,
        TimeSpan? checkTimeout = null) => new(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) },
            ManifestUri,
            verifier ?? new StubVerifier(true, "Tanmay Sharma"),
            clock,
            _root,
            checkTimeout);

    private static ApplicationUpdateManifest Manifest(string version) => new()
    {
        Version = version,
        InstallerUrl = $"https://github.com/tanmaysh17/cursorpocket/releases/download/v{version}/CursorPocket-Setup-x64.exe",
        Sha256 = new string('A', 64),
        SizeBytes = 123,
        MinimumWindowsVersion = "10.0.19041",
        ReleaseNotesUrl = "https://github.com/tanmaysh17/cursorpocket/releases/latest",
        PublishedAt = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
    };

    private static ApplicationUpdateInfo Update(long size, string sha) => new(
        new ReleaseVersion(0, 5, 0, null),
        new Uri("https://github.com/tanmaysh17/cursorpocket/releases/download/v0.5.0/CursorPocket-Setup-x64.exe"),
        sha,
        size,
        new Version(10, 0, 19041),
        new Uri("https://github.com/tanmaysh17/cursorpocket/releases/latest"),
        DateTimeOffset.UtcNow);

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "native", "Version.props")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory.FullName, .. pathParts]));
    }

    private static HttpResponseMessage JsonResponse(ApplicationUpdateManifest manifest)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(manifest), Encoding.UTF8),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class StubVerifier(bool valid, string publisher) : IInstallerSignatureVerifier
    {
        public Task<InstallerVerificationResult> VerifyAsync(string path, string expectedPublisher, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstallerVerificationResult(
                valid,
                publisher,
                valid ? null : $"The installer is signed by '{publisher}', not '{expectedPublisher}'."));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;
        private int _requestCount;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) :
            this((request, _) => Task.FromResult(response(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) => _response = response;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return _response(request, cancellationToken);
        }
    }
}
