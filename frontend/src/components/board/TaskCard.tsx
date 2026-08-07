import { useDraggable } from '@dnd-kit/core'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { cn, getContrastTextColor } from '@/lib/utils'
import { useTheme } from '@/lib/useTheme'
import { hexToRgba } from '@/lib/project-colors'
import { STATE_CONFIG } from '@/lib/state-config'
import type { TaskItem } from '@/lib/api'

function TaskCardBody({
  task,
  tag,
}: {
  task: TaskItem
  // "{ProjectPrefix}-{Number}" (e.g. "FORGE-42") - founder-requested, so a task can be
  // referenced in conversation without pasting a raw GUID.
  tag: string
}) {
  const config = STATE_CONFIG[task.state]
  return (
    <>
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="font-mono text-[9px] font-medium text-muted-foreground/70">{tag}</span>
        <p className="text-xs leading-snug text-foreground">{task.title}</p>
      </div>

      {(task.tags?.length ?? 0) > 0 && (
        <div className="flex flex-wrap gap-1">
          {task.tags!.map((tag) => (
            <Badge
              key={tag.id}
              className="px-1.5 text-[9px]"
              style={{ backgroundColor: tag.color, color: getContrastTextColor(tag.color) }}
            >
              {tag.name}
            </Badge>
          ))}
        </div>
      )}

      {config.spin && (
        <Badge variant="outline" className="w-fit gap-1 border-primary/30 text-[9px] text-primary">
          <config.icon className="size-2.5 animate-spin" />
          agent working
        </Badge>
      )}
    </>
  )
}

export function TaskCard({
  task,
  tag,
  color,
  onOpen,
}: {
  task: TaskItem
  tag: string
  // The owning Project's pastel color (Project.color) - tints the card background
  // instead of the old fixed bg-card/60, so a task's project is identifiable at a
  // glance on the board, not just in the sidebar.
  color: string
  onOpen: (id: string) => void
}) {
  const config = STATE_CONFIG[task.state]
  const draggable = Boolean(config.dragTargets?.length)
  const { theme } = useTheme()

  // App.tsx renders the moving clone via dnd-kit's <DragOverlay> (TaskCardOverlay
  // below), which lives outside BoardColumn's clipped, overflow-y-auto container -
  // so this in-place node no longer needs to translate itself. It just fades while
  // dragging so the overlay reads as the one "live" card instead of two full ones.
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: task.id,
    data: { state: task.state },
    disabled: !draggable,
  })

  const style = {
    // Pastel swatches read fine at full strength on the light theme's white cards but
    // wash out the dark theme's own contrast - a lighter tint in dark mode keeps the
    // card readable while still carrying the project's color. Text stays on
    // text-foreground/text-muted-foreground below, which already adapt per theme.
    backgroundColor: hexToRgba(color, theme === 'dark' ? 0.16 : 0.45),
  }

  return (
    <Card
      ref={setNodeRef}
      style={style}
      onClick={() => onOpen(task.id)}
      {...(draggable ? attributes : {})}
      {...(draggable ? listeners : {})}
      className={cn(
        // The whole card is the drag surface now (founder-reported: the old
        // hover-only grip icon was too small/undiscoverable to reliably grab, and
        // its reserved layout space made these columns' text visibly more indented
        // than every other column). A plain click still opens the task - dnd-kit's
        // activation distance (App.tsx) tells a click from a drag before either fires.
        // Founder-reported (FORGE-27): a column with many cards squished every card
        // down to an unreadably short height instead of scrolling. BoardColumn's
        // scroll container is `flex flex-col` - a flex item defaults to
        // `flex-shrink: 1`, so once the cards' combined natural height exceeded the
        // column's available space, the browser shrank each card to fit rather than
        // letting `overflow-y-auto` take over. `shrink-0` keeps every card at its
        // natural height; the column scrolls instead, which is what min-h-0 on the
        // ancestor chain (commit 289106d) was already meant to enable.
        'group shrink-0 cursor-pointer gap-1.5 border-border/60 p-2 shadow-sm transition-all hover:border-border hover:shadow-md',
        draggable && 'cursor-grab touch-none active:cursor-grabbing',
        isDragging && 'opacity-40',
      )}
    >
      <TaskCardBody task={task} tag={tag} />
    </Card>
  )
}

// The visual clone dnd-kit's <DragOverlay> renders in App.tsx while a card is being
// dragged. Deliberately does NOT call useDraggable - registering a second draggable
// under the same task id would collide with the source TaskCard's own registration
// above. Rendered in a portal outside every column, so BoardColumn's
// overflow-y-auto (which forces overflow-x to clip too - see App.tsx/BoardColumn.tsx
// notes) never clips it mid-drag between columns.
export function TaskCardOverlay({ task, tag, color }: { task: TaskItem; tag: string; color: string }) {
  const { theme } = useTheme()

  return (
    <Card
      style={{ backgroundColor: hexToRgba(color, theme === 'dark' ? 0.16 : 0.45) }}
      className="cursor-grabbing gap-1.5 border-border/60 p-2 shadow-lg"
    >
      <TaskCardBody task={task} tag={tag} />
    </Card>
  )
}
