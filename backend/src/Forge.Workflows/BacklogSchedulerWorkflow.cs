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
        while (true)
        {
            // Checked once per iteration, right where a continue-as-new is cheapest -
            // no in-flight activity, nothing to lose by restarting history here.
            if (Workflow.ContinueAsNewSuggested)
            {
                throw Workflow.CreateContinueAsNewException(
                    (BacklogSchedulerWorkflow wf) => wf.RunAsync(projectId));
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

            if (snapshot.TopBacklogTaskId is { } taskId &&
                snapshot.ExecutingCount < SchedulingActivities.MaxConcurrentExecutingPerProject)
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
}
