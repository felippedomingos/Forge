namespace Forge.Domain.Entities;

// docs/003-Domain.md §1 - the aggregate root. Named TaskItem, not Task, to avoid
// colliding with System.Threading.Tasks.Task in every file that also does async work.
public class TaskItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public TaskState State { get; set; } = TaskState.Inbox;
    public int? Priority { get; set; }
    public string? BranchName { get; set; }
    public Guid? WorktreeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Project? Project { get; set; }
    public Worktree? Worktree { get; set; }
    public ICollection<SubTask> SubTasks { get; set; } = [];
    public ICollection<AcceptanceCriterion> AcceptanceCriteria { get; set; } = [];
    public ICollection<Run> Runs { get; set; } = [];
    public ICollection<DomainEvent> Events { get; set; } = [];
}
