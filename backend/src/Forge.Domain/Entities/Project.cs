namespace Forge.Domain.Entities;

// docs/003-Domain.md §1 - maps 1:1 to a Git repository.
public class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string RepositoryUrl { get; set; }
    public required string RootBranch { get; set; } // "main" | "develop" | "dev"
    public Guid GitProviderPluginId { get; set; }
    // Where the canonical (non-worktree) clone lives on this Worker's machine - the
    // Planner reads from here directly (docs/005-Agents.md §2); the Developer agent
    // fetches/syncs it before creating a per-task worktree (docs/007-ExecutionEngine.md §2).
    // Nullable for now since it's set manually per-project until project onboarding
    // automates cloning - see docs/011-Database.md open items.
    public string? LocalPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Plugin? GitProviderPlugin { get; set; }
    public ICollection<TaskItem> Tasks { get; set; } = [];
}
