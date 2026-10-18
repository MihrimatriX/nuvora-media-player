using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Player.Windows.Interop;

// Shell COM interfaces, declared in vtable order for the COM source generator (no built-in interop, AOT/trim safe).
// Only the methods Nuvora calls are typed precisely; the rest exist to keep the slots in place.

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
internal unsafe partial interface ITaskbarList3
{
    [PreserveSig] int HrInit();
    [PreserveSig] int AddTab(nint hwnd);
    [PreserveSig] int DeleteTab(nint hwnd);
    [PreserveSig] int ActivateTab(nint hwnd);
    [PreserveSig] int SetActiveAlt(nint hwnd);
    [PreserveSig] int MarkFullscreenWindow(nint hwnd, int fullscreen);
    [PreserveSig] int SetProgressValue(nint hwnd, ulong completed, ulong total);
    [PreserveSig] int SetProgressState(nint hwnd, TaskbarProgressState state);
    [PreserveSig] int RegisterTab(nint tab, nint mdi);
    [PreserveSig] int UnregisterTab(nint tab);
    [PreserveSig] int SetTabOrder(nint tab, nint insertBefore);
    [PreserveSig] int SetTabActive(nint tab, nint mdi, uint reserved);
    [PreserveSig] int ThumbBarAddButtons(nint hwnd, uint count, ThumbButton* buttons);
    [PreserveSig] int ThumbBarUpdateButtons(nint hwnd, uint count, ThumbButton* buttons);
    [PreserveSig] int ThumbBarSetImageList(nint hwnd, nint imageList);
    [PreserveSig] int SetOverlayIcon(nint hwnd, nint icon, string? description);
    [PreserveSig] int SetThumbnailTooltip(nint hwnd, string? tip);
    [PreserveSig] int SetThumbnailClip(nint hwnd, nint rect);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ThumbButton
{
    public const uint MaskIcon = 0x2, MaskTooltip = 0x4, MaskFlags = 0x8;
    public const uint FlagEnabled = 0x0, FlagDisabled = 0x1;

    public uint Mask;
    public uint Id;
    public uint Bitmap;
    public nint Icon;
    public fixed char Tip[260];
    public uint Flags;

    public void SetTip(string text)
    {
        var span = MemoryMarshal.CreateSpan(ref Tip[0], 260);
        span.Clear();
        text.AsSpan(0, Math.Min(text.Length, 259)).CopyTo(span);
    }
}

[GeneratedComInterface, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9")]
internal partial interface IObjectArray
{
    uint GetCount();
    void GetAt(uint index, in Guid riid, out nint item);
}

[GeneratedComInterface, Guid("5632b1a4-e38a-400a-928a-d4cd63230295")]
internal partial interface IObjectCollection : IObjectArray
{
    void AddObject(IShellLinkW item);
    void AddFromArray(IObjectArray source);
    void RemoveObjectAt(uint index);
    void Clear();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("6332debf-87b5-4670-90c0-5e57b408a49e")]
internal partial interface ICustomDestinationList
{
    void SetAppID(string appId);
    void BeginList(out uint minSlots, in Guid riid, out nint removed);
    [PreserveSig] int AppendCategory(string category, IObjectArray items);
    void AppendKnownCategory(int category);
    void AddUserTasks(IObjectArray tasks);
    void CommitList();
    void GetRemovedDestinations(in Guid riid, out nint removed);
    void DeleteList(string? appId);
    void AbortList();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    void GetPath(char* file, int cch, nint findData, uint flags);
    void GetIDList(out nint pidl);
    void SetIDList(nint pidl);
    void GetDescription(char* name, int cch);
    void SetDescription(string name);
    void GetWorkingDirectory(char* dir, int cch);
    void SetWorkingDirectory(string dir);
    void GetArguments(char* args, int cch);
    void SetArguments(string args);
    void GetHotkey(out ushort hotkey);
    void SetHotkey(ushort hotkey);
    void GetShowCmd(out int showCmd);
    void SetShowCmd(int showCmd);
    void GetIconLocation(char* path, int cch, out int index);
    void SetIconLocation(string path, int index);
    void SetRelativePath(string path, uint reserved);
    void Resolve(nint hwnd, uint flags);
    void SetPath(string file);
}

[GeneratedComInterface, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
internal partial interface IPropertyStore
{
    uint GetCount();
    void GetAt(uint index, out PropertyKey key);
    void GetValue(in PropertyKey key, out PropVariant value);
    void SetValue(in PropertyKey key, in PropVariant value);
    void Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PropertyKey(Guid FormatId, uint PropertyId)
{
    /// <summary>System.Title: the text a jump list shows for a shell link.</summary>
    public static readonly PropertyKey Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
}

/// <summary>Just enough of PROPVARIANT for a VT_LPWSTR value (the union starts at offset 8 on every architecture).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public const ushort VtLpwstr = 31;
    public ushort Type;
    public ushort Reserved1, Reserved2, Reserved3;
    public nint Pointer;
    public nint Extra;
}

internal static partial class ComObjects
{
    static readonly StrategyBasedComWrappers Wrappers = new();

    public static readonly Guid ClsidTaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    public static readonly Guid ClsidDestinationList = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    public static readonly Guid ClsidObjectCollection = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    public static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

    public static T Create<T>(Guid clsid) where T : class
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, 0, 1 /* CLSCTX_INPROC_SERVER */, typeof(T).GUID, out var p));
        return Wrap<T>(p);
    }

    /// <summary>Wraps a raw interface pointer and releases the caller's reference.</summary>
    public static T Wrap<T>(nint p) where T : class
    {
        try { return (T)Wrappers.GetOrCreateObjectForComInstance(p, CreateObjectFlags.None); }
        finally { Marshal.Release(p); }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}

internal static partial class User32
{
    public const uint WmCommand = 0x0111;
    public const int ThbnClicked = 0x1800;
    public const int SmCxSmIcon = 49;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    public static partial nint CreateIconIndirect(in IconInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX, HotspotY;
        public nint Mask, Color;
    }
}

internal static unsafe partial class Gdi32
{
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint dc, in BitmapInfoHeader header, uint usage, out void* bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, void* bits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
}
