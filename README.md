# Forge

> AI-native Software Development Factory

Forge is an AI-native software engineering platform that turns a Kanban board into a working software factory. A task goes in as just a title; five Claude-driven agents plan it, write the code, deploy it and open the pull request, with a human approving at the gates that matter.

This isn't a prototype — Forge has been dogfooding itself: real tasks run end-to-end through the full pipeline (title in, real PR out) against both a sandbox repo and Forge's own codebase.

---

## Running Forge locally (Docker Compose)

The whole stack — Postgres, Temporal, Temporal UI, Forge API, Forge Worker, and the frontend — runs as 6 containers from a single `docker-compose.yml` at the repo root. No `dotnet`/`node` toolchain or manually-installed `git`/`gh`/`az`/`claude` CLIs needed on your machine; those are baked into the container images (the Worker's especially, since `Forge.Workflows` shells out to all four).

1. **Clone and configure:**
   ```bash
   git clone <this-repo-url> && cd Forge
   cp .env.example .env
   ```
   Edit `.env` — at minimum set `FORGE_JWT_SECRET` (e.g. `openssl rand -base64 48`). `GH_TOKEN` / `AZURE_DEVOPS_EXT_PAT` / `CLAUDE_CONFIG_DIR_HOST` are only needed for the Worker to actually run agent tasks (git/PR/Claude Code CLI credentials) — the board and API come up fine without them, but any task reaching the Developer/Deploy/Git stages will fail until they're set. See the comments in `.env.example` for what each variable needs and where to get it — `CLAUDE_CONFIG_DIR_HOST` in particular points at an *already logged-in* `claude` CLI config directory ([[ADR-0005]]: OAuth via your Claude.ai subscription, not an API key you can just generate).

2. **Bring it up:**
   ```bash
   docker compose up -d
   ```
   `forge-api` applies pending EF Core migrations on startup — no separate migration step. First run also builds the `forge-api`/`forge-worker`/`frontend` images, which takes a few minutes (the Worker image in particular, since it installs `gh`/`az`/`claude` at build time).

3. **Open the app:** the frontend is at `http://localhost:5173` by default (`FRONTEND_PORT` in `.env`). The Temporal UI (workflow inspection) is at `http://localhost:8233`.

4. **Add a project:** once logged in (first account bootstraps via the UI), create a Project pointing `LocalPath` at a checkout under `/data/repos/...` — that's the in-container path for whatever host directory you set as `FORGE_REPOS_DIR` (default `./data/repos`), mounted into `forge-worker`.

Full details — what each service does, how credentials are wired up, and the one still-open limitation around Forge restarting its own Worker after a self-hosted change — are in [`docs/015-Deployment.md`](docs/015-Deployment.md#6-containerized-bring-up-docker-compose).

---

## How it works

A `Task` moves through a 10-state board — `Inbox → Backlog → Blocked → Todo → Executing → AwaitingPublish → Publishing → Review → Done → Production` — driven by [Temporal](https://temporal.io) workflows, not polling or cron. Every transition is either a human moving a card, deterministic scheduler logic, or one of five agent roles producing a domain event. The full state machine, its events and its failure/rollback edges are specified in [`docs/003-Domain.md`](docs/003-Domain.md) and [`docs/004-Workflow.md`](docs/004-Workflow.md).

### The five agents

All five run against Claude through the [Claude Code CLI](docs/adr/ADR-0005-claude-code-cli-as-invocation-mechanism.md) (not the raw Anthropic API), each owning exactly one lifecycle transition:

- **Planner** — turns a title (plus optional seed notes/URLs) into a description and acceptance criteria, or raises genuine clarifying questions instead of guessing.
- **Prioritizer** — ranks a project's `Backlog` so the scheduler knows what to promote next.
- **Developer** — works inside a real Git worktree: reads the project, writes code, runs builds/tests, commits locally.
- **Deploy** — runs the project's `PublishRecipe` (migration, restart, health check) against the task's branch.
- **Git** — pushes the branch and opens a real pull request (`gh pr create` / `az repos pr create`), then cleans up the worktree.

Details, inputs/outputs and failure handling for each role: [`docs/005-Agents.md`](docs/005-Agents.md).

### Execution model

- **Temporal** is the workflow engine: task state, scheduling and long-running orchestration all live as real Temporal workflows/activities, not application-layer state machines — see [`docs/adr/ADR-0001-temporal-as-workflow-engine.md`](docs/adr/ADR-0001-temporal-as-workflow-engine.md).
- Each task that reaches `Executing` gets a **real, isolated Git worktree** — the Developer and Deploy agents operate on an actual checkout, not a simulated sandbox.
- A per-project `BacklogSchedulerWorkflow` polls for a free worker slot and promotes the top-priority `Backlog` task automatically.
- Once a task reaches `Done`, `TaskWorkflow` polls the PR it opened (`gh pr view` / `az repos pr show`) and advances the task to `Production` once it's merged — no webhook required.

---

## What's implemented today

- **Kanban board** — full read + the human-gated actions (answer questions, request publish, approve review, request rework), drag-and-drop, project sidebar/tree, cross-project view, task tags (`FORGE-42`), light/dark theme.
- **Live execution timeline** — every agent step (file read, command run, reasoning checkpoint) streams to the UI over **WebSocket**, backed by Postgres `LISTEN`/`NOTIFY`, with sub-second delivery.
- **Git providers** — **GitHub** (validated live: real pushes, real PRs) and **Azure DevOps** (`az repos pr create`, implemented and selectable per project) as swappable providers.
- **Authentication** — JWT bearer auth, admin-created accounts only (no public signup), a self-disabling bootstrap endpoint for the first account, WebSocket auth via token. See [`docs/014-Security.md`](docs/014-Security.md).
- **User management** — an Admin-only UI to list/create/edit users and roles, plus a change-password flow available to any logged-in user.
- **Cost tracking** — per-task and global rolled-up token usage/cost estimate from real `Run` rows, surfaced as a spend indicator in the UI (`GET /tasks/{id}/cost`, `GET /cost`).
- **Per-project shared memory** — a simple key/value store of conventions/decisions agents should know about a project, editable directly from the UI.
- **Self-hosted restart** — `scripts/restart-forge-dev.sh`, invoked by the Deploy agent's `restartTargets`, restarts Forge's own API/frontend processes (and can health-check the result) without touching the Worker process it runs inside of.
- **Trust gating** — `Project.AllowAgentBypassPermissions` (off by default) gates every agent operation that writes files or runs shell commands, so a new project can't execute arbitrary commands until explicitly marked trusted.

For the authoritative, evidence-based account of what's done vs. what's next, see [`docs/016-Roadmap.md`](docs/016-Roadmap.md) — it's updated from what's actually been run live, not from what's planned.

---

## Stack

- **Backend**: .NET 10 (`Forge.Api`, `Forge.Workflows`, `Forge.Worker`), Temporal, PostgreSQL.
- **Frontend**: React 19, Vite, TypeScript, Tailwind CSS.
- **Agents**: Claude via the Claude Code CLI.
- **Infrastructure (current)**: Docker Compose on a single Linux machine (`docker/local/`) — dedicated infrastructure is a planned v2 step, not yet built ([`docs/adr/ADR-0004-dedicated-infrastructure.md`](docs/adr/ADR-0004-dedicated-infrastructure.md)).

---

## Core Principles

- AI-first architecture
- Event-driven workflows
- Autonomous software agents
- Human approval where it matters
- Git-native
- Linux-first
- Multi-model AI (single provider today, router designed for more — see [`docs/008-ModelRouter.md`](docs/008-ModelRouter.md))
- Plugin-based architecture
- Transparent execution
- Complete audit trail

---

## Documentation

The project documentation lives under [`/docs`](docs/) and is the source of truth for anything not covered above — this README is an entry point, not a replacement for it.

- [000 — Vision](docs/000-Vision.md)
- [001 — Requirements](docs/001-Requirements.md)
- [002 — Architecture](docs/002-Architecture.md)
- [003 — Domain Model](docs/003-Domain.md)
- [004 — Workflow](docs/004-Workflow.md)
- [005 — Agents](docs/005-Agents.md)
- [006 — Scheduler](docs/006-Scheduler.md)
- [007 — Execution Engine](docs/007-ExecutionEngine.md)
- [008 — Model Router](docs/008-ModelRouter.md)
- [009 — MCP Integration](docs/009-MCP.md)
- [010 — Plugin System](docs/010-Plugins.md)
- [011 — Database](docs/011-Database.md)
- [012 — API](docs/012-API.md)
- [013 — Frontend](docs/013-Frontend.md)
- [014 — Security](docs/014-Security.md)
- [015 — Deployment](docs/015-Deployment.md)
- [016 — Roadmap](docs/016-Roadmap.md)
- [Architecture Decision Records](docs/adr/)
- [RFCs](docs/rfcs/)

---

## Status

The MVP described in [`docs/016-Roadmap.md`](docs/016-Roadmap.md) is substantially complete: all five agents, the full state machine, both Git providers, auth, user management, cost tracking and the self-hosted restart path have been validated live, end-to-end. What's left is mostly hardening (finer-grained trust scoping, broader `Event.Actor` attribution, Azure DevOps exercised against a real org) and infrastructure work (moving off the founder's local machine onto dedicated infrastructure) rather than missing features — see the roadmap doc for the current, evidence-based punch list.
