using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Player.Thumbnails;

/// <summary>The DLL's COM entry points. Explorer's thumbnail host calls DllGetClassObject with our CLSID
/// (registered by FileAssociation under HKCU\Software\Classes\CLSID), then asks the factory for a provider per file.</summary>
public static unsafe class ComServer
{
    public static readonly Guid Clsid = new("8B1F5C3E-6A2D-4F7B-9E41-C2D07A93B615");

    internal const int S_OK = 0, S_FALSE = 1, E_FAIL = unchecked((int)0x80004005), E_UNEXPECTED = unchecked((int)0x8000FFFF),
        CLASS_E_NOAGGREGATION = unchecked((int)0x80040110), CLASS_E_CLASSNOTAVAILABLE = unchecked((int)0x80040111);

    static readonly StrategyBasedComWrappers Wrappers = new();

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    static int DllGetClassObject(Guid* clsid, Guid* iid, nint* ppv)
    {
        *ppv = 0;
        try
        {
            return *clsid == Clsid ? Query(new ClassFactory(), *iid, out *ppv) : CLASS_E_CLASSNOTAVAILABLE;
        }
        catch
        {
            return E_FAIL; // an exception leaving an UnmanagedCallersOnly method kills the host process
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    static int DllCanUnloadNow() => S_FALSE; // a NativeAOT runtime cannot be unloaded

    /// <summary>A COM pointer to <paramref name="instance"/> for the interface <paramref name="iid"/>.</summary>
    internal static int Query(object instance, in Guid iid, out nint ppv)
    {
        var unknown = Wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);
        var hr = Marshal.QueryInterface(unknown, iid, out ppv);
        Marshal.Release(unknown);
        return hr;
    }

    /// <summary>Address of a function in this DLL, for finding the folder it was loaded from.</summary>
    internal static nint SelfAddress => (nint)(delegate* unmanaged<int>)&DllCanUnloadNow;
}

[GeneratedComInterface, Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, in Guid iid, out nint obj);
    [PreserveSig] int LockServer(int fLock);
}

[GeneratedComInterface, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
internal partial interface IInitializeWithStream
{
    /// <param name="stream">IStream over the file; read through its vtable by <see cref="FrameGrabber"/>.</param>
    [PreserveSig] int Initialize(nint stream, uint mode);
}

[GeneratedComInterface, Guid("e357fccd-a995-4576-b01f-234630154e96")]
internal partial interface IThumbnailProvider
{
    /// <param name="alpha">WTS_ALPHATYPE; the frame is opaque.</param>
    [PreserveSig] int GetThumbnail(uint cx, out nint hbitmap, out int alpha);
}

[GeneratedComClass]
internal sealed partial class ClassFactory : IClassFactory
{
    public int CreateInstance(nint outer, in Guid iid, out nint obj)
    {
        obj = 0;
        return outer != 0 ? ComServer.CLASS_E_NOAGGREGATION : ComServer.Query(new ThumbnailProvider(), iid, out obj);
    }

    public int LockServer(int fLock) => ComServer.S_OK;
}

/// <summary>Stream-based, so Windows can run it isolated in its COM Surrogate (dllhost) instead of inside Explorer.</summary>
[GeneratedComClass]
internal sealed partial class ThumbnailProvider : IInitializeWithStream, IThumbnailProvider
{
    const int WTSAT_RGB = 1;
    nint _stream;

    public int Initialize(nint stream, uint mode)
    {
        if (_stream != 0) return ComServer.E_UNEXPECTED;
        Marshal.AddRef(stream);
        _stream = stream;
        return ComServer.S_OK;
    }

    public int GetThumbnail(uint cx, out nint hbitmap, out int alpha)
    {
        alpha = WTSAT_RGB;
        hbitmap = 0;
        if (_stream == 0) return ComServer.E_UNEXPECTED;
        try
        {
            hbitmap = FrameGrabber.Grab(_stream, (int)Math.Clamp(cx, 16, 2560));
            return hbitmap != 0 ? ComServer.S_OK : ComServer.E_FAIL;
        }
        catch
        {
            return ComServer.E_FAIL;
        }
        finally
        {
            // Explorer asks once per instance; holding the stream longer would keep the video file open.
            Marshal.Release(_stream);
            _stream = 0;
        }
    }

    ~ThumbnailProvider()
    {
        if (_stream != 0) Marshal.Release(_stream);
    }
}
