using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Worker;
using Temporalio.Workflows;

// docs/007-ExecutionEngine.md §1: a Worker process polls a Temporal task queue and hosts
// the agent activity implementations. This is a connectivity skeleton only - it proves
// the SDK wiring against docker/local's Temporal server. The real workflow implementing
// docs/004-Workflow.md's full state machine, and the 5 agent activities from
// docs/005-Agents.md, are deliberately NOT implemented here yet - that's substantial
// business logic for its own dedicated pass, not something to rush into a skeleton.

var targetHost = Environment.GetEnvironmentVariable("TEMPORAL_ADDRESS") ?? "localhost:7233";

var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
{
    TargetHost = targetHost,
    Namespace = "default"
});

Console.WriteLine($"Connected to Temporal at {targetHost}");

using var worker = new TemporalWorker(client, new TemporalWorkerOptions("forge-task-queue")
{
    Workflows = { WorkflowDefinition.Create(typeof(PlaceholderWorkflow)) },
    Activities = { ActivityDefinition.Create(PlaceholderActivities.NoOpAsync) }
});

Console.WriteLine("Worker polling task queue 'forge-task-queue' - press Ctrl+C to stop.");
await worker.ExecuteAsync(CancellationToken.None);

// TODO(next pass): replace with the real per-task workflow from docs/004-Workflow.md
// and the Planner/Prioritizer/Developer/Deploy/Git activities from docs/005-Agents.md.
[Workflow]
public class PlaceholderWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        return await Workflow.ExecuteActivityAsync(
            () => PlaceholderActivities.NoOpAsync(),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    }
}

public static class PlaceholderActivities
{
    [Activity]
    public static Task<string> NoOpAsync() =>
        Task.FromResult("placeholder - real agent activities land in a later pass");
}
