using System.Text.Json.Serialization;
using Forge.Domain.Entities;
using Forge.Infrastructure;
using Forge.Workflows;
using Microsoft.EntityFrameworkCore;
using Temporalio.Client;

var builder = WebApplication.CreateBuilder(args);

// Enums (TaskState, AgentRole, ...) serialize as their names ("Inbox"), not integer
// indices - so the frontend contract matches docs/003-Domain.md directly.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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

var app = builder.Build();

app.UseCors();

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

app.MapGet("/tasks", async (ForgeDbContext db, Guid? projectId, TaskState? state) =>
{
    var query = db.Tasks.AsNoTracking().AsQueryable();
    if (projectId is not null) query = query.Where(t => t.ProjectId == projectId);
    if (state is not null) query = query.Where(t => t.State == state);
    return await query.ToListAsync();
});

app.MapPost("/tasks", async (ForgeDbContext db, TemporalClient temporal, CreateTaskRequest request) =>
{
    // docs/000-Vision.md UC-3: a task can be created with just a title. Creating the row
    // and starting its workflow aren't in one transaction - a crash between the two
    // would leave a task stuck with no workflow driving it. Known gap, not solved here;
    // see docs/007-ExecutionEngine.md open questions.
    var task = new TaskItem
    {
        Id = Guid.NewGuid(),
        ProjectId = request.ProjectId,
        Title = request.Title,
        State = TaskState.Inbox,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
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
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == id)
        is { } task
        ? Results.Ok(task)
        : Results.NotFound());

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

    var handle = temporal.GetWorkflowHandle<TaskWorkflow>(WorkflowIdFor(id));
    await handle.SignalAsync(wf => wf.AnswerQuestionsAsync());
    return Results.Ok();
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
record CreateProjectRequest(string Name, string RepositoryUrl, string RootBranch, Guid GitProviderPluginId);
record CreateTaskRequest(Guid ProjectId, string Title);
record AnswerQuestionsRequest(List<string> Answers);
record MoveTaskRequest(TaskState TargetState);
