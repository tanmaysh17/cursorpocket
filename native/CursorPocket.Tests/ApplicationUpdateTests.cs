using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Updates;
using CursorPocket_App.Services;

namespace CursorPocket.Tests;

public sealed class ApplicationUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CursorPocket.Update.Tests", Guid.NewGuid().ToString("N"));
    private static readonly Uri ManifestUri = new("https://github.com/tanmaysh17/cursorpocket/releases/latest/download/update.json");

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

    [Fact]
    public async Task Failed_automatic_attempt_is_persisted_and_throttles_the_next_check()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        var settings = new StubSettingsQueue(new AppSettings { AutomaticallyCheckForUpdates = true });
        var startedAt = DateTimeOffset.UtcNow;
        using var coordinator = new ApplicationUpdateCoordinator(
            Service(handler),
            settings,
            () => settings.Current);

        var first = await coordinator.CheckAsync(force: false);

        Assert.Equal(UpdateCheckStatus.Unavailable, first.Status);
        Assert.NotNull(settings.Current.LastUpdateCheckAt);
        Assert.InRange(settings.Current.LastUpdateCheckAt!.Value, startedAt, DateTimeOffset.UtcNow);

        var second = await coordinator.CheckAsync(force: false);

        Assert.Equal(UpdateCheckStatus.Throttled, second.Status);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Automatic_scheduler_rechecks_while_the_process_stays_open()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new StubUpdateService(() =>
        {
            if (Interlocked.Increment(ref calls) >= 2) completed.TrySetResult();
            return new ApplicationUpdateCheckResult(UpdateCheckStatus.UpToDate, DateTimeOffset.UtcNow);
        });
        var settings = new StubSettingsQueue(new AppSettings { AutomaticallyCheckForUpdates = true });
        using var coordinator = new ApplicationUpdateCoordinator(service, settings, () => settings.Current);

        coordinator.ScheduleAutomaticCheck(TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(service.CheckCount >= 2);
    }

    [Fact]
    public void Enabling_automatic_checks_reschedules_the_background_loop()
    {
        var mainWindow = ReadRepositoryFile("native", "CursorPocket.App", "MainWindow.xaml.cs").ReplaceLineEndings("\n");

        Assert.Contains(
            "ApplicationUpdateCoordinator.ShouldRescheduleAutomaticCheck(_automaticUpdatesEnabled, settings.AutomaticallyCheckForUpdates)",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "_automaticUpdatesEnabled = App.Services.Settings.AutomaticallyCheckForUpdates;",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains("_automaticUpdatesEnabled = settings.AutomaticallyCheckForUpdates;", mainWindow, StringComparison.Ordinal);
        Assert.Contains(
            "if (shouldRescheduleAutomaticCheck)\n            {\n                App.Services.Updates.ScheduleAutomaticCheck();\n            }",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void Automatic_check_rescheduling_is_limited_to_the_disabled_to_enabled_transition(
        bool wasEnabled,
        bool isEnabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            ApplicationUpdateCoordinator.ShouldRescheduleAutomaticCheck(wasEnabled, isEnabled));
    }

    [Fact]
    public void Failed_installer_launch_removes_the_pending_marker()
    {
        var marker = Path.Combine(_root, "pending-update.txt");
        var settings = new StubSettingsQueue(new AppSettings());
        using var coordinator = new ApplicationUpdateCoordinator(
            new StubUpdateService(() => new ApplicationUpdateCheckResult(UpdateCheckStatus.UpToDate)),
            settings,
            () => settings.Current,
            startInstaller: _ => throw new Win32Exception("blocked"),
            pendingUpdatePath: marker);
        var downloaded = new DownloadedApplicationUpdate(Update(123, new string('A', 64)), Path.Combine(_root, "setup.exe"));

        Assert.Throws<Win32Exception>(() => coordinator.LaunchInstaller(downloaded));

        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void Update_prompt_defers_active_work_and_keeps_explicit_receipt_actions()
    {
        var mainWindow = ReadRepositoryFile("native", "CursorPocket.App", "MainWindow.xaml.cs");
        var coordinator = ReadRepositoryFile("native", "CursorPocket.App", "Services", "ApplicationUpdateCoordinator.cs");
        var installer = ReadRepositoryFile("native", "installer", "CursorPocket.iss");

        Assert.Contains("QueueUpdatePrompt(update)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_pendingUpdateTimer.Start()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("if (IsBusyForUpdate())", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new ReceiptAction(\"Download and install\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new ReceiptAction(\"Release notes\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new ReceiptAction(\"Later\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Win32Exception", mainWindow, StringComparison.Ordinal);
        Assert.Contains("/RELAUNCH", coordinator, StringComparison.Ordinal);
        Assert.Contains("Check: RelaunchAfterUpdate", installer, StringComparison.Ordinal);
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
    public async Task Download_requires_exact_hash_and_size_and_optionally_checks_publisher()
    {
        var payload = Encoding.UTF8.GetBytes("installer fixture");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
        var update = Update(payload.Length, sha);

        using var valid = Service(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }),
            verifier: new StubVerifier(true, "Tanmay Sharma"));
        var downloaded = await valid.DownloadAndVerifyAsync(update, "Tanmay Sharma");
        Assert.Equal(payload, await File.ReadAllBytesAsync(downloaded.InstallerPath));

        var unsignedVerifier = new TrackingVerifier(false, "Unsigned");
        using var unsigned = Service(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }),
            verifier: unsignedVerifier);
        var unsignedDownload = await unsigned.DownloadAndVerifyAsync(update, expectedPublisher: null);
        Assert.Equal(payload, await File.ReadAllBytesAsync(unsignedDownload.InstallerPath));
        Assert.Equal(0, unsignedVerifier.CallCount);

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
    public void Main_build_queues_a_stable_free_release_for_the_declared_version()
    {
        var workflow = ReadRepositoryFile(".github", "workflows", "native-windows.yml");
        var coordinator = ReadRepositoryFile("native", "CursorPocket.App", "Services", "ApplicationUpdateCoordinator.cs");
        var versionProps = ReadRepositoryFile("native", "Version.props");
        var changelog = ReadRepositoryFile("CHANGELOG.md");

        Assert.Contains("validate-release-version:", workflow, StringComparison.Ordinal);
        Assert.Contains("Version $version is not newer than $baseVersion", workflow, StringComparison.Ordinal);
        Assert.Contains("git tag --list $tag", workflow, StringComparison.Ordinal);
        Assert.Contains("queue-release:", workflow, StringComparison.Ordinal);
        Assert.Contains("publish-release:", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: [validate-release-version, build-test-package]", workflow, StringComparison.Ordinal);
        Assert.Contains("actions: write", workflow, StringComparison.Ordinal);
        Assert.Contains("gh workflow run native-windows.yml --ref", workflow, StringComparison.Ordinal);
        Assert.Contains("gh workflow run pages.yml --ref main", workflow, StringComparison.Ordinal);
        Assert.Contains("refs/tags/$tag^{commit}", workflow, StringComparison.Ordinal);
        Assert.Contains("git rev-parse origin/main", workflow, StringComparison.Ordinal);
        Assert.Contains("should-dispatch == 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release upload $env:GITHUB_REF_NAME @assets --clobber", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("azure/", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AZURE_", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-AuthenticodeSignature", workflow, StringComparison.Ordinal);
        Assert.Equal(2, workflow.Split("overwrite: true", StringSplitOptions.None).Length - 1);
        Assert.Contains("releases/latest/download/update.json", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("github.io", coordinator, StringComparison.OrdinalIgnoreCase);

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

    private sealed class TrackingVerifier(bool valid, string publisher) : IInstallerSignatureVerifier
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<InstallerVerificationResult> VerifyAsync(string path, string expectedPublisher, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new InstallerVerificationResult(valid, publisher));
        }
    }

    private sealed class StubSettingsQueue(AppSettings current) : ISettingsUpdateQueue
    {
        public AppSettings Current { get; private set; } = current;

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            Current = update(Current);
            return Task.FromResult(Current);
        }
    }

    private sealed class StubUpdateService(Func<ApplicationUpdateCheckResult> check) : IApplicationUpdateService
    {
        private int _checkCount;
        public int CheckCount => Volatile.Read(ref _checkCount);

        public Task<ApplicationUpdateCheckResult> CheckAsync(
            string installedVersion,
            bool updatesEnabled,
            DateTimeOffset? lastSuccessfulCheck,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _checkCount);
            return Task.FromResult(check());
        }

        public Task<DownloadedApplicationUpdate> DownloadAndVerifyAsync(
            ApplicationUpdateInfo update,
            string? expectedPublisher,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
