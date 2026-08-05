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

**The Azure DevOps acceptance test's actual finding (§5)**: this interface was never built as real code for GitHub either — `GitFinalizeAsync` calls `GitOps.RunGhAsync`/`git` inline, no `IGitProviderPlugin` class exists anywhere in the codebase. So when Azure DevOps support was added, it followed that same real (procedural) shape rather than retrofitting this aspirational interface: `GitFinalizeAsync` branches on `Project.GitProviderPlugin.Name` and calls `GitOps.RunAzAsync` (`az repos pr create`) instead of `RunGhAsync` when it's `"azure-devops"`. The interface above remains unbuilt, not disproven - there's still only ever been inline provider-specific code, never an implementation of it to review.

## 3. Built-in Plugins at v1

- **GitHub** (`git_provider`) — the first Git provider, per [[ADR-0002]]. Backs the Developer agent's worktree/branch operations and the Git agent's push/PR ([[005-Agents]] §4, §6). **Validated live** end-to-end (real pushes, real PRs opened).
- **Azure DevOps** (`git_provider`) — founder-requested; **implemented, not yet validated against a real org/repo**. `GitFinalizeAsync` calls `az repos pr create` (via a new `GitOps.RunAzAsync`), with `--organization`/`--project`/`--repository` parsed directly from the Project's own `RepositoryUrl` (`GitOps.TryParseAzureRepo`, handling both the HTTPS and SSH URL shapes Azure Repos issues) rather than relying on this machine's `az devops configure -d` default — that default is correct for exactly one Azure DevOps project and silently wrong the moment a second one exists. Requires `az login` + the `azure-devops` CLI extension, already present on this machine. Selectable today from the project create/edit dialogs' git-provider picker ([[013-Frontend]]) via `GET /plugins`.
- **PostgreSQL** (`database`) — Forge's own persistence ([[011-Database]]); not a per-project plugin at v1 since Deploy-agent-driven project database migrations aren't concretely specified yet (flagged as the `PublishRecipe` gap in [[005-Agents]] §5, owned by [[015-Deployment]]).

Issue-tracker support for either provider remains unbuilt (§5's Azure DevOps acceptance test was about `git_provider` push/PR specifically).

## 4. Discovery and Configuration

A Project's `git_provider_plugin_id` ([[011-Database]]) points at a row in `plugins`, whose `configuration` JSONB column holds whatever that plugin instance needs (repository owner/name, auth reference — never a raw credential in this column, see [[014-Security]]). Plugin *code* (the `ForgePlugin` implementation) is registered at application startup, not hot-loaded from disk at v1 — dynamic plugin loading is a reasonable future direction once there's a real ecosystem of third-party plugins to load, not before.

## 5. Versioning and the Azure DevOps Acceptance Test — Resolved, With a Caveat

Per [[ADR-0002]]'s stated consequence, this was meant to be the moment the `IGitProviderPlugin` interface (§2) got proven against a second real implementation. **The actual finding**: there was nothing to prove it against, because GitHub was never built as a real `IGitProviderPlugin` either (§2's note) — both providers are inline procedural code (`GitOps` + branching on `Plugin.Name` in `GitFinalizeAsync`), not implementations of that interface. So Azure DevOps didn't fail the interface's acceptance test; it revealed the test was never actually runnable, since the thing being tested doesn't exist in code. The interface in §2 stays exactly as speculative as it was before this — worth revisiting if a third provider or a real refactor ever forces the abstraction to be built for real.

**Validation status**: Azure DevOps push+PR is implemented against `az repos pr create`'s documented argument shape (confirmed via `az repos pr create --help` on this machine, which also has `az login` + the `azure-devops` extension already configured against a real Azure tenant) but has **not been exercised against a real PR** — doing so would mean opening a real pull request against real Azure DevOps infrastructure, which needs the founder's explicit go-ahead, not something to do unprompted while building the feature. Treat this path as "correctly built, not yet proven live," the same honest bar [[015-Deployment]] applies elsewhere.

`Plugin.version` exists so a Project can pin a specific plugin version; no compatibility-range/semver policy is defined yet since there's only one version of each plugin in existence.
