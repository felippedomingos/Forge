using System.Collections.Concurrent;
using Forge.Domain.Entities;
using Forge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Forge.Workflows;

public record ClaudeAccountRef(Guid Id, string Name, string Token);

// Founder-requested (2026-08-08): "o recurso de poder cadastrar multiplas contas do
// claude e ele reautenticar automaticamente para nunca parar o trabalho esta se
// tornando cada vez mais necessario." docs/adr/ADR-0005 has the full history,
// including an earlier CLAUDE_CONFIG_DIR/directory-based design in this same session
// that the founder rejected ("nao faz sentido na minha cabeca") in favor of this one:
// each account is a `ClaudeAccount` row (Domain entity) holding a long-lived token
// from `claude setup-token` - a real Claude Code CLI subcommand ("Set up a long-lived
// authentication token, requires Claude subscription"), consumed via the
// CLAUDE_CODE_OAUTH_TOKEN env var (confirmed present in the installed CLI binary's own
// strings, not guessed). Same UX shape as Project.GitCredential: paste a token, no
// directory/session management.
//
// KNOWN LIMITATION: cooldown state is in-memory, per-Worker-process - lost on
// restart, and would need to move to Postgres if this ever ran as more than one
// Worker process (not the case today, docs/007-ExecutionEngine.md §1 - one Worker).
public static class ClaudeAccountPool
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("FORGE_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev";

    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> CooldownUntil = new();

    // Anthropic's usage-limit windows are commonly a rolling multi-hour period - this
    // is a conservative "try again soon" fallback for when the CLI's own message
    // doesn't include a parseable reset time (see ClaudeCliProvider.TryParseResetAt),
    // not a claimed real value.
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromHours(1);

    // Queried fresh every call rather than cached - infrequent enough (once per agent
    // invocation, not a hot path) that a cache would add complexity for no real
    // benefit, and always reflects the latest founder-added/removed/toggled account
    // immediately, with no stale-cache window to reason about.
    public static async Task<IReadOnlyList<ClaudeAccountRef>> GetActiveAccountsAsync()
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ForgeDbContext(options);
        return await db.ClaudeAccounts
            .Where(a => a.IsActive)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ClaudeAccountRef(a.Id, a.Name, a.Token))
            .ToListAsync();
    }

    public static bool IsAvailable(Guid accountId) =>
        !CooldownUntil.TryGetValue(accountId, out var until) || DateTimeOffset.UtcNow >= until;

    public static void MarkExhausted(Guid accountId, DateTimeOffset? resetAt = null) =>
        CooldownUntil[accountId] = resetAt ?? DateTimeOffset.UtcNow + DefaultCooldown;
}
