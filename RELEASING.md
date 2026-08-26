# Releasing CursorPocket

CursorPocket public releases contain the unsigned Windows Setup executable and a universal, ad-hoc-signed macOS app archive hosted by GitHub Releases. The macOS artifact currently supports interactive region/window screenshots only and must not be described as feature-equivalent to Windows. The Windows app reads its static update manifest from the latest Release asset; GitHub Pages links to the installer and temporarily mirrors the manifest for v0.4.1 compatibility. This release path uses only the free infrastructure available to public GitHub repositories; it does not require Azure, Apple Developer credentials, a paid signing certificate, repository secrets, or a `release` environment.

## One-time GitHub setup

1. In repository **Settings → Actions → General**, leave Actions enabled and set **Workflow permissions** to **Read and write permissions**. If the repository instead enforces restricted defaults, grant this workflow the permissions declared in `.github/workflows/native-windows.yml`.
2. In **Settings → Pages**, select **GitHub Actions** as the source.
3. In **Settings → Environments**, no `release` environment is needed. GitHub may create and manage the `github-pages` environment automatically.

That is the complete hosted setup. Do not add Azure credentials, PFX files, private keys, or certificate passwords.

## Prepare a stable version

Update `native/Version.props` so `CursorPocketVersion` is a stable semantic version such as `0.4.1` and `CursorPocketFileVersion` is its four-part Windows equivalent such as `0.4.1.0`. Update `CHANGELOG.md`, then build and test locally:

```powershell
dotnet restore .\native\CursorPocket.Native.sln -p:RuntimeIdentifier=win-x64
dotnet test .\native\CursorPocket.Tests\CursorPocket.Tests.csproj -c Release --no-restore
.\native\build-native.ps1 -SkipPortableArchive -SkipMsix -RequireInstaller
```

## Publish

Every app PR must advance `native/Version.props` to a stable version and add the matching changelog heading; CI rejects a reused or missing version before merge. After the ordinary Windows build and tests pass on `main`, the workflow creates the matching annotated tag and explicitly queues the release job. Tagging, Release asset upload, and manifest deployment are safe to rerun after a partial failure, while a released version cannot be reused for newer code.

For recovery or a deliberately manual release, create and push the matching annotated tag yourself:

```powershell
git tag -a v0.4.1 -m "CursorPocket 0.4.1"
git push origin v0.4.1
```

The release workflows test the app, build and verify the Windows installer, build and verify the universal macOS preview, perform a clean Windows silent install, generate hashes and `update.json`, attest GitHub build provenance, publish the GitHub Release assets, and queue the main-branch Pages workflow for v0.4.1 compatibility. The macOS workflow waits for the Windows workflow to create the tag's Release, then uploads `CursorPocket-macOS-universal.zip` with replacement enabled so interrupted publication can be rerun safely. Any failed release stage prevents that platform's artifact from publishing.

## What users will see

Because the installer is intentionally unsigned, Windows identifies it as **Unknown publisher** and Microsoft Defender SmartScreen may show **Windows protected your PC**. Tell users to download only from the CursorPocket site or this repository's GitHub Releases page, then use **More info → Run anyway** if they trust the project. Never tell users to install a certificate or disable SmartScreen.

Builds released before 0.4.1 still require the old code-signing publisher during self-update, so they cannot automatically install the first unsigned release. Existing users must download and install 0.4.1 manually once. Releases after that use the verified unsigned path automatically.

After each release, download through the public Pages button on a clean Windows 10 and Windows 11 account and complete the onboarding, capture, update-check, uninstall, and reinstall smoke tests.

If the project later receives a trusted code-signing certificate, set `ApplicationUpdateCoordinator.ExpectedPublisher` to the exact certificate publisher and add signing before the workflow's installed-payload verification step. The existing verifier will then enforce the certificate again.
