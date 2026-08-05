namespace Forge.Domain.Entities;

// docs/003-Domain.md §1 - the aggregate root. Named TaskItem, not Task, to avoid
// colliding with System.Threading.Tasks.Task in every file that also does async work.
public class TaskItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    // Per-project sequential number - combined with Project.Prefix to form the
    // human-readable tag ("FORGE-42") shown throughout the UI instead of the raw Id.
    public int Number { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public TaskState State { get; set; } = TaskState.Inbox;
    public int? Priority { get; set; }
    // Set by PATCH /tasks/{id}/priority (Product Owner override, docs/000-Vision.md's
    // Product Owner persona) - tells AgentActivities.PrioritizeAsync to leave this
    // task's Priority alone on its next run instead of overwriting it with a fresh
    // FIFO/LLM ranking, per docs/006-Scheduler.md.
    public bool PriorityManuallySet { get; set; }
    public string? BranchName { get; set; }
    // Set by AgentActivities.GitFinalizeAsync once the PR is actually created (at
    // Review->Done, docs/003-Domain.md row 9->row10's GitFinalizeAsync call) - lets
    // the Done->Production polling loop (docs/015-Deployment.md §4) check this
    // specific PR's merge status without re-parsing gh/az CLI output every time.
    public string? PullRequestUrl { get; set; }
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
