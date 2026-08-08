using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Forge.Domain.Entities;

// Founder-requested (2026-08-08): per-user Claude accounts for automatic multi-account
// failover, with real usage tracking per account - see docs/adr/ADR-0005 for the full
// history. Deliberately NOT modeled around CLAUDE_CONFIG_DIR/OAuth session state
// (an earlier design in this same session, reverted) - the founder pushed back on
// that mental model ("nao faz sentido na minha cabeca"). Instead: `claude setup-token`
// (a real Claude Code CLI subcommand, "Set up a long-lived authentication token,
// requires Claude subscription") produces a single stable token string, consumed via
// the CLAUDE_CODE_OAUTH_TOKEN env var (confirmed present in the installed CLI binary's
// own strings - not guessed). This is exactly the same UX shape as Project.GitCredential
// (paste a token, no directory/session management) rather than a second, different
// credential model.
public class ClaudeAccount
{
    public Guid Id { get; set; }
    // Friendly label shown in the UI ("Felippe - conta 2") - not the Anthropic
    // account's own email/identity, which Forge never learns (the token doesn't
    // carry it in any form Forge parses).
    public required string Name { get; set; }
    public Guid UserId { get; set; }
    // Plaintext in Postgres - same security posture as Project.GitCredential (no
    // encryption-at-rest infra exists yet, a deliberate scope decision). [JsonIgnore]
    // is a defense-in-depth safety net (every ClaudeAccount endpoint today
    // deliberately projects to an anonymous object that omits this field, so it's
    // never actually at risk, but a future endpoint returning the raw entity would
    // be) - NOT `required`: System.Text.Json can't reconcile a required property with
    // one it's told to ignore (a real startup crash, found live), and EF Core
    // materializes this from the database via the property setter regardless of the
    // `required` keyword, so nothing is lost by dropping it. The one construction
    // site (POST /users/{id}/claude-accounts) always sets it explicitly.
    [JsonIgnore]
    public string Token { get; set; } = "";
    // Toggle without deleting - e.g. temporarily pull an account out of rotation
    // without losing its usage history (ClaudeAccountId on Run rows would otherwise
    // dangle or need cascading).
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
    public ICollection<Run> Runs { get; set; } = [];
}
