namespace Forge.Domain.Entities;

// docs/003-Domain.md §1 - named LlmModel, not Model, to avoid the generic "Model" name
// clashing with MVC/view-model conventions elsewhere in an ASP.NET Core codebase.
// Claude-only row populated at v1 per docs/adr/ADR-0003-claude-only-model-router-v1.md.
public class LlmModel
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string CapabilityTier { get; set; }
    public decimal CostPerToken { get; set; }
    public bool Enabled { get; set; } = true;
}
