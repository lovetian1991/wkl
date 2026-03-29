using System.Runtime.InteropServices;

namespace LanRemoteControl.Agent;

/// <summary>
/// P/Invoke and COM interop definitions for DXGI Desktop Duplication and D3D11 APIs.
/// </summary>
internal static class NativeMethods
{
    // ─── HRESULTs ───────────────────────────────────────────────────────
    public const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    public const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);

    // ─── D3D11 Constants ────────────────────────────────────────────────
    public const uint D3D11_SDK_VERSION = 7;
    public const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    public const uint D3D_FEATURE_LEVEL_10_0 = 0xa000;
    public const uint D3D_FEATURE_LEVEL_10_1 = 0xa100;
    public const uint D3D_FEATURE_LEVEL_11_0 = 0xb000;
    public const uint D3D11_USAGE_STAGING = 3;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    public const uint D3D11_MAP_READ = 1;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    // ─── COM IIDs ───────────────────────────────────────────────────────
    public static Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static Guid IID_IDXGIOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    public static Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    // ─── Structures ─────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced;
        public int ProtectedContentMaskedOut;
        public DXGI_OUTDUPL_POINTER_POSITION PointerPosition;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_POINTER_POSITION
    {
        public POINT Position;
        public int Visible;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public char[] DeviceName;
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public uint Rotation;
        public nint Monitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public nint pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    // ─── D3D11CreateDevice ──────────────────────────────────────────────

    [DllImport("d3d11.dll", PreserveSig = true)]
    public static extern int D3D11CreateDevice(
        nint pAdapter,
        uint DriverType,
        nint Software,
        uint Flags,
        [In] uint[] pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out nint ppDevice,
        out uint pFeatureLevel,
        out nint ppImmediateContext);

    // ─── COM vtable-based method calls ──────────────────────────────────
    //
    // We call COM methods by reading function pointers from the vtable.
    // Each COM interface pointer points to a vtable pointer, which in turn
    // points to an array of function pointers.
    //
    // IUnknown vtable layout:
    //   [0] QueryInterface
    //   [1] AddRef
    //   [2] Release

    // ─── IDXGIDevice (inherits IUnknown → IDXGIObject) ─────────────────
    // IDXGIObject vtable: [0-2] IUnknown, [3] SetPrivateData, [4] SetPrivateDataInterface, [5] GetPrivateData, [6] GetParent
    // IDXGIDevice vtable: [7] GetAdapter, [8] CreateSurface, [9] QueryResourceResidency, [10] SetGPUThreadPriority, [11] GetGPUThreadPriority

    public static int IDXGIDevice_GetAdapter(nint dxgiDevice, out nint adapter)
    {
        var vtable = Marshal.ReadIntPtr(dxgiDevice);
        var fn = Marshal.ReadIntPtr(vtable, 7 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<IDXGIDevice_GetAdapterDelegate>(fn);
        return del(dxgiDevice, out adapter);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int IDXGIDevice_GetAdapterDelegate(nint self, out nint adapter);

    // ─── IDXGIAdapter (inherits IDXGIObject) ────────────────────────────
    // IDXGIObject vtable: [0-6]
    // IDXGIAdapter: [7] EnumOutputs, [8] GetDesc, [9] CheckInterfaceSupport

    public static int IDXGIAdapter_EnumOutputs(nint adapter, uint outputIndex, out nint output)
    {
        var vtable = Marshal.ReadIntPtr(adapter);
        var fn = Marshal.ReadIntPtr(vtable, 7 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<IDXGIAdapter_EnumOutputsDelegate>(fn);
        return del(adapter, outputIndex, out output);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int IDXGIAdapter_EnumOutputsDelegate(nint self, uint outputIndex, out nint output);

    // ─── IDXGIOutput (inherits IDXGIObject) ─────────────────────────────
    // IDXGIObject: [0-6]
    // IDXGIOutput: [7] GetDesc, [8] GetDisplayModeList, [9] FindClosestMatchingMode,
    //              [10] WaitForVBlank, [11] TakeOwnership, [12] ReleaseOwnership,
    //              [13] GetGammaControlCapabilities, [14] SetGammaControl, [15] GetGammaControl,
    //              [16] SetDisplaySurface, [17] GetDisplaySurfaceData, [18] GetFrameStatistics

    public static int IDXGIOutput_GetDesc(nint output, out DXGI_OUTPUT_DESC desc)
    {
        var vtable = Marshal.ReadIntPtr(output);
        var fn = Marshal.ReadIntPtr(vtable, 7 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<IDXGIOutput_GetDescDelegate>(fn);
        return del(output, out desc);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int IDXGIOutput_GetDescDelegate(nint self, out DXGI_OUTPUT_DESC desc);

    // ─── IDXGIOutput1 (inherits IDXGIOutput) ────────────────────────────
    // IDXGIOutput: [0-18]
    // IDXGIOutput1: [19] GetDisplayModeList1, [20] FindClosestMatchingMode1,
    //               [21] GetDisplaySurfaceData1, [22] DuplicateOutput

    public static int IDXGIOutput1_DuplicateOutput(nint output1, nint device, out nint outputDuplication)
    {
        var vtable = Marshal.ReadIntPtr(output1);
        var fn = Marshal.ReadIntPtr(vtable, 22 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<IDXGIOutput1_DuplicateOutputDelegate>(fn);
        return del(output1, device, out outputDuplication);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int IDXGIOutput1_DuplicateOutputDelegate(nint self, nint device, out nint outputDuplication);

    // ─── IDXGIOutputDuplication (inherits IDXGIObject) ──────────────────
    // IDXGIObject: [0-6]
    // IDXGIOutputDuplication: [7] GetDesc, [8] AcquireNextFrame, [9] GetFrameDirtyRects,
    //                         [10] GetFrameMoveRects, [11] GetFramePointerShape,
    //                         [12] MapDesktopSurface, [13] UnMapDesktopSurface, [14] ReleaseFrame

    public static int IDXGIOutputDuplication_AcquireNextFrame(
        nint duplication, uint timeoutMs, out DXGI_OUTDUPL_FRAME_INFO frameInfo, out nint desktopResource)
    {
        var vtable = Marshal.ReadIntPtr(duplication);
        var fn = Marshal.ReadIntPtr(vtable, 8 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<AcquireNextFrameDelegate>(fn);
        return del(duplication, timeoutMs, out frameInfo, out desktopResource);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AcquireNextFrameDelegate(
        nint self, uint timeoutMs, out DXGI_OUTDUPL_FRAME_INFO frameInfo, out nint desktopResource);

    public static void IDXGIOutputDuplication_ReleaseFrame(nint duplication)
    {
        var vtable = Marshal.ReadIntPtr(duplication);
        var fn = Marshal.ReadIntPtr(vtable, 14 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<ReleaseFrameDelegate>(fn);
        del(duplication);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReleaseFrameDelegate(nint self);

    // ─── ID3D11Device (inherits IUnknown) ───────────────────────────────
    // IUnknown: [0-2]
    // ID3D11Device: [3] CreateBuffer, [4] CreateTexture1D, [5] CreateTexture2D, ...

    public static int ID3D11Device_CreateTexture2D(
        nint device, ref D3D11_TEXTURE2D_DESC desc, nint initialData, out nint texture)
    {
        var vtable = Marshal.ReadIntPtr(device);
        var fn = Marshal.ReadIntPtr(vtable, 5 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(fn);
        return del(device, ref desc, initialData, out texture);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(
        nint self, ref D3D11_TEXTURE2D_DESC desc, nint initialData, out nint texture);

    // ─── ID3D11DeviceContext (inherits ID3D11DeviceChild → IUnknown) ────
    // IUnknown: [0-2]
    // ID3D11DeviceChild: [3] GetDevice, [4] GetPrivateData, [5] SetPrivateData, [6] SetPrivateDataInterface
    // ID3D11DeviceContext has many methods. Key ones:
    //   [14] Map, [15] Unmap
    //   [47] CopyResource

    public static void ID3D11DeviceContext_CopyResource(nint context, nint dst, nint src)
    {
        var vtable = Marshal.ReadIntPtr(context);
        var fn = Marshal.ReadIntPtr(vtable, 47 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(fn);
        del(context, dst, src);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceDelegate(nint self, nint dst, nint src);

    public static int ID3D11DeviceContext_Map(
        nint context, nint resource, uint subresource, uint mapType, uint mapFlags,
        out D3D11_MAPPED_SUBRESOURCE mappedResource)
    {
        var vtable = Marshal.ReadIntPtr(context);
        var fn = Marshal.ReadIntPtr(vtable, 14 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<MapDelegate>(fn);
        return del(context, resource, subresource, mapType, mapFlags, out mappedResource);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapDelegate(
        nint self, nint resource, uint subresource, uint mapType, uint mapFlags,
        out D3D11_MAPPED_SUBRESOURCE mappedResource);

    public static void ID3D11DeviceContext_Unmap(nint context, nint resource, uint subresource)
    {
        var vtable = Marshal.ReadIntPtr(context);
        var fn = Marshal.ReadIntPtr(vtable, 15 * nint.Size);
        var del = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(fn);
        del(context, resource, subresource);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapDelegate(nint self, nint resource, uint subresource);
}
