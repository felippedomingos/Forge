# Forge

> AI-native Software Development Factory

Forge is an AI-native software engineering platform that transforms a Kanban board into a fully autonomous software factory.

Instead of treating AI as a coding assistant, Forge orchestrates specialized agents that understand requirements, plan work, write code, execute tests, deploy applications and manage the entire software development lifecycle.

Developers remain in control while autonomous agents execute repetitive and time-consuming work.

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

## Vision

Software development is evolving from manually writing code to supervising autonomous engineering teams.

Forge provides the orchestration layer that coordinates these AI workers.

---

## Core Principles

- AI-first architecture
- Event-driven workflows
- Autonomous software agents
- Human approval where it matters
- Git-native
- Linux-first
- Multi-model AI
- Plugin-based architecture
- Transparent execution
- Complete audit trail

---

## Planned Features

- Kanban board
- Autonomous planning agent
- Coding agent
- Deployment agent
- Review agent
- Git automation
- Worktree management
- Multi-LLM routing
- MCP integration
- Azure DevOps integration
- GitHub integration
- Docker support
- Local execution
- Live execution timeline
- Prompt history
- Cost tracking
- Plugin SDK

---

## Project Status

⚠️ Early architecture phase.

The repository currently contains specifications and architectural documents.

Implementation will begin after the architecture is finalized.

---

## Documentation

The project documentation is available under [`/docs`](docs/), following a phased specification process — architecture is settled before implementation begins.

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

## Roadmap

- [ ] Product Vision
- [ ] Requirements
- [ ] Domain Model
- [ ] Architecture
- [ ] Workflow Engine
- [ ] Scheduler
- [ ] Backend
- [ ] Frontend
- [ ] Worker Runtime
- [ ] Plugin SDK
- [ ] v1.0
