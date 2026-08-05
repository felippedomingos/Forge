namespace Forge.Domain.Entities;

// Founder-requested: a free-form, per-project label distinct from the auto-assigned
// Project.Prefix+Number task tag ("FORGE-42") - used to categorize/filter tasks on the
// board (docs/013-Frontend.md).
public class Tag
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    // Hex color (e.g. "#3B82F6") rendered as the badge background on TaskCard/TaskDetailSheet.
    public required string Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Project? Project { get; set; }
    public ICollection<TaskItem> Tasks { get; set; } = [];
}
