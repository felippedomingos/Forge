using System.Text.Json.Serialization;
using Forge.Domain.Entities;
using Forge.Infrastructure;
using Microsoft.EntityFrameworkCore;

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
// that lives in Temporal workflows/activities (Forge.Worker). This DbContext is for
// straightforward CRUD reads/writes and for translating card moves into Temporal
// signals - not yet wired to Temporal in this skeleton, see docs/012-API.md open items.
builder.Services.AddDbContext<ForgeDbContext>(options =>
    options.UseNpgsql(
            builder.Configuration.GetConnectionString("Forge")
            ?? "Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev")
        // Keeps columns/tables snake_case, matching docs/011-Database.md's SQL exactly.
        .UseSnakeCaseNamingConvention());

var app = builder.Build();

app.UseCors();

// docs/012-API.md §2 - the endpoints below are the first slice, not the full v1 surface yet.

app.MapGet("/projects", async (ForgeDbContext db) =>
    await db.Projects.AsNoTracking().ToListAsync());

app.MapPost("/projects", async (ForgeDbContext db, CreateProjectRequest request) =>
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
    return Results.Created($"/projects/{project.Id}", project);
});

app.MapGet("/tasks", async (ForgeDbContext db, Guid? projectId, TaskState? state) =>
{
    var query = db.Tasks.AsNoTracking().AsQueryable();
    if (projectId is not null) query = query.Where(t => t.ProjectId == projectId);
    if (state is not null) query = query.Where(t => t.State == state);
    return await query.ToListAsync();
});

app.MapPost("/tasks", async (ForgeDbContext db, CreateTaskRequest request) =>
{
    // docs/000-Vision.md UC-3: a task can be created with just a title. In the full
    // system this also starts that task's Temporal workflow (docs/ADR-0001) so the
    // Planner agent picks it up while it sits in Inbox - not wired yet in this skeleton.
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

app.Run();

// docs/012-API.md §2 request shapes - kept next to Program.cs at this skeleton stage,
// move to their own files once the API grows past this first slice.
record CreateProjectRequest(string Name, string RepositoryUrl, string RootBranch, Guid GitProviderPluginId);
record CreateTaskRequest(Guid ProjectId, string Title);
