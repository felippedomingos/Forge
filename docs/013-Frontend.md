# 013 — Frontend

## Status

Draft — Phase 4 (Implementation), visual design pass complete

## 1. Stack

Vite + React + TypeScript + Tailwind v4 (via the `@tailwindcss/vite` plugin) + TanStack Query for server state + **shadcn/ui (Radix primitives) + lucide-react icons** for the component layer + **@dnd-kit** for drag-and-drop. No client-side router yet — a single-page Kanban board is the entire UI at this stage.

## 2. Design System (founder-directed)

Chosen via direct founder Q&A, not assumed:

- **Aesthetic**: Notion-inspired — warm neutrals, generous spacing, friendly rather than clinical — but **dark-first** (the founder's own combination, not a stock preset). `index.html` sets `class="dark"` by default; light mode is still fully defined in `index.css`, not a half-finished fallback, since a toggle is a reasonable near-future add.
- **Accent color**: amber/orange (`oklch` hue ~55-60°), chosen to fit "Forge" (forge, fire, hot metal) rather than the indigo/violet nearly every dev tool defaults to.
- **Warm neutrals, not cold gray**: every neutral token (background/card/border/muted) carries a slight warm hue (~55-70° in OKLCH) instead of shadcn's default 0-chroma gray — this is what actually reads as "Notion-like" rather than "generic dark mode."
- **Components**: shadcn/ui's Radix-based primitives (Button, Input, Select, Dialog, Sheet, Badge, Card, Skeleton, Separator, Label, Sonner/Toast) — accessible by default, source lives in `src/components/ui/` (not an npm dependency), fully restyled via the theme tokens above rather than shadcn's stock look.

**Known shadcn CLI quirk hit during setup**: `npx shadcn add` initially wrote every component into a literal `./@/` directory instead of resolving the `@/*` path alias to `src/`, because the alias wasn't declared in the root `tsconfig.json` (only in `tsconfig.app.json`). Fixed by adding the `paths` mapping to root `tsconfig.json` too; moved the misplaced files into `src/` by hand. Worth knowing if a future `shadcn add` run ever silently creates a `@/` folder again — check there first.

## 3. What's Built

- **Board**: all 10 states from [[003-Domain]] §3, laid out as an even CSS grid (`grid-cols-10`) so **all columns fit the viewport with no horizontal scroll** — founder feedback against the earlier flex-scroll layout. Each column scrolls vertically on its own (thin themed scrollbar, `.board-scroll` utility in `index.css`) if its card list grows past the column height.
- **Drag-and-drop** (founder-requested): cards are draggable only from the 3 states with a real human-gated forward transition (`Backlog`, `AwaitingPublish`, `Review` — matching [[003-Domain]] §3 exactly); dropping is only accepted on that task's one valid target column (`Todo`, `Publishing`, `Done` respectively). Dropping anywhere else shows a toast ("Can't move a task directly from X to Y") and the card stays put — domain correctness (INV-3) enforced in the interaction itself, not just the backend. Columns visually highlight as a valid/invalid target while a drag is in progress.
- **Task creation**: a `Dialog` (not an inline form) — project select + title input, `POST /tasks` per [[000-Vision]] UC-3.
- **Task detail panel**: a `Sheet` sliding from the right — description, acceptance-criteria checklist, live event timeline, per-run cost, all polling every 2s. Shows `PlannerStarted → PlannerInvokingModel → PlannerCompleted` in near-real-time during a real (multi-second) Claude call — validated live in-browser, not just by inspection.
- **Toasts** (Sonner) for action feedback (task created, promoted, published, approved, drag rejected) instead of silent state changes.
- **Live-ish updates**: 2s polling (`refetchInterval`) on both the board and the detail panel.

## 4. What's Explicitly Not Built Yet

- **Navigation / other views**: Projects list, Execution view, Logs, Metrics, Models, Workers, Settings — none exist.
- **Diff/commits view** in the task detail panel — meaningless until there's a real diff to show beyond the commit message already in the timeline.
- **Real-time via WebSocket**: 2-second polling is a deliberate stand-in for both the board and the detail panel. [[007-ExecutionEngine]] §4 already specifies the real design; the activities now produce real trace events, so a WebSocket layer is additive, not a rework.
- **Board views beyond Kanban** (list, timeline, tree) — post-v1 per [[000-Vision]] §7.
- **Light/dark toggle UI** — both themes are fully defined in CSS, but there's no control to switch; dark is hardcoded via the `class="dark"` on `<html>`.

## 5. Design Notes

- Font sizes throughout the board are deliberately small (`text-xs`/`text-[11px]`/`text-[10px]`) per founder feedback — "mais harmônico" (more harmonious) after an initial pass felt too large for a 10-column dense board. The task detail panel and dialogs stay slightly larger since they're focused, occasional-use surfaces, not part of the board's density.
- The Vite dev server proxies `/api` to `http://localhost:5080` ([[012-API]]) so no absolute URL is hardcoded anywhere in the frontend.

## 6. Open Questions

- The task detail panel is a client-side overlay (no router, no real route/URL) — fine for now, revisit if deep-linking to a specific task becomes a real need.
- Whether polling should stay as a fallback even after WebSocket exists (e.g. for clients that lose the socket) — reasonable, not designed yet.
- Drag-and-drop keyboard accessibility (dnd-kit supports it; not explicitly tested here) — worth a pass once real users beyond the founder exist.
