namespace Forge.Domain.Entities;

// docs/003-Domain.md §1 - one row per agent invocation, feeding docs/000-Vision.md UC-9.
public class Run
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public AgentRole AgentRole { get; set; }
    public required string ModelProvider { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public RunStatus Status { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal CostEstimate { get; set; }

    public TaskItem? Task { get; set; }
}
