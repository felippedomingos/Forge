using System.Collections.Concurrent;
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

// docs/005-Agents.md - one static activity per agent role. All 5 roles are real, per
// docs/016-Roadmap.md - none of these are stubs.
public static class AgentActivities
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("FORGE_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev";

    private static ForgeDbContext OpenDb() => new(new DbContextOptionsBuilder<ForgeDbContext>()
        .UseNpgsql(ConnectionString)
        .UseSnakeCaseNamingConvention()
        .Options);

    // docs/015-Deployment.md §3 - healthCheckUrl polling only, never mutating; no auth
    // headers or custom config needed for a plain GET-and-check-2xx probe.
    private static readonly HttpClient HealthCheckClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private record PlannerLlmResponse(
        [property: JsonPropertyName("needsClarification")] bool NeedsClarification,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("acceptanceCriteria")] List<string>? AcceptanceCriteria,
        [property: JsonPropertyName("questions")] List<string>? Questions);

    private record DeveloperLlmResponse(
        [property: JsonPropertyName("needsClarification")] bool NeedsClarification,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("questions")] List<string>? Questions);

    // workingDirectory is whatever path ClaudeCliProvider.InvokeAsync actually ran
    // against (localPath for Planner/Prioritizer, worktreePath for Developer) - needed
    // alongside the CLI result's own session_id to resolve where the CLI wrote that
    // session's transcript (ClaudeTranscriptReader.ComputeTranscriptPath).
    private static async Task RecordRunAsync(ForgeDbContext db, Guid taskId, AgentRole role, ClaudeCliResult cliResult, string workingDirectory, RunStatus status)
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
            PromptTokens = cliResult.InputTokens,
            CompletionTokens = cliResult.OutputTokens,
            CostEstimate = cliResult.CostUsd,
            SessionId = cliResult.SessionId,
            TranscriptPath = cliResult.SessionId is null
                ? null
                : ClaudeTranscriptReader.ComputeTranscriptPath(cliResult.SessionId, workingDirectory),
        });
        await db.SaveChangesAsync();
    }

    // docs/003-Domain.md §4's event catalog, finally written to for real - previously
    // only UserAnsweredQuestions (from Forge.Api) ever hit the `events` table. This is
    // what lets the task detail view show "what the agent is doing right now"
    // (docs/000-Vision.md UC-9) without needing the WebSocket channel
    // (docs/007-ExecutionEngine.md §4) built yet - the frontend just polls
    // GET /tasks/{id}/events.
    // Tries the clean path first (strip fences, deserialize); falls back to extracting
    // just the {...} substring if that fails - see ClaudeCliProvider.ExtractJsonObject
    // for why (an occasional stray sentence before the JSON despite instructions).
    private static T? TryParseLlmJson<T>(string rawText) where T : class
    {
        var stripped = ClaudeCliProvider.StripMarkdownFences(rawText);
        try
        {
            return JsonSerializer.Deserialize<T>(stripped, LlmJsonOptions);
        }
        catch (JsonException)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(ClaudeCliProvider.ExtractJsonObject(stripped), LlmJsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    // docs/005-Agents.md §7 - shared project memory (AgentMemory), read by every role
    // regardless of which role originally wrote an entry (per the founder's framing:
    // it's the project's memory, not the Planner's or Developer's). Returns a block
    // ready to splice into a prompt, or a note that there's nothing recorded yet -
    // never an empty string, so the prompt always explains what the section means.
    private static async Task<string> FormatMemoryAsync(ForgeDbContext db, Guid projectId)
    {
        var entries = await db.AgentMemories
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.Key)
            .ToListAsync();

        return entries.Count == 0
            ? "(no shared project memory recorded yet)"
            : string.Join("\n", entries.Select(e => $"- {e.Key}: {e.Value}"));
    }

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
        await PostgresNotify.TaskChangedAsync(db, taskId);
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

        var memory = await FormatMemoryAsync(db, task.ProjectId);
        // Founder-requested: a task's title is sometimes accompanied by user-provided
        // notes (set at creation, docs/012-API.md POST /tasks) that may contain a URL -
        // a spec doc, an issue, a design mock. The Planner is explicitly told to fetch
        // those (WebFetch is allowed below, scoped narrowly rather than via
        // bypassPermissions) rather than guessing at what a bare link implies.
        var seedNotesSection = string.IsNullOrWhiteSpace(task.Description)
            ? ""
            : $$"""

                The user also provided these initial notes when creating the task:
                {{task.Description}}

                If these notes contain a URL, fetch it (you have WebFetch access) before
                planning - ground your description and acceptance criteria in what that
                page/doc/issue actually says, not just the link text.
                """;
        var prompt = $$"""
            You are the Planner agent inside Forge, an AI-native software factory.
            A task was created with this title: "{{task.Title}}"
            {{seedNotesSection}}

            Shared project memory (notes accumulated across prior tasks on this
            project - conventions, decisions, gotchas; treat as authoritative context
            alongside the repository itself):
            {{memory}}

            Investigate the repository in the current working directory to understand
            enough context to plan this task. Then decide one of two things:

            1. If the title (and notes, if any) are clear enough to act on, produce a
               short description and a list of 2-5 concrete, verifiable acceptance
               criteria.
            2. If it is genuinely ambiguous, or you are missing information only a human
               can provide, list specific clarifying questions instead - do not guess.

            Respond with ONLY a single JSON object, no other text, no markdown code
            fences, matching exactly this shape:
            {"needsClarification": boolean, "description": string or null, "acceptanceCriteria": string array, "questions": string array}
            """;

        var cliResult = await ClaudeCliProvider.InvokeAsync(prompt, localPath, allowedTools: ["WebFetch"]);
        var parsed = TryParseLlmJson<PlannerLlmResponse>(cliResult.Text);

        await RecordRunAsync(db, taskId, AgentRole.Planner, cliResult, localPath,
            parsed is null ? RunStatus.Failed : RunStatus.Success);

        if (parsed is null)
        {
            await RecordEventAsync(db, taskId, "PlannerNeedsClarification", AgentRole.Planner,
                new { reason = "unparseable model response", rawResponse = Truncate(cliResult.Text, 1000) });
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

    private static string WorktreesRootDir =>
        Environment.GetEnvironmentVariable("FORGE_WORKTREES_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "forge-worktrees");

    // docs/005-Agents.md §4. Real implementation: sync root branch, create/reuse
    // worktree (docs/007-ExecutionEngine.md §2), then run the same ClaudeCliProvider
    // the Planner uses, but pointed at the worktree with bypassPermissions=true so it
    // can actually edit files. Gated on Project.AllowAgentBypassPermissions (below) -
    // refuses outright for any project not explicitly marked trusted, rather than
    // relying on "only ever pointed at the sandbox" as the sole safeguard.
    [Activity]
    public static async Task<DeveloperResult> DevelopAsync(Guid taskId)
    {
        await using var db = OpenDb();
        var task = await db.Tasks
            .Include(t => t.Project)
            .Include(t => t.AcceptanceCriteria)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task?.Project is null)
        {
            return new DeveloperResult(true, [
                "Task or its Project could not be loaded from the database - an operational anomaly, not a real question."
            ]);
        }

        await RecordEventAsync(db, taskId, "DeveloperStarted", AgentRole.Developer);

        // Founder-requested trust gate (Project.AllowAgentBypassPermissions): editing
        // files at all in a headless subprocess requires bypassPermissions (ADR-0005 -
        // there's no human to click "allow"), so an untrusted project must refuse
        // outright rather than silently no-op or (worse) let the model report
        // "success" over a write that was actually denied.
        if (!task.Project.AllowAgentBypassPermissions)
        {
            await RecordEventAsync(db, taskId, "DeveloperNeedsClarification", AgentRole.Developer,
                new { reason = "project not marked as trusted for autonomous execution" });
            return new DeveloperResult(true, [
                $"Project '{task.Project.Name}' isn't marked as trusted for autonomous code execution - enable \"Allow agent bypass permissions\" in the project's edit dialog before the Developer agent can write to it."
            ]);
        }

        var localPath = task.Project.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
        {
            await RecordEventAsync(db, taskId, "DeveloperNeedsClarification", AgentRole.Developer,
                new { reason = "no usable Project.LocalPath" });
            return new DeveloperResult(true, [
                $"Project '{task.Project.Name}' has no usable LocalPath - the Developer needs a real checkout to branch from."
            ]);
        }

        // docs/004-Workflow.md §3: resume against the SAME worktree if one already
        // exists for this task, rather than recreating it.
        var worktree = await db.Worktrees.FirstOrDefaultAsync(w => w.TaskId == taskId && w.DeletedAt == null);
        string worktreePath;
        string branchName;

        if (worktree is not null && Directory.Exists(worktree.Path))
        {
            worktreePath = worktree.Path;
            branchName = worktree.BranchName;
            await RecordEventAsync(db, taskId, "DeveloperResumingWorktree", AgentRole.Developer, new { worktreePath });
        }
        else
        {
            branchName = $"forge/task-{taskId}-{GitOps.Slugify(task.Title)}";
            worktreePath = Path.Combine(WorktreesRootDir, task.ProjectId.ToString(), $"task-{taskId}");
            Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);

            await RecordEventAsync(db, taskId, "DeveloperSyncingRepo", AgentRole.Developer,
                new { message = $"git fetch origin {task.Project.RootBranch}" });

            var fetch = await GitOps.RunAsync(localPath, "fetch", "origin", task.Project.RootBranch);
            if (!fetch.Success)
                throw new InvalidOperationException($"git fetch failed: {fetch.Stderr}");

            var addWorktree = await GitOps.RunAsync(localPath, "worktree", "add", worktreePath, "-b", branchName, $"origin/{task.Project.RootBranch}");
            if (!addWorktree.Success)
                throw new InvalidOperationException($"git worktree add failed: {addWorktree.Stderr}");

            worktree = new Worktree
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                ProjectId = task.ProjectId,
                Path = worktreePath,
                BranchName = branchName,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Worktrees.Add(worktree);
            task.WorktreeId = worktree.Id;
            task.BranchName = branchName;
            await db.SaveChangesAsync();

            await RecordEventAsync(db, taskId, "DeveloperWorktreeCreated", AgentRole.Developer,
                new { worktreePath, branchName });
        }

        await RecordEventAsync(db, taskId, "DeveloperInvokingModel", AgentRole.Developer,
            new { message = "Implementing the task..." });

        var criteriaText = task.AcceptanceCriteria.Count > 0
            ? string.Join("\n", task.AcceptanceCriteria.Select(c => $"- {c.Description}"))
            : "(none recorded - use judgment based on the description)";

        var memory = await FormatMemoryAsync(db, task.ProjectId);
        // Founder-requested: a reviewer sending this task back from Review (§ the new
        // "Request changes" action) leaves feedback here - surfaced on every rework
        // pass, not just the one right after it was left, since a task can bounce
        // back and forth more than once.
        var reviewFeedback = await GetLatestReviewFeedbackAsync(db, taskId);
        var reviewFeedbackSection = reviewFeedback is null
            ? ""
            : $$"""

                A human reviewer sent this task back with feedback on your previous
                attempt - address it directly, it takes priority over guessing:
                {{reviewFeedback}}
                """;
        var prompt = $$"""
            You are the Developer agent inside Forge. You are on a dedicated git branch
            in a real checkout - it is safe to edit files here.

            Shared project memory (notes accumulated across prior tasks on this
            project - conventions, decisions, gotchas; treat as authoritative context
            alongside the repository itself):
            {{memory}}

            Title: {{task.Title}}
            Description: {{task.Description}}
            Acceptance Criteria:
            {{criteriaText}}
            {{reviewFeedbackSection}}

            Make the necessary code changes to satisfy the acceptance criteria. Keep
            changes minimal and focused only on this task. Do not commit - Forge commits
            on your behalf after you finish.

            If you are missing information only a human can provide, make no changes and
            list clarifying questions instead of guessing.

            When done, respond with ONLY a single JSON object, no other text, no markdown
            fences, matching exactly this shape:
            {"needsClarification": boolean, "summary": string or null, "questions": string array}
            """;

        var cliResult = await ClaudeCliProvider.InvokeAsync(prompt, worktreePath, bypassPermissions: true);
        var parsed = TryParseLlmJson<DeveloperLlmResponse>(cliResult.Text);

        await RecordRunAsync(db, taskId, AgentRole.Developer, cliResult, worktreePath,
            parsed is null ? RunStatus.Failed : RunStatus.Success);

        if (parsed is null)
        {
            await RecordEventAsync(db, taskId, "DeveloperNeedsClarification", AgentRole.Developer,
                new { reason = "unparseable model response", rawResponse = Truncate(cliResult.Text, 1000) });
            return new DeveloperResult(true, [
                "The Developer's response could not be parsed as the expected JSON shape - treating as a clarification request rather than guessing."
            ]);
        }

        if (parsed.NeedsClarification)
        {
            await RecordEventAsync(db, taskId, "DeveloperNeedsClarification", AgentRole.Developer,
                new { questions = parsed.Questions });
            return new DeveloperResult(true, parsed.Questions ?? []);
        }

        // docs/005-Agents.md §4: the Developer commits locally for checkpointing; the
        // Git agent owns push+PR later, at Done.
        var status = await GitOps.RunAsync(worktreePath, "status", "--porcelain");
        if (!string.IsNullOrWhiteSpace(status.Stdout))
        {
            await GitOps.RunAsync(worktreePath, "add", "-A");
            var commitMessage = string.IsNullOrWhiteSpace(parsed.Summary) ? task.Title : parsed.Summary;
            var commit = await GitOps.RunAsync(worktreePath, "commit", "-m", commitMessage);
            await RecordEventAsync(db, taskId, "DeveloperCommitted", AgentRole.Developer,
                new { message = commitMessage, success = commit.Success });
        }
        else
        {
            await RecordEventAsync(db, taskId, "DeveloperCommitted", AgentRole.Developer,
                new { message = "no file changes to commit" });
        }

        await RecordEventAsync(db, taskId, "DeveloperCompleted", AgentRole.Developer,
            new { summary = parsed.Summary });

        return new DeveloperResult(false, []);
    }

    private record PublishRecipeDto(
        [property: JsonPropertyName("migrationCommand")] string? MigrationCommand,
        [property: JsonPropertyName("restartTargets")] List<string>? RestartTargets,
        [property: JsonPropertyName("healthCheckUrl")] string? HealthCheckUrl,
        // docs/015-Deployment.md §2 - purely informational: where a human clicks "Testar"
        // to eyeball the result once a task reaches Review. Never read by DeployAsync
        // itself (unlike healthCheckUrl, which is for an automated check that isn't
        // implemented yet either) - this field only exists for the frontend button.
        [property: JsonPropertyName("previewUrl")] string? PreviewUrl);

    // Found live (2026-08-06): a `restartTargets` command that backgrounds a detached
    // process (scripts/restart-forge-dev.sh's own bug, since fixed) can leave a
    // lingering shell holding the redirected stdout/stderr pipe open forever, even
    // after the actual work finished - ReadToEndAsync/WaitForExitAsync then hang
    // indefinitely with no way for Temporal to notice (StartToCloseTimeout doesn't
    // cancel worker-side code without heartbeating). Worse than a single stuck Deploy:
    // since the orphaned Task never returns, it never hits the `finally` that releases
    // GetDeployLock's per-project semaphore either, wedging every subsequent Deploy for
    // that project until the Worker process is restarted. A hard ceiling here, with a
    // full process-tree kill on expiry, means a future command with this same class of
    // bug fails loudly in ~5 minutes instead of silently locking out the whole project.
    private static async Task<(bool Success, string Output)> RunShellAsync(string workingDirectory, string command)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/bash",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);

        using var process = System.Diagnostics.Process.Start(psi)!;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            // Found live (2026-08-06): `git merge` on a real conflict exits non-zero but
            // writes its actually-useful "CONFLICT (content): ..." message to stdout, not
            // stderr - returning stderr-only on failure surfaced a completely empty
            // DeployFailed output for a real, diagnosable conflict. Concatenate both on
            // failure so nothing informative is silently dropped; success still returns
            // stdout alone, unchanged.
            var output = process.ExitCode == 0 ? stdout : string.Join("\n", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return (process.ExitCode == 0, output);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return (false, $"Command timed out after 5 minutes and was killed: {command}");
        }
    }

    // docs/015-Deployment.md §3 - polls up to ~30s (10 attempts, 3s apart) rather than
    // a single check, since a just-restarted service typically isn't accepting
    // connections for the first second or two.
    private static async Task<bool> PollHealthCheckAsync(string url)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var response = await HealthCheckClient.GetAsync(url);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (Exception) when (attempt < 9)
            {
                // Connection refused/reset while the service is still coming up -
                // expected during the first few attempts, not a reason to give up yet.
            }
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        return false;
    }

    // Founder-hit incident (2026-08-05): two Deploy activities for the same project
    // racing on the same shared restartTargets process (e.g. both restarting the same
    // self-hosted dev Api) each killed the other's freshly-started process mid-restart,
    // stalling the health check past the activity's own 10-minute StartToCloseTimeout -
    // twice in a row, which exhausted DeployActivityOptions' retries and failed the
    // whole workflow even though the underlying restart script itself works fine run
    // alone. One semaphore per project serializes Deploy's actual side-effecting work
    // (migration + restarts + health check) so a second Deploy for the same project
    // waits its turn instead of racing. In-process only - fine for the single-Worker
    // deployment model today (docs/016-Roadmap.md's "real dedicated infrastructure"
    // v2 item would need a distributed lock if that ever becomes multiple Workers).
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> DeployLocks = new();

    private static SemaphoreSlim GetDeployLock(Guid projectId) =>
        DeployLocks.GetOrAdd(projectId, static _ => new SemaphoreSlim(1, 1));

    // docs/005-Agents.md §5, docs/015-Deployment.md §2/§3. Runs `migrationCommand`,
    // then each `restartTargets` entry as its own shell command in order, then polls
    // `healthCheckUrl` before declaring success - the full recipe, not just the
    // migration step. `restartTargets` entries are raw shell commands, not bare
    // Docker Compose service names as originally documented - found live (founder
    // dogfooding Forge on itself) that assuming Compose specifically didn't fit a
    // project whose own dev processes run as plain `dotnet run`/`vite`, not Compose
    // services. A project that does use Compose just writes
    // "docker compose restart X" as the command instead of bare "X" - strictly more
    // flexible, no project had restartTargets configured yet so nothing to migrate.
    // Gated on Project.AllowAgentBypassPermissions, same reasoning as DevelopAsync:
    // this executes arbitrary shell commands unattended, which needs the same
    // explicit per-project trust as editing files does.
    [Activity]
    public static async Task<DeployResult> DeployAsync(Guid taskId)
    {
        await using var db = OpenDb();
        var task = await db.Tasks
            .Include(t => t.Project)
            .Include(t => t.Worktree)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task?.Project is null)
            return new DeployResult(false, "Task or its Project could not be loaded.");

        await RecordEventAsync(db, taskId, "DeployStarted", AgentRole.Deploy);

        var recipeJson = task.Project.PublishRecipe;
        if (string.IsNullOrWhiteSpace(recipeJson))
        {
            await RecordEventAsync(db, taskId, "DeployCompleted", AgentRole.Deploy,
                new { note = $"Project '{task.Project.Name}' has no PublishRecipe configured - nothing to do." });
            return new DeployResult(true, null);
        }

        PublishRecipeDto? recipe;
        try
        {
            recipe = JsonSerializer.Deserialize<PublishRecipeDto>(recipeJson, LlmJsonOptions);
        }
        catch (JsonException ex)
        {
            await RecordEventAsync(db, taskId, "DeployCompleted", AgentRole.Deploy,
                new { note = $"PublishRecipe is not valid JSON: {ex.Message}" });
            return new DeployResult(false, "Invalid PublishRecipe JSON.");
        }

        var hasMigration = !string.IsNullOrWhiteSpace(recipe?.MigrationCommand);
        var hasRestarts = recipe?.RestartTargets is { Count: > 0 };
        if (!hasMigration && !hasRestarts)
        {
            await RecordEventAsync(db, taskId, "DeployCompleted", AgentRole.Deploy,
                new { note = "PublishRecipe has no migrationCommand or restartTargets - nothing to run." });
            return new DeployResult(true, null);
        }

        if (!task.Project.AllowAgentBypassPermissions)
        {
            await RecordEventAsync(db, taskId, "DeployFailed", AgentRole.Deploy,
                new { reason = "project not marked as trusted for autonomous execution" });
            return new DeployResult(false,
                $"Project '{task.Project.Name}' isn't marked as trusted for autonomous code execution - enable \"Allow agent bypass permissions\" before Deploy can run its recipe.");
        }

        var runDirectory = task.Worktree is { DeletedAt: null } wt && Directory.Exists(wt.Path)
            ? wt.Path
            : task.Project.LocalPath;

        if (string.IsNullOrWhiteSpace(runDirectory) || !Directory.Exists(runDirectory))
        {
            await RecordEventAsync(db, taskId, "DeployCompleted", AgentRole.Deploy,
                new { note = "No worktree or LocalPath available to run the publish command against." });
            return new DeployResult(false, "No directory to run the PublishRecipe against.");
        }

        var deployLock = GetDeployLock(task.ProjectId);
        if (!await deployLock.WaitAsync(TimeSpan.FromMinutes(8)))
        {
            await RecordEventAsync(db, taskId, "DeployFailed", AgentRole.Deploy,
                new { reason = "another Deploy for this project was still running after 8 minutes - not running concurrently against the same target." });
            return new DeployResult(false, "Timed out waiting for another in-progress Deploy for this project to finish.");
        }

        try
        {
            if (hasMigration)
            {
                var (success, output) = await RunShellAsync(runDirectory, recipe!.MigrationCommand!);
                await RecordEventAsync(db, taskId, success ? "DeployMigrationCompleted" : "DeployFailed", AgentRole.Deploy,
                    new { command = recipe.MigrationCommand, output = Truncate(output, 1000) });
                if (!success) return new DeployResult(false, Truncate(output, 500));
            }

            if (hasRestarts)
            {
                // Found live (2026-08-06, founder's original ask from early this session):
                // `restartTargets` restarts whatever's already checked out at
                // Project.LocalPath - it never pulled in the task's own commit, which
                // only ever lives in the Worktree until GitFinalizeAsync pushes it
                // (Review->Done, well after Deploy). A founder clicking "Publish" then
                // "Testar" was restarting the *old* code every time. When this task has
                // both a Worktree and a distinct LocalPath (the self-hosted "restart the
                // real dev servers" shape), merge the task's branch into LocalPath first
                // - refusing outright on a dirty LocalPath rather than merging over
                // whatever the founder's own working copy currently has uncommitted.
                if (task.Worktree is { DeletedAt: null } && task.BranchName is not null &&
                    !string.IsNullOrWhiteSpace(task.Project.LocalPath) && task.Project.LocalPath != runDirectory)
                {
                    var localPath = task.Project.LocalPath;
                    var status = await RunShellAsync(localPath, "git status --porcelain");
                    if (!string.IsNullOrWhiteSpace(status.Output))
                    {
                        await RecordEventAsync(db, taskId, "DeployFailed", AgentRole.Deploy,
                            new { reason = "LocalPath has uncommitted changes - refusing to merge the task branch over them", output = Truncate(status.Output, 1000) });
                        return new DeployResult(false, "LocalPath has uncommitted changes - commit/stash them before publishing.");
                    }

                    var merge = await RunShellAsync(localPath, $"git merge --no-edit {task.BranchName}");
                    await RecordEventAsync(db, taskId, merge.Success ? "DeployBranchMerged" : "DeployFailed", AgentRole.Deploy,
                        new { branch = task.BranchName, output = Truncate(merge.Output, 1000) });
                    if (!merge.Success)
                    {
                        await RunShellAsync(localPath, "git merge --abort");
                        return new DeployResult(false, $"Merging '{task.BranchName}' into LocalPath failed: {Truncate(merge.Output, 500)}");
                    }
                }

                foreach (var command in recipe!.RestartTargets!)
                {
                    var (success, output) = await RunShellAsync(runDirectory, command);
                    await RecordEventAsync(db, taskId, success ? "DeployRestartCompleted" : "DeployFailed", AgentRole.Deploy,
                        new { command, output = Truncate(output, 1000) });
                    if (!success) return new DeployResult(false, $"Restart command failed ('{command}'): {Truncate(output, 500)}");
                }
            }

            if (!string.IsNullOrWhiteSpace(recipe?.HealthCheckUrl))
            {
                var healthy = await PollHealthCheckAsync(recipe.HealthCheckUrl);
                await RecordEventAsync(db, taskId, healthy ? "DeployHealthCheckPassed" : "DeployFailed", AgentRole.Deploy,
                    new { url = recipe.HealthCheckUrl, healthy });
                if (!healthy)
                    return new DeployResult(false, $"Health check at {recipe.HealthCheckUrl} never returned a successful status.");
            }

            await RecordEventAsync(db, taskId, "DeployCompleted", AgentRole.Deploy, new
            {
                ranMigration = hasMigration,
                restartedTargets = recipe?.RestartTargets ?? [],
                healthChecked = !string.IsNullOrWhiteSpace(recipe?.HealthCheckUrl),
            });
            return new DeployResult(true, null);
        }
        finally
        {
            deployLock.Release();
        }
    }

    // docs/015-Deployment.md §4 / TaskWorkflow's resume-from-state path - GitFinalizeAsync
    // isn't safely re-runnable (it'd push+`pr create` again), so a resumed workflow
    // checks this first rather than blindly re-running it.
    [Activity]
    public static async Task<bool> HasPullRequestAsync(Guid taskId)
    {
        await using var db = OpenDb();
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId);
        return task?.PullRequestUrl is not null;
    }

    // docs/005-Agents.md §6 - push + PR creation. Branches on the Project's
    // GitProviderPlugin (GitHub via `gh`, or Azure DevOps via `az repos pr create` -
    // docs/010-Plugins.md §5) rather than a real IGitProviderPlugin implementation -
    // neither provider actually goes through that interface, it's inline subprocess
    // calls for both, consistent with this whole pass's shape (ClaudeCliProvider,
    // GitOps). The Azure DevOps path is unvalidated against a real org/repo - built
    // correctly against `az repos pr create`'s documented shape, but not exercised
    // live the way the GitHub path was (docs/015-Deployment.md's own validated-live
    // findings are all GitHub).
    // docs/010-Plugins.md §5 - explicit org/project/repo derived from the Project's own
    // RepositoryUrl (GitOps.TryParseAzureRepo) rather than the machine-wide `az devops
    // configure -d` default, which is correct for exactly one Azure DevOps project at
    // a time and silently wrong for a second one.
    private static async Task<GitCommandResult> CreateAzureDevOpsPrAsync(string worktreePath, Project project, TaskItem task)
    {
        var args = new List<string>
        {
            "repos", "pr", "create",
            "--title", task.Title,
            "--source-branch", task.BranchName!,
            "--target-branch", project.RootBranch,
            "--output", "json",
        };

        if (GitOps.TryParseAzureRepo(project.RepositoryUrl) is { } repo)
        {
            args.AddRange(["--organization", $"https://dev.azure.com/{repo.Organization}"]);
            args.AddRange(["--project", repo.Project]);
            args.AddRange(["--repository", repo.Repository]);
        }

        return await GitOps.RunAzAsync(worktreePath, args.ToArray());
    }

    // docs/015-Deployment.md §4 - the Done->Production polling loop needs a stable URL
    // to check merge status against, not raw CLI output text. `gh pr create --fill`
    // prints just the PR URL as its final stdout line; `az repos pr create --output
    // json` prints a JSON object with the web URL under `_links.web.href`.
    private static string? ExtractPrUrl(bool isAzureDevOps, GitCommandResult result)
    {
        if (!result.Success) return null;
        try
        {
            if (isAzureDevOps)
            {
                using var doc = JsonDocument.Parse(result.Stdout);
                return doc.RootElement.GetProperty("_links").GetProperty("web").GetProperty("href").GetString();
            }
            return result.Stdout.Trim().Split('\n').Last().Trim();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    [Activity]
    public static async Task GitFinalizeAsync(Guid taskId)
    {
        await using var db = OpenDb();
        var task = await db.Tasks.Include(t => t.Worktree).FirstOrDefaultAsync(t => t.Id == taskId);

        if (task?.Worktree is null || task.BranchName is null)
        {
            await RecordEventAsync(db, taskId, "GitPushed", AgentRole.Git,
                new { note = "no worktree/branch recorded for this task - nothing to push (Developer never committed real changes, e.g. the stub-era path or an empty diff)" });
            return;
        }

        var worktreePath = task.Worktree.Path;
        if (!Directory.Exists(worktreePath))
        {
            await RecordEventAsync(db, taskId, "GitPushed", AgentRole.Git,
                new { note = $"worktree path {worktreePath} no longer exists - cannot push" });
            return;
        }

        var project = await db.Projects.Include(p => p.GitProviderPlugin).FirstOrDefaultAsync(p => p.Id == task.ProjectId);

        var push = await GitOps.RunAsync(worktreePath, "push", "-u", "origin", task.BranchName);
        await RecordEventAsync(db, taskId, "GitPushed", AgentRole.Git,
            new { branch = task.BranchName, success = push.Success, stderr = push.Success ? null : push.Stderr });

        if (push.Success)
        {
            var isAzureDevOps = project?.GitProviderPlugin?.Name == "azure-devops";
            GitCommandResult pr;
            if (isAzureDevOps)
            {
                pr = await CreateAzureDevOpsPrAsync(worktreePath, project!, task);
            }
            else
            {
                // Found live (2026-08-06): `--fill` derives the PR title from the commit
                // message's first line - Developer's commit subjects run long (detailed
                // one-line summaries), and GitHub rejects any PR title over 256 chars
                // (a hard GraphQL validation error, not a warning). The push already
                // succeeded by this point, so a title-length failure silently left a
                // pushed branch with no PR and no way to notice short of checking
                // PullRequestUrl by hand. Task.Title is short by construction - use it
                // explicitly instead, with the commit message as the PR body so nothing
                // from `--fill` is actually lost.
                var commitMessage = await GitOps.RunAsync(worktreePath, "log", "-1", "--format=%B");
                var body = commitMessage.Success ? commitMessage.Stdout.Trim() : task.Description ?? "";
                pr = await GitOps.RunGhAsync(worktreePath, "pr", "create", "--title", task.Title, "--body", body, "--head", task.BranchName!);
            }

            var prUrl = ExtractPrUrl(isAzureDevOps, pr);
            if (prUrl is not null)
            {
                task.PullRequestUrl = prUrl;
                await db.SaveChangesAsync();
            }

            await RecordEventAsync(db, taskId, "PRCreated", AgentRole.Git,
                new { success = pr.Success, url = prUrl, output = pr.Success ? pr.Stdout.Trim() : pr.Stderr.Trim() });
        }

        // docs/007-ExecutionEngine.md §2 - cleanup only after push(+PR); `git worktree
        // remove` must run from the canonical clone, not the worktree being removed.
        // A FORCED removal (uncommitted changes present) is deliberately NOT something
        // an agent does unattended - that's in the project's permission "ask" list.
        if (project?.LocalPath is { } localPath && Directory.Exists(localPath))
        {
            var remove = await GitOps.RunAsync(localPath, "worktree", "remove", worktreePath);
            if (remove.Success)
            {
                task.Worktree.DeletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
                await RecordEventAsync(db, taskId, "WorktreeDeleted", AgentRole.Git, new { worktreePath });
            }
            else
            {
                await RecordEventAsync(db, taskId, "WorktreeDeleted", AgentRole.Git,
                    new { success = false, stderr = remove.Stderr });
            }
        }
    }

    // docs/015-Deployment.md §4 - resolves the previously-undesigned CI/CD integration
    // by polling the PR's own merge status directly (provider-agnostic: `gh pr view`
    // or `az repos pr show`) instead of a webhook receiver, which would need Forge's
    // API reachable from GitHub/Azure DevOps - not true for a bare-metal/local
    // deployment (ADR-0004). Returns false (never throws) on anything unexpected -
    // a transient CLI hiccup shouldn't fail the whole polling loop, just try again
    // next tick.
    [Activity]
    public static async Task<bool> CheckPullRequestMergedAsync(Guid taskId)
    {
        await using var db = OpenDb();
        var task = await db.Tasks.Include(t => t.Project).ThenInclude(p => p!.GitProviderPlugin)
            .FirstOrDefaultAsync(t => t.Id == taskId);
        if (task?.PullRequestUrl is not { } url) return false;

        try
        {
            if (task.Project?.GitProviderPlugin?.Name == "azure-devops")
            {
                var prId = url.Split("/pullrequest/").ElementAtOrDefault(1);
                if (prId is null) return false;
                var azResult = await GitOps.RunAzAsync(Path.GetTempPath(), "repos", "pr", "show", "--id", prId, "--output", "json");
                if (!azResult.Success) return false;
                using var azDoc = JsonDocument.Parse(azResult.Stdout);
                return azDoc.RootElement.GetProperty("status").GetString() == "completed";
            }

            var ghResult = await GitOps.RunGhAsync(Path.GetTempPath(), "pr", "view", url, "--json", "state");
            if (!ghResult.Success) return false;
            using var ghDoc = JsonDocument.Parse(ghResult.Stdout);
            return ghDoc.RootElement.GetProperty("state").GetString() == "MERGED";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // docs/003-Domain.md row 9's new sibling (founder-requested): a reviewer can send
    // a task back for another Developer pass instead of only approving. Reads the most
    // recent "ReviewRequestedChanges" event (docs/012-API.md POST /tasks/{id}/request-
    // changes writes it before signaling the workflow) so the next DevelopAsync run
    // knows what the reviewer actually said, rather than blindly retrying.
    private static async Task<string?> GetLatestReviewFeedbackAsync(ForgeDbContext db, Guid taskId)
    {
        var latest = await db.Events
            .Where(e => e.TaskId == taskId && e.Type == "ReviewRequestedChanges")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();
        if (latest is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(latest.Payload);
            return doc.RootElement.GetProperty("comment").GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private record PrioritizationLlmResponse(
        [property: JsonPropertyName("orderedTaskIds")] List<string>? OrderedTaskIds);

    // docs/005-Agents.md §3 - real. Unlike the other 4 roles, this one is scoped to the
    // whole project's Backlog, not a single task, per the decision already recorded
    // there (per-project, not per-task, priority ordering). Called by
    // BacklogSchedulerWorkflow only when unprioritized tasks exist, not on a fixed
    // timer - re-running this on every 5s poll tick would spend real money
    // re-prioritizing a backlog that hasn't changed.
    [Activity]
    public static async Task PrioritizeAsync(Guid projectId)
    {
        await using var db = OpenDb();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId);
        var allBacklogTasks = await db.Tasks
            .Where(t => t.ProjectId == projectId && t.State == TaskState.Backlog)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        if (allBacklogTasks.Count == 0) return;

        // Tasks with a Product Owner-set priority (PATCH /tasks/{id}/priority) are
        // excluded from this agent's ranking entirely - never reassigned, regardless
        // of how many other still-unprioritized tasks in the same project triggered
        // this run.
        var backlogTasks = allBacklogTasks.Where(t => !t.PriorityManuallySet).ToList();
        if (backlogTasks.Count == 0) return;

        if (backlogTasks.Count == 1 || project?.LocalPath is not { } localPath || !Directory.Exists(localPath))
        {
            // Nothing to compare against, or no repo context to reason with - fall
            // back to creation order (FIFO) rather than blocking scheduling on it.
            for (var i = 0; i < backlogTasks.Count; i++) backlogTasks[i].Priority = i;
            await db.SaveChangesAsync();
            foreach (var t in backlogTasks)
                await RecordEventAsync(db, t.Id, "PrioritizationCompleted", AgentRole.Prioritizer,
                    new { priority = t.Priority, method = "fifo-fallback" });
            return;
        }

        var taskList = string.Join("\n", backlogTasks.Select(t => $"- {t.Id}: {t.Title}"));
        var prompt = $$"""
            You are the Prioritizer agent inside Forge. Order the following backlog
            tasks for this project by importance/impact, most important first. Use
            your judgment about the project based on the repository in the current
            working directory; if nothing distinguishes them, keep the given order.

            Tasks:
            {{taskList}}

            Respond with ONLY a single JSON object, no other text, no markdown fences,
            matching exactly this shape:
            {"orderedTaskIds": string array containing exactly the task IDs above, most important first}
            """;

        var cliResult = await ClaudeCliProvider.InvokeAsync(prompt, localPath);
        var parsed = TryParseLlmJson<PrioritizationLlmResponse>(cliResult.Text);

        // Attaching this batch call's cost to the first task by creation order - Run
        // is schema'd as single-task (docs/011-Database.md), which doesn't cleanly fit
        // a call spanning multiple tasks. Noted as a known limitation, not fixed here.
        await RecordRunAsync(db, backlogTasks[0].Id, AgentRole.Prioritizer, cliResult, localPath,
            parsed is null ? RunStatus.Failed : RunStatus.Success);

        // Validate the model's ordering against the real task set: keep only IDs that
        // actually exist, then append anything it omitted (preserving original order)
        // so every task still gets a priority - never leave one unprioritized because
        // the model's list was incomplete or hallucinated an ID.
        var byId = backlogTasks.ToDictionary(t => t.Id.ToString(), t => t);
        var ordered = new List<TaskItem>();
        foreach (var id in parsed?.OrderedTaskIds ?? [])
            if (byId.TryGetValue(id, out var t) && !ordered.Contains(t))
                ordered.Add(t);
        foreach (var t in backlogTasks)
            if (!ordered.Contains(t))
                ordered.Add(t);

        for (var i = 0; i < ordered.Count; i++) ordered[i].Priority = i;
        await db.SaveChangesAsync();

        foreach (var t in ordered)
            await RecordEventAsync(db, t.Id, "PrioritizationCompleted", AgentRole.Prioritizer,
                new { priority = t.Priority, method = parsed is null ? "fallback-unparsed-response" : "llm" });
    }
}
