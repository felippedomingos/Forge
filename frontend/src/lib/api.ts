import { getToken, logout } from './auth'

// Mirrors docs/003-Domain.md §3 and docs/012-API.md exactly.
export const TASK_STATES = [
  'Inbox',
  'Backlog',
  'Blocked',
  'Todo',
  'Executing',
  'AwaitingPublish',
  'Publishing',
  'Review',
  'Done',
  'Production',
] as const

export type TaskState = (typeof TASK_STATES)[number]

export interface Project {
  id: string
  name: string
  // Short uppercase code used to build every task's tag ("FORGE-42") - see
  // Project.Prefix in the backend. Immutable once tasks reference it.
  prefix: string
  nextTaskNumber: number
  repositoryUrl: string
  rootBranch: string
  gitProviderPluginId: string
  localPath: string | null
  publishRecipe: string | null
  // Founder-requested trust gate (docs/009-MCP.md §4's per-role scoping idea,
  // simplified to one flag): Developer/Deploy refuse to touch the filesystem/shell
  // for this project's tasks unless it's true - see AgentActivities on the backend.
  allowAgentBypassPermissions: boolean
  // Always one of PROJECT_COLOR_PALETTE (lib/project-colors.ts) - auto-assigned at
  // creation, never null/free-form. See Project.Color on the backend.
  color: string
  // docs/006-Scheduler.md §2 - per-project cap on how many of this project's tasks
  // BacklogSchedulerWorkflow lets sit in Executing at once. Backend default is 2;
  // once the cap is hit, the next Backlog task just waits for a slot (no UX change
  // here beyond the value itself being editable).
  maxConcurrentExecuting: number
  createdAt: string
}

// docs/015-Deployment.md §2 - Project.publishRecipe's shape, stored as a raw JSON
// string rather than parsed server-side (same reasoning as the backend's own
// PublishRecipeDto). Only `previewUrl` has a frontend affordance today (the
// Review-stage "Testar" button) - the rest exist for the Deploy agent.
export interface PublishRecipe {
  migrationCommand: string | null
  restartTargets: string[] | null
  healthCheckUrl: string | null
  previewUrl: string | null
}

export function parsePublishRecipe(raw: string | null): PublishRecipe | null {
  if (!raw) return null
  try {
    return JSON.parse(raw) as PublishRecipe
  } catch {
    return null
  }
}

// docs/010-Plugins.md - GitHub today per ADR-0002; the new-project dialog picks from
// whatever's actually registered rather than a hardcoded GUID.
export interface Plugin {
  id: string
  name: string
  kind: string
  version: string
}

// docs/005-Agents.md §7 - project-wide shared memory, read by every agent role
// regardless of which role wrote a given entry.
export interface MemoryEntry {
  id: string
  projectId: string
  agentRole: string
  key: string
  value: string
  updatedAt: string
}

export interface AcceptanceCriterion {
  id: string
  description: string
  satisfied: boolean
}

// Free-form, per-project label distinct from the auto-assigned {prefix}-{number} task
// tag - see backend Tag entity. Many-to-many with TaskItem via /tasks/{id}/tags.
export interface Tag {
  id: string
  projectId: string
  name: string
  // Hex color (e.g. "#3B82F6") - rendered directly as a badge's background.
  color: string
  createdAt: string
}

export interface TaskEvent {
  id: string
  type: string
  payload: string
  occurredAt: string
  actor: string
}

export interface Run {
  id: string
  agentRole: string
  modelProvider: string
  status: string
  promptTokens: number
  completionTokens: number
  costEstimate: number
  startedAt: string
}

// Mirrors the backend's TranscriptContentBlock (ClaudeTranscriptReader) - one block of
// a transcript message's content array. Exactly one of text/toolName/toolResultText is
// populated depending on `type`.
export interface TranscriptContentBlock {
  type: string
  text: string | null
  toolName: string | null
  toolInput: unknown | null
  toolResultText: string | null
  isError: boolean | null
}

export interface TranscriptMessage {
  role: string
  timestamp: string | null
  content: TranscriptContentBlock[]
}

export interface RunSession {
  available: boolean
  messages: TranscriptMessage[]
}

export interface Worktree {
  id: string
  taskId: string
  projectId: string
  path: string
  branchName: string
  createdAt: string
  deletedAt: string | null
}

export interface TaskItem {
  id: string
  projectId: string
  // Per-project sequential number - combine with the owning Project's `prefix`
  // ("FORGE" + "-" + number) to render the task's tag.
  number: number
  title: string
  description: string | null
  state: TaskState
  priority: number | null
  // True once a Product Owner has set `priority` via setTaskPriority - tells the
  // backend's Prioritizer agent to leave this task's ranking alone on its next run
  // instead of overwriting it (AgentActivities.PrioritizeAsync).
  priorityManuallySet: boolean
  branchName: string | null
  // Set once the Git agent actually creates the PR (Review->Done) - docs/003-Domain.md
  // row 9->10, docs/015-Deployment.md §4's Done->Production polling reads this.
  pullRequestUrl: string | null
  worktreeId: string | null
  createdAt: string
  updatedAt: string
  acceptanceCriteria?: AcceptanceCriterion[]
  runs?: Run[]
  tags?: Tag[]
  worktree?: Worktree | null
}

const BASE_URL = '/api'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getToken()
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    ...init,
  })
  // docs/adr/ADR-0006 - a 401 anywhere means the token's gone bad (expired, revoked) -
  // drop back to the login screen immediately rather than letting every call site
  // independently notice and handle it.
  if (res.status === 401) {
    logout()
    throw new Error(`${init?.method ?? 'GET'} ${path} failed: 401`)
  }
  if (!res.ok) {
    throw new Error(`${init?.method ?? 'GET'} ${path} failed: ${res.status}`)
  }
  return (await res.json()) as T
}

export const api = {
  listProjects: () => request<Project[]>('/projects'),
  listPlugins: () => request<Plugin[]>('/plugins'),
  // Founder-requested: list a repo's actual branches instead of guessing at common
  // names - provider-agnostic (`git ls-remote` under the hood, works the same for
  // GitHub/Azure DevOps/anything else). Throws (via `request`'s !res.ok check) on an
  // unreachable/invalid URL - callers should treat that as "fall back to manual entry."
  listBranches: (repositoryUrl: string) =>
    request<{ branches: string[] }>(`/git/branches?repositoryUrl=${encodeURIComponent(repositoryUrl)}`),
  getProject: (projectId: string) => request<Project>(`/projects/${projectId}`),
  createProject: (input: {
    name: string
    prefix: string
    repositoryUrl: string
    rootBranch: string
    gitProviderPluginId: string
    localPath?: string
    allowAgentBypassPermissions?: boolean
    maxConcurrentExecuting?: number
  }) =>
    request<Project>('/projects', {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  updateProject: (
    projectId: string,
    patch: Partial<
      Pick<
        Project,
        | 'name'
        | 'repositoryUrl'
        | 'rootBranch'
        | 'localPath'
        | 'allowAgentBypassPermissions'
        | 'color'
        | 'maxConcurrentExecuting'
      >
    >,
  ) =>
    request<Project>(`/projects/${projectId}`, {
      method: 'PATCH',
      body: JSON.stringify(patch),
    }),
  // Preserves whatever fields aren't exposed in the edit dialog (e.g.
  // migrationCommand) by merging into the current recipe before sending - a plain
  // PATCH-like partial update, since the backend endpoint replaces the whole thing.
  updatePublishRecipe: (projectId: string, recipe: PublishRecipe) =>
    request<void>(`/projects/${projectId}/publish-recipe`, {
      method: 'PATCH',
      body: JSON.stringify(recipe),
    }),
  deleteProject: (projectId: string) => request<void>(`/projects/${projectId}`, { method: 'DELETE' }),
  getProjectMemory: (projectId: string) => request<MemoryEntry[]>(`/projects/${projectId}/memory`),
  setMemoryEntry: (projectId: string, key: string, value: string) =>
    request<void>(`/projects/${projectId}/memory`, {
      method: 'PUT',
      body: JSON.stringify({ key, value }),
    }),
  deleteMemoryEntry: (projectId: string, key: string) =>
    request<void>(`/projects/${projectId}/memory/${encodeURIComponent(key)}`, {
      method: 'DELETE',
    }),
  listTasks: (projectId?: string) =>
    request<TaskItem[]>(`/tasks${projectId ? `?projectId=${projectId}` : ''}`),
  createTask: (projectId: string, title: string, description?: string) =>
    request<TaskItem>('/tasks', {
      method: 'POST',
      body: JSON.stringify({ projectId, title, description: description || null }),
    }),
  // docs/006-Scheduler.md §1: stand-in for the BacklogSchedulerWorkflow, which doesn't
  // exist yet - manually promotes one task instead of priority-ordered auto-promotion.
  promoteTask: (taskId: string) =>
    request<void>(`/tasks/${taskId}/promote`, { method: 'POST' }),
  // docs/012-API.md §2 - only the two human-gated transitions this endpoint owns
  // (AwaitingPublish->Publishing, Review->Done). Blocked->Inbox goes through answers().
  moveTask: (taskId: string, targetState: 'Publishing' | 'Done') =>
    request<void>(`/tasks/${taskId}/move`, {
      method: 'POST',
      body: JSON.stringify({ targetState }),
    }),
  // docs/000-Vision.md's Product Owner persona - a manual override distinct from the
  // Prioritizer agent's automatic ranking. Only valid while the task is in Backlog
  // (enforced server-side).
  setTaskPriority: (taskId: string, priority: number) =>
    request<TaskItem>(`/tasks/${taskId}/priority`, {
      method: 'PATCH',
      body: JSON.stringify({ priority }),
    }),
  answerTask: (taskId: string, answers: string[]) =>
    request<void>(`/tasks/${taskId}/answers`, {
      method: 'POST',
      body: JSON.stringify({ answers }),
    }),
  // docs/004-Workflow.md row 14 - founder-requested: send a Review-stage task back for
  // another Developer pass instead of only approving it forward.
  requestChanges: (taskId: string, comment: string) =>
    request<void>(`/tasks/${taskId}/request-changes`, {
      method: 'POST',
      body: JSON.stringify({ comment }),
    }),
  getTask: (taskId: string) => request<TaskItem>(`/tasks/${taskId}`),
  // Mirrors deleteProject above - cascades on the backend (sub-tasks, acceptance
  // criteria, runs, events) and best-effort terminates the task's TaskWorkflow.
  deleteTask: (taskId: string) => request<void>(`/tasks/${taskId}`, { method: 'DELETE' }),
  // Founder-requested (docs/013-Frontend.md) - free-form per-project labels, CRUD
  // scoped to a Project; assignment onto a Task is the separate pair below.
  listProjectTags: (projectId: string) => request<Tag[]>(`/projects/${projectId}/tags`),
  createTag: (projectId: string, name: string, color: string) =>
    request<Tag>(`/projects/${projectId}/tags`, {
      method: 'POST',
      body: JSON.stringify({ name, color }),
    }),
  updateTag: (tagId: string, patch: { name?: string; color?: string }) =>
    request<Tag>(`/tags/${tagId}`, {
      method: 'PATCH',
      body: JSON.stringify(patch),
    }),
  deleteTag: (tagId: string) => request<void>(`/tags/${tagId}`, { method: 'DELETE' }),
  assignTag: (taskId: string, tagId: string) =>
    request<Tag[]>(`/tasks/${taskId}/tags`, {
      method: 'POST',
      body: JSON.stringify({ tagId }),
    }),
  removeTag: (taskId: string, tagId: string) =>
    request<void>(`/tasks/${taskId}/tags/${tagId}`, { method: 'DELETE' }),
  // docs/000-Vision.md UC-9 - the task detail view's event timeline. Polling stands in
  // for the WebSocket channel docs/007-ExecutionEngine.md §4 still describes as the
  // target mechanism.
  getTaskEvents: (taskId: string) => request<TaskEvent[]>(`/tasks/${taskId}/events`),
  // Full Claude Code session transcript for one Run - loaded on demand (only when a
  // run is expanded in the detail sheet), not as part of the task/runs fetch.
  getRunSession: (taskId: string, runId: string) =>
    request<RunSession>(`/tasks/${taskId}/runs/${runId}/session`),
  // docs/013-Frontend.md - founder-requested global spend indicator. NOT a real
  // account-level quota (see the backend endpoint's own comment) - a sum of
  // per-run CostEstimate, itself an API-list-price estimate, across every project.
  getGlobalCost: () => request<{ totalCostUsd: number; runCount: number }>('/cost'),
  // docs/adr/ADR-0006 - auth endpoints. login/bootstrap/needsBootstrap are the only
  // calls made with no token yet (handled fine here since `request` just omits the
  // header when getToken() returns null).
  needsBootstrap: () => request<{ needsBootstrap: boolean }>('/auth/needs-bootstrap'),
  bootstrap: (name: string, email: string, password: string) =>
    request<{ token: string }>('/auth/bootstrap', {
      method: 'POST',
      body: JSON.stringify({ name, email, password }),
    }),
  login: (email: string, password: string) =>
    request<{ token: string }>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),
  listUsers: () => request<{ id: string; name: string; email: string; role: string }[]>('/users'),
  createUser: (name: string, email: string, role: string, password: string) =>
    request<{ id: string; name: string; email: string; role: string }>('/users', {
      method: 'POST',
      body: JSON.stringify({ name, email, role, password }),
    }),
  updateUser: (userId: string, patch: { name?: string; email?: string; role?: string }) =>
    request<{ id: string; name: string; email: string; role: string }>(`/users/${userId}`, {
      method: 'PUT',
      body: JSON.stringify(patch),
    }),
  // Self-service only - proves the current password via BCrypt server-side before
  // replacing the hash. No Admin-driven reset endpoint exists (ADR-0006's noted gap).
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>('/users/me/change-password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword, newPassword }),
    }),
  // 409 when the target is the last remaining Admin - see Program.cs's DELETE /users/{id}.
  deleteUser: (userId: string) => request<void>(`/users/${userId}`, { method: 'DELETE' }),
}
