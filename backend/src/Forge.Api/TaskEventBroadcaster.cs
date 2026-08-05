using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Forge.Api;

// docs/007-ExecutionEngine.md §4 / docs/012-API.md §3 - the "Forge API trace
// endpoint" side of the two-channel design. Holds one WebSocket connection list per
// task in memory (fine for a single-process API at this scale - see docs/002-Architecture.md
// §4, multi-instance API is a v3+ concern per docs/016-Roadmap.md, not designed for here).
public class TaskEventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<WebSocket>> _connections = new();

    public void Add(Guid taskId, WebSocket socket) =>
        _connections.GetOrAdd(taskId, _ => []).Add(socket);

    public void Remove(Guid taskId, WebSocket socket)
    {
        if (_connections.TryGetValue(taskId, out var sockets))
        {
            var remaining = sockets.Where(s => s != socket).ToArray();
            _connections[taskId] = new ConcurrentBag<WebSocket>(remaining);
        }
    }

    // Payload is deliberately minimal ("refresh") - clients re-fetch the task/events
    // over the existing REST endpoints rather than this channel carrying the actual
    // data. Keeps the WebSocket layer dumb (a wake-up signal) and the REST endpoints
    // as the single source of truth for shape, matching docs/012-API.md.
    public async Task NotifyAsync(Guid taskId)
    {
        if (!_connections.TryGetValue(taskId, out var sockets)) return;

        var payload = new ArraySegment<byte>(Encoding.UTF8.GetBytes("refresh"));
        var stale = new List<WebSocket>();

        foreach (var socket in sockets)
        {
            if (socket.State != WebSocketState.Open)
            {
                stale.Add(socket);
                continue;
            }
            try
            {
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception)
            {
                stale.Add(socket);
            }
        }

        if (stale.Count > 0)
        {
            var remaining = sockets.Where(s => !stale.Contains(s)).ToArray();
            _connections[taskId] = new ConcurrentBag<WebSocket>(remaining);
        }
    }
}
