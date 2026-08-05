using System.Diagnostics;
using System.Text.Json;

namespace Forge.Workflows;

public record ClaudeCliResult(string Text, decimal CostUsd, int InputTokens, int OutputTokens);

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
    public static async Task<ClaudeCliResult> InvokeAsync(string prompt, string workingDirectory, bool bypassPermissions = false, CancellationToken ct = default)
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

        return new ClaudeCliResult(text, cost, inputTokens, outputTokens);
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
