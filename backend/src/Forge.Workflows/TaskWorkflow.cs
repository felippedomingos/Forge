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
    private bool _productionConfirmed;

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
    public async Task RunAsync(Guid taskId)
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

        await SetStateAsync(taskId, TaskState.AwaitingPublish);

        // docs/004-Workflow.md §5: a failed deploy bounces back to AwaitingPublish, no
        // auto-retry - the human decides whether/when to press Publish again.
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

        await Workflow.WaitConditionAsync(() => _reviewApproved);
        await SetStateAsync(taskId, TaskState.Done);

        await Workflow.ExecuteActivityAsync(
            () => AgentActivities.GitFinalizeAsync(taskId),
            DefaultActivityOptions);

        // docs/003-Domain.md row 10 - external CI/CD confirms, not a human or an agent.
        await Workflow.WaitConditionAsync(() => _productionConfirmed);
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

    // docs/003-Domain.md row 10 (PipelineConfirmedDeployment) - sent by whatever
    // integration watches the external CI/CD pipeline, not implemented yet.
    [WorkflowSignal]
    public Task ConfirmProductionAsync()
    {
        if (_state == TaskState.Done) _productionConfirmed = true;
        return Task.CompletedTask;
    }
}
