namespace Forge.Domain.Entities;

// docs/003-Domain.md §1/INV-4 - a planning artifact/checklist, not a state-machine participant
// (docs/004-Workflow.md §7: incomplete sub-tasks never block publishing).
public class SubTask
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public int OrderIndex { get; set; }
    public bool Done { get; set; }

    public TaskItem? Task { get; set; }
}
