using Temporalio.Activities;

namespace Forge.Workflows;

public record PlannerResult(bool NeedsClarification, string? Description, List<string> AcceptanceCriteria, List<string> Questions);
public record DeveloperResult(bool NeedsClarification, List<string> Questions);
public record DeployResult(bool Success, string? Error);

// docs/005-Agents.md - one static activity per agent role. Every method here is
// DELIBERATELY a stub: the real implementation calls the Claude Agent SDK through
// the Model Router (docs/008-ModelRouter.md), with MCP tool access (docs/009-MCP.md)
// scoped per docs/005-Agents.md §8. Wiring that up needs real credentials and running
// MCP server processes - out of scope for this pass, which focuses on getting the
// *workflow shape* (TaskWorkflow.cs) correct and exercised end-to-end against real
// Temporal, with these as placeholders a later pass replaces one at a time.
public static class AgentActivities
{
    // docs/005-Agents.md §2
    [Activity]
    public static Task<PlannerResult> PlanAsync(Guid taskId) =>
        Task.FromResult(new PlannerResult(
            NeedsClarification: false,
            Description: "TODO: real Planner agent not implemented yet - docs/005-Agents.md §2",
            AcceptanceCriteria: [],
            Questions: []));

    // docs/005-Agents.md §4. Real implementation: sync root branch, create/reuse
    // worktree (docs/007-ExecutionEngine.md §2), run the agent loop, build/test.
    [Activity]
    public static Task<DeveloperResult> DevelopAsync(Guid taskId) =>
        Task.FromResult(new DeveloperResult(NeedsClarification: false, Questions: []));

    // docs/005-Agents.md §5. Real implementation needs the PublishRecipe concept
    // flagged as a gap there and in docs/015-Deployment.md - not invented here.
    [Activity]
    public static Task<DeployResult> DeployAsync(Guid taskId) =>
        Task.FromResult(new DeployResult(Success: true, Error: null));

    // docs/005-Agents.md §6 - push + PR creation via the GitHub plugin (ADR-0002).
    [Activity]
    public static Task GitFinalizeAsync(Guid taskId) => Task.CompletedTask;

    // docs/005-Agents.md §3. Per-project ordering - not implemented as real logic yet;
    // returning 0 rather than throwing keeps the workflow shape testable.
    [Activity]
    public static Task<int> PrioritizeAsync(Guid projectId) => Task.FromResult(0);
}
