using Forge.Domain.Entities;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

namespace Forge.Workflows.Tests;

// Covers TaskWorkflow.RequestReplanAsync's state guard (backend/src/Forge.Workflows/TaskWorkflow.cs):
// the same signal backs both the TaskDetailSheet's "Rewrite (back to Inbox)" button and the
// Backlog->Inbox drag-and-drop gesture (frontend/src/App.tsx handleDragEnd). A Backlog task must
// go back to Inbox for a fresh PlanAsync pass; sent from any other state, the signal must be a
// no-op, per docs/003-Domain.md INV-3 (illegal transitions structurally unrepresentable, not
// merely convention).
//
// Same fake-activity approach as TaskWorkflowCapacityGateTests: real activities hit Postgres, so
// fakes are registered under the exact activity names the workflow calls by reference, driving
// TaskWorkflow's real orchestration logic without a database.
public class TaskWorkflowReplanGuardTests
{
    private const string TaskQueue = "task-workflow-replan-guard-tests";

    [Fact]
    public async Task RequestReplan_FromBacklog_SendsTaskBackToInboxForAnotherPlanPass()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        var taskId = Guid.NewGuid();

        var persistedStates = new List<TaskState>();
        var statesLock = new object();
        var planCalls = 0;

        var planActivity = ActivityDefinition.Create(
            "Plan",
            typeof(Task<PlannerResult>),
            new[] { typeof(Guid) },
            1,
            _ =>
            {
                Interlocked.Increment(ref planCalls);
                return Task.FromResult(new PlannerResult(false, "desc", new List<string> { "ac" }, new List<string>()));
            });

        var persistActivity = ActivityDefinition.Create(
            "PersistTaskState",
            typeof(Task),
            new[] { typeof(Guid), typeof(TaskState) },
            2,
            args =>
            {
                lock (statesLock) persistedStates.Add((TaskState)args[1]!);
                return Task.CompletedTask;
            });

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(TaskQueue)
                .AddWorkflow<TaskWorkflow>()
                .AddActivity(planActivity)
                .AddActivity(persistActivity));

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (TaskWorkflow wf) => wf.RunAsync(taskId),
                new WorkflowOptions($"task-{taskId}", TaskQueue));

            await WaitUntilAsync(() => CountOf(persistedStates, statesLock, TaskState.Backlog) >= 1);
            Assert.Equal(TaskState.Backlog, await handle.QueryAsync(wf => wf.State));

            await handle.SignalAsync(wf => wf.RequestReplanAsync());

            // The replan sends the task back through Inbox for a second PlanAsync pass,
            // landing in Backlog again - same re-entry shape Blocked already uses
            // (docs/004-Workflow.md §3), not a second/different transition.
            await WaitUntilAsync(() => CountOf(persistedStates, statesLock, TaskState.Backlog) >= 2);

            List<TaskState> snapshot;
            lock (statesLock) snapshot = new List<TaskState>(persistedStates);

            Assert.Equal(
                new[] { TaskState.Inbox, TaskState.Backlog, TaskState.Inbox, TaskState.Backlog },
                snapshot);
            Assert.True(Volatile.Read(ref planCalls) >= 2);
            Assert.Equal(TaskState.Backlog, await handle.QueryAsync(wf => wf.State));
        });
    }

    [Fact]
    public async Task RequestReplan_FromNonBacklogState_IsIgnored()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        var taskId = Guid.NewGuid();

        var capacityAvailable = false;
        var persistedStates = new List<TaskState>();
        var statesLock = new object();

        var planActivity = ActivityDefinition.Create(
            "Plan",
            typeof(Task<PlannerResult>),
            new[] { typeof(Guid) },
            1,
            _ => Task.FromResult(new PlannerResult(false, "desc", new List<string> { "ac" }, new List<string>())));

        var persistActivity = ActivityDefinition.Create(
            "PersistTaskState",
            typeof(Task),
            new[] { typeof(Guid), typeof(TaskState) },
            2,
            args =>
            {
                lock (statesLock) persistedStates.Add((TaskState)args[1]!);
                return Task.CompletedTask;
            });

        var capacityActivity = ActivityDefinition.Create(
            "HasExecutingCapacity",
            typeof(Task<bool>),
            new[] { typeof(Guid) },
            1,
            _ => Task.FromResult(capacityAvailable));

        var developActivity = ActivityDefinition.Create(
            "Develop",
            typeof(Task<DeveloperResult>),
            new[] { typeof(Guid) },
            1,
            _ => Task.FromResult(new DeveloperResult(true, new List<string> { "parked for test" })));

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(TaskQueue)
                .AddWorkflow<TaskWorkflow>()
                .AddActivity(planActivity)
                .AddActivity(persistActivity)
                .AddActivity(capacityActivity)
                .AddActivity(developActivity));

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (TaskWorkflow wf) => wf.RunAsync(taskId),
                new WorkflowOptions($"task-{taskId}", TaskQueue));

            await WaitUntilAsync(() => CountOf(persistedStates, statesLock, TaskState.Backlog) >= 1);
            await handle.SignalAsync(wf => wf.PromoteToTodoAsync());
            await WaitUntilAsync(() => CountOf(persistedStates, statesLock, TaskState.Todo) >= 1);
            Assert.Equal(TaskState.Todo, await handle.QueryAsync(wf => wf.State));

            // Fired from Todo, not Backlog - RequestReplanAsync's guard must ignore this and
            // leave the task exactly where the capacity gate parked it.
            await handle.SignalAsync(wf => wf.RequestReplanAsync());

            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.Equal(TaskState.Todo, await handle.QueryAsync(wf => wf.State));
            // Only the one initial Inbox->Backlog pass should have happened - the ignored
            // signal must not send the task back through Inbox a second time.
            Assert.Equal(1, CountOf(persistedStates, statesLock, TaskState.Inbox));

            // Confirm the ignored signal didn't disrupt the normal flow either: once
            // capacity frees up, the task still proceeds straight to Executing.
            capacityAvailable = true;
            await WaitUntilAsync(() => CountOf(persistedStates, statesLock, TaskState.Executing) >= 1, attempts: 400);

            // Drive the task into Blocked (dev clarification) and back through Inbox to a
            // second Backlog arrival - the same re-entry loop RequestReplanAsync uses. If the
            // guard didn't actually gate on state (e.g. it unconditionally set the field), the
            // earlier signal sent from Todo would still be sitting in _replanRequested and
            // would fire the instant this second Backlog is reached, sending the task straight
            // back to Inbox with no further signal. Asserting the task stays put here is what
            // actually catches that regression - unlike the immediate Todo check above, which
            // passes even without the guard, since WaitConditionAsync had already returned by
            // the time the ignored signal was sent.
            await WaitUntilAsync(() => CountOf(persistedStates, statesLock, TaskState.Blocked) >= 1, attempts: 400);
            await handle.SignalAsync(wf => wf.AnswerQuestionsAsync());
            await WaitUntilAsync(() => CountOf(persistedStates, statesLock, TaskState.Backlog) >= 2, attempts: 400);

            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.Equal(TaskState.Backlog, await handle.QueryAsync(wf => wf.State));
            Assert.Equal(2, CountOf(persistedStates, statesLock, TaskState.Inbox));
        });
    }

    private static int CountOf(List<TaskState> states, object statesLock, TaskState target)
    {
        lock (statesLock) return states.Count(s => s == target);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 200)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        Assert.True(condition());
    }
}
