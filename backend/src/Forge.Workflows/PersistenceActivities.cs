using Forge.Domain.Entities;
using Forge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;

namespace Forge.Workflows;

// Writes the workflow's state transitions back to Postgres. docs/011-Database.md §3:
// the `tasks` table holds current state directly, updated in place - Temporal's own
// history is authoritative for replay/audit, but the DB copy must stay in sync for the
// board (docs/013-Frontend.md) to ever show anything but "Inbox".
//
// KNOWN SIMPLIFICATION: this opens its own short-lived DbContext per call from a
// connection string env var, rather than using the Worker's DI container to inject one
// (Temporal's .NET SDK supports activity instances resolved via a service provider -
// that's the more idiomatic pattern and should replace this once the Worker itself
// takes on a proper DI setup). Good enough to prove the workflow keeps Postgres honest;
// not the final shape.
public static class PersistenceActivities
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("FORGE_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev";

    [Activity]
    public static async Task PersistTaskStateAsync(Guid taskId, TaskState state)
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ForgeDbContext(options);

        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return; // task row missing is an operational anomaly, not this activity's to resolve
        task.State = state;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        // docs/007-ExecutionEngine.md §4 - same NOTIFY channel AgentActivities uses,
        // so a state transition wakes the frontend's WebSocket subscribers just like
        // an event does.
        await PostgresNotify.TaskChangedAsync(db, taskId);
    }
}
