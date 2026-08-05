import { useMemo, useState } from 'react'
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query'
import { DndContext, type DragEndEvent, type DragStartEvent } from '@dnd-kit/core'
import { Flame } from 'lucide-react'
import { Toaster, toast } from 'sonner'
import { Skeleton } from '@/components/ui/skeleton'
import { CreateTaskDialog } from '@/components/board/CreateTaskDialog'
import { BoardColumn } from '@/components/board/BoardColumn'
import { TaskDetailSheet } from '@/components/board/TaskDetailSheet'
import { ProjectSidebar } from '@/components/board/ProjectSidebar'
import { api, TASK_STATES, type TaskState } from '@/lib/api'
import { DROP_TARGETS } from '@/lib/state-config'

// docs/013-Frontend.md: first slice of the board (docs/000-Vision.md §9 states).
// Cross-project view (UC-1) by default; a project filter narrows it.
function Board() {
  const queryClient = useQueryClient()
  const [selectedProjectId, setSelectedProjectId] = useState<string>('all')
  const [openTaskId, setOpenTaskId] = useState<string | null>(null)
  const [draggingFromState, setDraggingFromState] = useState<TaskState | null>(null)

  const projectsQuery = useQuery({ queryKey: ['projects'], queryFn: api.listProjects })
  // 2s polling stands in for the WebSocket trace (docs/007-ExecutionEngine.md §4) so
  // automatic transitions (driven by agent activities, not a human click) show up
  // without a manual refresh.
  const tasksQuery = useQuery({
    queryKey: ['tasks'],
    queryFn: () => api.listTasks(),
    refetchInterval: 2000,
  })

  const invalidateTasks = () => queryClient.invalidateQueries({ queryKey: ['tasks'] })
  const promote = useMutation({ mutationFn: (id: string) => api.promoteTask(id), onSuccess: invalidateTasks })
  const publish = useMutation({ mutationFn: (id: string) => api.moveTask(id, 'Publishing'), onSuccess: invalidateTasks })
  const approve = useMutation({ mutationFn: (id: string) => api.moveTask(id, 'Done'), onSuccess: invalidateTasks })

  const projects = projectsQuery.data ?? []
  const allTasks = tasksQuery.data ?? []
  const tasks =
    selectedProjectId === 'all' ? allTasks : allTasks.filter((t) => t.projectId === selectedProjectId)

  const prefixByProjectId = useMemo(
    () => Object.fromEntries(projects.map((p) => [p.id, p.prefix])),
    [projects],
  )
  const taskCountByProject = useMemo(() => {
    const counts: Record<string, number> = {}
    for (const t of allTasks) counts[t.projectId] = (counts[t.projectId] ?? 0) + 1
    return counts
  }, [allTasks])

  const handleDragStart = (event: DragStartEvent) => {
    setDraggingFromState((event.active.data.current?.state as TaskState) ?? null)
  }

  const handleDragEnd = (event: DragEndEvent) => {
    setDraggingFromState(null)
    const sourceState = event.active.data.current?.state as TaskState | undefined
    const targetState = event.over?.id as TaskState | undefined
    const taskId = event.active.id as string
    if (!sourceState || !targetState) return

    if (DROP_TARGETS[targetState] !== sourceState) {
      toast.error(`Can't move a task directly from ${sourceState} to ${targetState}.`)
      return
    }

    if (sourceState === 'Backlog') promote.mutate(taskId)
    else if (sourceState === 'AwaitingPublish') publish.mutate(taskId)
    else if (sourceState === 'Review') approve.mutate(taskId)
  }

  const selectedProject = projects.find((p) => p.id === selectedProjectId)

  return (
    <div className="flex h-screen overflow-hidden bg-background text-foreground">
      <Toaster theme="dark" position="bottom-right" />

      <ProjectSidebar
        projects={projects}
        selectedProjectId={selectedProjectId}
        onSelectProject={setSelectedProjectId}
        taskCountByProject={taskCountByProject}
        totalTaskCount={allTasks.length}
      />

      <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
        <header className="flex items-center gap-3 border-b border-border/60 px-4 py-2.5">
          <h2 className="text-sm font-medium">
            {selectedProjectId === 'all' ? 'All tasks' : (selectedProject?.name ?? '…')}
          </h2>

          <p className="text-xs text-muted-foreground">
            {tasks.length} task{tasks.length === 1 ? '' : 's'}
          </p>

          <div className="ml-auto">
            <CreateTaskDialog projects={projects} />
          </div>
        </header>

        {projectsQuery.isLoading ? (
          <div className="grid flex-1 grid-cols-10 gap-2 p-4">
            {Array.from({ length: 10 }).map((_, i) => (
              <Skeleton key={i} className="rounded-lg" />
            ))}
          </div>
        ) : projects.length === 0 ? (
          <div className="flex flex-1 flex-col items-center justify-center gap-2 text-center">
            <Flame className="size-8 text-muted-foreground/40" />
            <p className="text-sm text-muted-foreground">
              No projects yet — create one via <code className="rounded bg-muted px-1">POST /api/projects</code>.
            </p>
          </div>
        ) : (
          <DndContext onDragStart={handleDragStart} onDragEnd={handleDragEnd}>
            {/* Founder feedback: all 10 columns fit on screen, no horizontal scroll -
                an even grid instead of a scrolling flex row. */}
            <div className="grid flex-1 grid-cols-10 gap-2 overflow-hidden p-4">
              {TASK_STATES.map((state) => (
                <BoardColumn
                  key={state}
                  state={state}
                  tasks={tasks.filter((t) => t.state === state)}
                  onOpenTask={setOpenTaskId}
                  draggingFromState={draggingFromState}
                  prefixByProjectId={prefixByProjectId}
                />
              ))}
            </div>
          </DndContext>
        )}
      </div>

      <TaskDetailSheet taskId={openTaskId} onClose={() => setOpenTaskId(null)} />
    </div>
  )
}

export default function App() {
  return <Board />
}
