using System.Runtime.InteropServices;

namespace Player.Enhancement;

/// <summary>Primary hardware GPU, read through DXGI (IDXGIAdapter1::GetDesc1).</summary>
public sealed partial record GpuInfo(string Name, uint VendorId)
{
    public const uint Nvidia = 0x10DE, Intel = 0x8086, Amd = 0x1002;

    /// <summary>RTX VSR / RTX Video HDR need an RTX card (Tensor cores).</summary>
    public bool HasRtxVideo => VendorId == Nvidia && Name.Contains("RTX", StringComparison.OrdinalIgnoreCase);

    /// <summary>Null when DXGI is unavailable (non-Windows, no hardware adapter).</summary>
    public static GpuInfo? Detect()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return DetectDxgi(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    static unsafe GpuInfo? DetectDxgi()
    {
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
        if (CreateDXGIFactory1(iid, out var factory) < 0) return null;
        try
        {
            // IDXGIFactory1 vtable slot 12 = EnumAdapters1; IDXGIAdapter1 slot 10 = GetDesc1.
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(*(nint**)factory)[12];
            for (uint i = 0; ; i++)
            {
                nint adapter;
                if (enumAdapters1(factory, i, &adapter) < 0) return null; // DXGI_ERROR_NOT_FOUND: no more adapters
                try
                {
                    var getDesc1 = (delegate* unmanaged[Stdcall]<nint, AdapterDesc1*, int>)(*(nint**)adapter)[10];
                    AdapterDesc1 desc;
                    if (getDesc1(adapter, &desc) < 0 || (desc.Flags & 2) != 0) continue; // 2 = software adapter
                    return new GpuInfo(new string(desc.Description), desc.VendorId);
                }
                finally { Release(adapter); }
            }
        }
        finally { Release(factory); }
    }

    static unsafe void Release(nint com) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)com)[2])(com);

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid riid, out nint factory);

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }
}
