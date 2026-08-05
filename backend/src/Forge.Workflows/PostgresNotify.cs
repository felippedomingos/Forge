using Forge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Forge.Workflows;

// docs/007-ExecutionEngine.md §4 - shared by AgentActivities/PersistenceActivities
// (Forge.Workflows, runs in the Worker process) and Forge.Api's /answers endpoint.
public static class PostgresNotify
{
    // PostgreSQL's NOTIFY payload only accepts a string literal, not a bind
    // parameter - confirmed live: using EF Core's parameterized ExecuteSqlAsync threw
    // "42601: syntax error at or near '$1'" because Postgres's NOTIFY grammar won't
    // accept a parameter placeholder there. ExecuteSqlRawAsync is safe here
    // specifically because the value is a typed Guid (not a raw user-supplied
    // string) - Guid.ToString() can only ever produce hex digits and hyphens, so
    // there is no injection surface despite the analyzer's blanket warning.
    public static async Task TaskChangedAsync(ForgeDbContext db, Guid taskId)
    {
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"NOTIFY task_events, '{taskId}'");
#pragma warning restore EF1002
    }
}
