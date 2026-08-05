namespace Forge.Domain.Entities;

public class AcceptanceCriterion
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public required string Description { get; set; }
    public bool Satisfied { get; set; }

    public TaskItem? Task { get; set; }
}
