using Forge.Domain.Entities;
using Forge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;

namespace Forge.Workflows;

public record SchedulingSnapshot(
    int ExecutingCount,
    Guid? TopBacklogTaskId,
    int UnprioritizedBacklogCount,
    int MaxConcurrentExecuting);

// docs/006-Scheduler.md §1 - queries Postgres directly (the authoritative current-state
// store per docs/011-Database.md §3) rather than tracking counts in workflow memory,
// so the scheduler can never drift from what's actually true.
public static class SchedulingActivities
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("FORGE_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev";

    // TaskWorkflow's gate between Todo and Executing: the scheduler's promotion
    // decision (GetSchedulingSnapshotAsync below) was made from a snapshot taken
    // before PromoteToTodoAsync was signaled, so another task can race into
    // Executing between that snapshot and this task actually starting. Rechecking
    // ExecutingCount here, right before the Executing transition, closes that race.
    // Reads Project.MaxConcurrentExecuting (same column BacklogSchedulerWorkflow's
    // ShouldPromote reads via GetSchedulingSnapshotAsync below) rather than its own
    // constant - found live while merging this in: an earlier draft hardcoded a
    // separate `MaxConcurrentExecutingPerProject = 2` here, which would have silently
    // drifted from a project's actual configured limit the moment someone changed it.
    [Activity]
    public static async Task<bool> HasExecutingCapacityAsync(Guid taskId)
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ForgeDbContext(options);

        var task = await db.Tasks
            .Where(t => t.Id == taskId)
            .Select(t => new { t.ProjectId, MaxConcurrentExecuting = t.Project!.MaxConcurrentExecuting })
            .FirstAsync();

        var executingCount = await db.Tasks
            .CountAsync(t => t.ProjectId == task.ProjectId && t.State == TaskState.Executing);

        return executingCount < task.MaxConcurrentExecuting;
    }

    [Activity]
    public static async Task<SchedulingSnapshot> GetSchedulingSnapshotAsync(Guid projectId)
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ForgeDbContext(options);

        var maxConcurrentExecuting = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => p.MaxConcurrentExecuting)
            .FirstAsync();

        var executingCount = await db.Tasks
            .CountAsync(t => t.ProjectId == projectId && t.State == TaskState.Executing);

        // docs/005-Agents.md §3: priority first, creation order as tiebreak.
        var topBacklogTaskId = await db.Tasks
            .Where(t => t.ProjectId == projectId && t.State == TaskState.Backlog)
            .OrderBy(t => t.Priority ?? int.MaxValue)
            .ThenBy(t => t.CreatedAt)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync();

        var unprioritizedCount = await db.Tasks
            .CountAsync(t => t.ProjectId == projectId && t.State == TaskState.Backlog && t.Priority == null);

        return new SchedulingSnapshot(executingCount, topBacklogTaskId, unprioritizedCount, maxConcurrentExecuting);
    }
}
