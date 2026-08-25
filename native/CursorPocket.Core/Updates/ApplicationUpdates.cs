using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CursorPocket.Core.Updates;

public sealed record ApplicationUpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("installer_url")]
    public string InstallerUrl { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("minimum_windows_version")]
    public string MinimumWindowsVersion { get; init; } = "10.0.19041";

    [JsonPropertyName("release_notes_url")]
    public string ReleaseNotesUrl { get; init; } = string.Empty;

    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; init; }
}

public sealed record ApplicationUpdateInfo(
    ReleaseVersion Version,
    Uri InstallerUri,
    string Sha256,
    long SizeBytes,
    Version MinimumWindowsVersion,
    Uri ReleaseNotesUri,
    DateTimeOffset PublishedAt);

public enum UpdateCheckStatus
{
    Disabled,
    Throttled,
    UpToDate,
    Available,
    Unavailable,
    InvalidManifest,
}

public sealed record ApplicationUpdateCheckResult(
    UpdateCheckStatus Status,
    DateTimeOffset? CheckedAt = null,
    ApplicationUpdateInfo? Update = null,
    string? Message = null);

public sealed record InstallerVerificationResult(bool IsValid, string? Publisher = null, string? Error = null);

public sealed record DownloadedApplicationUpdate(ApplicationUpdateInfo Update, string InstallerPath);

public interface IInstallerSignatureVerifier
{
    Task<InstallerVerificationResult> VerifyAsync(
        string path,
        string expectedPublisher,
        CancellationToken cancellationToken = default);
}

public interface IApplicationUpdateService
{
    Task<ApplicationUpdateCheckResult> CheckAsync(
        string installedVersion,
        bool updatesEnabled,
        DateTimeOffset? lastSuccessfulCheck,
        bool force = false,
        CancellationToken cancellationToken = default);

    Task<DownloadedApplicationUpdate> DownloadAndVerifyAsync(
        ApplicationUpdateInfo update,
        string expectedPublisher,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ApplicationUpdateService : IApplicationUpdateService, IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private readonly HttpClient _httpClient;
    private readonly Uri _manifestUri;
    private readonly IInstallerSignatureVerifier _signatureVerifier;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _downloadRoot;
    private readonly TimeSpan _checkTimeout;
    private readonly object _checkSync = new();
    private Task<ApplicationUpdateCheckResult>? _activeCheck;
    private bool _disposed;

    public ApplicationUpdateService(
        HttpClient httpClient,
        Uri manifestUri,
        IInstallerSignatureVerifier signatureVerifier,
        Func<DateTimeOffset>? clock = null,
        string? downloadRoot = null,
        TimeSpan? checkTimeout = null)
    {
        _httpClient = httpClient;
        _manifestUri = manifestUri;
        _signatureVerifier = signatureVerifier;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _downloadRoot = downloadRoot ?? Path.Combine(Path.GetTempPath(), "CursorPocket", "updates");
        _checkTimeout = checkTimeout ?? TimeSpan.FromSeconds(8);
    }

    public Task<ApplicationUpdateCheckResult> CheckAsync(
        string installedVersion,
        bool updatesEnabled,
        DateTimeOffset? lastSuccessfulCheck,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!force && !updatesEnabled)
        {
            return Task.FromResult(new ApplicationUpdateCheckResult(UpdateCheckStatus.Disabled));
        }

        var now = _clock();
        if (!force && lastSuccessfulCheck is { } last && now - last < CheckInterval)
        {
            return Task.FromResult(new ApplicationUpdateCheckResult(UpdateCheckStatus.Throttled));
        }

        lock (_checkSync)
        {
            if (_activeCheck is { IsCompleted: false })
            {
                return _activeCheck;
            }
            _activeCheck = CheckCoreAsync(installedVersion, cancellationToken);
            return _activeCheck;
        }
    }

    private async Task<ApplicationUpdateCheckResult> CheckCoreAsync(
        string installedVersion,
        CancellationToken cancellationToken)
    {
        var checkedAt = _clock();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_checkTimeout);
            using var response = await _httpClient.GetAsync(
                _manifestUri,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var manifest = await JsonSerializer.DeserializeAsync<ApplicationUpdateManifest>(stream, cancellationToken: timeout.Token);
            if (!TryValidate(manifest, out var update, out var error) ||
                !ReleaseVersion.TryParse(installedVersion, out var installed))
            {
                return new ApplicationUpdateCheckResult(UpdateCheckStatus.InvalidManifest, checkedAt, Message: error ?? "The installed version is invalid.");
            }

            var status = update!.Version > installed
                ? UpdateCheckStatus.Available
                : UpdateCheckStatus.UpToDate;
            return new ApplicationUpdateCheckResult(status, checkedAt, status == UpdateCheckStatus.Available ? update : null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApplicationUpdateCheckResult(UpdateCheckStatus.Unavailable, Message: "The update check timed out.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            return new ApplicationUpdateCheckResult(UpdateCheckStatus.Unavailable, Message: error.Message);
        }
    }

    public async Task<DownloadedApplicationUpdate> DownloadAndVerifyAsync(
        ApplicationUpdateInfo update,
        string expectedPublisher,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var directory = Path.Combine(_downloadRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "CursorPocket-Setup-x64.exe");
        try
        {
            using var response = await _httpClient.GetAsync(
                update.InstallerUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                total += read;
                if (update.SizeBytes > 0) progress?.Report(Math.Clamp((double)total / update.SizeBytes, 0, 1));
            }
            await target.FlushAsync(cancellationToken);

            if (update.SizeBytes > 0 && total != update.SizeBytes)
            {
                throw new InvalidDataException("The downloaded installer size does not match the release manifest.");
            }
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash),
                Convert.FromHexString(update.Sha256)))
            {
                throw new InvalidDataException("The downloaded installer hash does not match the release manifest.");
            }

            var signature = await _signatureVerifier.VerifyAsync(destination, expectedPublisher, cancellationToken);
            if (!signature.IsValid)
            {
                throw new InvalidDataException(signature.Error ?? "The downloaded installer signature is not trusted.");
            }
            progress?.Report(1);
            return new DownloadedApplicationUpdate(update, destination);
        }
        catch
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            throw;
        }
    }

    public static bool TryValidate(
        ApplicationUpdateManifest? manifest,
        out ApplicationUpdateInfo? update,
        out string? error)
    {
        update = null;
        error = null;
        if (manifest is null || !ReleaseVersion.TryParse(manifest.Version, out var version))
        {
            error = "The release version is missing or invalid.";
            return false;
        }
        if (!Uri.TryCreate(manifest.InstallerUrl, UriKind.Absolute, out var installer) ||
            installer.Scheme != Uri.UriSchemeHttps ||
            !installer.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !installer.AbsolutePath.StartsWith(
                "/tanmaysh17/cursorpocket/releases/download/",
                StringComparison.OrdinalIgnoreCase))
        {
            error = "The installer URL is not an approved CursorPocket GitHub release address.";
            return false;
        }
        if (!Uri.TryCreate(manifest.ReleaseNotesUrl, UriKind.Absolute, out var notes) ||
            notes.Scheme != Uri.UriSchemeHttps ||
            !IsApprovedReleaseNotesUri(notes))
        {
            error = "The release-notes URL is not an approved project HTTPS address.";
            return false;
        }
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            error = "The installer SHA-256 is invalid.";
            return false;
        }
        if (manifest.SizeBytes <= 0 || !Version.TryParse(manifest.MinimumWindowsVersion, out var minimumWindows) ||
            manifest.PublishedAt <= DateTimeOffset.UnixEpoch)
        {
            error = "The release metadata is incomplete.";
            return false;
        }
        update = new ApplicationUpdateInfo(
            version,
            installer,
            manifest.Sha256.ToUpperInvariant(),
            manifest.SizeBytes,
            minimumWindows,
            notes,
            manifest.PublishedAt);
        return true;
    }

    private static bool IsApprovedReleaseNotesUri(Uri uri) =>
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
         uri.AbsolutePath.StartsWith("/tanmaysh17/cursorpocket/releases/", StringComparison.OrdinalIgnoreCase)) ||
        (uri.Host.Equals("tanmaysh17.github.io", StringComparison.OrdinalIgnoreCase) &&
         uri.AbsolutePath.StartsWith("/cursorpocket/", StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        _disposed = true;
        _httpClient.Dispose();
    }
}

public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var clean = value.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        var parts = clean.Split('-', 2);
        var numbers = parts[0].Split('.');
        var patch = 0;
        if (numbers.Length != 3 ||
            !int.TryParse(numbers[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(numbers[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            (numbers.Length > 2 && !int.TryParse(numbers[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch)))
        {
            return false;
        }
        version = new ReleaseVersion(major, minor, numbers.Length > 2 ? patch : 0, parts.Length == 2 ? parts[1] : null);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        return string.Compare(Prerelease, other.Prerelease, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(Prerelease is null ? string.Empty : $"-{Prerelease}")}";
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
}
