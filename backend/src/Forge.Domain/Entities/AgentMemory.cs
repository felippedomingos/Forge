namespace Forge.Domain.Entities;

// docs/005-Agents.md §7, docs/011-Database.md - per-project, per-agent-role notes
// ("this project uses XAF") so an agent doesn't rediscover them every run.
// Unique on (ProjectId, AgentRole, Key), enforced in Forge.Infrastructure.
public class AgentMemory
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public AgentRole AgentRole { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Project? Project { get; set; }
}
