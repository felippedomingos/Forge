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
  branchName: string | null
  worktreeId: string | null
  createdAt: string
  updatedAt: string
  acceptanceCriteria?: AcceptanceCriterion[]
  runs?: Run[]
}

const BASE_URL = '/api'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
  if (!res.ok) {
    throw new Error(`${init?.method ?? 'GET'} ${path} failed: ${res.status}`)
  }
  return (await res.json()) as T
}

export const api = {
  listProjects: () => request<Project[]>('/projects'),
  listPlugins: () => request<Plugin[]>('/plugins'),
  getProject: (projectId: string) => request<Project>(`/projects/${projectId}`),
  createProject: (input: {
    name: string
    prefix: string
    repositoryUrl: string
    rootBranch: string
    gitProviderPluginId: string
    localPath?: string
  }) =>
    request<Project>('/projects', {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  updateProject: (
    projectId: string,
    patch: Partial<Pick<Project, 'name' | 'repositoryUrl' | 'rootBranch' | 'localPath'>>,
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
  answerTask: (taskId: string, answers: string[]) =>
    request<void>(`/tasks/${taskId}/answers`, {
      method: 'POST',
      body: JSON.stringify({ answers }),
    }),
  getTask: (taskId: string) => request<TaskItem>(`/tasks/${taskId}`),
  // docs/000-Vision.md UC-9 - the task detail view's event timeline. Polling stands in
  // for the WebSocket channel docs/007-ExecutionEngine.md §4 still describes as the
  // target mechanism.
  getTaskEvents: (taskId: string) => request<TaskEvent[]>(`/tasks/${taskId}/events`),
  // docs/013-Frontend.md - founder-requested global spend indicator. NOT a real
  // account-level quota (see the backend endpoint's own comment) - a sum of
  // per-run CostEstimate, itself an API-list-price estimate, across every project.
  getGlobalCost: () => request<{ totalCostUsd: number; runCount: number }>('/cost'),
}
