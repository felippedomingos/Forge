import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, TASK_STATES, type TaskState } from './lib/api'

// First slice of docs/013-Frontend.md's Kanban board (docs/000-Vision.md §9 states).
// Cross-project view (UC-1): no project filter applied by default.
function Board() {
  const queryClient = useQueryClient()
  const [newTitle, setNewTitle] = useState('')
  const [selectedProjectId, setSelectedProjectId] = useState<string>('')

  const projectsQuery = useQuery({ queryKey: ['projects'], queryFn: api.listProjects })
  const tasksQuery = useQuery({ queryKey: ['tasks'], queryFn: () => api.listTasks() })

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
                <div
                  key={task.id}
                  className="rounded border border-neutral-200 bg-white p-3 text-sm shadow-sm dark:border-neutral-800 dark:bg-neutral-900"
                >
                  {task.title}
                </div>
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
