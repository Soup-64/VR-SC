using Godot;
using Gst;
using Gst.App;
using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using GLib;

public partial class screenRect : TextureRect
{
    private byte[] _sharedFrameBuffer;
    private Pipeline _pipeline;
    private AppSink _appSink;
    private RenderingDevice _rd;
    private Rid _textureRid;
    private Godot.Texture2Drd _texRd;

    // 1080p RGBA
    private int _width = 1920;
    private int _height = 1080;
    private int _expectedBytes;

   static screenRect()
    {
        // GD.Print("Init soname patching");

        // foreach (AssemblyLoadContext c in AssemblyLoadContext.All)
        // {
        //     c.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
        // }
        // foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        // {
        //     AttachResolver(asm);
        // }

        // AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
        // {
        //     AttachResolver(args.LoadedAssembly);
        // };
    }

    private static void AttachResolver(Assembly asm)
    {
        string name = asm.GetName().Name;

        if (name == null || name.StartsWith("netstandard") || name.StartsWith("gstreamer-sharp") || name.StartsWith("System") || name.StartsWith("Godot") || name.StartsWith("mscorlib"))
            return;
        
        GD.Print($"Setting resolver for {name}");
        try
        {
            NativeLibrary.SetDllImportResolver(asm, ResolveGStreamerLibrary);
        }
        catch(Exception e)
        {
            GD.PrintErr($"\tFailed to set resolver! {e.Message}");
        }
    }

    private static IntPtr CoreResolver(string libraryName, Assembly assembly)
    {
        string targetLib = libraryName.ToLowerInvariant() switch
        {
            "libdl.so.2" or "dl" => "libdl.so",
            var name when name.Contains("gstreamer") || name.Contains("glib") || name.Contains("gobject") => "libgstreamer_android.so",
            _ => null
        };

        if (targetLib == null) return IntPtr.Zero;

        GD.Print($"Redirecting '{libraryName}' -> '{targetLib}' from {assembly.GetName().Name}");
        try
        {
            return NativeLibrary.Load(targetLib, assembly, null);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"\tFailed to load {targetLib}: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    private static IntPtr ResolveGStreamerLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        return CoreResolver(libraryName, assembly);
    }

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string libraryName)
    {
        return CoreResolver(libraryName, assembly);
    }

    public override void _Ready()
    {
        _expectedBytes = _width * _height * 4;
        _sharedFrameBuffer = new byte[_expectedBytes];

        // texture init
        SetupTexture();
        // gst init (DllImport)
        // NativeLibrary.SetDllImportResolver(typeof(Gst.Application).Assembly, ResolveGStreamerLibrary);
        Gst.Application.Init();

        //example src gst-launch-1.0 -v videotestsrc pattern=ball ! video/x-raw,width=1920,height=1080,format=RGBA,framerate=60/1 ! videoconvert ! videorate ! queue ! vaav1enc rate-control=vbr bitrate=10000 ! av1parse ! rtpav1pay ! udpsink host=127.0.0.1 port=8255 sync=false
        //test sink udpsrc port=8255 caps=\"application/x-rtp, media=(string)video, clock-rate=(int)90000, encoding-name=(string)AV1 ! rtpav1depay ! av1parse ! vaav1dec ! videoconvert ! video/x-raw,format=RGBA ! autovideosink

        // gst pipeline
        // Cemit-signals=true required to trigger app sink updates 
        string pipelineString =
            "udpsrc port=8255 buffer-size=2000000 caps=\"application/x-rtp, media=(string)video, clock-rate=(int)90000, encoding-name=(string)AV1\" ! " +
            "rtpjitterbuffer latency=50 drop-on-latency=true ! rtpav1depay ! queue max-size-bytes=0 max-size-buffers=3 max-size-time=0 ! " +
            "av1parse ! decodebin ! videorate ! videoconvert ! video/x-raw,format=RGBA,framerate=60/1 ! " +
            "appsink name=godotsink drop=true max-buffers=1 sync=false emit-signals=true";
        //             swap vaav1dec with decodebin for quest

        _pipeline = Parse.Launch(pipelineString) as Pipeline;

        if (_pipeline == null)
        {
            GD.PrintErr("Error: Parse.Launch failed to return a valid Pipeline.");
            return;
        }

        Element sinkElement = _pipeline.GetByName("godotsink");

        if (sinkElement == null)
        {
            GD.PrintErr("Error: Could not find element named 'godotsink'.");
            return;
        }

        _appSink = sinkElement as AppSink;

        // The cast failed because gstreamer-sharp wrapped it as a generic Element.
        // Workaround: We connect to the raw signal dynamically.
        sinkElement.Connect("new-sample", OnNewSampleDynamic);

        // 5. Start the stream
        _pipeline.SetState(State.Playing);
        GD.Print("GStreamer Pipeline Playing...");
    }

    private void SetupTexture()
    {
        _rd = RenderingServer.GetRenderingDevice();

        var fmt = new RDTextureFormat
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
                        RenderingDevice.TextureUsageBits.CanUpdateBit
        };

        byte[] emptyData = new byte[_expectedBytes];
        var view = new RDTextureView();

        var dataArray = new Godot.Collections.Array<byte[]>
        {
            emptyData
        };

        _textureRid = _rd.TextureCreate(fmt, view, dataArray);

        _texRd = new()
        {
            TextureRdRid = _textureRid
        };
        this.Texture = _texRd;
    }

    // Fallback Element callback if the AppSink wrapper fails
    private void OnNewSampleDynamic(object sender, GLib.SignalArgs args)
    {
        // GD.Print("running!");
        var element = (Element)sender;
        // 'pull-sample' is an action signal, we can trigger it directly to get the sample
        using Sample sample = (Sample)element.Emit("pull-sample");
        using Caps caps = sample.Caps;
        if (caps != null)
        {
            var structure = caps.GetStructure(0);
            if (structure.GetInt("width", out int width) &&
                structure.GetInt("height", out int height))
            {
                if (_width != width || _height != height)
                {
                    GD.Print($"Video Size Changed: {width}x{height}");
                    //rebuild buffer if size changes
                    _width = width;
                    _height = height;
                    _expectedBytes = _width * _height * 4;
                    _sharedFrameBuffer = new byte[_expectedBytes];
                    CallDeferred(method: MethodName.SetupTexture);
                }
            }
        }
        ProcessSample(sample);
    }

    private void UpdateGpuTexture()
    {
        Stopwatch sw = Stopwatch.StartNew();
        _rd.TextureUpdate(_textureRid, 0, _sharedFrameBuffer);
        // GD.Print($"textupdate took {sw.Elapsed.TotalMilliseconds}ms");
    }

    private void ProcessSample(Sample sample)
    {
        if (sample == null) return;

        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch sw = Stopwatch.StartNew();

        using Gst.Buffer buffer = sample.Buffer;

        if (buffer.Map(out MapInfo mapInfo, MapFlags.Read))
        {

            double mapTime = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            unsafe
            {
                // Zero-copy span over GStreamer memory
                var nativeSpan = new ReadOnlySpan<byte>((void*)mapInfo.DataPtr, _expectedBytes);

                // Span over our pre-allocated heap array
                var managedSpan = new Span<byte>(_sharedFrameBuffer);

                // Fast hardware copy from C -> C#
                nativeSpan.CopyTo(managedSpan);
            }

            // Safe to defer, because _sharedFrameBuffer lives on the heap
            CallDeferred(method: MethodName.UpdateGpuTexture);

            double uploadTime = sw.Elapsed.TotalMilliseconds;

            buffer.Unmap(mapInfo);

            // 2. Calculate total allocations for this single frame
            long totalAllocated = GC.GetAllocatedBytesForCurrentThread() - startBytes;

            // Print the profile data (Warning: This will spam your console, use it briefly to gather data)
            // GD.Print($"Allocated: {totalAllocated} bytes | Map Time: {mapTime:F3}ms | GPU Upload Time: {uploadTime:F3}ms");
        }

        // //init to expect size not actual to strip extra data
        // //breaks a later check probably lol
        // byte[] frameData = new byte[_expectedBytes];

        // //Trim data, mapinfo.data does this internally but we only want a subset
        // unsafe
        // {
        //     // Zero-copy: This span points directly to GStreamer's native memory block
        //     Span<byte> nativeSpan = new Span<byte>((void*)mapInfo.DataPtr, _expectedBytes);
        // }
        // // Marshal.Copy(mapInfo.DataPtr, frameData, 0, (int)_expectedBytes);

        // // System.Buffer.BlockCopy(mapInfo.Data, 0, frameData, 0, _expectedBytes);
        // // mapInfo.Data.CopyTo(frameData, 0);

        // CallDeferred(MethodName.UpdateGpuTexture, frameData);

        // buffer.Unmap(mapInfo);
    }

    public override void _ExitTree()
    {
        if (_pipeline != null)
        {
            _pipeline.SetState(State.Null);
            _pipeline.Dispose();
        }
    }
}
