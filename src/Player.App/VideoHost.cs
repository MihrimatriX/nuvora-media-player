using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Player.App;

/// <summary>Native child window that mpv renders into (passed as "wid").</summary>
public sealed partial class VideoHost : NativeControlHost
{
    const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, SS_BLACKRECT = 0x4;

    public event Action<nint>? HandleCreated;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = OperatingSystem.IsWindows()
            ? new PlatformHandle(CreateWindowExW(0, "STATIC", null, WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | SS_BLACKRECT,
                0, 0, 1, 1, parent.Handle, 0, 0, 0), "HWND")
            : base.CreateNativeControlCore(parent);
        // Let attachment finish before mpv starts parenting its window to ours.
        Dispatcher.UIThread.Post(() => HandleCreated?.Invoke(handle.Handle));
        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (OperatingSystem.IsWindows()) DestroyWindow(control.Handle);
        else base.DestroyNativeControlCore(control);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(int exStyle, string className, string? windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);
}
