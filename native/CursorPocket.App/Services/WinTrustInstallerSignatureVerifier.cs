using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CursorPocket.Core.Updates;

namespace CursorPocket_App.Services;

public sealed class WinTrustInstallerSignatureVerifier : IInstallerSignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public Task<InstallerVerificationResult> VerifyAsync(
        string path,
        string expectedPublisher,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return Task.FromResult(new InstallerVerificationResult(false, Error: "The downloaded installer is missing."));
        }

        using var fileInfo = new WinTrustFileInfo(path);
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePointer, false);
            var trustData = new WinTrustData(filePointer);
            var action = GenericVerifyV2;
            var status = WinVerifyTrust(nint.Zero, ref action, ref trustData);
            if (status != 0)
            {
                return Task.FromResult(new InstallerVerificationResult(
                    false,
                    Error: $"Windows did not trust the installer signature (0x{status:X8})."));
            }

            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            var publisher = certificate.GetNameInfo(X509NameType.SimpleName, false);
            if (!string.Equals(publisher, expectedPublisher, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new InstallerVerificationResult(
                    false,
                    publisher,
                    $"The installer is signed by '{publisher}', not the expected publisher '{expectedPublisher}'."));
            }
            return Task.FromResult(new InstallerVerificationResult(true, publisher));
        }
        catch (Exception error) when (error is CryptographicException or ExternalException)
        {
            return Task.FromResult(new InstallerVerificationResult(false, Error: error.Message));
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo : IDisposable
    {
        public int StructSize = Marshal.SizeOf<WinTrustFileInfo>();
        public nint FilePath;
        public nint FileHandle = nint.Zero;
        public nint KnownSubject = nint.Zero;

        public WinTrustFileInfo(string path) => FilePath = Marshal.StringToCoTaskMemUni(path);

        public void Dispose()
        {
            if (FilePath == nint.Zero) return;
            Marshal.FreeCoTaskMem(FilePath);
            FilePath = nint.Zero;
            GC.SuppressFinalize(this);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public int StructSize;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public int UiChoice;
        public int RevocationChecks;
        public int UnionChoice;
        public nint FileInfo;
        public int StateAction;
        public nint StateData;
        public nint UrlReference;
        public int ProviderFlags;
        public int UiContext;
        public nint SignatureSettings;

        public WinTrustData(nint fileInfo)
        {
            StructSize = Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = nint.Zero;
            SipClientData = nint.Zero;
            UiChoice = 2; // WTD_UI_NONE
            RevocationChecks = 0;
            UnionChoice = 1; // WTD_CHOICE_FILE
            FileInfo = fileInfo;
            StateAction = 0;
            StateData = nint.Zero;
            UrlReference = nint.Zero;
            ProviderFlags = 0x00000080; // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
            UiContext = 0;
            SignatureSettings = nint.Zero;
        }
    }
}
