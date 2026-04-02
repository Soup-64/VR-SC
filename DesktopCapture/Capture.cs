using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus;
using Gst;
using Task = System.Threading.Tasks.Task;

namespace DesktopCapture;

class Capture
{
    public static async Task ProcessDbus()
    {
        int reqnum = 0;
        string sessionToken = "VR_Screencaster_app_session_0";
        string handleTokenPrefix = "VR_Screencaster_app_request_"; //append reqnum to track request number

        using var connection = new Connection(Address.Session);
        await connection.ConnectAsync();

        var castPortal = connection.CreateProxy<IScreenCast>(
            "org.freedesktop.portal.Desktop",
            "/org/freedesktop/portal/desktop"
        );

        Console.WriteLine("1. Asking portal to create a session...");

        //create session
        ObjectPath createHandle = await castPortal.CreateSessionAsync(new Dictionary<string, object>
        {
            { "session_handle_token", sessionToken },
            { "handle_token", handleTokenPrefix + reqnum++ }
        });

        // figure out session path
        var createRequest = connection.CreateProxy<IRequest>("org.freedesktop.portal.Desktop", createHandle);
        var sessionTcs = new TaskCompletionSource<ObjectPath>();

        await createRequest.WatchResponseAsync(reply =>
        {
            if (reply.response == 0)
            {
                string realSessionPath = reply.results["session_handle"].ToString()!;
                sessionTcs.SetResult(new ObjectPath(realSessionPath));
            }
            else
            {
                sessionTcs.SetException(new Exception("User or system rejected session creation."));
            }
        });

        //await session
        ObjectPath realSessionHandle = await sessionTcs.Task;
        Console.WriteLine($"Session established at: {realSessionHandle}");

        Console.WriteLine("2. Prompting user to select screens/windows...");

        //select windows
        ObjectPath selectRequestHandle = await castPortal.SelectSourcesAsync(realSessionHandle, new Dictionary<string, object>
            {
                { "handle_token", handleTokenPrefix + reqnum++ },
                { "types", (uint)3 },       //1 monitor 2 window 3 both
                { "multiple", false },      //allow multiple selections
                { "cursor_mode", (uint)2 }, //1 no cursor 2 show cursor 4 cursor metadata
                { "persist_mode", (uint)1 } //0 no persist 1 while running 2 until revoked
            });

        var selectRequest = connection.CreateProxy<IRequest>("org.freedesktop.portal.Desktop", selectRequestHandle);
        var selectTcs = new TaskCompletionSource<bool>();

        await selectRequest.WatchResponseAsync(reply =>
        {
            if (reply.response == 0)
            {
                selectTcs.SetResult(true);
            }
            else
            {
                selectTcs.SetException(new Exception("config error!"));
            }
        });

        await selectTcs.Task;

        ObjectPath startRequestHandle = await castPortal.StartAsync(realSessionHandle, "", new Dictionary<string, object>
            {
                { "handle_token", handleTokenPrefix + reqnum++ }
            });

        var startRequest = connection.CreateProxy<IRequest>("org.freedesktop.portal.Desktop", startRequestHandle);

        var startTcs = new TaskCompletionSource<(uint nodeId, int width, int height)>();

        await startRequest.WatchResponseAsync(reply =>
        {
            if (reply.response == 0)
            {
                var streams = ((uint nodeId, IDictionary<string, object> props)[])reply.results["streams"];
                uint selectedNodeId = streams[0].nodeId;
                var props = streams[0].props;
                Console.WriteLine(streams.Length);
                foreach (var prop in streams[0].props)
                {
                    Console.WriteLine($"key: {prop.Key} value: {prop.Value}");
                }

                int width = 1920;
                int height = 1080;

                if (props.TryGetValue("size", out var sizeObj) && sizeObj is ValueTuple<int, int> size)
                {
                    width = size.Item1;
                    height = size.Item2;
                }
                else
                {
                    Console.WriteLine("Dimensions error! using fallback...");
                }

                startTcs.SetResult((selectedNodeId, width, height));
            }
            else
            {
                startTcs.SetException(new Exception("user cancel!"));
            }
        });

        var (pipewireNode, sourceWidth, sourceHeight) = await startTcs.Task;
        Console.WriteLine("accept!");

        CloseSafeHandle fdHandle = await castPortal.OpenPipeWireRemoteAsync(
            realSessionHandle,
            new Dictionary<string, object>() //no options needed
        );

        //Linux FD
        IntPtr fd = fdHandle.DangerousGetHandle();
        Console.WriteLine($"Setup done... FD: {fd}, PW node: {pipewireNode}");

        Application.Init();
        Console.WriteLine(Application.VersionString());

        //cpu version
        //rav1enc vs svtav1enc, hard call
        // string pipestr = $"pipewiresrc fd={fd} path={pipewireNode} always-copy=true ! " +
        //                 $"video/x-raw ! " +
        //                 $"videoconvert ! videorate ! " +
        //                 $"video/x-raw,format=I420,framerate=60/1 ! queue ! " +
        //                 $"svtav1enc target-bitrate=10000 ! av1parse ! " + //mostly unusable, tuning needed?
        //                 // $"rav1enc bitrate=10000 ! av1parse ! " +  //actually unusable
        //                 $"rtpav1pay ! udpsink host=127.0.0.1 port=8255 sync=false"; 
        //                 $"matroskamux ! filesink location=wayland_capture_cpu.mkv";

        //keepalive-time resend every 16ms if no new frames to avoid latency spikes on lazy processing
        //always-copy trying to work with raw dmabuf formats in VRAM seems to be impossible so we have to copy and soft reformat
        //(colorspace pixel format stuff mostly) before encoding
        //videoscale/videorate required to use fps and other values when specifying format
        //queue required to eliminate stutter
        //av1 since very fast and light on the network 
        //we use vbr to enhance fine low contrast details, otherwise very blocky, variable bitrate with a 10,000 limit
        //av1parse is just a formality thing, not really sure what it does other than being a post encode step,
        //containerization/headers maybe?
        //rtpav1pay wrap into rtp (realtime protocol) packets sent using UDP
        //then udp sink as usual
        //sync=false to ignore timing data because we want the most recent frame regardless in this case,
        //very important for streaming/realtime, otherwise you get absurd stutter/latency issues

        //KNOWN ISSUES
        //resizing will nuke the stream, pipeline has to be rebuilt but handling this is very hard
        //artifacting on certain window sizes
        //mostly mitigated by targeting full screen share only instead
        string pipestr = $"pipewiresrc fd={fd} path={pipewireNode} always-copy=true keepalive-time=16 ! " +
                        $"videoconvert ! videorate ! " +
                        $"video/x-raw,format=NV12,framerate=60/1 ! queue ! " +
                        $"vaav1enc rate-control=vbr bitrate=10000 ! av1parse ! " + //rav1enc for cpu?
                        $"rtpav1pay ! udpsink host=127.0.0.1 port=8255 sync=false";
        //                 //$"matroskamux ! filesink location=wayland_capture_gpu.mkv";

        var pipeline = Parse.Launch(pipestr);
        Console.WriteLine($"Pipeline valid? [{pipeline != null}]");
        Console.WriteLine($"Dims: {sourceWidth}X{sourceHeight}");
        if (pipeline == null) return;
        pipeline.SetState(State.Playing);

        Console.ReadLine();

        //for testing file size
        // Thread.Sleep(5000);

        pipeline.SendEvent(Event.NewEos());

        var bus = pipeline.Bus;
        var msg = bus.TimedPopFiltered(Constants.CLOCK_TIME_NONE, MessageType.Eos | MessageType.Error);

        if (msg.Type == MessageType.Error)
        {
            msg.ParseError(out GLib.GException err, out string debug);
            Console.WriteLine($"GStreamer Error: {err.Message} - {debug}");
        }
        else if (msg.Type == MessageType.Eos)
        {
            Console.WriteLine("End of stream reached successfully.");
        }

        pipeline.SetState(State.Null);
        pipeline.Dispose();
        fdHandle.Close();
    }
}

[DBusInterface("org.freedesktop.portal.Request")]
public interface IRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint response, IDictionary<string, object> results)> handler);
}

[DBusInterface("org.freedesktop.portal.ScreenCast")]
public interface IScreenCast : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
    Task<ObjectPath> SelectSourcesAsync(ObjectPath sessionHandle, IDictionary<string, object> options);
    Task<ObjectPath> StartAsync(ObjectPath sessionHandle, string parentWindow);
    Task<ObjectPath> StartAsync(ObjectPath sessionHandle, string parentWindow, IDictionary<string, object> options);
    Task<CloseSafeHandle> OpenPipeWireRemoteAsync(ObjectPath sessionHandle, IDictionary<string, object> options);
}
