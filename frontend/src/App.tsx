import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, TASK_STATES, type TaskItem, type TaskState } from './lib/api'

// docs/012-API.md endpoints wired to contextual per-state actions - the only states
// with a human-gated transition (docs/003-Domain.md §3): Blocked (answer), Backlog
// (promote - stand-in for the missing BacklogSchedulerWorkflow, docs/006-Scheduler.md
// §1), AwaitingPublish (publish), Review (approve). Every other state only advances
// on its own via the workflow's own agent activities.
function TaskCard({ task }: { task: TaskItem }) {
  const queryClient = useQueryClient()
  const [answerText, setAnswerText] = useState('')
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['tasks'] })

  const promote = useMutation({ mutationFn: () => api.promoteTask(task.id), onSuccess: invalidate })
  const publish = useMutation({ mutationFn: () => api.moveTask(task.id, 'Publishing'), onSuccess: invalidate })
  const approve = useMutation({ mutationFn: () => api.moveTask(task.id, 'Done'), onSuccess: invalidate })
  const answer = useMutation({
    mutationFn: () => api.answerTask(task.id, [answerText]),
    onSuccess: () => {
      setAnswerText('')
      invalidate()
    },
  })

  return (
    <div className="rounded border border-neutral-200 bg-white p-3 text-sm shadow-sm dark:border-neutral-800 dark:bg-neutral-900">
      <p className="mb-2">{task.title}</p>

      {task.state === 'Blocked' && (
        <div className="flex flex-col gap-1">
          <input
            className="rounded border border-neutral-300 px-2 py-1 text-xs dark:border-neutral-700 dark:bg-neutral-800"
            placeholder="Answer the Planner's question…"
            value={answerText}
            onChange={(e) => setAnswerText(e.target.value)}
          />
          <button
            className="rounded bg-neutral-900 px-2 py-1 text-xs text-white disabled:opacity-40 dark:bg-neutral-100 dark:text-neutral-900"
            disabled={!answerText || answer.isPending}
            onClick={() => answer.mutate()}
          >
            Answer → back to Inbox
          </button>
        </div>
      )}

      {task.state === 'Backlog' && (
        <button
          className="rounded border border-neutral-300 px-2 py-1 text-xs dark:border-neutral-700"
          disabled={promote.isPending}
          onClick={() => promote.mutate()}
        >
          Promote to Todo →
        </button>
      )}

      {task.state === 'AwaitingPublish' && (
        <button
          className="rounded bg-emerald-600 px-2 py-1 text-xs text-white disabled:opacity-40"
          disabled={publish.isPending}
          onClick={() => publish.mutate()}
        >
          Publish →
        </button>
      )}

      {task.state === 'Review' && (
        <button
          className="rounded bg-emerald-600 px-2 py-1 text-xs text-white disabled:opacity-40"
          disabled={approve.isPending}
          onClick={() => approve.mutate()}
        >
          Approve → Done
        </button>
      )}
    </div>
  )
}

// First slice of docs/013-Frontend.md's Kanban board (docs/000-Vision.md §9 states).
// Cross-project view (UC-1): no project filter applied by default.
function Board() {
  const queryClient = useQueryClient()
  const [newTitle, setNewTitle] = useState('')
  const [selectedProjectId, setSelectedProjectId] = useState<string>('')

  const projectsQuery = useQuery({ queryKey: ['projects'], queryFn: api.listProjects })
  // 2s polling stands in for the real WebSocket trace (docs/007-ExecutionEngine.md §4)
  // so automatic transitions (Inbox->Backlog, etc, driven by the workflow's own agent
  // activities, not a human click) show up without a manual refresh.
  const tasksQuery = useQuery({
    queryKey: ['tasks'],
    queryFn: () => api.listTasks(),
    refetchInterval: 2000,
  })

  const createTask = useMutation({
    mutationFn: () => api.createTask(selectedProjectId, newTitle),
    onSuccess: () => {
      setNewTitle('')
      queryClient.invalidateQueries({ queryKey: ['tasks'] })
    },
  })

  const projects = projectsQuery.data ?? []
  const tasks = tasksQuery.data ?? []

  const tasksByState = (state: TaskState) => tasks.filter((t) => t.state === state)

  return (
    <div className="min-h-screen bg-neutral-50 text-neutral-900 dark:bg-neutral-950 dark:text-neutral-100">
      <header className="border-b border-neutral-200 px-6 py-4 dark:border-neutral-800">
        <h1 className="text-xl font-semibold">Forge</h1>
        <p className="text-sm text-neutral-500">
          {projects.length} project{projects.length === 1 ? '' : 's'} · {tasks.length} task
          {tasks.length === 1 ? '' : 's'}
        </p>
      </header>

      <section className="flex gap-2 border-b border-neutral-200 px-6 py-3 dark:border-neutral-800">
        <select
          className="rounded border border-neutral-300 bg-white px-2 py-1 text-sm dark:border-neutral-700 dark:bg-neutral-900"
          value={selectedProjectId}
          onChange={(e) => setSelectedProjectId(e.target.value)}
        >
          <option value="">Select a project…</option>
          {projects.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        <input
          className="flex-1 rounded border border-neutral-300 bg-white px-2 py-1 text-sm dark:border-neutral-700 dark:bg-neutral-900"
          placeholder="New task title — docs/000-Vision.md UC-3"
          value={newTitle}
          onChange={(e) => setNewTitle(e.target.value)}
        />
        <button
          className="rounded bg-neutral-900 px-3 py-1 text-sm text-white disabled:opacity-40 dark:bg-neutral-100 dark:text-neutral-900"
          disabled={!selectedProjectId || !newTitle || createTask.isPending}
          onClick={() => createTask.mutate()}
        >
          Create
        </button>
      </section>

      {projects.length === 0 && !projectsQuery.isLoading && (
        <p className="px-6 py-4 text-sm text-neutral-500">
          No projects yet — create one via <code>POST /api/projects</code> (no UI for this
          yet, it needs a Git provider plugin configured first, see docs/010-Plugins.md).
        </p>
      )}

      <div className="flex gap-4 overflow-x-auto p-6">
        {TASK_STATES.map((state) => (
          <div key={state} className="w-64 shrink-0">
            <h2 className="mb-2 text-sm font-medium text-neutral-500">
              {state} ({tasksByState(state).length})
            </h2>
            <div className="flex flex-col gap-2">
              {tasksByState(state).map((task) => (
                <TaskCard key={task.id} task={task} />
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

export default function App() {
  return <Board />
}
