namespace Forge.Domain.Entities;

// docs/003-Domain.md INV-2: at most one ACTIVE (DeletedAt == null) worktree per task -
// enforced by a partial unique index in Forge.Infrastructure, not just this class shape.
public class Worktree
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid ProjectId { get; set; }
    public required string Path { get; set; }
    public required string BranchName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public TaskItem? Task { get; set; }
    public Project? Project { get; set; }
}
