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
    public static async Task<ClaudeCliResult> InvokeAsync(string prompt, string workingDirectory, CancellationToken ct = default)
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
}
