using Forge.Domain.Entities;
using Temporalio.Common;
using Temporalio.Workflows;

namespace Forge.Workflows;

// One Temporal workflow execution per Task, implementing docs/004-Workflow.md's full
// state machine (extending docs/003-Domain.md §3's happy path with the failure/rollback
// edges). Workflow ID convention: "task-{taskId}" (see Forge.Api's Program.cs).
[Workflow]
public class TaskWorkflow
{
    private TaskState _state = TaskState.Inbox;
    private bool _answered;
    private bool _promoted;
    private bool _publishRequested;
    private bool _reviewApproved;
    private bool _changesRequested;
    private bool _productionConfirmed;

    // docs/015-Deployment.md §4 - how often to poll the PR's merge status once a task
    // reaches Done. A field (not a literal in RunAsync) purely so a test/override can
    // shrink it - the value itself was never meant to be tunable per-task.
    private static readonly TimeSpan ProductionPollInterval = TimeSpan.FromSeconds(60);

    // docs/006-Scheduler.md §1 - how often to recheck project capacity while parked in
    // Todo waiting for an Executing slot. Mirrors ProductionPollInterval's pattern
    // (field, not a literal, so a test can shrink it).
    private static readonly TimeSpan TodoCapacityPollInterval = TimeSpan.FromSeconds(5);

    [WorkflowQuery]
    public TaskState State => _state;

    // docs/006-Scheduler.md §3 - Deploy gets fewer retry attempts than the other roles,
    // deliberately, since a deploy activity has real side effects worth not retrying blindly.
    private static readonly ActivityOptions DefaultActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(30),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(30),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromMinutes(10),
            MaximumAttempts = 5,
        },
    };

    private static readonly ActivityOptions DeployActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(10),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(30),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromMinutes(5),
            MaximumAttempts = 2,
        },
    };

    private static readonly ActivityOptions PersistActivityOptions = new()
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

    private static readonly ActivityOptions CapacityActivityOptions = new()
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

    // docs/011-Database.md §3: the `tasks` table holds current state directly - this is
    // the one place that keeps it in sync with what the workflow (the actual source of
    // truth for lifecycle) is doing, via PersistenceActivities rather than a raw field set.
    private async Task SetStateAsync(Guid taskId, TaskState newState)
    {
        _state = newState;
        await Workflow.ExecuteActivityAsync(
            () => PersistenceActivities.PersistTaskStateAsync(taskId, newState),
            PersistActivityOptions);
    }

    [WorkflowRun]
    public async Task RunAsync(Guid taskId, TaskState? resumeFrom = null)
    {
        // Found necessary live (2026-08-05): a Deploy activity that timed out twice in
        // a row (StartToClose exhausted, no more retries) kills the whole workflow
        // outright - Temporal's WORKFLOW_EXECUTION_FAILED is terminal, no auto-resume.
        // Before this, POST /tasks/{id}/resume always restarted at Inbox, silently
        // re-running Planner+Developer (wasted tokens, and a real risk of the Developer
        // re-committing on top of a worktree that already has the right commit) even
        // though only Deploy had actually failed. `resumeFrom` - the task's own
        // last-persisted State, passed by the /resume endpoint - lets a restart skip
        // straight past whichever stages already succeeded.
        var pastDevelop = resumeFrom is TaskState.AwaitingPublish or TaskState.Publishing or TaskState.Review;
        var pastReviewApproval = resumeFrom is TaskState.Done;

        if (!pastDevelop && !pastReviewApproval)
        {
            // docs/004-Workflow.md §3: whichever agent raises a clarification need (Planner
            // during planning, Developer during execution), Blocked ALWAYS re-enters via
            // Inbox - one edge, one meaning. This loop is that decision made literal: it
            // restarts from Inbox every time, whether this is the first pass or a resume
            // after answers arrive.
            while (true)
            {
                await SetStateAsync(taskId, TaskState.Inbox);
                var plan = await Workflow.ExecuteActivityAsync(
                    () => AgentActivities.PlanAsync(taskId),
                    DefaultActivityOptions);

                if (plan.NeedsClarification)
                {
                    await SetStateAsync(taskId, TaskState.Blocked);
                    await Workflow.WaitConditionAsync(() => _answered);
                    _answered = false;
                    continue;
                }

                await SetStateAsync(taskId, TaskState.Backlog);

                // docs/003-Domain.md §3 reconciliation: Backlog->Todo is deterministic
                // scheduler logic (docs/006-Scheduler.md), not an agent - this signal is
                // sent by that scheduler. A real BacklogSchedulerWorkflow doesn't exist yet
                // (see docs/006-Scheduler.md §1) - Forge.Api exposes a stand-in
                // POST /tasks/{id}/promote endpoint until it does.
                await Workflow.WaitConditionAsync(() => _promoted);
                _promoted = false;
                await SetStateAsync(taskId, TaskState.Todo);

                // BacklogSchedulerWorkflow's MaxConcurrentExecutingPerProject check ran
                // against a Postgres snapshot taken before it signaled PromoteToTodoAsync -
                // by the time we get here, another task from the same project may have
                // already taken the slot that promotion was counting on. Recheck capacity
                // for real, right at the Todo->Executing boundary, and stay parked in Todo
                // (visibly, via SetStateAsync/PersistTaskStateAsync above) until a slot is
                // actually free instead of trusting that stale snapshot.
                // Workflow.Patched, found necessary live (2026-08-06): this gate landed
                // while real tasks already had open workflow executions whose recorded
                // history has no HasExecutingCapacity activity at this point. Replaying
                // an old history against the new code unconditionally would schedule an
                // activity the history doesn't expect right here -
                // TMPRL1100 non-determinism, confirmed live on a real task. Patched()
                // stamps a marker for brand-new executions (gate runs) but returns false
                // on replay of any history recorded before this line existed (gate
                // skipped, matching what that history actually did).
                if (Workflow.Patched("todo-executing-capacity-gate"))
                {
                    while (!await Workflow.ExecuteActivityAsync(
                        () => SchedulingActivities.HasExecutingCapacityAsync(taskId),
                        CapacityActivityOptions))
                    {
                        await Workflow.DelayAsync(TodoCapacityPollInterval);
                    }
                }

                await SetStateAsync(taskId, TaskState.Executing);
                var dev = await Workflow.ExecuteActivityAsync(
                    () => AgentActivities.DevelopAsync(taskId),
                    DefaultActivityOptions);

                if (dev.NeedsClarification)
                {
                    await SetStateAsync(taskId, TaskState.Blocked);
                    await Workflow.WaitConditionAsync(() => _answered);
                    _answered = false;
                    continue; // same Blocked->Inbox rule applies to execution-time questions
                }

                break;
            }
        }

        if (!pastReviewApproval)
        {
            // docs/004-Workflow.md row 14 (founder-requested, new): Review can now send a
            // task back for another Developer pass instead of only approving it forward.
            // This outer loop is what makes that possible without re-running the Planner -
            // AwaitingPublish/Publishing/Deploy/Review repeat, but the plan itself
            // (Task.Description/AcceptanceCriteria) is never touched again.
            while (true)
            {
                await SetStateAsync(taskId, TaskState.AwaitingPublish);

                // docs/004-Workflow.md §5: a failed deploy bounces back to AwaitingPublish,
                // no auto-retry - the human decides whether/when to press Publish again.
                DeployResult deploy;
                do
                {
                    await Workflow.WaitConditionAsync(() => _publishRequested);
                    _publishRequested = false;
                    await SetStateAsync(taskId, TaskState.Publishing);

                    deploy = await Workflow.ExecuteActivityAsync(
                        () => AgentActivities.DeployAsync(taskId),
                        DeployActivityOptions);

                    if (!deploy.Success)
                    {
                        await SetStateAsync(taskId, TaskState.AwaitingPublish);
                    }
                } while (!deploy.Success);

                await SetStateAsync(taskId, TaskState.Review);
                await Workflow.WaitConditionAsync(() => _reviewApproved || _changesRequested);

                if (!_changesRequested)
                {
                    break; // _reviewApproved - the only way out to Done
                }

                _changesRequested = false;
                await SetStateAsync(taskId, TaskState.Todo);

                // A rework pass can itself hit a genuine question (same as the first
                // Developer attempt) - resolve it here without unwinding all the way back
                // to the outer Planner loop, since re-planning was never the problem.
                while (true)
                {
                    await SetStateAsync(taskId, TaskState.Executing);
                    var redev = await Workflow.ExecuteActivityAsync(
                        () => AgentActivities.DevelopAsync(taskId),
                        DefaultActivityOptions);

                    if (redev.NeedsClarification)
                    {
                        await SetStateAsync(taskId, TaskState.Blocked);
                        await Workflow.WaitConditionAsync(() => _answered);
                        _answered = false;
                        continue;
                    }

                    break;
                }
                // back to AwaitingPublish for another Publish/Deploy/Review cycle
            }

            await SetStateAsync(taskId, TaskState.Done);
        }

        // GitFinalizeAsync creates a real PR - not safely re-runnable blind. A resume
        // landing straight in Done (or a fresh run that just got here) both go through
        // this same idempotency check, so a resume can never open a second PR for the
        // same task.
        // Workflow.Patched, same reasoning as the capacity gate above: an open workflow
        // whose history already has a bare GitFinalizeAsync call recorded (from before
        // this HasPullRequestAsync check existed) would mismatch on replay if this ran
        // unconditionally. Patched()==false on that old history skips straight to the
        // unconditional GitFinalizeAsync call, matching what actually happened there.
        if (Workflow.Patched("skip-git-finalize-if-pr-exists"))
        {
            var alreadyFinalized = await Workflow.ExecuteActivityAsync(
                () => AgentActivities.HasPullRequestAsync(taskId),
                PersistActivityOptions);
            if (!alreadyFinalized)
            {
                await Workflow.ExecuteActivityAsync(
                    () => AgentActivities.GitFinalizeAsync(taskId),
                    DefaultActivityOptions);
            }
        }
        else
        {
            await Workflow.ExecuteActivityAsync(
                () => AgentActivities.GitFinalizeAsync(taskId),
                DefaultActivityOptions);
        }

        // docs/003-Domain.md row 10 / docs/015-Deployment.md §4 (resolved): poll the
        // PR's own merge status directly instead of waiting on an undesigned webhook.
        // The manual ConfirmProductionAsync signal still works too (e.g. a project
        // without a PullRequestUrl captured, or a provider this polling doesn't cover).
        while (!_productionConfirmed)
        {
            var merged = await Workflow.ExecuteActivityAsync(
                () => AgentActivities.CheckPullRequestMergedAsync(taskId),
                DefaultActivityOptions);
            if (merged)
            {
                _productionConfirmed = true;
                break;
            }
            await Workflow.WaitConditionAsync(() => _productionConfirmed, ProductionPollInterval);
        }
        await SetStateAsync(taskId, TaskState.Production);
    }

    // Every signal below guards on the current state before honoring itself - per
    // docs/003-Domain.md INV-3, an illegal transition should be structurally
    // unrepresentable, not merely convention. Without this guard, signals are
    // order-independent booleans consumed by WaitConditionAsync, so a signal sent
    // before its state is reached would otherwise be silently "banked" and fire the
    // instant the workflow gets there - skipping the human decision it's meant to gate.

    // docs/003-Domain.md row 3 (UserAnsweredQuestions). Answers themselves aren't
    // persisted by the workflow - Forge.Api writes them to the task's Event log
    // (docs/012-API.md POST /tasks/{id}/answers) before sending this signal.
    [WorkflowSignal]
    public Task AnswerQuestionsAsync()
    {
        if (_state == TaskState.Blocked) _answered = true;
        return Task.CompletedTask;
    }

    [WorkflowSignal]
    public Task PromoteToTodoAsync()
    {
        if (_state == TaskState.Backlog) _promoted = true;
        return Task.CompletedTask;
    }

    // docs/003-Domain.md row 7 (UserRequestedPublish).
    [WorkflowSignal]
    public Task RequestPublishAsync()
    {
        if (_state == TaskState.AwaitingPublish) _publishRequested = true;
        return Task.CompletedTask;
    }

    // docs/003-Domain.md row 9 (UserApprovedReview).
    [WorkflowSignal]
    public Task ApproveReviewAsync()
    {
        if (_state == TaskState.Review) _reviewApproved = true;
        return Task.CompletedTask;
    }

    // docs/003-Domain.md row 10 (PipelineConfirmedDeployment) - kept as a manual
    // escape hatch even though RunAsync's own polling loop (docs/015-Deployment.md §4)
    // now detects this automatically for the common case.
    [WorkflowSignal]
    public Task ConfirmProductionAsync()
    {
        if (_state == TaskState.Done) _productionConfirmed = true;
        return Task.CompletedTask;
    }

    // docs/004-Workflow.md row 14 (new, founder-requested): the reviewer's comment
    // itself isn't carried by the signal - Forge.Api records it as a
    // "ReviewRequestedChanges" event before signaling, same pattern as
    // AnswerQuestionsAsync above.
    [WorkflowSignal]
    public Task RequestChangesAsync()
    {
        if (_state == TaskState.Review) _changesRequested = true;
        return Task.CompletedTask;
    }
}
