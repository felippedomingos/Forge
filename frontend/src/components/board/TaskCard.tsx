import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { GripVertical } from 'lucide-react'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
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
      className={cn(
        'group cursor-pointer gap-1.5 border-border/60 bg-card/60 p-2 shadow-sm transition-all hover:border-border hover:shadow-md',
        isDragging && 'z-10 opacity-50 shadow-lg',
      )}
    >
      <div className="flex items-start gap-1.5">
        {draggable && (
          <button
            {...attributes}
            {...listeners}
            onClick={(e) => e.stopPropagation()}
            className="mt-0.5 shrink-0 cursor-grab touch-none text-muted-foreground/40 opacity-0 transition-opacity group-hover:opacity-100 active:cursor-grabbing"
            aria-label="Drag to move"
          >
            <GripVertical className="size-3.5" />
          </button>
        )}
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="font-mono text-[9px] font-medium text-muted-foreground/70">{tag}</span>
          <p className="text-xs leading-snug text-foreground">{task.title}</p>
        </div>
      </div>

      {config.spin && (
        <Badge variant="outline" className="w-fit gap-1 border-primary/30 text-[9px] text-primary">
          <config.icon className="size-2.5 animate-spin" />
          agent working
        </Badge>
      )}
    </Card>
  )
}
