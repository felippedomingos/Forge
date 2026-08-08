using System.Collections.Concurrent;

namespace Forge.Workflows;

// Founder-requested (2026-08-08): "o recurso de poder cadastrar multiplas contas do
// claude e ele reautenticar automaticamente para nunca parar o trabalho esta se
// tornando cada vez mais necessario." docs/adr/ADR-0005-claude-code-cli-as-invocation-
// mechanism.md already designed for this (§ "Multi-account fallback is designed for,
// not built yet") but never built it, since it was blocked on the founder actually
// completing N interactive OAuth logins - that's still true, this only builds the
// rotation mechanism itself, ready for whenever more than one account exists.
//
// Each "account" is just a CLAUDE_CONFIG_DIR path (ADR-0005 confirmed this env var
// isolates credentials/config per directory, independent of $HOME) that the founder
// has already logged into by hand - Forge cannot create one itself, only use it.
// Configured via CLAUDE_ACCOUNT_CONFIG_DIRS (colon-separated, matching $PATH's own
// convention on this platform). Unset/empty means exactly today's single-account
// behavior: one account, represented as `null`, meaning "don't override
// CLAUDE_CONFIG_DIR at all - inherit whatever the process already has."
//
// KNOWN LIMITATION: cooldown state is in-memory, per-Worker-process - lost on
// restart, and would need to move to Postgres if this ever runs as more than one
// Worker process (not the case today, docs/007-ExecutionEngine.md §1 - one Worker).
// A false "still exhausted" retry right after a restart is harmless: it fails fast
// (Claude's own CLI rejects the request near-instantly on a real usage-limit
// account) and moves to the next configured account exactly as it would mid-run.
public static class ClaudeAccountPool
{
    private static readonly string?[] SingleDefaultAccount = [null];

    private static readonly Lazy<IReadOnlyList<string?>> Accounts = new(() =>
    {
        var raw = Environment.GetEnvironmentVariable("CLAUDE_ACCOUNT_CONFIG_DIRS");
        if (string.IsNullOrWhiteSpace(raw)) return SingleDefaultAccount;
        var dirs = raw.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return dirs.Length == 0 ? SingleDefaultAccount : dirs;
    });

    private static readonly ConcurrentDictionary<string, DateTimeOffset> CooldownUntil = new();

    // Anthropic's usage-limit windows are commonly a rolling multi-hour period - this
    // is a conservative "try again soon" fallback for when the CLI's own message
    // doesn't include a parseable reset time (see ClaudeCliProvider.TryParseResetAt),
    // not a claimed real value. Revisit once a real usage-limit failure's exact
    // message format has actually been observed against this CLI version.
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromHours(1);

    public static IReadOnlyList<string?> AllAccounts => Accounts.Value;

    public static bool IsAvailable(string? account) =>
        account is null || !CooldownUntil.TryGetValue(account, out var until) || DateTimeOffset.UtcNow >= until;

    // `account: null` (the implicit single-account default) is deliberately never
    // marked exhausted - there's nothing to rotate away TO, so recording a cooldown
    // would only make every subsequent call in this Worker's lifetime fail
    // immediately instead of letting Temporal's existing activity retry policy decide
    // whether to try again. Multi-account setups don't have this problem: every real
    // account is an explicit path, always cooldown-tracked.
    public static void MarkExhausted(string? account, DateTimeOffset? resetAt = null)
    {
        if (account is null) return;
        CooldownUntil[account] = resetAt ?? DateTimeOffset.UtcNow + DefaultCooldown;
    }
}
