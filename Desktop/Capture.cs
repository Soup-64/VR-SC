using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tmds.DBus;

class Capture
{

    public static async Task processDbus()
    {
        using var connection = new Connection(Address.Session);
        await connection.ConnectAsync();

        var castPortal = connection.CreateProxy<IScreenCast>(
            "org.freedesktop.portal.Desktop", 
            "/org/freedesktop/portal/desktop"
        );

        Console.WriteLine("Asking user for screen capture permission...");
        var options = new Dictionary<string, object>
        {
            { "interactive", true } // Prompt the user to select screen/window
        };
        ObjectPath requestPath = await castPortal.SelectSourcesAsync("", options);
        var request = connection.CreateProxy<IRequest>("org.freedesktop.portal.Desktop", requestPath);
        var tcs = new TaskCompletionSource<string>();

        IDisposable watcher = await request.WatchResponseAsync(reply =>
        {
            if (reply.response == 0)
            {
                string uri = reply.results["uri"].ToString();
                tcs.SetResult(uri);
            }
            else
            {
                tcs.SetException(new Exception("User cancelled the screen capture."));
            }
        });

        try
        {
            string savedFileUri = await tcs.Task;
            Console.WriteLine($"Success! Screenshot saved to: {savedFileUri}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            watcher.Dispose();
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
        Task<SafeHandle> OpenPipeWireRemoteAsync(ObjectPath sessionHandle, IDictionary<string, object> options);
    }
}