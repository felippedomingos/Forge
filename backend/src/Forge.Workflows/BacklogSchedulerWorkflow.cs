using Temporalio.Common;
using Temporalio.Workflows;

namespace Forge.Workflows;

// docs/006-Scheduler.md §1 - one long-running workflow per Project, owning
// Backlog->Todo promotion. Replaces the manual POST /tasks/{id}/promote stand-in that
// existed before this workflow did (that endpoint is now an optional manual override,
// harmless to call since PromoteToTodoAsync guards on state per docs/003-Domain.md INV-3).
//
// Workflow ID convention: "scheduler-{projectId}" (started once, on project creation -
// see Forge.Api's POST /projects).
//
// KNOWN SIMPLIFICATION: re-evaluates on a fixed timer rather than being woken by real
// events (a task entering Backlog, another task leaving Executing) - docs/002-Architecture
// §2 prefers event-driven over polling, so this is a deliberate stand-in, not the target
// design.
//
// Found live: this workflow ran for ~19h at the 5s poll interval and hit Temporal's
// history size limit, terminated by the server (51,200 events - docs/006-Scheduler.md
// didn't have a real long-lived project to prove this against until now). Fixed below
// via Workflow.ContinueAsNewSuggested - the server-recommended signal for exactly this,
// rather than guessing at an iteration count that's right for one poll interval and
// wrong for another.
[Workflow]
public class BacklogSchedulerWorkflow
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // Founder-requested safeguard (2026-08-06): how often this project's tasks get
    // swept for a dead/stuck TaskWorkflow (SchedulingActivities.RecoverStuckTasksAsync)
    // and auto-resumed. Much coarser than PollInterval on purpose - this is a real
    // Temporal DescribeAsync + history fetch per non-terminal task, not a cheap DB
    // query, and recovery only ever matters on the order of minutes, not seconds.
    private static readonly TimeSpan RecoveryCheckInterval = TimeSpan.FromMinutes(5);

    private static readonly ActivityOptions RecoveryActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        RetryPolicy = new RetryPolicy { MaximumAttempts = 3 },
    };

    private static readonly ActivityOptions SnapshotActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromSeconds(30),
            MaximumAttempts = 5,
        },
    };

    // Prioritizer calls a real LLM (docs/005-Agents.md §3) - a generous timeout, no
    // aggressive retry (unlike the snapshot query, this costs real money per attempt).
    private static readonly ActivityOptions PrioritizeActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(5),
        RetryPolicy = new RetryPolicy { MaximumAttempts = 2 },
    };

    [WorkflowRun]
    public async Task RunAsync(Guid projectId)
    {
        // Reset on every ContinueAsNew, which just means the next check happens up to
        // RecoveryCheckInterval sooner than strictly scheduled - harmless, and simpler
        // than threading the last-checked time through CreateContinueAsNewException.
        var nextRecoveryCheck = Workflow.UtcNow + RecoveryCheckInterval;

        while (true)
        {
            // Checked once per iteration, right where a continue-as-new is cheapest -
            // no in-flight activity, nothing to lose by restarting history here.
            if (Workflow.ContinueAsNewSuggested)
            {
                throw Workflow.CreateContinueAsNewException(
                    (BacklogSchedulerWorkflow wf) => wf.RunAsync(projectId));
            }

            // Founder-requested safeguard (2026-08-06): found live, three times this
            // session - a TaskWorkflow can die outright (a missing activity
            // registration) or get wedged in an infinite non-determinism retry loop,
            // and the task just sits frozen with nothing on the board showing anything
            // is wrong. This project's own scheduler is already the thing responsible
            // for its tasks making progress, so it's also the natural place to notice
            // and auto-resume one that's stopped.
            //
            // Workflow.Patched, same reasoning as TaskWorkflow's own capacity gate: both
            // of this project's scheduler executions have been running for hours before
            // this loop iteration existed, so their history has no
            // RecoverStuckTasksAsync activity recorded at this point. Patched()==false
            // on replay of that old history skips the whole block, matching what
            // actually happened there; a fresh iteration (real time, not replay) always
            // gets true.
            if (Workflow.Patched("stuck-task-recovery-safeguard") && Workflow.UtcNow >= nextRecoveryCheck)
            {
                await Workflow.ExecuteActivityAsync(
                    () => SchedulingActivities.RecoverStuckTasksAsync(projectId),
                    RecoveryActivityOptions);
                nextRecoveryCheck = Workflow.UtcNow + RecoveryCheckInterval;
            }

            var snapshot = await Workflow.ExecuteActivityAsync(
                () => SchedulingActivities.GetSchedulingSnapshotAsync(projectId),
                SnapshotActivityOptions);

            // docs/005-Agents.md §3 - only invoked when there's actually something
            // unprioritized, not on every poll tick, since each call is a real (paid)
            // LLM invocation. Re-loops immediately afterward to pick up fresh priorities.
            if (snapshot.UnprioritizedBacklogCount > 0)
            {
                await Workflow.ExecuteActivityAsync(
                    () => AgentActivities.PrioritizeAsync(projectId),
                    PrioritizeActivityOptions);
                continue;
            }

            if (snapshot.TopBacklogTaskId is { } taskId && ShouldPromote(snapshot))
            {
                var handle = Workflow.GetExternalWorkflowHandle<TaskWorkflow>($"task-{taskId}");
                await handle.SignalAsync(wf => wf.PromoteToTodoAsync());
                // Loop again immediately - another slot may still be free this round,
                // rather than waiting a full PollInterval before checking again.
                continue;
            }

            await Workflow.DelayAsync(PollInterval);
        }
    }

    // docs/006-Scheduler.md §2 - the per-project backpressure check: promote the top
    // Backlog task only while this Project has a free Executing slot, per its own
    // Project.MaxConcurrentExecuting (was a hardcoded MaxConcurrentExecutingPerProject
    // constant here; now read per-Project via the snapshot activity). Pulled out as its
    // own method so the promotion decision is unit-testable without a Temporal test
    // environment.
    internal static bool ShouldPromote(SchedulingSnapshot snapshot) =>
        snapshot.ExecutingCount < snapshot.MaxConcurrentExecuting;
}
