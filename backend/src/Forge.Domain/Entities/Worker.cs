namespace Forge.Domain.Entities;

// docs/003-Domain.md §1, docs/007-ExecutionEngine.md §1 - a long-lived process hosting
// agent activities. Distinct from Temporalio's own Worker type in Forge.Worker.
public class Worker
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public WorkerStatus Status { get; set; } = WorkerStatus.Idle;
    public Guid? CurrentTaskId { get; set; }
    public required string HomeDirectory { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public TaskItem? CurrentTask { get; set; }
}
