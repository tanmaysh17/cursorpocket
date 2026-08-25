# Releasing CursorPocket

CursorPocket public releases are signed Windows Setup executables hosted by GitHub Releases. GitHub Pages links to the latest installer and hosts the static update manifest used by the app.

## One-time GitHub and Azure setup

1. Create an Azure Artifact Signing account and a Public Trust certificate profile for the verified individual publisher **Tanmay Sharma**.
2. Create an Entra application and federated GitHub credential restricted to this repository and the `release` environment.
3. Grant that identity only the **Artifact Signing Certificate Profile Signer** role on the certificate profile.
4. Create a protected GitHub environment named `release`. Restrict it to version tags and require review before first production use.
5. Add these environment secrets:
   - `AZURE_CLIENT_ID`
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`
6. Add these environment variables:
   - `AZURE_ARTIFACT_SIGNING_ENDPOINT`
   - `AZURE_ARTIFACT_SIGNING_ACCOUNT`
   - `AZURE_ARTIFACT_SIGNING_PROFILE`
7. In repository Settings → Pages, select **GitHub Actions** as the source.

No PFX file, private key, or certificate password belongs in GitHub.

## Prepare a stable version

Update `native/Version.props` so `CursorPocketVersion` is a stable semantic version such as `0.4.0` and `CursorPocketFileVersion` is its four-part Windows equivalent such as `0.4.0.0`. Update `CHANGELOG.md`, then build and test locally:

```powershell
dotnet restore .\native\CursorPocket.Native.sln -p:RuntimeIdentifier=win-x64
dotnet test .\native\CursorPocket.Tests\CursorPocket.Tests.csproj -c Release --no-restore
.\native\build-native.ps1 -SkipPortableArchive -SkipMsix -RequireInstaller
```

Do not share the local installer as a public release; it is unsigned.

## Publish

Merge the stable version and changelog heading to `main`. After the ordinary Windows build and tests pass, the workflow creates the matching annotated tag and explicitly queues the existing signed-release job. The tag step is idempotent: rerunning it resumes dispatch when the tag already points to the same `main` commit, but rejects reusing a released version for newer code.

For recovery or a deliberately manual release, create and push the matching annotated tag yourself:

```powershell
git tag -a v0.4.0 -m "CursorPocket 0.4.0"
git push origin v0.4.0
```

The release workflow tests, signs the app, builds and signs the installer, validates publisher and timestamp, performs a clean silent install, compares the installed payload, generates hashes and `update.json`, attests provenance, publishes the GitHub Release, and deploys Pages. Any failed stage prevents publication.

Afterward, download through the public Pages button on a clean Windows 10 and Windows 11 account and complete the onboarding, capture, update-check, uninstall, and reinstall smoke tests.
