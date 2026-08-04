# ADR-0002: GitHub as the First Git/Issue-Tracker Plugin

## Status

Accepted

## Context

Forge needs at least one working Git provider plugin ([[010-Plugins]]) to validate the full Developer → Git agent pipeline end-to-end: worktree creation, branch, commit, push, PR.

Two real candidates exist:

- **GitHub** — where Forge itself is hosted. No VPN dependency, no corporate credential setup required to start testing.
- **Azure DevOps** — the provider actually used in production by Actiz. Requires VPN access and corporate credentials from day one.

## Decision

GitHub is the first Git provider and PR-creation plugin implemented. Azure DevOps support is planned (Forge's own conversation history assumes it — see original scope, item 3: "as vezes acessar... abrir o devops, chamar o azure cli") but deferred past MVP.

## Consequences

- The end-to-end pipeline (sync root branch → worktree → branch → commit → push → PR) can be validated against Forge's own repository with zero additional credential or VPN setup.
- Faster path to a working demo of the full lifecycle described in [[000-Vision]] §9.
- The plugin interface in [[010-Plugins]] is only proven against one real provider until Azure DevOps is implemented — there is a real risk of GitHub-specific assumptions leaking into what's supposed to be a generic interface. The Azure DevOps plugin implementation should be treated as the acceptance test for that interface, not an afterthought.
- Azure DevOps-specific concerns relevant to Actiz's own workflow (work item linking, branch policies, PR build validation) are not exercised until that second plugin exists.
