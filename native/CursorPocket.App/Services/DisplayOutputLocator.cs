using System.Runtime.InteropServices;

namespace CursorPocket_App.Services;

/// <summary>
/// Resolves a monitor to the DXGI output index that FFmpeg's <c>ddagrab</c> expects.
/// <para>
/// This exists because two different index spaces were being confused.
/// <c>EnumDisplayMonitors</c> hands out one ordering; <c>ddagrab</c>'s
/// <c>output_idx</c> walks the DXGI outputs of the adapter its D3D11 device landed
/// on. On a multi-monitor machine those orderings routinely disagree, which is how
/// a recording of "this screen" ended up capturing another one.
/// </para>
/// <para>
/// Matching is by device name (<c>\\.\DISPLAYn</c>), which both
/// <c>MONITORINFOEX</c> and <c>DXGI_OUTPUT_DESC</c> report. A monitor attached to a
/// different adapter has no index on the default adapter at all, so callers must
/// handle null by capturing the monitor's rectangle instead.
/// </para>
/// </summary>
internal static class DisplayOutputLocator
{
    public static int? FindOutputIndex(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }
        try
        {
            return FindOnDefaultAdapter(deviceName);
        }
        catch (Exception)
        {
            // Any DXGI failure means we cannot prove which output this monitor is.
            // Recording still has to capture the right pixels, so the caller falls
            // back to grabbing the monitor rectangle directly.
            return null;
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2050:COM interop correctness cannot be guaranteed after trimming",
        Justification = "build-native.ps1 publishes untrimmed. If trimming were ever enabled and these " +
            "interfaces were removed, the call throws and FindOutputIndex returns null, which falls back " +
            "to grabbing the monitor rectangle — degraded performance, still the correct screen.")]
    private static int? FindOnDefaultAdapter(string deviceName)
    {
        var factoryId = typeof(IDxgiFactory1).GUID;
        if (CreateDXGIFactory1(ref factoryId, out var factoryObject) != 0 || factoryObject is null)
        {
            return null;
        }
        var factory = (IDxgiFactory1)factoryObject;
        try
        {
            // ddagrab captures through a D3D11 device created on the default adapter,
            // so only that adapter's outputs are addressable by output_idx.
            if (factory.EnumAdapters1(0, out var adapter) != 0 || adapter is null)
            {
                return null;
            }
            try
            {
                for (var index = 0u; index < 32; index++)
                {
                    if (adapter.EnumOutputs(index, out var output) != 0 || output is null)
                    {
                        return null;
                    }
                    try
                    {
                        if (output.GetDesc(out var description) == 0 &&
                            string.Equals(description.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            return (int)index;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(output);
                    }
                }
                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(adapter);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiOutputDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public NativeMethods.Rect DesktopCoordinates;

        [MarshalAs(UnmanagedType.Bool)]
        public bool AttachedToDesktop;

        public uint Rotation;
        public nint Monitor;
    }

    // The interfaces below declare every inherited method so each real call lands on
    // the right vtable slot. The Reserved entries are never invoked.
    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiFactory1
    {
        // IDXGIObject
        void ReservedSetPrivateData();
        void ReservedSetPrivateDataInterface();
        void ReservedGetPrivateData();
        void ReservedGetParent();

        // IDXGIFactory
        void ReservedEnumAdapters();
        void ReservedMakeWindowAssociation();
        void ReservedGetWindowAssociation();
        void ReservedCreateSwapChain();
        void ReservedCreateSoftwareAdapter();

        // IDXGIFactory1
        [PreserveSig]
        int EnumAdapters1(uint adapter, [MarshalAs(UnmanagedType.Interface)] out IDxgiAdapter1? result);
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiAdapter1
    {
        // IDXGIObject
        void ReservedSetPrivateData();
        void ReservedSetPrivateDataInterface();
        void ReservedGetPrivateData();
        void ReservedGetParent();

        // IDXGIAdapter
        [PreserveSig]
        int EnumOutputs(uint output, [MarshalAs(UnmanagedType.Interface)] out IDxgiOutput? result);
    }

    [ComImport]
    [Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiOutput
    {
        // IDXGIObject
        void ReservedSetPrivateData();
        void ReservedSetPrivateDataInterface();
        void ReservedGetPrivateData();
        void ReservedGetParent();

        // IDXGIOutput
        [PreserveSig]
        int GetDesc(out DxgiOutputDescription description);
    }
}
