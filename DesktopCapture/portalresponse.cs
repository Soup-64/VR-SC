using Tmds.DBus.Protocol;

/* USE:
var response = await connection.Call(
            () => screenCast.CreateSessionAsync(
                new Dictionary<string, VariantValue> {
                    { "handle_token", "s0" },
                    { "session_handle_token", "s1" },
                }), log).ConfigureAwait(false);
*/

class PortalResponse {
    public required string RequestPath { get; init; }
    public required Dictionary<string, VariantValue> Results { get; init; }

    public static async Task<PortalResponse> WaitAsync(DBusConnection connection,
                                                       Func<Task<ObjectPath>> request,
                                                       CancellationToken cancel = default) {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<PortalResponse>();
        var result = new TaskCompletionSource<PortalResponse>();
        string? requestPath = null;

        using var matcher = await connection.AddMatchAsync(
            new() {
                Type = MessageType.Signal,
                Member = "Response",
                Interface = "org.freedesktop.portal.Request",
            },
            reader: (message, state) => {
                var reader = message.GetBodyReader();
                uint arg0 = reader.ReadUInt32();
                return new PortalResponse {
                    RequestPath = message.PathAsString,
                    Results = reader.ReadDictionaryOfStringToVariantValue(),
                };
            },
            handler: (ex, response, _, __) => {
                Console.WriteLine($"Got {response}");
                if (ex is null) {
                    lock (results) {
                        if (requestPath != null) {
                            if (response.RequestPath != requestPath)
                                return;

                            result.SetResult(response);
                            return;
                        }

                        results.Add(response);
                    }
                } else {
                    result.SetException(ex);
                }
            },
            ObserverFlags.NoSubscribe, emitOnCapturedContext: false).ConfigureAwait(false);

        var path = await request().WaitAsync(cancel).ConfigureAwait(false);

        lock (results) {
            requestPath = path;
            if (results.Find(r => r.RequestPath == path) is { } response)
                return response;
        }

        return await result.Task.WaitAsync(cancel).ConfigureAwait(false);
    }
    
    public override string ToString() {
        var result = new System.Text.StringBuilder();
        result.Append(this.RequestPath.LastIndexOf('/'));
        result.Append("{ ");
        foreach (var (key, value) in this.Results) {
            result.Append(key);
            result.Append('=');
            result.Append(value);
            result.AppendLine(",");
        }

        result.Append('}');
        return result.ToString();
    }
}

static class ConnectionExtensions {
    public static Task<PortalResponse> Call(this DBusConnection connection,
                                            Func<Task<ObjectPath>> request,
                                            CancellationToken cancel = default)
        => PortalResponse.WaitAsync(connection, request, cancel);
}