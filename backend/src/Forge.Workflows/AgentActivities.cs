using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Domain.Entities;
using Forge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;

namespace Forge.Workflows;

public record PlannerResult(bool NeedsClarification, string? Description, List<string> AcceptanceCriteria, List<string> Questions);
public record DeveloperResult(bool NeedsClarification, List<string> Questions);
public record DeployResult(bool Success, string? Error);

// docs/005-Agents.md - one static activity per agent role. PlanAsync is the first one
// that's REAL (docs/adr/ADR-0005-claude-code-cli-as-invocation-mechanism.md - calls the
// Claude Code CLI, not a stub). Developer/Deploy/Git remain stubs: Developer needs real
// worktree mechanics (docs/007-ExecutionEngine.md §2) wired next; Deploy needs the
// PublishRecipe concept (docs/015-Deployment.md §2) implemented first; Git needs the
// plugin push/PR interface (docs/010-Plugins.md §2) implemented. Each is a TODO pointing
// at the relevant doc section, not faked.
public static class AgentActivities
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("FORGE_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev";

    private static ForgeDbContext OpenDb() => new(new DbContextOptionsBuilder<ForgeDbContext>()
        .UseNpgsql(ConnectionString)
        .UseSnakeCaseNamingConvention()
        .Options);

    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private record PlannerLlmResponse(
        [property: JsonPropertyName("needsClarification")] bool NeedsClarification,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("acceptanceCriteria")] List<string>? AcceptanceCriteria,
        [property: JsonPropertyName("questions")] List<string>? Questions);

    private static async Task RecordRunAsync(ForgeDbContext db, Guid taskId, AgentRole role, decimal costUsd, int promptTokens, int completionTokens, RunStatus status)
    {
        db.Runs.Add(new Run
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            AgentRole = role,
            ModelProvider = "claude-cli", // docs/adr/ADR-0005 - not "anthropic-api"
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            Status = status,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CostEstimate = costUsd,
        });
        await db.SaveChangesAsync();
    }

    // docs/003-Domain.md §4's event catalog, finally written to for real - previously
    // only UserAnsweredQuestions (from Forge.Api) ever hit the `events` table. This is
    // what lets the task detail view show "what the agent is doing right now"
    // (docs/000-Vision.md UC-9) without needing the WebSocket channel
    // (docs/007-ExecutionEngine.md §4) built yet - the frontend just polls
    // GET /tasks/{id}/events.
    private static async Task RecordEventAsync(ForgeDbContext db, Guid taskId, string type, AgentRole actorRole, object? payload = null)
    {
        db.Events.Add(new DomainEvent
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Type = type,
            Payload = payload is null ? "{}" : JsonSerializer.Serialize(payload),
            OccurredAt = DateTimeOffset.UtcNow,
            Actor = $"agent:{actorRole}",
        });
        await db.SaveChangesAsync();
    }

    // docs/005-Agents.md §2. Real implementation: shells out to the Claude Code CLI
    // (ClaudeCliProvider) against the project's LocalPath checkout, asks it to produce
    // a description + acceptance criteria or flag genuine ambiguity, and persists
    // whichever it returns directly - the workflow only needs NeedsClarification.
    [Activity]
    public static async Task<PlannerResult> PlanAsync(Guid taskId)
    {
        await using var db = OpenDb();
        var task = await db.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId);

        if (task?.Project is null)
        {
            return new PlannerResult(true, null, [], [
                "Task or its Project could not be loaded from the database - this is an operational anomaly, not a real planning question."
            ]);
        }

        await RecordEventAsync(db, taskId, "PlannerStarted", AgentRole.Planner);

        var localPath = task.Project.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
        {
            await RecordEventAsync(db, taskId, "PlannerNeedsClarification", AgentRole.Planner,
                new { reason = "no usable Project.LocalPath" });
            return new PlannerResult(true, null, [], [
                $"Project '{task.Project.Name}' has no usable LocalPath configured on this machine - the Planner needs a real checkout to read for context (docs/003-Domain.md's Project.LocalPath)."
            ]);
        }

        await RecordEventAsync(db, taskId, "PlannerInvokingModel", AgentRole.Planner,
            new { message = $"Reading {localPath} and analyzing \"{task.Title}\"..." });

        var prompt = $$"""
            You are the Planner agent inside Forge, an AI-native software factory.
            A task was created with only this title: "{{task.Title}}"

            Investigate the repository in the current working directory to understand
            enough context to plan this task. Then decide one of two things:

            1. If the title is clear enough to act on, produce a short description and
               a list of 2-5 concrete, verifiable acceptance criteria.
            2. If it is genuinely ambiguous, or you are missing information only a human
               can provide, list specific clarifying questions instead - do not guess.

            Respond with ONLY a single JSON object, no other text, no markdown code
            fences, matching exactly this shape:
            {"needsClarification": boolean, "description": string or null, "acceptanceCriteria": string array, "questions": string array}
            """;

        var cliResult = await ClaudeCliProvider.InvokeAsync(prompt, localPath);

        PlannerLlmResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlannerLlmResponse>(
                ClaudeCliProvider.StripMarkdownFences(cliResult.Text), LlmJsonOptions);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        await RecordRunAsync(db, taskId, AgentRole.Planner, cliResult.CostUsd, cliResult.InputTokens, cliResult.OutputTokens,
            parsed is null ? RunStatus.Failed : RunStatus.Success);

        if (parsed is null)
        {
            await RecordEventAsync(db, taskId, "PlannerNeedsClarification", AgentRole.Planner,
                new { reason = "unparseable model response" });
            return new PlannerResult(true, null, [], [
                "The Planner's response could not be parsed as the expected JSON shape - treating as a clarification request rather than guessing at malformed output."
            ]);
        }

        if (!parsed.NeedsClarification)
        {
            task.Description = parsed.Description;
            foreach (var criterion in parsed.AcceptanceCriteria ?? [])
            {
                db.AcceptanceCriteria.Add(new AcceptanceCriterion
                {
                    Id = Guid.NewGuid(),
                    TaskId = taskId,
                    Description = criterion,
                });
            }
            await db.SaveChangesAsync();
            await RecordEventAsync(db, taskId, "PlannerCompleted", AgentRole.Planner,
                new { description = parsed.Description, acceptanceCriteriaCount = parsed.AcceptanceCriteria?.Count ?? 0 });
        }
        else
        {
            await RecordEventAsync(db, taskId, "PlannerNeedsClarification", AgentRole.Planner,
                new { questions = parsed.Questions });
        }

        return new PlannerResult(
            parsed.NeedsClarification,
            parsed.Description,
            parsed.AcceptanceCriteria ?? [],
            parsed.Questions ?? []);
    }

    // docs/005-Agents.md §4. Real implementation: sync root branch, create/reuse
    // worktree (docs/007-ExecutionEngine.md §2), run the agent loop, build/test.
    [Activity]
    public static async Task<DeveloperResult> DevelopAsync(Guid taskId)
    {
        await using var db = OpenDb();
        await RecordEventAsync(db, taskId, "DeveloperStarted", AgentRole.Developer);
        // TODO: real worktree + coding loop - docs/005-Agents.md §4, docs/007-ExecutionEngine.md §2
        await RecordEventAsync(db, taskId, "DeveloperCompleted", AgentRole.Developer,
            new { note = "stub - no real worktree/code changes made yet" });
        return new DeveloperResult(NeedsClarification: false, Questions: []);
    }

    // docs/005-Agents.md §5. Real implementation needs the PublishRecipe concept
    // (docs/015-Deployment.md §2) implemented - schema exists as a proposal, not yet built.
    [Activity]
    public static async Task<DeployResult> DeployAsync(Guid taskId)
    {
        await using var db = OpenDb();
        await RecordEventAsync(db, taskId, "DeployStarted", AgentRole.Deploy);
        // TODO: real PublishRecipe execution - docs/015-Deployment.md §2/§3
        await RecordEventAsync(db, taskId, "DeployCompleted", AgentRole.Deploy,
            new { note = "stub - no real publish steps executed yet" });
        return new DeployResult(Success: true, Error: null);
    }

    // docs/005-Agents.md §6 - push + PR creation via the GitHub plugin (ADR-0002).
    [Activity]
    public static async Task GitFinalizeAsync(Guid taskId)
    {
        await using var db = OpenDb();
        // TODO: real push + PR via gh/GitHub plugin - docs/005-Agents.md §6, docs/010-Plugins.md §2
        await RecordEventAsync(db, taskId, "GitPushed", AgentRole.Git,
            new { note = "stub - no real git push/PR performed yet" });
    }

    // docs/005-Agents.md §3. Per-project ordering - not implemented as real logic yet;
    // returning 0 rather than throwing keeps the workflow shape testable.
    [Activity]
    public static Task<int> PrioritizeAsync(Guid projectId) => Task.FromResult(0);
}
