using System.Runtime.InteropServices;

namespace Player.Thumbnails;

/// <summary>Decodes one frame with libmpv (the player's own libmpv-2.dll, next to this DLL) and returns it as a
/// 32-bit DIB. Explorer hands us an IStream, not a path: a custom mpv protocol reads the file through it.</summary>
internal static unsafe partial class FrameGrabber
{
    const string Lib = "libmpv-2.dll";
    const int EventShutdown = 1, EventEndFile = 7, EventPlaybackRestart = 21;
    const int FormatInt64 = 4, FormatNodeMap = 8, FormatByteArray = 9;
    const int MpvErrorGeneric = -20, MpvErrorUnsupported = -18;
    const int TimeoutMs = 10_000;

    static readonly Lock LoadLock = new();
    static bool _loaded;

    public static nint Grab(nint stream, int size)
    {
        if (!LoadLibmpv()) return 0;
        var ctx = mpv_create();
        if (ctx == 0) return 0;
        try
        {
            string[][] options =
            [
                ["vo", "null"], ["aid", "no"], ["sid", "no"], ["hwdec", "no"], ["ytdl", "no"], ["load-scripts", "no"],
                ["pause", "yes"], ["hr-seek", "no"],
                // 20 % in: past intros and black openings, and a keyframe seek away.
                ["start", "20%"],
                // Square pixels first so anamorphic video keeps its shape, then fit the box Explorer asked for.
                ["vf", $"lavfi=[scale=iw*sar:ih,setsar=1,scale={size}:{size}:force_original_aspect_ratio=decrease]"],
            ];
            foreach (var o in options) mpv_set_option_string(ctx, o[0], o[1]);
            if (mpv_initialize(ctx) < 0) return 0;
            if (mpv_stream_cb_add_ro(ctx, "nuvora", stream, &Open) < 0) return 0;
            if (Command(ctx, "loadfile", "nuvora://video") < 0 || !WaitForFrame(ctx)) return 0;
            return Screenshot(ctx);
        }
        finally
        {
            mpv_terminate_destroy(ctx);
        }
    }

    /// <summary>The thumbnail host's working folder is System32: load libmpv by full path once, and the
    /// [LibraryImport] calls below bind to the already loaded module by name.</summary>
    static bool LoadLibmpv()
    {
        lock (LoadLock)
        {
            if (_loaded) return true;
            nint self;
            if (!GetModuleHandleExW(6, ComServer.SelfAddress, &self)) return false; // FROM_ADDRESS | UNCHANGED_REFCOUNT
            var buffer = stackalloc char[1024];
            var length = GetModuleFileNameW(self, buffer, 1024);
            if (length == 0 || length >= 1024) return false;
            var dir = Path.GetDirectoryName(new string(buffer, 0, (int)length))!;
            return _loaded = NativeLibrary.TryLoad(Path.Combine(dir, Lib), out _);
        }
    }

    static bool WaitForFrame(nint ctx)
    {
        var deadline = Environment.TickCount64 + TimeoutMs;
        for (long left; (left = deadline - Environment.TickCount64) > 0;)
        {
            switch (*(int*)mpv_wait_event(ctx, left / 1000.0)) // mpv_event.event_id
            {
                case EventPlaybackRestart: return true; // first frame after the start seek is on the (null) VO
                case EventEndFile or EventShutdown: return false; // unreadable, or no video track
            }
        }
        return false;
    }

    static nint Screenshot(nint ctx)
    {
        MpvNode result;
        var args = Utf8Args("screenshot-raw", "video");
        try
        {
            if (mpv_command_ret(ctx, args, &result) < 0) return 0;
        }
        finally
        {
            FreeArgs(args);
        }
        try
        {
            if (result.Format != FormatNodeMap) return 0;
            var map = (MpvNodeList*)result.Value;
            long w = 0, h = 0, stride = 0;
            MpvByteArray* data = null;
            for (var i = 0; i < map->Count; i++)
            {
                var value = map->Values[i];
                switch (Marshal.PtrToStringUTF8(map->Keys[i]))
                {
                    case "w" when value.Format == FormatInt64: w = value.Value; break;
                    case "h" when value.Format == FormatInt64: h = value.Value; break;
                    case "stride" when value.Format == FormatInt64: stride = value.Value; break;
                    case "data" when value.Format == FormatByteArray: data = (MpvByteArray*)value.Value; break;
                }
            }
            // Default format is bgr0: the byte order of a 32-bit DIB.
            if (w <= 0 || h <= 0 || stride < w * 4 || data == null || (long)data->Size < stride * h) return 0;
            return ToBitmap((byte*)data->Data, (int)w, (int)h, stride);
        }
        finally
        {
            mpv_free_node_contents(&result);
        }
    }

    static nint ToBitmap(byte* pixels, int w, int h, long stride)
    {
        var header = new BitmapInfoHeader { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 }; // top-down
        void* bits;
        var bitmap = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (bitmap == 0) return 0;
        var row = w * 4L;
        for (var y = 0; y < h; y++) Buffer.MemoryCopy(pixels + y * stride, (byte*)bits + y * row, row, row);
        return bitmap;
    }

    static int Command(nint ctx, params string[] words)
    {
        var args = Utf8Args(words);
        try
        {
            return mpv_command(ctx, args);
        }
        finally
        {
            FreeArgs(args);
        }
    }

    static nint* Utf8Args(params string[] words)
    {
        var args = (nint*)NativeMemory.AllocZeroed((nuint)(words.Length + 1), (nuint)sizeof(nint));
        for (var i = 0; i < words.Length; i++) args[i] = Marshal.StringToCoTaskMemUTF8(words[i]);
        return args;
    }

    static void FreeArgs(nint* args)
    {
        for (var p = args; *p != 0; p++) Marshal.FreeCoTaskMem(*p);
        NativeMemory.Free(args);
    }

    // ---------------- "nuvora://" protocol: mpv reads the file through Explorer's IStream ----------------
    // mpv calls these from its demuxer thread with the IStream pointer as cookie. The shell's file streams are
    // free-threaded; an apartment-bound one fails the reads and Explorer keeps the icon.

    [UnmanagedCallersOnly]
    static int Open(nint stream, byte* uri, StreamCbInfo* info)
    {
        info->Cookie = stream;
        info->Read = &Read;
        info->Seek = &Seek;
        info->Size = &StreamSize;
        info->Close = &Close;
        return 0;
    }

    /// <summary>IStream vtable slot: 3 Read, 5 Seek, 12 Stat (IUnknown 0-2, ISequentialStream 3-4).</summary>
    static void* Slot(nint com, int index) => (*(void***)com)[index];

    [UnmanagedCallersOnly]
    static long Read(nint stream, byte* buffer, ulong count)
    {
        uint read;
        var hr = ((delegate* unmanaged<nint, byte*, uint, uint*, int>)Slot(stream, 3))(stream, buffer, (uint)Math.Min(count, int.MaxValue), &read);
        return hr < 0 ? -1 : read; // S_FALSE with a short count is end of file
    }

    [UnmanagedCallersOnly]
    static long Seek(nint stream, long offset)
    {
        ulong position;
        var hr = ((delegate* unmanaged<nint, long, uint, ulong*, int>)Slot(stream, 5))(stream, offset, 0, &position); // STREAM_SEEK_SET
        return hr < 0 ? MpvErrorGeneric : (long)position;
    }

    [UnmanagedCallersOnly]
    static long StreamSize(nint stream)
    {
        var stat = stackalloc byte[96]; // STATSTG is 80 bytes on x64; cbSize at offset 16
        var hr = ((delegate* unmanaged<nint, byte*, uint, int>)Slot(stream, 12))(stream, stat, 1); // STATFLAG_NONAME
        return hr < 0 ? MpvErrorUnsupported : *(long*)(stat + 16);
    }

    [UnmanagedCallersOnly]
    static void Close(nint stream) { } // ThumbnailProvider owns the stream reference

    // ---------------- native types and imports ----------------

    [StructLayout(LayoutKind.Sequential)]
    struct MpvNode { public long Value; public int Format; } // union { char*, int64, list*, byte_array* } + format

    [StructLayout(LayoutKind.Sequential)]
    struct MpvNodeList { public int Count; public MpvNode* Values; public nint* Keys; }

    [StructLayout(LayoutKind.Sequential)]
    struct MpvByteArray { public void* Data; public nuint Size; }

    [StructLayout(LayoutKind.Sequential)]
    struct StreamCbInfo
    {
        public nint Cookie;
        public delegate* unmanaged<nint, byte*, ulong, long> Read;
        public delegate* unmanaged<nint, long, long> Seek;
        public delegate* unmanaged<nint, long> Size;
        public delegate* unmanaged<nint, void> Close;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }

    [LibraryImport(Lib)] private static partial nint mpv_create();
    [LibraryImport(Lib)] private static partial int mpv_initialize(nint ctx);
    [LibraryImport(Lib)] private static partial void mpv_terminate_destroy(nint ctx);
    [LibraryImport(Lib)] private static partial nint mpv_wait_event(nint ctx, double timeout);
    [LibraryImport(Lib)] private static partial int mpv_command(nint ctx, nint* args);
    [LibraryImport(Lib)] private static partial int mpv_command_ret(nint ctx, nint* args, MpvNode* result);
    [LibraryImport(Lib)] private static partial void mpv_free_node_contents(MpvNode* node);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int mpv_set_option_string(nint ctx, string name, string data);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int mpv_stream_cb_add_ro(nint ctx, string protocol, nint userData, delegate* unmanaged<nint, byte*, StreamCbInfo*, int> open);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleExW(uint flags, nint address, nint* module);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetModuleFileNameW(nint module, char* buffer, uint size);
}
