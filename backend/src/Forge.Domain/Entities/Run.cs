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

    // Claude Code CLI session identifier for this run (docs/012-API.md GET
    // /tasks/{id}/runs/{runId}/session) - null for runs recorded before this field
    // existed, or if the CLI's JSON result omitted it for some reason.
    public string? SessionId { get; set; }
    // Resolved path to the session's JSONL transcript on disk (ClaudeTranscriptReader),
    // computed once at record time from SessionId + the working directory the CLI ran
    // in. Null under the same conditions as SessionId; may also point at a file that
    // no longer exists if the CLI's own transcript retention has since pruned it.
    public string? TranscriptPath { get; set; }

    public TaskItem? Task { get; set; }
}
