import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Badge } from '@/components/ui/badge'
import { Switch } from '@/components/ui/switch'
import { Separator } from '@/components/ui/separator'
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { api, parsePublishRecipe, type Project } from '@/lib/api'
import { BranchSelect } from './BranchSelect'

// docs/003-Domain.md / docs/005-Agents.md §7 - founder-requested: each project's repo
// link and shared memory must be editable in one place, not scattered across raw API
// calls. Repository URL/branch/local checkout here; memory (read by every agent role
// regardless of who wrote it) below, since agents actually read this at plan/develop
// time - it's not just descriptive metadata.
export function ProjectEditDialog({
  project,
  open,
  onOpenChange,
  onDeleted,
}: {
  project: Project
  open: boolean
  onOpenChange: (open: boolean) => void
  // Called after a successful delete, in addition to onOpenChange(false) - lets the
  // sidebar reset its selected-project filter if this was the one showing.
  onDeleted?: () => void
}) {
  const queryClient = useQueryClient()
  const [name, setName] = useState(project.name)
  const [repositoryUrl, setRepositoryUrl] = useState(project.repositoryUrl)
  const [rootBranch, setRootBranch] = useState(project.rootBranch)
  const [localPath, setLocalPath] = useState(project.localPath ?? '')
  const [previewUrl, setPreviewUrl] = useState(parsePublishRecipe(project.publishRecipe)?.previewUrl ?? '')
  const [allowBypass, setAllowBypass] = useState(project.allowAgentBypassPermissions)
  const [newKey, setNewKey] = useState('')
  const [newValue, setNewValue] = useState('')
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  useEffect(() => {
    if (!open) return
    setName(project.name)
    setRepositoryUrl(project.repositoryUrl)
    setRootBranch(project.rootBranch)
    setLocalPath(project.localPath ?? '')
    setPreviewUrl(parsePublishRecipe(project.publishRecipe)?.previewUrl ?? '')
    setAllowBypass(project.allowAgentBypassPermissions)
    setConfirmingDelete(false)
  }, [open, project])

  const memoryQuery = useQuery({
    queryKey: ['project-memory', project.id],
    queryFn: () => api.getProjectMemory(project.id),
    enabled: open,
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['projects'] })
    queryClient.invalidateQueries({ queryKey: ['project-memory', project.id] })
  }

  const saveDetails = useMutation({
    mutationFn: () =>
      api.updateProject(project.id, {
        name,
        repositoryUrl,
        rootBranch,
        localPath: localPath || null,
        allowAgentBypassPermissions: allowBypass,
      }),
    onSuccess: () => {
      toast.success('Project updated.')
      invalidate()
    },
    onError: () => toast.error('Could not update the project.'),
  })

  const savePreviewUrl = useMutation({
    mutationFn: () => {
      const current = parsePublishRecipe(project.publishRecipe)
      return api.updatePublishRecipe(project.id, {
        migrationCommand: current?.migrationCommand ?? null,
        restartTargets: current?.restartTargets ?? null,
        healthCheckUrl: current?.healthCheckUrl ?? null,
        previewUrl: previewUrl || null,
      })
    },
    onSuccess: () => {
      toast.success('Preview URL saved.')
      invalidate()
    },
    onError: () => toast.error('Could not save the preview URL.'),
  })

  const saveMemoryEntry = useMutation({
    mutationFn: () => api.setMemoryEntry(project.id, newKey, newValue),
    onSuccess: () => {
      setNewKey('')
      setNewValue('')
      invalidate()
    },
    onError: () => toast.error('Could not save the memory entry.'),
  })

  const deleteMemoryEntry = useMutation({
    mutationFn: (key: string) => api.deleteMemoryEntry(project.id, key),
    onSuccess: invalidate,
    onError: () => toast.error('Could not delete the memory entry.'),
  })

  // Cascades on the backend (tasks, runs, events, memory, ...) and best-effort
  // terminates the project's Temporal workflows - see the DELETE /projects/{id}
  // endpoint's own comment. Irreversible, so this button requires a second click
  // ("Confirmar exclusão") rather than firing on the first.
  const deleteProject = useMutation({
    mutationFn: () => api.deleteProject(project.id),
    onSuccess: () => {
      toast.success(`"${project.name}" deleted.`)
      queryClient.invalidateQueries({ queryKey: ['projects'] })
      queryClient.invalidateQueries({ queryKey: ['tasks'] })
      onOpenChange(false)
      onDeleted?.()
    },
    onError: () => {
      toast.error('Could not delete the project.')
      setConfirmingDelete(false)
    },
  })

  const memory = memoryQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            {project.name}
            <Badge variant="outline" className="font-mono text-[10px]">
              {project.prefix}
            </Badge>
          </DialogTitle>
        </DialogHeader>

        <div className="flex max-h-[65vh] flex-col gap-4 overflow-y-auto py-2 pr-1">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="proj-name">Name</Label>
            <Input id="proj-name" value={name} onChange={(e) => setName(e.target.value)} />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="proj-repo">Repository URL</Label>
            <Input id="proj-repo" value={repositoryUrl} onChange={(e) => setRepositoryUrl(e.target.value)} />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="proj-branch">Root branch</Label>
            <BranchSelect id="proj-branch" repositoryUrl={repositoryUrl} value={rootBranch} onChange={setRootBranch} />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="proj-localpath">Local checkout path</Label>
            <Input
              id="proj-localpath"
              placeholder="/home/.../repo (where agents read/write on this machine)"
              value={localPath}
              onChange={(e) => setLocalPath(e.target.value)}
            />
          </div>

          <div className="flex items-start justify-between gap-3 rounded-lg border border-border/60 p-2.5">
            <div className="flex flex-col gap-0.5">
              <Label htmlFor="proj-bypass">Allow agent bypass permissions</Label>
              <p className="text-xs text-muted-foreground">
                Required for the Developer/Deploy agents to actually edit files or run
                commands here — otherwise every task stays Blocked before touching this
                project. Only enable for a project you trust with unattended writes.
              </p>
            </div>
            <Switch id="proj-bypass" checked={allowBypass} onCheckedChange={setAllowBypass} />
          </div>

          <Button
            size="sm"
            className="w-fit"
            disabled={saveDetails.isPending}
            onClick={() => saveDetails.mutate()}
          >
            Save details
          </Button>

          <Separator />

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="proj-preview-url">Preview URL</Label>
            <p className="text-xs text-muted-foreground">
              Opened by the "Testar" button on a task once it reaches Review — the deployed
              instance a human checks by hand. Not polled or verified by Forge itself.
            </p>
            <div className="flex gap-2">
              <Input
                id="proj-preview-url"
                placeholder="http://localhost:5173"
                value={previewUrl}
                onChange={(e) => setPreviewUrl(e.target.value)}
              />
              <Button
                size="sm"
                variant="secondary"
                className="shrink-0"
                disabled={savePreviewUrl.isPending}
                onClick={() => savePreviewUrl.mutate()}
              >
                Save
              </Button>
            </div>
          </div>

          <Separator />

          <div className="flex flex-col gap-1.5">
            <Label>Shared memory</Label>
            <p className="text-xs text-muted-foreground">
              Read by every agent (Planner, Developer, …) when working on this project's tasks —
              conventions, decisions, gotchas that shouldn't be re-discovered every run.
            </p>
          </div>

          <div className="flex flex-col gap-2">
            {memoryQuery.isLoading && <p className="text-xs text-muted-foreground">Loading…</p>}
            {!memoryQuery.isLoading && memory.length === 0 && (
              <p className="text-xs text-muted-foreground/60">No memory recorded yet.</p>
            )}
            {memory.map((entry) => (
              <div
                key={entry.id}
                className="flex items-start gap-2 rounded-lg border border-border/60 bg-card/40 p-2"
              >
                <div className="min-w-0 flex-1">
                  <p className="text-xs font-medium text-foreground">{entry.key}</p>
                  <p className="whitespace-pre-wrap text-xs text-muted-foreground">{entry.value}</p>
                </div>
                <button
                  onClick={() => deleteMemoryEntry.mutate(entry.key)}
                  className="shrink-0 text-muted-foreground/50 hover:text-destructive"
                  aria-label={`Delete ${entry.key}`}
                >
                  <Trash2 className="size-3.5" />
                </button>
              </div>
            ))}
          </div>

          <div className="flex flex-col gap-1.5 rounded-lg border border-dashed border-border/60 p-2">
            <Input placeholder='Key (e.g. "coding-style")' value={newKey} onChange={(e) => setNewKey(e.target.value)} />
            <Textarea placeholder="Value" value={newValue} onChange={(e) => setNewValue(e.target.value)} />
            <Button
              size="sm"
              variant="secondary"
              className="w-fit"
              disabled={!newKey || !newValue || saveMemoryEntry.isPending}
              onClick={() => saveMemoryEntry.mutate()}
            >
              Add memory entry
            </Button>
          </div>

          <Separator />

          <div className="flex flex-col gap-1.5">
            <Label className="text-destructive">Danger zone</Label>
            <p className="text-xs text-muted-foreground">
              Deletes the project and every task, run, event and memory entry under it.
              Cannot be undone.
            </p>
            <Button
              size="sm"
              variant="destructive"
              className="w-fit"
              disabled={deleteProject.isPending}
              onClick={() => (confirmingDelete ? deleteProject.mutate() : setConfirmingDelete(true))}
            >
              {confirmingDelete ? 'Confirmar exclusão — clique novamente' : 'Delete project'}
            </Button>
          </div>
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
