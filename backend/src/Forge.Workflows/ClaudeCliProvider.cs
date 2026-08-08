using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forge.Workflows;

public record ClaudeCliResult(string Text, decimal CostUsd, int InputTokens, int OutputTokens, string? SessionId);

// Thrown instead of the plain InvalidOperationException other CLI failures raise -
// the one signal ClaudeCliProvider.InvokeAsync's rotation loop acts on. Everything
// else (a bad prompt, a real bug, a network blip) stays a plain InvalidOperationException
// and propagates immediately - rotating accounts wouldn't fix any of those.
public class ClaudeUsageLimitException(string message, DateTimeOffset? resetAt) : Exception(message)
{
    public DateTimeOffset? ResetAt { get; } = resetAt;
}

// docs/008-ModelRouter.md §1, docs/adr/ADR-0005-claude-code-cli-as-invocation-mechanism.md
// - the one Provider implementation at v1. Wraps the Claude Code CLI as a subprocess.
// Founder-requested (2026-08-08): rotates across ClaudeAccountPool's configured
// accounts on a detected usage-limit failure, instead of failing outright - ADR-0005
// designed for this but deferred building it until more than one account existed to
// test against. See ClaudeAccountPool.cs for the account list/cooldown mechanism.
public static class ClaudeCliProvider
{
    // bypassPermissions: needed for any agent role that actually edits files or runs
    // shell commands (Developer, Deploy) since there's no human to click "allow" in a
    // headless subprocess. Safe ONLY because docs/adr/ADR-0004's sandbox
    // (felippedomingos/forge-test-sandbox) is disposable and isolated - a real project
    // would need docs/009-MCP.md §4's per-role tool scoping instead of a blanket
    // bypass, which is not implemented yet (docs/014-Security.md §4 gap).
    // allowedTools: pre-approves specific tools without granting bypassPermissions'
    // blanket access - e.g. the Planner (docs/005-Agents.md §2) needs WebFetch to
    // follow a link a human put in a task's description, but should stay read-only
    // otherwise. Additive to whatever's already auto-allowed in headless -p mode
    // (Read/Glob/Grep already work today with no flags at all); irrelevant when
    // bypassPermissions is set, since that already allows everything.
    public static async Task<ClaudeCliResult> InvokeAsync(string prompt, string workingDirectory, bool bypassPermissions = false, IReadOnlyList<string>? allowedTools = null, CancellationToken ct = default)
    {
        ClaudeUsageLimitException? lastLimitError = null;
        foreach (var account in ClaudeAccountPool.AllAccounts)
        {
            if (!ClaudeAccountPool.IsAvailable(account)) continue;

            try
            {
                return await InvokeOnceAsync(prompt, workingDirectory, account, bypassPermissions, allowedTools, ct);
            }
            catch (ClaudeUsageLimitException ex)
            {
                ClaudeAccountPool.MarkExhausted(account, ex.ResetAt);
                lastLimitError = ex;
                // Try the next configured account, if any - a single-account setup
                // (ClaudeAccountPool.AllAccounts == [null]) has nothing left to try
                // and falls through to the throw below on the very next loop check.
            }
        }

        // Every configured account is either on cooldown already or was just
        // exhausted by this call - surface the most recent real usage-limit error
        // (not a generic message) so existing callers' error handling still sees
        // something meaningful to log/report, and Temporal's own activity retry
        // policy gets a chance to succeed later once a cooldown lifts.
        throw (Exception?)lastLimitError ?? new InvalidOperationException("No Claude account is currently available (all configured accounts are on cooldown).");
    }

    private static async Task<ClaudeCliResult> InvokeOnceAsync(string prompt, string workingDirectory, string? configDir, bool bypassPermissions, IReadOnlyList<string>? allowedTools, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (configDir is not null) psi.Environment["CLAUDE_CONFIG_DIR"] = configDir;
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");
        if (bypassPermissions)
        {
            psi.ArgumentList.Add("--permission-mode");
            psi.ArgumentList.Add("bypassPermissions");
        }
        else if (allowedTools is { Count: > 0 })
        {
            psi.ArgumentList.Add("--allowedTools");
            foreach (var tool in allowedTools) psi.ArgumentList.Add(tool);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the claude CLI process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            if (IsUsageLimitError(stdout, stderr))
                throw new ClaudeUsageLimitException($"claude CLI usage limit reached: {stderr}", TryParseResetAt(stdout, stderr));
            throw new InvalidOperationException($"claude CLI exited {process.ExitCode}: {stderr}");
        }

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        if (root.TryGetProperty("is_error", out var isErrorProp) && isErrorProp.GetBoolean())
        {
            if (IsUsageLimitError(stdout, stderr))
                throw new ClaudeUsageLimitException($"claude CLI usage limit reached: {stdout}", TryParseResetAt(stdout, stderr));
            throw new InvalidOperationException($"claude CLI reported an error: {stdout}");
        }

        var text = root.GetProperty("result").GetString() ?? string.Empty;
        var cost = root.TryGetProperty("total_cost_usd", out var costProp) ? (decimal)costProp.GetDouble() : 0m;
        var usage = root.GetProperty("usage");
        var inputTokens = usage.GetProperty("input_tokens").GetInt32();
        var outputTokens = usage.GetProperty("output_tokens").GetInt32();
        var sessionId = root.TryGetProperty("session_id", out var sessionIdProp) ? sessionIdProp.GetString() : null;

        return new ClaudeCliResult(text, cost, inputTokens, outputTokens, sessionId);
    }

    // NOT YET VALIDATED against a real usage-limit failure from this CLI version -
    // no second account exists yet to trigger one (ClaudeAccountPool.cs's own
    // caveat). Broad, case-insensitive substring matching on the phrasing Anthropic's
    // products are known to use for this condition, deliberately erring toward
    // over-matching (a false positive just costs one wasted account-rotation attempt)
    // rather than under-matching (which would silently skip rotation and behave like
    // today - no worse than the status quo, but defeats the point of building this).
    // Revisit and tighten once a real failure's exact text is observed live.
    private static bool IsUsageLimitError(string stdout, string stderr)
    {
        var combined = $"{stdout}\n{stderr}";
        return combined.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase);
    }

    // Best-effort: Claude's own products have used a "resets at <unix-epoch-seconds>"
    // suffix on usage-limit messages - if that shape shows up here, use it; otherwise
    // ClaudeAccountPool falls back to its own default cooldown. Not load-bearing for
    // correctness either way, just a nicer estimate when available.
    private static DateTimeOffset? TryParseResetAt(string stdout, string stderr)
    {
        var match = Regex.Match($"{stdout}\n{stderr}", @"reset[s]?\s*(?:at)?[|:\s]+(\d{10,13})", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var raw = match.Groups[1].Value;
        return long.TryParse(raw, out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(raw.Length > 10 ? epoch / 1000 : epoch)
            : null;
    }

    // Agents are instructed to respond with pure JSON but occasionally wrap it in
    // markdown fences anyway - stripped defensively rather than failing the activity
    // over a formatting slip.
    public static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
    }

    // Found live: an occasional response prepends a sentence before the JSON despite
    // explicit "respond with ONLY a JSON object" instructions. Rather than failing the
    // whole activity over one stray sentence, extract the substring between the first
    // '{' and its matching last '}' and let the caller try parsing that instead.
    public static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}
