using Godot;
using Gst;
using Gst.App;
using GLib;
using System;
using System.Runtime.InteropServices;

public partial class screenRect : TextureRect
{
    private Pipeline _pipeline;
    private AppSink _appSink;
    private RenderingDevice _rd;
    private Rid _textureRid;
    private Godot.Texture2Drd _texRd;

    // 1080p RGBA
    private int _width = 1920;
    private int _height = 1080;
    private int _expectedBytes;

    public override void _Ready()
    {
        _expectedBytes = _width * _height * 4;

        // 1. Setup the Godot GPU Texture
        SetupTexture();

        // 2. Initialize GStreamer
        Gst.Application.Init();

        //example src gst-launch-1.0 -v videotestsrc pattern=ball ! video/x-raw,width=1920,height=1080,format=RGBA,framerate=60/1 ! videoconvert ! videorate ! queue ! vaav1enc rate-control=vbr bitrate=10000 ! av1parse ! rtpav1pay ! udpsink host=127.0.0.1 port=8255 sync=false
        //test sink udpsrc port=8255 caps=\"application/x-rtp, media=(string)video, clock-rate=(int)90000, encoding-name=(string)AV1 ! rtpav1depay ! av1parse ! vaav1dec ! videoconvert ! video/x-raw,format=RGBA ! autovideosink

        // gst pipeline
        // Cemit-signals=true required to trigger app sink updates 
        string pipelineString =
            "udpsrc port=8255 caps=\"application/x-rtp, media=(string)video, clock-rate=(int)90000, encoding-name=(string)AV1\" ! queue ! " +
            "rtpav1depay ! av1parse ! vaav1dec ! videorate ! videoconvert ! video/x-raw,format=RGBA,framerate=60/1 ! " +
            "appsink name=godotsink drop=true max-buffers=1 sync=false emit-signals=true";

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

        var dataArray = new Godot.Collections.Array<byte[]>();
        dataArray.Add(emptyData);

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
        ProcessSample(sample);
    }

    private void ProcessSample(Sample sample)
    {
        if (sample == null) return;

        using Gst.Buffer buffer = sample.Buffer;

        if (buffer.Map(out MapInfo mapInfo, MapFlags.Read))
        {
            //init to expect size not actual to strip extra data
            //breaks a later check probably lol
            byte[] frameData = new byte[_expectedBytes];

            System.Buffer.BlockCopy(mapInfo.Data, 0, frameData, 0, _expectedBytes);
            // mapInfo.Data.CopyTo(frameData, 0);

            CallDeferred(MethodName.UpdateGpuTexture, frameData);

            buffer.Unmap(mapInfo);
        }
    }

    private void UpdateGpuTexture(byte[] frameData)
    {
        if (frameData.Length == _expectedBytes)
        {
            var ret = _rd.TextureUpdate(_textureRid, 0, frameData);
            // GD.Print(ret);
        }
        else
        {
            GD.Print($"no draw! got: {frameData.Length} =/= expect: {_expectedBytes}");
        }
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