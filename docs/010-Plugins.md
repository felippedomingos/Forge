# 010 — Plugin System

## Status

Draft — Phase 3 (Architecture)

## 1. Plugin vs. MCP Server — the Actual Boundary

[[009-MCP]] §1 already draws this line; restated here from the Plugin side for clarity: a **Plugin** is a concrete provider behind a capability Forge's domain model treats as swappable — "the Git provider," "the deployment target" — represented by the `Plugin` entity in [[003-Domain]]/[[011-Database]] (`kind`: `git_provider` / `issue_tracker` / `cloud_cli` / `database` / `deployment_target`). An **MCP server** is how an agent actually invokes that provider's operations at runtime. A Plugin typically wraps one or more MCP servers plus whatever configuration/credentials binds it to a specific Project.

## 2. Plugin Interface Contract

```
interface ForgePlugin {
  string Name;
  PluginKind Kind;                       // matches Plugin.kind (011-Database)
  string Version;

  Task<bool> ValidateConfiguration(JsonObject config);
  IReadOnlyList<string> RequiredMcpServers();
  Task Initialize(JsonObject config, CancellationToken ct);
}

interface IGitProviderPlugin : ForgePlugin {
  Task SyncRootBranch(Project project, CancellationToken ct);
  Task<Worktree> CreateWorktree(Task task, CancellationToken ct);
  Task Push(Worktree worktree, CancellationToken ct);
  Task<PullRequestRef> CreatePullRequest(Worktree worktree, string title, string body, CancellationToken ct);
}
```

Only `IGitProviderPlugin` is specified concretely here because it's the only kind with a real implementation at v1 ([[ADR-0002]]). `IIssueTrackerPlugin`, `IDeploymentTargetPlugin`, etc. get their own interface once a second real plugin forces the actual shape to be discovered rather than guessed — inventing method signatures for capabilities nothing calls yet would be exactly the speculative design [[000-Vision]]'s engineering priorities warn against.

## 3. Built-in Plugins at v1

- **GitHub** (`git_provider` + `issue_tracker`) — the only Git/issue-tracker plugin implemented at v1, per [[ADR-0002]]. Backs the Developer agent's worktree/branch operations and the Git agent's push/PR ([[005-Agents]] §4, §6).
- **PostgreSQL** (`database`) — Forge's own persistence ([[011-Database]]); not a per-project plugin at v1 since Deploy-agent-driven project database migrations aren't concretely specified yet (flagged as the `PublishRecipe` gap in [[005-Agents]] §5, owned by [[015-Deployment]]).

Azure DevOps (`git_provider` + `issue_tracker`) is the next plugin, deliberately deferred rather than built alongside GitHub — see §5.

## 4. Discovery and Configuration

A Project's `git_provider_plugin_id` ([[011-Database]]) points at a row in `plugins`, whose `configuration` JSONB column holds whatever that plugin instance needs (repository owner/name, auth reference — never a raw credential in this column, see [[014-Security]]). Plugin *code* (the `ForgePlugin` implementation) is registered at application startup, not hot-loaded from disk at v1 — dynamic plugin loading is a reasonable future direction once there's a real ecosystem of third-party plugins to load, not before.

## 5. Versioning and the Azure DevOps Acceptance Test

Per [[ADR-0002]]'s stated consequence: the plugin interface above is only proven against one real implementation (GitHub) until Azure DevOps exists. Concretely, implementing `IGitProviderPlugin` for Azure DevOps should be treated as a design review of this interface, not just "another integration" — if the Azure DevOps plugin needs a method GitHub's implementation never used, or if `CreateWorktree`/`CreatePullRequest` turn out to encode GitHub-specific assumptions, that's the interface failing its own acceptance test and this document needs to be revised, not worked around inside the Azure DevOps plugin.

`Plugin.version` exists so a Project can pin a specific plugin version; no compatibility-range/semver policy is defined yet since there's only one version of one plugin in existence.
