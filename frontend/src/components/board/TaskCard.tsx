import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { cn, getContrastTextColor } from '@/lib/utils'
import { STATE_CONFIG } from '@/lib/state-config'
import type { TaskItem } from '@/lib/api'

export function TaskCard({
  task,
  tag,
  onOpen,
}: {
  task: TaskItem
  // "{ProjectPrefix}-{Number}" (e.g. "FORGE-42") - founder-requested, so a task can be
  // referenced in conversation without pasting a raw GUID.
  tag: string
  onOpen: (id: string) => void
}) {
  const config = STATE_CONFIG[task.state]
  const draggable = Boolean(config.dragTarget)

  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: task.id,
    data: { state: task.state },
    disabled: !draggable,
  })

  const style = transform
    ? { transform: CSS.Translate.toString(transform) }
    : undefined

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
        'group cursor-pointer gap-1.5 border-border/60 bg-card/60 p-2 shadow-sm transition-all hover:border-border hover:shadow-md',
        draggable && 'cursor-grab touch-none active:cursor-grabbing',
        isDragging && 'z-10 opacity-50 shadow-lg',
      )}
    >
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
    </Card>
  )
}
