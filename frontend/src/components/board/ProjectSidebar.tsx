import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  FolderGit2,
  ListTodo,
  Pencil,
  Flame,
  Sun,
  Moon,
  DollarSign,
  LogOut,
  ChevronDown,
  ChevronRight,
  PanelLeftClose,
  PanelLeftOpen,
} from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { useTheme } from '@/lib/useTheme'
import { api, type Project } from '@/lib/api'
import { logout, type AuthUser } from '@/lib/auth'
import { ProjectEditDialog } from './ProjectEditDialog'
import { CreateProjectDialog } from './CreateProjectDialog'
import { UsersDialog } from './UsersDialog'
import { ChangePasswordDialog } from './ChangePasswordDialog'

// Founder-requested: a left-hand "Projetos" nav (repo + shared memory live per-project,
// so navigating by project - not just filtering a dropdown - is the natural shape) plus
// a standing "All tasks" entry for the cross-project view (docs/000-Vision.md UC-1).
export function ProjectSidebar({
  projects,
  selectedProjectId,
  onSelectProject,
  taskCountByProject,
  totalTaskCount,
  user,
}: {
  projects: Project[]
  selectedProjectId: string
  onSelectProject: (id: string) => void
  taskCountByProject: Record<string, number>
  totalTaskCount: number
  user: AuthUser
}) {
  const { theme, toggleTheme } = useTheme()
  const [editingProjectId, setEditingProjectId] = useState<string | null>(null)
  // Founder-requested: the SlayZone import (docs/016-Roadmap.md) took the Projects list
  // from ~1 to 9 overnight, worth collapsing out of the way most of the time. Persisted
  // per-browser (not per-project state) since it's a pure display preference - starts
  // expanded so a first-time/cleared-storage view isn't missing projects by surprise.
  const [projectsCollapsed, setProjectsCollapsed] = useState(
    () => localStorage.getItem('forge:projects-collapsed') === 'true',
  )
  const toggleProjectsCollapsed = () => {
    setProjectsCollapsed((prev) => {
      const next = !prev
      localStorage.setItem('forge:projects-collapsed', String(next))
      return next
    })
  }
  // Founder-requested follow-up: collapsing just the Projects list (above) wasn't
  // enough - the whole nav should tuck away to the left like a drawer, reclaiming
  // board width entirely rather than only shortening one section. Separate toggle/key
  // from projectsCollapsed since they're independent preferences (e.g. a collapsed
  // Projects list should stay collapsed once the drawer is reopened).
  const [sidebarCollapsed, setSidebarCollapsed] = useState(
    () => localStorage.getItem('forge:sidebar-collapsed') === 'true',
  )
  const toggleSidebarCollapsed = () => {
    setSidebarCollapsed((prev) => {
      const next = !prev
      localStorage.setItem('forge:sidebar-collapsed', String(next))
      return next
    })
  }
  // Founder-requested: a global spend indicator. Polls rather than reacting to the
  // WebSocket (docs/007-ExecutionEngine.md §4 is per-task, not board-wide) - 15s is
  // frequent enough for a figure that only moves when a Run completes.
  const costQuery = useQuery({
    queryKey: ['global-cost'],
    queryFn: api.getGlobalCost,
    refetchInterval: 15000,
  })
  // Looked up fresh from `projects` every render (rather than snapshotting the Project
  // object on click) so the dialog's title/fields stay in sync after a save re-fetches
  // the projects list.
  const editingProject = projects.find((p) => p.id === editingProjectId) ?? null

  if (sidebarCollapsed) {
    return (
      <nav className="flex w-10 shrink-0 flex-col items-center gap-3 border-r border-border/60 bg-card/30 p-2">
        <button
          onClick={toggleSidebarCollapsed}
          className="text-muted-foreground/70 hover:text-foreground"
          aria-label="Expand sidebar"
          title="Expand sidebar"
        >
          <PanelLeftOpen className="size-4" />
        </button>
        <Flame className="size-4 text-primary" />
      </nav>
    )
  }

  return (
    <nav className="flex w-56 shrink-0 flex-col gap-3 border-r border-border/60 bg-card/30 p-3">
      <div className="flex items-center gap-1.5 px-1">
        <Flame className="size-4 text-primary" />
        <h1 className="flex-1 text-sm font-semibold">Forge</h1>
        <button
          onClick={toggleSidebarCollapsed}
          className="text-muted-foreground/50 hover:text-foreground"
          aria-label="Collapse sidebar"
          title="Collapse sidebar"
        >
          <PanelLeftClose className="size-3.5" />
        </button>
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
        <div className="flex items-center justify-between px-2 py-1">
          <button
            onClick={toggleProjectsCollapsed}
            className="flex items-center gap-1 text-[10px] font-medium tracking-wide text-muted-foreground/70 uppercase hover:text-muted-foreground"
            aria-expanded={!projectsCollapsed}
          >
            {projectsCollapsed ? (
              <ChevronRight className="size-3 shrink-0" />
            ) : (
              <ChevronDown className="size-3 shrink-0" />
            )}
            Projects
            <Badge variant="secondary" className="rounded-full px-1.5 text-[10px] normal-case">
              {projects.length}
            </Badge>
          </button>
          <CreateProjectDialog />
        </div>
        {!projectsCollapsed && projects.map((project) => (
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
              {/* Founder-requested: a color swatch per row so a project's color is
                  discoverable from the sidebar alone, not just by spotting it on the
                  board. */}
              <span
                className="size-2.5 shrink-0 rounded-full border border-border/40"
                style={{ backgroundColor: project.color }}
                aria-hidden
              />
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
              <Pencil className="size-3.5" />
            </button>
          </div>
        ))}
        {!projectsCollapsed && projects.length === 0 && (
          <p className="px-2 py-1 text-[11px] text-muted-foreground/50">No projects yet</p>
        )}
      </div>

      <div className="mt-auto flex flex-col gap-1.5">
        {/* Founder-requested: a visible sense of spend, not just per-task. See
            api.getGlobalCost's own comment for why this is an estimate, not a real
            account-level quota. */}
        <div
          className="flex items-center gap-1.5 rounded-lg px-2 py-1 text-xs text-muted-foreground"
          title="Estimated spend across every project (API list-price estimate, not real subscription usage - docs/015-Deployment.md)"
        >
          <DollarSign className="size-3.5 shrink-0" />
          <span>
            {costQuery.data ? `$${costQuery.data.totalCostUsd.toFixed(2)} spent` : '—'}
          </span>
        </div>

        <Button variant="ghost" size="sm" className="w-fit gap-1.5 text-muted-foreground" onClick={toggleTheme}>
          {theme === 'dark' ? <Sun className="size-3.5" /> : <Moon className="size-3.5" />}
          {theme === 'dark' ? 'Light mode' : 'Dark mode'}
        </Button>

        {/* Admin-only account management (ADR-0006's `Role == "Admin"` check, mirrored
            here so the entry point isn't shown to someone who'd just get a 403). */}
        {user.role === 'Admin' && <UsersDialog />}

        <ChangePasswordDialog />

        <div className="flex items-center justify-between gap-1.5 px-2 py-1">
          <span className="min-w-0 truncate text-xs text-muted-foreground" title={user.email}>
            {user.name}
          </span>
          <button
            onClick={logout}
            className="shrink-0 text-muted-foreground/60 hover:text-foreground"
            aria-label="Log out"
            title="Log out"
          >
            <LogOut className="size-3.5" />
          </button>
        </div>
      </div>

      {editingProject && (
        <ProjectEditDialog
          project={editingProject}
          open={Boolean(editingProject)}
          onOpenChange={(open) => !open && setEditingProjectId(null)}
          onDeleted={() => {
            if (selectedProjectId === editingProject.id) onSelectProject('all')
          }}
        />
      )}
    </nav>
  )
}
