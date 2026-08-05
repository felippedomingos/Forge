# 013 — Frontend

## Status

Draft — Phase 4 (Implementation), visual design pass complete

## 1. Stack

Vite + React + TypeScript + Tailwind v4 (via the `@tailwindcss/vite` plugin) + TanStack Query for server state + **shadcn/ui (Radix primitives) + lucide-react icons** for the component layer + **@dnd-kit** for drag-and-drop. No client-side router yet — a single-page Kanban board is the entire UI at this stage.

## 2. Design System (founder-directed)

Chosen via direct founder Q&A, not assumed:

- **Aesthetic**: Notion-inspired — warm neutrals, generous spacing, friendly rather than clinical. Originally shipped dark-first; flipped to **light-first** after founder feedback that dark read as too dark for daily use (§3's toggle). Both themes are fully defined in `index.css` — this was a default change, not a removal.
- **Accent color**: amber/orange (`oklch` hue ~55-60°), chosen to fit "Forge" (forge, fire, hot metal) rather than the indigo/violet nearly every dev tool defaults to.
- **Warm neutrals, not cold gray**: every neutral token (background/card/border/muted) carries a slight warm hue (~55-70° in OKLCH) instead of shadcn's default 0-chroma gray — this is what actually reads as "Notion-like" rather than "generic dark mode."
- **Components**: shadcn/ui's Radix-based primitives (Button, Input, Select, Dialog, Sheet, Badge, Card, Skeleton, Separator, Label, Sonner/Toast) — accessible by default, source lives in `src/components/ui/` (not an npm dependency), fully restyled via the theme tokens above rather than shadcn's stock look.

**Known shadcn CLI quirk hit during setup**: `npx shadcn add` initially wrote every component into a literal `./@/` directory instead of resolving the `@/*` path alias to `src/`, because the alias wasn't declared in the root `tsconfig.json` (only in `tsconfig.app.json`). Fixed by adding the `paths` mapping to root `tsconfig.json` too; moved the misplaced files into `src/` by hand. Worth knowing if a future `shadcn add` run ever silently creates a `@/` folder again — check there first.

## 3. What's Built

- **Board**: all 10 states from [[003-Domain]] §3, laid out as an even CSS grid (`grid-cols-10`) so **all columns fit the viewport with no horizontal scroll** — founder feedback against the earlier flex-scroll layout. Each column scrolls vertically on its own (thin themed scrollbar, `.board-scroll` utility in `index.css`) if its card list grows past the column height.
- **Drag-and-drop** (founder-requested): cards are draggable only from the 3 states with a real human-gated forward transition (`Backlog`, `AwaitingPublish`, `Review` — matching [[003-Domain]] §3 exactly); dropping is only accepted on that task's one valid target column (`Todo`, `Publishing`, `Done` respectively). Dropping anywhere else shows a toast ("Can't move a task directly from X to Y") and the card stays put — domain correctness (INV-3) enforced in the interaction itself, not just the backend. Columns visually highlight as a valid/invalid target while a drag is in progress.
- **Task creation**: a `Dialog` (not an inline form) — project select + title input, `POST /tasks` per [[000-Vision]] UC-3.
- **Task detail panel**: a `Sheet` sliding from the right — description, acceptance-criteria checklist, live event timeline, per-run cost. Shows `PlannerStarted → PlannerInvokingModel → PlannerCompleted` in near-real-time during a real (multi-second) Claude call.
- **Column hints** (founder-requested): a short, always-visible, amber-highlighted line above every column header explaining what happens in/to a task there (e.g. Awaiting Publish → "Drag/click Publish when ready") — makes the board self-explanatory without hovering or reading docs.
- **Project sidebar** (founder-requested): a left-hand nav replacing the old header dropdown — an "All tasks" entry (cross-project view, [[000-Vision]] UC-1) plus one row per Project, each showing its live task count and an edit (pencil) action revealed on hover.
- **Project edit dialog**: opened from the sidebar's pencil icon — `name`/`repositoryUrl`/`rootBranch`/`localPath` (`PATCH /projects/{id}`, [[012-API]]), a **Preview URL** field (see below), plus a **shared memory** editor (list existing `AgentMemory` entries with delete, add a new key/value pair) — the same memory the Planner/Developer prompts actually read ([[005-Agents]] §7). `prefix` is shown as a badge but not editable here (immutable once tasks reference it).
- **Task tags** (founder-requested): every card and the task detail panel render `{Project.prefix}-{Task.number}` (e.g. `FORGE-42`) so a task can be referenced in conversation without pasting a raw GUID.
- **"Testar" button** (founder-requested): on a `Review`-stage task, if the project's `PublishRecipe.previewUrl` is set ([[015-Deployment]] §2), the detail panel shows a "Testar" button next to "Approve → Done" that opens it in a new tab — one click from "task is ready for human review" to "see it running," instead of hunting down the URL manually. Absent if no `previewUrl` is configured.
- **Light/dark toggle** (`useTheme.ts`): a button at the bottom of the sidebar switches themes and persists the choice in `localStorage` (`forge-theme`); an inline script in `index.html` applies it before React mounts to avoid a flash on load. **Light is now the default** — founder feedback that the original dark-first choice ([[013-Frontend]] §2, as originally written) read as too dark day-to-day. Dark remains fully defined and one click away, not removed.
- **Real-time via WebSocket** (`useTaskWebSocket`, [[007-ExecutionEngine]] §4): the task detail panel connects to `/ws/tasks/{id}` and refetches the instant a "refresh" push arrives — validated live with sub-second delivery. A 10s poll (`refetchInterval`) remains only as a fallback for a dropped socket, not the primary mechanism anymore. The board's own task list still polls every 2s and does **not** yet have a WebSocket (would need one connection per visible task or a board-wide channel that doesn't exist — see [[007-ExecutionEngine]] §6).
- **Toasts** (Sonner) for action feedback (task created, promoted, published, approved, drag rejected) instead of silent state changes.

## 4. What's Explicitly Not Built Yet

- **Navigation / other views**: Execution view, Logs, Metrics, Models, Workers, Settings — none exist. (Projects now has a sidebar + edit dialog, per §3 above.)
- **Project creation from the UI** — `POST /projects` has no dialog counterpart yet; new projects are still created directly against the API.
- **Diff/commits view** in the task detail panel — meaningless until there's a real diff to show beyond the commit message already in the timeline.
- **Board-wide real-time**: only the (single, currently-open) task detail panel has a WebSocket; the board's cross-task list still polls every 2s.
- **Board views beyond Kanban** (list, timeline, tree) — post-v1 per [[000-Vision]] §7.

## 5. Design Notes

- Font sizes throughout the board are deliberately small (`text-xs`/`text-[11px]`/`text-[10px]`) per founder feedback — "mais harmônico" (more harmonious) after an initial pass felt too large for a 10-column dense board. The task detail panel and dialogs stay slightly larger since they're focused, occasional-use surfaces, not part of the board's density.
- The Vite dev server proxies `/api` to `http://localhost:5080` ([[012-API]]) so no absolute URL is hardcoded anywhere in the frontend.

## 6. Open Questions

- The task detail panel is a client-side overlay (no router, no real route/URL) — fine for now, revisit if deep-linking to a specific task becomes a real need.
- Whether polling should stay as a fallback even after WebSocket exists (e.g. for clients that lose the socket) — reasonable, not designed yet.
- Drag-and-drop keyboard accessibility (dnd-kit supports it; not explicitly tested here) — worth a pass once real users beyond the founder exist.
