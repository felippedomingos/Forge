using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forge.Workflows;

public record TranscriptContentBlock(
    string Type,
    string? Text,
    string? ToolName,
    JsonElement? ToolInput,
    string? ToolResultText,
    bool? IsError);

public record TranscriptMessage(string Role, DateTimeOffset? Timestamp, List<TranscriptContentBlock> Content);

// Reads the Claude Code CLI's own session transcript (JSONL), the same file the
// interactive CLI itself keeps for "resume this session" - there is no separate
// transcript format Forge controls. See docs/012-API.md's new
// GET /tasks/{id}/runs/{runId}/session for why this exists.
public static class ClaudeTranscriptReader
{
    // The CLI writes transcripts under $CLAUDE_CONFIG_DIR/projects (default ~/.claude),
    // same as this Forge host's own interactive sessions - confirmed live by inspecting
    // an actual `claude -p --output-format json` invocation's session_id against the
    // file it produced.
    private static string ConfigDir =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    // The CLI derives a project directory name from the working directory by replacing
    // every non-alphanumeric character with '-' (confirmed against real ~/.claude/projects
    // directory names, e.g. "/home/felippe/.foo" -> "-home-felippe--foo").
    public static string ComputeTranscriptPath(string sessionId, string workingDirectory)
    {
        var sanitized = Regex.Replace(workingDirectory, "[^a-zA-Z0-9]", "-");
        return Path.Combine(ConfigDir, "projects", sanitized, $"{sessionId}.jsonl");
    }

    // Returns null if the file doesn't exist (e.g. the CLI's own retention pruned it,
    // or it never existed) - callers surface that as an empty state, not an error.
    public static async Task<List<TranscriptMessage>?> ReadAsync(string transcriptPath, CancellationToken ct = default)
    {
        if (!File.Exists(transcriptPath)) return null;

        var messages = new List<TranscriptMessage>();
        foreach (var line in await File.ReadAllLinesAsync(transcriptPath, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }
            using (doc)
            {
                var root = doc.RootElement;
                // Non-message lines (queue-operation, attachment deltas, etc.) carry no
                // "message" property - only user/assistant turns do.
                if (!root.TryGetProperty("message", out var messageEl)) continue;
                if (!messageEl.TryGetProperty("role", out var roleEl)) continue;

                var role = roleEl.GetString() ?? "unknown";
                DateTimeOffset? timestamp = root.TryGetProperty("timestamp", out var tsEl)
                    && tsEl.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(tsEl.GetString(), out var ts)
                        ? ts
                        : null;

                var blocks = ParseContent(messageEl);
                if (blocks.Count > 0)
                    messages.Add(new TranscriptMessage(role, timestamp, blocks));
            }
        }

        return messages;
    }

    private static List<TranscriptContentBlock> ParseContent(JsonElement messageEl)
    {
        var blocks = new List<TranscriptContentBlock>();
        if (!messageEl.TryGetProperty("content", out var contentEl)) return blocks;

        if (contentEl.ValueKind == JsonValueKind.String)
        {
            blocks.Add(new TranscriptContentBlock("text", contentEl.GetString(), null, null, null, null));
            return blocks;
        }

        if (contentEl.ValueKind != JsonValueKind.Array) return blocks;

        foreach (var block in contentEl.EnumerateArray())
        {
            var type = block.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "unknown" : "unknown";
            switch (type)
            {
                case "text":
                    blocks.Add(new TranscriptContentBlock("text",
                        block.TryGetProperty("text", out var textEl) ? textEl.GetString() : null,
                        null, null, null, null));
                    break;
                case "tool_use":
                    blocks.Add(new TranscriptContentBlock("tool_use", null,
                        block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null,
                        block.TryGetProperty("input", out var inputEl) ? inputEl.Clone() : null,
                        null, null));
                    break;
                case "tool_result":
                    blocks.Add(new TranscriptContentBlock("tool_result", null, null, null,
                        ExtractToolResultText(block),
                        block.TryGetProperty("is_error", out var isErrorEl) && isErrorEl.ValueKind == JsonValueKind.True));
                    break;
                default:
                    blocks.Add(new TranscriptContentBlock(type, null, null, null, null, null));
                    break;
            }
        }

        return blocks;
    }

    private static string? ExtractToolResultText(JsonElement toolResultBlock)
    {
        if (!toolResultBlock.TryGetProperty("content", out var content)) return null;
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind == JsonValueKind.Array)
        {
            var texts = content.EnumerateArray()
                .Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(c => c.TryGetProperty("text", out var t) ? t.GetString() : null)
                .Where(s => s is not null);
            return string.Join("\n", texts);
        }
        return content.GetRawText();
    }
}
