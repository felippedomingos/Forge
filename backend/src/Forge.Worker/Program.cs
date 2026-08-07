using Forge.Workflows;
using Temporalio.Client;
using Temporalio.Worker;
using Temporalio.Workflows;
using Temporalio.Activities;

// docs/007-ExecutionEngine.md §1: a Worker process polls a Temporal task queue and hosts
// the agent activity implementations. Hosts the real TaskWorkflow (docs/004-Workflow.md)
// and the 5 agent activity stubs (docs/005-Agents.md) from Forge.Workflows.

var targetHost = Environment.GetEnvironmentVariable("TEMPORAL_ADDRESS") ?? "localhost:7233";

var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
{
    TargetHost = targetHost,
    Namespace = "default"
});

Console.WriteLine($"Connected to Temporal at {targetHost}");

using var worker = new TemporalWorker(client, new TemporalWorkerOptions("forge-task-queue")
{
    Workflows =
    {
        WorkflowDefinition.Create(typeof(TaskWorkflow)),
        WorkflowDefinition.Create(typeof(BacklogSchedulerWorkflow)),
    },
    Activities =
    {
        ActivityDefinition.Create(AgentActivities.PlanAsync),
        ActivityDefinition.Create(AgentActivities.DevelopAsync),
        ActivityDefinition.Create(AgentActivities.DeployAsync),
        ActivityDefinition.Create(AgentActivities.GitFinalizeAsync),
        ActivityDefinition.Create(AgentActivities.HasPullRequestAsync),
        ActivityDefinition.Create(AgentActivities.CheckPullRequestMergedAsync),
        ActivityDefinition.Create(AgentActivities.IsAlreadyIntegratedAsync),
        ActivityDefinition.Create(AgentActivities.PrioritizeAsync),
        ActivityDefinition.Create(PersistenceActivities.PersistTaskStateAsync),
        ActivityDefinition.Create(SchedulingActivities.GetSchedulingSnapshotAsync),
        ActivityDefinition.Create(SchedulingActivities.HasExecutingCapacityAsync),
        ActivityDefinition.Create(SchedulingActivities.RecoverStuckTasksAsync),
    }
});

Console.WriteLine("Worker polling task queue 'forge-task-queue' - press Ctrl+C to stop.");
await worker.ExecuteAsync(CancellationToken.None);
