using Temporalio.Common;
using Temporalio.Workflows;

namespace Forge.Workflows;

// Founder-requested (2026-08-07): "algo que evite essas tarefas ficarem presas" -
// found live that the existing per-project safeguard (BacklogSchedulerWorkflow's own
// RecoverStuckTasksAsync sweep, docs/006-Scheduler.md §4a) is entirely dependent on
// that project's scheduler workflow staying alive. 7 SlayZone project schedulers had
// been deliberately terminated earlier in this same session (to stop a real
// over-promotion incident) and never restarted - so their safeguard silently stopped
// running too, and two tasks sat stuck for 2.5+ hours with nothing watching them.
//
// One single global instance (workflow ID "global-watchdog", started once from
// Forge.Api's own startup - see Program.cs), independent of any project's own
// scheduler, on a coarse interval:
//   1. Restarts any project's scheduler that's dead (GlobalWatchdogActivities.
//      EnsureSchedulersRunningAsync) - fixes the root cause (a dead scheduler also
//      stops backlog promotion/prioritization, not just stuck-task recovery).
//   2. ALSO calls RecoverStuckTasksAsync directly for every project, regardless of
//      whether its scheduler is currently healthy - genuine defense in depth: this
//      alone would have caught today's incident even without restarting anything,
//      and covers the (currently theoretical) case of a scheduler whose top-level
//      Temporal status is Running but is internally wedged for some other reason.
[Workflow]
public class GlobalWatchdogWorkflow
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    private static readonly ActivityOptions ActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        RetryPolicy = new RetryPolicy { MaximumAttempts = 3 },
    };

    [WorkflowRun]
    public async Task RunAsync()
    {
        while (true)
        {
            if (Workflow.ContinueAsNewSuggested)
            {
                throw Workflow.CreateContinueAsNewException((GlobalWatchdogWorkflow wf) => wf.RunAsync());
            }

            var health = await Workflow.ExecuteActivityAsync(
                () => GlobalWatchdogActivities.EnsureSchedulersRunningAsync(),
                ActivityOptions);

            foreach (var projectId in health.ProjectIds)
            {
                await Workflow.ExecuteActivityAsync(
                    () => SchedulingActivities.RecoverStuckTasksAsync(projectId),
                    ActivityOptions);
            }

            await Workflow.DelayAsync(CheckInterval);
        }
    }
}
