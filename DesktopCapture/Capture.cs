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
        var startTcs = new TaskCompletionSource<uint>();

        await startRequest.WatchResponseAsync(reply =>
        {
            if (reply.response == 0)
            {
                var streams = ((uint nodeId, IDictionary<string, object> props)[])reply.results["streams"];
                uint selectedNodeId = streams[0].nodeId;
                startTcs.SetResult(selectedNodeId);
            }
            else
            {
                startTcs.SetException(new Exception("user cancel!"));
            }
        });

        uint pipewireNode = await startTcs.Task;
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

        // string pipestr = $"pipewiresrc fd={fd} path={pipewireNode} ! videorate ! video/x-raw,format=I420,framerate=60/1 ! videoconvert ! filesink location=wayland_capture.mp4";
        string pipestr = $"pipewiresrc fd={fd} path={pipewireNode} ! " +
                            $"videoconvert ! video/x-raw,format=I420,framerate=60/1 ! " +
                            $"x264enc pass=qual quantizer=20 speed-preset=superfast ! matroskamux ! " + //tune=zerolatency if you want video, unlimited video, but no video.
                            $"filesink location=wayland_capture.mkv";

        var pipeline = Parse.Launch(pipestr);
        Console.WriteLine(pipeline != null);
        if (pipeline == null) return;
        pipeline.SetState(State.Playing);

        Console.ReadLine();

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