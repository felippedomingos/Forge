import { useDroppable } from '@dnd-kit/core'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import { STATE_CONFIG, DROP_TARGETS } from '@/lib/state-config'
import { TaskCard } from './TaskCard'
import type { TaskItem, TaskState } from '@/lib/api'

interface BoardColumnProps {
  state: TaskState
  tasks: TaskItem[]
  onOpenTask: (id: string) => void
  // Which source state is currently being dragged, if any - used to highlight this
  // column as a valid or invalid drop target while a drag is in progress.
  draggingFromState: TaskState | null
  // projectId -> "PREFIX" lookup, so each card can render its "{PREFIX}-{number}" tag
  // without every column needing the full Project objects.
  prefixByProjectId: Record<string, string>
  // projectId -> pastel hex color, same shape/reasoning as prefixByProjectId - lets
  // TaskCard tint its background per-project without every column needing full
  // Project objects.
  colorByProjectId: Record<string, string>
}

// Founder feedback: all 10 columns must fit on screen (no horizontal scroll) - each
// column is a flexible grid cell (min-w-0 lets it shrink below its content's natural
// width) rather than a fixed-width item in a scrolling row. Cards scroll vertically
// within their own column instead of growing the page.
export function BoardColumn({
  state,
  tasks,
  onOpenTask,
  draggingFromState,
  prefixByProjectId,
  colorByProjectId,
}: BoardColumnProps) {
  const config = STATE_CONFIG[state]
  const acceptsFrom = DROP_TARGETS[state]
  const { setNodeRef, isOver } = useDroppable({ id: state, disabled: !acceptsFrom })

  const isValidTarget = draggingFromState !== null && acceptsFrom === draggingFromState
  const isInvalidTarget = draggingFromState !== null && acceptsFrom !== draggingFromState

  return (
    <div className="flex min-h-0 min-w-0 flex-col">
      {/* Founder-requested: always-visible explanation of what this column means/does,
          highlighted so it's immediately clear to anyone looking at the board, not
          just whoever built it - amber to match the accent, not a hover-only tooltip. */}
      <div className="mb-1.5 rounded-md border border-primary/30 bg-primary/10 px-1.5 py-1">
        <p className="text-[10px] leading-tight text-primary/90">{config.hint}</p>
      </div>
      <div className="mb-2 flex items-center gap-1.5 px-0.5">
        <config.icon className={cn('size-3.5 shrink-0 text-muted-foreground', config.spin && 'animate-spin')} />
        <h2 className="truncate text-xs font-medium text-foreground">{config.label}</h2>
        <Badge variant="secondary" className="ml-auto shrink-0 rounded-full px-1.5 text-[10px]">
          {tasks.length}
        </Badge>
      </div>
      <div
        ref={setNodeRef}
        className={cn(
          'board-scroll flex min-h-0 flex-1 flex-col gap-1.5 overflow-y-auto rounded-lg p-1.5 transition-colors',
          isValidTarget && 'bg-primary/10 ring-2 ring-primary/40',
          isInvalidTarget && 'opacity-40',
          isOver && isValidTarget && 'bg-primary/20',
        )}
      >
        {tasks.map((task) => (
          <TaskCard
            key={task.id}
            task={task}
            tag={`${prefixByProjectId[task.projectId] ?? '?'}-${task.number}`}
            color={colorByProjectId[task.projectId] ?? '#E5E7EB'}
            onOpen={onOpenTask}
          />
        ))}
        {tasks.length === 0 && (
          <p className="px-1 py-2 text-center text-[11px] text-muted-foreground/50">Empty</p>
        )}
      </div>
    </div>
  )
}
