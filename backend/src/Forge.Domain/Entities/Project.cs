namespace Forge.Domain.Entities;

// docs/003-Domain.md §1 - maps 1:1 to a Git repository.
public class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    // Short, human-readable code (e.g. "FORGE", "SAND") used to build each Task's
    // display tag ("FORGE-42") - founder-requested, since raw GUIDs aren't something
    // anyone can reference in conversation. Uppercase alnum, uniqueness enforced at
    // the database level (docs/011-Database.md).
    public required string Prefix { get; set; }
    // The next number to assign to a Task created under this project - incremented
    // atomically in the same SaveChanges call that creates the Task, so two tasks
    // created concurrently never collide (docs/012-API.md POST /tasks).
    public int NextTaskNumber { get; set; } = 1;
    public required string RepositoryUrl { get; set; }
    public required string RootBranch { get; set; } // "main" | "develop" | "dev"
    public Guid GitProviderPluginId { get; set; }
    // Where the canonical (non-worktree) clone lives on this Worker's machine - the
    // Planner reads from here directly (docs/005-Agents.md §2); the Developer agent
    // fetches/syncs it before creating a per-task worktree (docs/007-ExecutionEngine.md §2).
    // Nullable for now since it's set manually per-project until project onboarding
    // automates cloning - see docs/011-Database.md open items.
    public string? LocalPath { get; set; }
    // docs/015-Deployment.md §2 - the PublishRecipe proposal, stored as raw JSON (same
    // pattern as Plugin.Configuration) rather than an EF owned type, so the schema can
    // grow without a migration every time. Nullable: most projects won't have one yet.
    // Only "migrationCommand" is actually executed by DeployAsync today -
    // "restartTargets"/"healthCheckUrl" are accepted by the shape but not exercised,
    // since no test project has a real running service to restart/poll.
    public string? PublishRecipe { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Plugin? GitProviderPlugin { get; set; }
    public ICollection<TaskItem> Tasks { get; set; } = [];
}
