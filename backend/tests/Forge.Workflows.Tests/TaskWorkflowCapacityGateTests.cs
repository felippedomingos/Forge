using System.Collections.Concurrent;
using Forge.Domain.Entities;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

namespace Forge.Workflows.Tests;

// Covers the acceptance criteria for the Todo->Executing capacity gate
// (backend/src/Forge.Workflows/TaskWorkflow.cs): with a project already at
// MaxConcurrentExecutingPerProject, a task promoted to Todo must stay there -
// visibly, via the same SetStateAsync/PersistTaskStateAsync path every other
// transition uses - until HasExecutingCapacityAsync reports a free slot.
//
// Real activities (SchedulingActivities/PersistenceActivities/AgentActivities) all
// hit Postgres, so this test swaps in fakes registered under the exact same activity
// names the workflow calls by reference - Temporal's .NET worker dispatches by
// activity name, not by which delegate produced it, so this is enough to drive
// TaskWorkflow's real orchestration logic without a database. Names are the C#
// method names with a trailing "Async" trimmed off (Temporalio's default naming,
// see Temporalio.Activities.ActivityAttribute.Name).
//
// Assertions read off a recording of every PersistTaskState call rather than the
// live State query: the fake Develop activity resolves instantly once Executing is
// reached (no clarification needed to model here), so the workflow races on to
// Blocked right after - polling the live query for "Executing" would be a coin
// flip depending on exactly when the poll lands. The persisted-states log doesn't
// have that problem: once Executing is recorded, it's recorded for good.
public class TaskWorkflowCapacityGateTests
{
    private const string TaskQueue = "task-workflow-capacity-gate-tests";

    [Fact]
    public async Task TaskStaysInTodo_UntilCapacityFrees_ThenMovesToExecuting()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        var taskId = Guid.NewGuid();

        // Starts false, simulating the project already sitting at
        // MaxConcurrentExecutingPerProject (e.g. 2 other tasks Executing) at the
        // moment this task gets promoted to Todo.
        var capacityAvailable = false;
        var persistedStates = new ConcurrentQueue<TaskState>();
        var capacityChecks = 0;

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
                persistedStates.Enqueue((TaskState)args[1]!);
                return Task.CompletedTask;
            });

        var capacityActivity = ActivityDefinition.Create(
            "HasExecutingCapacity",
            typeof(Task<bool>),
            new[] { typeof(Guid) },
            1,
            _ =>
            {
                Interlocked.Increment(ref capacityChecks);
                return Task.FromResult(capacityAvailable);
            });

        // Once Executing is reached, park the workflow at Blocked (a genuine,
        // durable wait) rather than needing to mock the rest of the lifecycle -
        // this test only cares about the Todo->Executing boundary.
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

            await WaitUntilAsync(() => persistedStates.Contains(TaskState.Backlog));

            await handle.SignalAsync(wf => wf.PromoteToTodoAsync());

            // The task must reach Todo ...
            await WaitUntilAsync(() => persistedStates.Contains(TaskState.Todo));

            // ... and, with no capacity available, stay there rather than racing
            // ahead to Executing. Let the gate recheck capacity a few times
            // (TaskWorkflow.TodoCapacityPollInterval is 5s of real/workflow time)
            // and confirm it never let the task through.
            await WaitUntilAsync(() => Volatile.Read(ref capacityChecks) >= 3, attempts: 400);
            Assert.DoesNotContain(TaskState.Executing, persistedStates);
            Assert.Equal(TaskState.Todo, await handle.QueryAsync(wf => wf.State));

            // A slot frees up (another Executing task in the project left that
            // state) - the next capacity recheck should let this task through.
            capacityAvailable = true;

            await WaitUntilAsync(() => persistedStates.Contains(TaskState.Executing), attempts: 400);
        });
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
