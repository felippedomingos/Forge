using System.Net.WebSockets;
using System.Text.Json.Serialization;
using Forge.Api;
using Forge.Domain.Entities;
using Forge.Infrastructure;
using Forge.Workflows;
using Microsoft.EntityFrameworkCore;
using Temporalio.Client;

var builder = WebApplication.CreateBuilder(args);

// Enums (TaskState, AgentRole, ...) serialize as their names ("Inbox"), not integer
// indices - so the frontend contract matches docs/003-Domain.md directly.
//
// ReferenceHandler.IgnoreCycles: entities have bidirectional nav properties
// (TaskItem.AcceptanceCriteria <-> AcceptanceCriterion.Task, etc.) - EF Core's change
// tracker fixes these up automatically once loaded in the same DbContext, and without
// this, System.Text.Json throws on the cycle the moment a real AcceptanceCriterion
// exists (found live: the Planner's first real run created one and GET /tasks/{id}
// started 500-ing). A known simplification, not the final shape - proper response
// DTOs (docs/012-API.md) would avoid serializing entity graphs directly and are the
// better long-term fix.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});

// Local dev only: lets the Vite dev server (localhost:5173) call this API directly
// when not going through the /api proxy. Tightened before any real deployment
// per docs/014-Security.md (not yet written).
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

// docs/002-Architecture.md §1: the API is a thin control plane - no lifecycle logic here,
// that lives in the Temporal workflow (Forge.Workflows / Forge.Worker). This DbContext is
// for straightforward CRUD reads/writes only.
builder.Services.AddDbContext<ForgeDbContext>(options =>
    options.UseNpgsql(
            builder.Configuration.GetConnectionString("Forge")
            ?? "Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev")
        // Keeps columns/tables snake_case, matching docs/011-Database.md's SQL exactly.
        .UseSnakeCaseNamingConvention());

// docs/002-Architecture.md §1: card moves become Temporal signals against the task's
// own workflow, never a direct agent invocation. One client, shared for the app's
// lifetime (TemporalClient is thread-safe / cheap to reuse per the SDK's own guidance).
builder.Services.AddSingleton(await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
{
    TargetHost = Environment.GetEnvironmentVariable("TEMPORAL_ADDRESS") ?? "localhost:7233",
    Namespace = "default",
}));

// docs/007-ExecutionEngine.md §4 - the WebSocket trace channel, real now (not just
// documented): PostgresNotificationListener bridges Postgres NOTIFY (fired by
// Forge.Workflows activities, a separate process) to these in-memory connections.
builder.Services.AddSingleton<TaskEventBroadcaster>();
builder.Services.AddHostedService<PostgresNotificationListener>();

var app = builder.Build();

app.UseCors();
app.UseWebSockets();

const string TaskQueue = "forge-task-queue";
static string WorkflowIdFor(Guid taskId) => $"task-{taskId}";

// docs/012-API.md §2 - the endpoints below are the first slice, not the full v1 surface yet.

app.MapGet("/projects", async (ForgeDbContext db) =>
    await db.Projects.AsNoTracking().ToListAsync());

app.MapPost("/projects", async (ForgeDbContext db, TemporalClient temporal, CreateProjectRequest request) =>
{
    var project = new Project
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Prefix = request.Prefix.ToUpperInvariant(),
        RepositoryUrl = request.RepositoryUrl,
        RootBranch = request.RootBranch,
        GitProviderPluginId = request.GitProviderPluginId,
        CreatedAt = DateTimeOffset.UtcNow
    };
    db.Projects.Add(project);
    await db.SaveChangesAsync();

    // docs/006-Scheduler.md §1: one BacklogSchedulerWorkflow per project, started once
    // here rather than lazily - it's a long-running loop, not a per-request thing.
    await temporal.StartWorkflowAsync(
        (BacklogSchedulerWorkflow wf) => wf.RunAsync(project.Id),
        new WorkflowOptions($"scheduler-{project.Id}", TaskQueue));

    return Results.Created($"/projects/{project.Id}", project);
});

app.MapGet("/projects/{id:guid}", async (ForgeDbContext db, Guid id) =>
    await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id)
        is { } project ? Results.Ok(project) : Results.NotFound());

// Sidebar project-edit view (founder-requested) - repo location/branch and where the
// canonical checkout lives on this machine. Name/RepositoryUrl/RootBranch/LocalPath
// only; Prefix is immutable once tasks reference it in their tags, and PublishRecipe
// keeps its own dedicated endpoint above.
app.MapPatch("/projects/{id:guid}", async (ForgeDbContext db, Guid id, UpdateProjectRequest request) =>
{
    var project = await db.Projects.FindAsync(id);
    if (project is null) return Results.NotFound();

    if (request.Name is not null) project.Name = request.Name;
    if (request.RepositoryUrl is not null) project.RepositoryUrl = request.RepositoryUrl;
    if (request.RootBranch is not null) project.RootBranch = request.RootBranch;
    if (request.LocalPath is not null) project.LocalPath = request.LocalPath;
    await db.SaveChangesAsync();
    return Results.Ok(project);
});

// docs/005-Agents.md §7 - project-wide shared memory (see MemoryEntryRequest's note on
// why this ignores AgentRole even though the underlying table has one).
app.MapGet("/projects/{id:guid}/memory", async (ForgeDbContext db, Guid id) =>
    await db.AgentMemories.AsNoTracking().Where(m => m.ProjectId == id).OrderBy(m => m.Key).ToListAsync());

app.MapPut("/projects/{id:guid}/memory", async (ForgeDbContext db, Guid id, MemoryEntryRequest request) =>
{
    var existing = await db.AgentMemories.FirstOrDefaultAsync(m => m.ProjectId == id && m.Key == request.Key);
    if (existing is not null)
    {
        existing.Value = request.Value;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }
    else
    {
        db.AgentMemories.Add(new AgentMemory
        {
            Id = Guid.NewGuid(),
            ProjectId = id,
            // Shared memory has no single "owning" role - Planner is just a stable
            // default so the (ProjectId, AgentRole, Key) unique index has something
            // to key on. Every agent reads every entry regardless (AgentActivities).
            AgentRole = AgentRole.Planner,
            Key = request.Key,
            Value = request.Value,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapDelete("/projects/{id:guid}/memory/{key}", async (ForgeDbContext db, Guid id, string key) =>
{
    var existing = await db.AgentMemories.FirstOrDefaultAsync(m => m.ProjectId == id && m.Key == key);
    if (existing is null) return Results.NotFound();
    db.AgentMemories.Remove(existing);
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapGet("/tasks", async (ForgeDbContext db, Guid? projectId, TaskState? state) =>
{
    var query = db.Tasks.AsNoTracking().AsQueryable();
    if (projectId is not null) query = query.Where(t => t.ProjectId == projectId);
    if (state is not null) query = query.Where(t => t.State == state);
    return await query.ToListAsync();
});

app.MapPost("/tasks", async (ForgeDbContext db, TemporalClient temporal, CreateTaskRequest request) =>
{
    var project = await db.Projects.FindAsync(request.ProjectId);
    if (project is null) return Results.NotFound($"Project {request.ProjectId} not found.");

    // docs/000-Vision.md UC-3: a task can be created with just a title. Creating the row
    // and starting its workflow aren't in one transaction - a crash between the two
    // would leave a task stuck with no workflow driving it. Known gap, not solved here;
    // see docs/007-ExecutionEngine.md open questions.
    //
    // Number is assigned from Project.NextTaskNumber and both are saved in the same
    // SaveChangesAsync call (one DB transaction), so two concurrent task creations
    // for the same project can never be handed the same number.
    var task = new TaskItem
    {
        Id = Guid.NewGuid(),
        ProjectId = request.ProjectId,
        Number = project.NextTaskNumber,
        Title = request.Title,
        State = TaskState.Inbox,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    project.NextTaskNumber++;
    db.Tasks.Add(task);
    await db.SaveChangesAsync();

    await temporal.StartWorkflowAsync(
        (TaskWorkflow wf) => wf.RunAsync(task.Id),
        new WorkflowOptions(WorkflowIdFor(task.Id), TaskQueue));

    return Results.Created($"/tasks/{task.Id}", task);
});

app.MapGet("/tasks/{id:guid}", async (ForgeDbContext db, Guid id) =>
    await db.Tasks
        .Include(t => t.SubTasks)
        .Include(t => t.AcceptanceCriteria)
        .Include(t => t.Runs)
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == id)
        is { } task
        ? Results.Ok(task)
        : Results.NotFound());

app.MapGet("/tasks/{id:guid}/runs", async (ForgeDbContext db, Guid id) =>
    await db.Runs.AsNoTracking().Where(r => r.TaskId == id).OrderBy(r => r.StartedAt).ToListAsync());

app.MapGet("/tasks/{id:guid}/events", async (ForgeDbContext db, Guid id) =>
    await db.Events.AsNoTracking().Where(e => e.TaskId == id).OrderBy(e => e.OccurredAt).ToListAsync());

app.MapGet("/tasks/{id:guid}/cost", async (ForgeDbContext db, Guid id) =>
{
    var runs = await db.Runs.AsNoTracking().Where(r => r.TaskId == id).ToListAsync();
    return Results.Ok(new
    {
        TotalCostUsd = runs.Sum(r => r.CostEstimate),
        TotalPromptTokens = runs.Sum(r => r.PromptTokens),
        TotalCompletionTokens = runs.Sum(r => r.CompletionTokens),
        RunCount = runs.Count,
    });
});

// docs/012-API.md §3 / docs/007-ExecutionEngine.md §4 - one WebSocket per task,
// pushed a "refresh" signal whenever PostgresNotificationListener sees a NOTIFY for
// this task ID. Clients re-fetch GET /tasks/{id} and /tasks/{id}/events over REST on
// receiving it - this channel carries no data of its own, deliberately (§ see
// TaskEventBroadcaster).
app.Map("/ws/tasks/{id:guid}", async (HttpContext context, Guid id, TaskEventBroadcaster broadcaster) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    broadcaster.Add(id, socket);

    var buffer = new byte[1024];
    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;
        }
    }
    catch (WebSocketException)
    {
        // client disconnected without a clean close handshake - fine, just clean up below
    }
    finally
    {
        broadcaster.Remove(id, socket);
    }
});

app.MapPost("/tasks/{id:guid}/answers", async (ForgeDbContext db, TemporalClient temporal, Guid id, AnswerQuestionsRequest request) =>
{
    // docs/012-API.md §2: answers are meaningful data, recorded before the signal that
    // just tells the workflow "an answer arrived" - the workflow itself holds no
    // question/answer text, only the docs/003-Domain.md row 3 transition.
    db.Events.Add(new DomainEvent
    {
        Id = Guid.NewGuid(),
        TaskId = id,
        Type = "UserAnsweredQuestions",
        Payload = System.Text.Json.JsonSerializer.Serialize(new { request.Answers }),
        OccurredAt = DateTimeOffset.UtcNow,
        Actor = "user:unknown", // docs/014-Security.md: no auth model yet to attribute a real user
    });
    await db.SaveChangesAsync();
    await PostgresNotify.TaskChangedAsync(db, id);

    var handle = temporal.GetWorkflowHandle<TaskWorkflow>(WorkflowIdFor(id));
    await handle.SignalAsync(wf => wf.AnswerQuestionsAsync());
    return Results.Ok();
});

// docs/015-Deployment.md §5 open item: no endpoint existed to configure a
// PublishRecipe - it was set via direct SQL for testing. This closes that gap.
app.MapPatch("/projects/{id:guid}/publish-recipe", async (ForgeDbContext db, Guid id, PublishRecipeRequest request) =>
{
    var project = await db.Projects.FindAsync(id);
    if (project is null) return Results.NotFound();

    project.PublishRecipe = System.Text.Json.JsonSerializer.Serialize(request);
    await db.SaveChangesAsync();
    return Results.Ok(project);
});

// Manual override now that BacklogSchedulerWorkflow exists (it normally promotes tasks
// itself, priority-ordered, every few seconds) - harmless to call since
// PromoteToTodoAsync guards on state==Backlog. Useful for tests/demos that don't want
// to wait for the scheduler's poll interval.
app.MapPost("/tasks/{id:guid}/promote", async (TemporalClient temporal, Guid id) =>
{
    var handle = temporal.GetWorkflowHandle<TaskWorkflow>(WorkflowIdFor(id));
    await handle.SignalAsync(wf => wf.PromoteToTodoAsync());
    return Results.Ok();
});

app.MapPost("/tasks/{id:guid}/move", async (TemporalClient temporal, Guid id, MoveTaskRequest request) =>
{
    var handle = temporal.GetWorkflowHandle<TaskWorkflow>(WorkflowIdFor(id));
    // docs/012-API.md §2: only the two human-gated transitions this endpoint owns.
    // Blocked->Inbox goes through /answers instead (see docs/012-API.md's own note on
    // why that's a separate endpoint).
    switch (request.TargetState)
    {
        case TaskState.Publishing:
            await handle.SignalAsync(wf => wf.RequestPublishAsync());
            break;
        case TaskState.Done:
            await handle.SignalAsync(wf => wf.ApproveReviewAsync());
            break;
        default:
            return Results.BadRequest($"Unsupported move target: {request.TargetState}");
    }
    return Results.Ok();
});

app.Run();

// docs/012-API.md §2 request shapes - kept next to Program.cs at this skeleton stage,
// move to their own files once the API grows past this first slice.
record CreateProjectRequest(string Name, string Prefix, string RepositoryUrl, string RootBranch, Guid GitProviderPluginId);
record UpdateProjectRequest(string? Name, string? RepositoryUrl, string? RootBranch, string? LocalPath);
record CreateTaskRequest(Guid ProjectId, string Title);
record AnswerQuestionsRequest(List<string> Answers);
record MoveTaskRequest(TaskState TargetState);
// docs/015-Deployment.md §2 - matches AgentActivities.PublishRecipeDto's shape exactly.
record PublishRecipeRequest(string? MigrationCommand, List<string>? RestartTargets, string? HealthCheckUrl);
// docs/005-Agents.md §7 - despite AgentMemory's per-(project,role) schema, the founder
// wants this to read/write as project-wide SHARED memory: the UI and these endpoints
// don't scope by role at all, and prompts (AgentActivities) read every entry for the
// project regardless of which role originally wrote it.
record MemoryEntryRequest(string Key, string Value);
