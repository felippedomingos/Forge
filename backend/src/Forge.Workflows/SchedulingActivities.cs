using Forge.Domain.Entities;
using Forge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

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
    // ExecutingCount here, right before the Executing transition, was meant to close
    // that race. Reads Project.MaxConcurrentExecuting (same column
    // BacklogSchedulerWorkflow's ShouldPromote reads via GetSchedulingSnapshotAsync
    // below) rather than its own constant - found live while merging this in: an
    // earlier draft hardcoded a separate `MaxConcurrentExecutingPerProject = 2` here,
    // which would have silently drifted from a project's actual configured limit the
    // moment someone changed it.
    //
    // Found live (2026-08-07): a bare read-only check here does NOT actually close
    // the race it exists for. Two Todo tasks for the same project can both call this
    // when exactly one slot is free - both read the same pre-write count, both get
    // `true`, both promote (the real Executing-state write happens moments later, in
    // TaskWorkflow.cs's own SetStateAsync call, well outside this activity). Confirmed
    // live: two DeveloperStarted events 17ms apart in the same project, and that
    // project sitting at 3 Executing tasks against a MaxConcurrentExecuting of 2.
    // Rechecking more often doesn't help - only claiming the slot atomically, in the
    // same transaction as the check, does. A per-project Postgres advisory
    // transaction lock (`pg_advisory_xact_lock(hashtext(projectId))`) serializes
    // concurrent callers for the SAME project (a different project's callers proceed
    // independently) - the second caller's count query then runs only after the
    // first has already committed its claim, so it correctly sees the slot as taken.
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

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({task.ProjectId.ToString()}))");

        var executingCount = await db.Tasks
            .CountAsync(t => t.ProjectId == task.ProjectId && t.State == TaskState.Executing);

        if (executingCount >= task.MaxConcurrentExecuting)
        {
            await tx.RollbackAsync();
            return false;
        }

        // Claim the slot now, inside the lock - TaskWorkflow.cs's own SetStateAsync
        // call right after this returns true is a harmless, idempotent re-write of
        // the same value (it also fires the NOTIFY the frontend's WebSocket listens
        // for, unchanged).
        await db.Tasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.State, TaskState.Executing)
            .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow));
        await tx.CommitAsync();
        return true;
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

    // Founder-requested (2026-08-06) safeguard: this session hit three separate real
    // incidents where a TaskWorkflow died outright (a missing activity registration,
    // twice) or got wedged in an infinite non-determinism retry loop, and the affected
    // task just sat frozen in whatever state it was in - no error visible anywhere on
    // the board, only discoverable by cross-checking Temporal directly. Called
    // periodically from BacklogSchedulerWorkflow (already a per-project long-running
    // loop) rather than as its own workflow - one fewer moving part, and this project's
    // scheduler is already the thing responsible for this project's tasks making
    // progress.
    //
    // Two tiers, both auto-recovering via the same mechanism POST /tasks/{id}/resume
    // uses (start a fresh execution with resumeFrom=the task's own last-persisted
    // State, so nothing already-completed gets redone):
    //   1. Workflow already terminal (Failed/Terminated/TimedOut/Canceled) - always
    //      recovered regardless of how long ago that happened, since nothing will ever
    //      move a task like that forward on its own.
    //   2. Workflow still Running but the task's own state hasn't moved in
    //      StaleThreshold, AND it's in one of the agent-driven states (nothing here
    //      should ever be waiting on a human) - only recovered if the workflow's own
    //      most recent history event is itself a task failure, the concrete symptom of
    //      the non-determinism loop found live. A Blocked/AwaitingPublish/Publishing/
    //      Review task can legitimately sit for hours waiting on a person and must
    //      never be touched just for being old.
    private static readonly TaskState[] AgentDrivenStates =
    [
        TaskState.Inbox, TaskState.Backlog, TaskState.Todo, TaskState.Executing,
    ];

    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(15);

    [Activity]
    public static async Task<int> RecoverStuckTasksAsync(Guid projectId)
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ForgeDbContext(options);

        var candidates = await db.Tasks
            .Where(t => t.ProjectId == projectId && t.State != TaskState.Done && t.State != TaskState.Production)
            .Select(t => new { t.Id, t.State, t.UpdatedAt })
            .ToListAsync();

        if (candidates.Count == 0) return 0;

        var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
        {
            TargetHost = Environment.GetEnvironmentVariable("TEMPORAL_ADDRESS") ?? "localhost:7233",
            Namespace = "default",
        });

        var recovered = 0;
        foreach (var task in candidates)
        {
            var workflowId = $"task-{task.Id}";
            var handle = client.GetWorkflowHandle(workflowId);

            WorkflowExecutionDescription description;
            try
            {
                description = await handle.DescribeAsync();
            }
            catch (Exception)
            {
                continue; // Workflow genuinely doesn't exist - a different recovery path (row/workflow race), not this one.
            }

            var isTerminal = description.Status is WorkflowExecutionStatus.Failed
                or WorkflowExecutionStatus.Terminated or WorkflowExecutionStatus.TimedOut
                or WorkflowExecutionStatus.Canceled;

            var isStale = DateTimeOffset.UtcNow - task.UpdatedAt > StaleThreshold;
            var isAgentDriven = AgentDrivenStates.Contains(task.State);

            var stuckInDeterminismLoop = false;
            if (!isTerminal && isStale && isAgentDriven)
            {
                stuckInDeterminismLoop = await LastHistoryEventIsTaskFailureAsync(handle);
            }

            if (!isTerminal && !stuckInDeterminismLoop) continue;

            if (stuckInDeterminismLoop)
            {
                try
                {
                    await handle.TerminateAsync("Auto-recovered: stuck in a non-determinism retry loop");
                }
                catch (Exception)
                {
                    // Already gone between the describe and here - fine, StartWorkflowAsync below still applies.
                }
            }

            try
            {
                await client.StartWorkflowAsync(
                    (TaskWorkflow wf) => wf.RunAsync(task.Id, task.State),
                    new WorkflowOptions(workflowId, "forge-task-queue"));
            }
            catch (Exception)
            {
                continue; // A workflow is already running for this id after all - nothing to do.
            }

            db.Events.Add(new DomainEvent
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                Type = "AutoRecovered",
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    fromStatus = description.Status.ToString(),
                    resumeFrom = task.State.ToString(),
                    reason = stuckInDeterminismLoop ? "non-determinism retry loop" : "workflow already terminal",
                }),
                OccurredAt = DateTimeOffset.UtcNow,
                Actor = "system:auto-recovery",
            });
            recovered++;
        }

        if (recovered > 0) await db.SaveChangesAsync();
        return recovered;
    }

    private static async Task<bool> LastHistoryEventIsTaskFailureAsync(WorkflowHandle handle)
    {
        Temporalio.Api.Enums.V1.EventType? lastType = null;
        await foreach (var evt in handle.FetchHistoryEventsAsync())
        {
            lastType = evt.EventType;
        }
        return lastType == Temporalio.Api.Enums.V1.EventType.WorkflowTaskFailed;
    }
}

public record SchedulerHealthCheckResult(List<Guid> ProjectIds, int RestartedSchedulers);

// docs/006-Scheduler.md §4a's own auto-recovery only runs from inside a project's
// BacklogSchedulerWorkflow - found live (2026-08-07) that this makes the whole
// safeguard's uptime entirely dependent on that one workflow's uptime: 7 SlayZone
// project schedulers were deliberately terminated earlier in the same session (to stop
// a real over-promotion incident, docs/016-Roadmap.md) and never restarted, so
// RecoverStuckTasksAsync simply never ran again for any of them - two tasks sat stuck
// for 2.5+ hours with zero recovery attempted, the exact failure this class of
// safeguard exists to prevent. GlobalWatchdogWorkflow (this file's sibling) calls this
// on a fixed interval, independent of any single project's scheduler.
public static class GlobalWatchdogActivities
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("FORGE_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev";

    [Activity]
    public static async Task<SchedulerHealthCheckResult> EnsureSchedulersRunningAsync()
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ForgeDbContext(options);

        var projectIds = await db.Projects.Select(p => p.Id).ToListAsync();
        if (projectIds.Count == 0) return new SchedulerHealthCheckResult(projectIds, 0);

        var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
        {
            TargetHost = Environment.GetEnvironmentVariable("TEMPORAL_ADDRESS") ?? "localhost:7233",
            Namespace = "default",
        });

        var restarted = 0;
        foreach (var projectId in projectIds)
        {
            var workflowId = $"scheduler-{projectId}";
            var handle = client.GetWorkflowHandle(workflowId);

            bool needsStart;
            try
            {
                var description = await handle.DescribeAsync();
                needsStart = description.Status is WorkflowExecutionStatus.Failed
                    or WorkflowExecutionStatus.Terminated or WorkflowExecutionStatus.TimedOut
                    or WorkflowExecutionStatus.Canceled;
            }
            catch (Exception)
            {
                needsStart = true; // never started at all (e.g. an older project row predating this workflow)
            }

            if (!needsStart) continue;

            try
            {
                await client.StartWorkflowAsync(
                    (BacklogSchedulerWorkflow wf) => wf.RunAsync(projectId),
                    new WorkflowOptions(workflowId, "forge-task-queue"));
            }
            catch (Exception)
            {
                continue; // race - already running after all
            }

            db.Events.Add(new DomainEvent
            {
                Id = Guid.NewGuid(),
                TaskId = null, // system-level, not scoped to one task - docs/003-Domain.md §1
                Type = "SchedulerAutoRecovered",
                Payload = System.Text.Json.JsonSerializer.Serialize(new { projectId }),
                OccurredAt = DateTimeOffset.UtcNow,
                Actor = "system:global-watchdog",
            });
            restarted++;
        }

        if (restarted > 0) await db.SaveChangesAsync();
        return new SchedulerHealthCheckResult(projectIds, restarted);
    }
}
