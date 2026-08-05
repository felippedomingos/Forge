using System.Diagnostics;
using System.Text.Json;

namespace Forge.Workflows;

public record ClaudeCliResult(string Text, decimal CostUsd, int InputTokens, int OutputTokens, string? SessionId);

// docs/008-ModelRouter.md §1, docs/adr/ADR-0005-claude-code-cli-as-invocation-mechanism.md
// - the one Provider implementation at v1. Wraps the Claude Code CLI as a subprocess,
// authenticated via whatever account is logged into the default CLAUDE_CONFIG_DIR on
// this machine. No multi-account fallback yet - ADR-0005 explicitly defers that until
// the founder has logged in more than one account (an interactive step only a human
// can complete).
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
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
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
            throw new InvalidOperationException($"claude CLI exited {process.ExitCode}: {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        if (root.TryGetProperty("is_error", out var isErrorProp) && isErrorProp.GetBoolean())
            throw new InvalidOperationException($"claude CLI reported an error: {stdout}");

        var text = root.GetProperty("result").GetString() ?? string.Empty;
        var cost = root.TryGetProperty("total_cost_usd", out var costProp) ? (decimal)costProp.GetDouble() : 0m;
        var usage = root.GetProperty("usage");
        var inputTokens = usage.GetProperty("input_tokens").GetInt32();
        var outputTokens = usage.GetProperty("output_tokens").GetInt32();
        var sessionId = root.TryGetProperty("session_id", out var sessionIdProp) ? sessionIdProp.GetString() : null;

        return new ClaudeCliResult(text, cost, inputTokens, outputTokens, sessionId);
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
