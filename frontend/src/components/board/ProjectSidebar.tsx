import { useState } from 'react'
import { FolderGit2, ListTodo, Pencil, Flame, Sun, Moon } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { useTheme } from '@/lib/useTheme'
import { ProjectEditDialog } from './ProjectEditDialog'
import type { Project } from '@/lib/api'

// Founder-requested: a left-hand "Projetos" nav (repo + shared memory live per-project,
// so navigating by project - not just filtering a dropdown - is the natural shape) plus
// a standing "All tasks" entry for the cross-project view (docs/000-Vision.md UC-1).
export function ProjectSidebar({
  projects,
  selectedProjectId,
  onSelectProject,
  taskCountByProject,
  totalTaskCount,
}: {
  projects: Project[]
  selectedProjectId: string
  onSelectProject: (id: string) => void
  taskCountByProject: Record<string, number>
  totalTaskCount: number
}) {
  const { theme, toggleTheme } = useTheme()
  const [editingProjectId, setEditingProjectId] = useState<string | null>(null)
  // Looked up fresh from `projects` every render (rather than snapshotting the Project
  // object on click) so the dialog's title/fields stay in sync after a save re-fetches
  // the projects list.
  const editingProject = projects.find((p) => p.id === editingProjectId) ?? null

  return (
    <nav className="flex w-56 shrink-0 flex-col gap-3 border-r border-border/60 bg-card/30 p-3">
      <div className="flex items-center gap-1.5 px-1">
        <Flame className="size-4 text-primary" />
        <h1 className="text-sm font-semibold">Forge</h1>
      </div>

      <button
        onClick={() => onSelectProject('all')}
        className={cn(
          'flex items-center gap-2 rounded-lg px-2 py-1.5 text-left text-xs font-medium transition-colors',
          selectedProjectId === 'all'
            ? 'bg-primary/15 text-primary'
            : 'text-foreground hover:bg-muted',
        )}
      >
        <ListTodo className="size-3.5 shrink-0" />
        <span className="flex-1">All tasks</span>
        <Badge variant="secondary" className="rounded-full px-1.5 text-[10px]">
          {totalTaskCount}
        </Badge>
      </button>

      <div className="flex flex-col gap-0.5">
        <p className="px-2 py-1 text-[10px] font-medium tracking-wide text-muted-foreground/70 uppercase">
          Projects
        </p>
        {projects.map((project) => (
          <div
            key={project.id}
            className={cn(
              'group flex items-center gap-2 rounded-lg py-1.5 pr-1 pl-2 text-xs transition-colors',
              selectedProjectId === project.id
                ? 'bg-primary/15 text-primary'
                : 'text-foreground hover:bg-muted',
            )}
          >
            <button
              onClick={() => onSelectProject(project.id)}
              className="flex min-w-0 flex-1 items-center gap-2 text-left"
            >
              <FolderGit2 className="size-3.5 shrink-0" />
              <span className="min-w-0 flex-1 truncate">{project.name}</span>
              <Badge variant="secondary" className="rounded-full px-1.5 text-[10px]">
                {taskCountByProject[project.id] ?? 0}
              </Badge>
            </button>
            <button
              onClick={() => setEditingProjectId(project.id)}
              className="shrink-0 text-muted-foreground/50 opacity-0 transition-opacity group-hover:opacity-100 hover:text-foreground"
              aria-label={`Edit ${project.name}`}
            >
              <Pencil className="size-3" />
            </button>
          </div>
        ))}
        {projects.length === 0 && (
          <p className="px-2 py-1 text-[11px] text-muted-foreground/50">No projects yet</p>
        )}
      </div>

      <Button
        variant="ghost"
        size="sm"
        className="mt-auto w-fit gap-1.5 text-muted-foreground"
        onClick={toggleTheme}
      >
        {theme === 'dark' ? <Sun className="size-3.5" /> : <Moon className="size-3.5" />}
        {theme === 'dark' ? 'Light mode' : 'Dark mode'}
      </Button>

      {editingProject && (
        <ProjectEditDialog
          project={editingProject}
          open={Boolean(editingProject)}
          onOpenChange={(open) => !open && setEditingProjectId(null)}
        />
      )}
    </nav>
  )
}
