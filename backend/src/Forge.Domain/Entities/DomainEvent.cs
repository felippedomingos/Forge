namespace Forge.Domain.Entities;

// docs/003-Domain.md §4 - append-only audit/timeline log. Named DomainEvent, not Event,
// to keep it unambiguous next to C#'s `event` keyword/member concept.
// This is NOT the event-sourcing source of truth - see docs/011-Database.md §3:
// Temporal's own workflow history is authoritative; this table is for UI/audit queries.
public class DomainEvent
{
    public Guid Id { get; set; }
    public Guid? TaskId { get; set; }
    public required string Type { get; set; } // see docs/003-Domain.md §4 catalog
    public string Payload { get; set; } = "{}"; // jsonb
    public DateTimeOffset OccurredAt { get; set; }
    public required string Actor { get; set; } // "user:<id>" | "agent:<role>"

    public TaskItem? Task { get; set; }
}
