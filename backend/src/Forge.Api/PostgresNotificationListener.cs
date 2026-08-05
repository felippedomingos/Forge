using Npgsql;

namespace Forge.Api;

// Bridges Forge.Workflows (running in a separate Worker process) to this API's
// in-memory WebSocket connections (TaskEventBroadcaster), via Postgres LISTEN/NOTIFY
// on the `task_events` channel - so the Worker never needs to know this API's
// address, and the API doesn't need to poll the database to find out something
// changed. Requires its own dedicated connection (LISTEN doesn't work through the
// normal pooled EF Core connections, which get returned/reused).
public class PostgresNotificationListener(TaskEventBroadcaster broadcaster, IConfiguration config)
    : BackgroundService
{
    private string ConnectionString =>
        config.GetConnectionString("Forge")
        ?? "Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync(stoppingToken);

                connection.Notification += async (_, e) =>
                {
                    if (Guid.TryParse(e.Payload, out var taskId))
                        await broadcaster.NotifyAsync(taskId);
                };

                await using (var cmd = new NpgsqlCommand("LISTEN task_events", connection))
                    await cmd.ExecuteNonQueryAsync(stoppingToken);

                // Npgsql only delivers notifications while a command is in flight or
                // WaitAsync is polling the connection - loop keeps this connection
                // alive and listening until cancelled or the connection drops.
                while (!stoppingToken.IsCancellationRequested)
                    await connection.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Connection dropped (e.g. Postgres restarted) - back off and retry
                // rather than letting the whole API crash over a transient DB blip.
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
