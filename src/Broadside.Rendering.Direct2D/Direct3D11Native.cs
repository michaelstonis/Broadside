using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;

namespace Broadside.Rendering.Direct2D;

/// <summary>The one Direct3D 11 entry point the backend calls, declared with opaque pointers so that none of the Direct3D 11 interfaces needs to be bound.</summary>
internal static unsafe partial class Direct3D11Native
{
    /// <summary>Creates a Direct3D 11 device with the default adapter, feature levels and no immediate-context out parameter (<c>D3D11CreateDevice</c>).</summary>
    public static HRESULT CreateDevice(D3D_DRIVER_TYPE type, D3D11_CREATE_DEVICE_FLAG flags, ID3D11Device** device) =>
        new(D3D11CreateDevice(null, (int)type, 0, (uint)flags, null, 0, PInvoke.D3D11_SDK_VERSION, device, null, null));

    [LibraryImport("d3d11.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int D3D11CreateDevice(
        void* adapter,
        int driverType,
        nint software,
        uint flags,
        void* featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        ID3D11Device** device,
        void* featureLevel,
        void** immediateContext);
}
