using System.Runtime.InteropServices;

namespace Player.Core;

internal static partial class MpvNative
{
    // ponytail: Windows name only; Linux/macOS need a NativeLibrary resolver for libmpv.so.2 / libmpv.2.dylib.
    const string Lib = "libmpv-2";

    internal const int EventShutdown = 1, EventEndFile = 7, EventFileLoaded = 8, EventPlaybackRestart = 21, EventPropertyChange = 22;
    internal const int EndFileReasonError = 4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEvent { public int EventId; public int Error; public ulong ReplyUserdata; public nint Data; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEventProperty { public nint Name; public PropertyKind Format; public nint Data; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEventEndFile { public int Reason; public int Error; }

    [LibraryImport(Lib)] internal static partial nint mpv_create();
    [LibraryImport(Lib)] internal static partial int mpv_initialize(nint ctx);
    [LibraryImport(Lib)] internal static partial void mpv_terminate_destroy(nint ctx);
    [LibraryImport(Lib)] internal static partial nint mpv_wait_event(nint ctx, double timeout);
    [LibraryImport(Lib)] internal static partial nint mpv_error_string(int error);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_set_option_string(nint ctx, string name, string data);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_observe_property(nint ctx, ulong userdata, string name, PropertyKind format);

    // args: null-terminated array of UTF-8 strings. mpv copies them before returning.
    // (The runtime can't marshal string[] as UTF-8, so MpvPlayer.Command converts by hand.)
    [LibraryImport(Lib)] internal static partial int mpv_command_async(nint ctx, ulong userdata, nint[] args);
    [LibraryImport(Lib)] internal static partial int mpv_command(nint ctx, nint[] args);

    internal static string ErrorString(int error) => Marshal.PtrToStringUTF8(mpv_error_string(error)) ?? error.ToString();
}
