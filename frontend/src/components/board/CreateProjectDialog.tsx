import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { api } from '@/lib/api'

// docs/003-Domain.md §1 / docs/012-API.md - closes the "no UI to create a project" gap.
// `prefix` and the git provider plugin are required up front (prefix is immutable once
// a task references it); `localPath` is optional here too but strongly encouraged for a
// real test - without it the Planner/Developer have no checkout to work against
// (docs/005-Agents.md §2/§4) and every task lands in Blocked immediately.
export function CreateProjectDialog() {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [prefix, setPrefix] = useState('')
  const [repositoryUrl, setRepositoryUrl] = useState('')
  const [rootBranch, setRootBranch] = useState('main')
  const [localPath, setLocalPath] = useState('')
  const [pluginId, setPluginId] = useState('')

  const pluginsQuery = useQuery({ queryKey: ['plugins'], queryFn: api.listPlugins, enabled: open })
  const plugins = pluginsQuery.data ?? []

  useEffect(() => {
    if (plugins.length > 0 && !pluginId) setPluginId(plugins[0].id)
  }, [plugins, pluginId])

  const createProject = useMutation({
    mutationFn: () =>
      api.createProject({
        name,
        prefix: prefix.toUpperCase(),
        repositoryUrl,
        rootBranch,
        gitProviderPluginId: pluginId,
        localPath: localPath || undefined,
      }),
    onSuccess: () => {
      toast.success('Project created.')
      setName('')
      setPrefix('')
      setRepositoryUrl('')
      setRootBranch('main')
      setLocalPath('')
      setOpen(false)
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    },
    onError: () => toast.error('Could not create the project.'),
  })

  const canSubmit = name && prefix && repositoryUrl && rootBranch && pluginId

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="ghost" size="icon-xs" aria-label="New project">
          <Plus className="size-3.5" />
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>New project</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-4 py-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="new-proj-name">Name</Label>
            <Input id="new-proj-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="new-proj-prefix">Prefix</Label>
            <Input
              id="new-proj-prefix"
              placeholder="e.g. FORGE"
              value={prefix}
              onChange={(e) => setPrefix(e.target.value)}
            />
            <p className="text-xs text-muted-foreground">
              Builds every task's tag ("{prefix.toUpperCase() || 'PREFIX'}-1"). Can't be changed later.
            </p>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="new-proj-repo">Repository URL</Label>
            <Input
              id="new-proj-repo"
              placeholder="git@github.com:org/repo.git"
              value={repositoryUrl}
              onChange={(e) => setRepositoryUrl(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="new-proj-branch">Root branch</Label>
            <Input id="new-proj-branch" value={rootBranch} onChange={(e) => setRootBranch(e.target.value)} />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="new-proj-plugin">Git provider</Label>
            <Select value={pluginId} onValueChange={setPluginId}>
              <SelectTrigger id="new-proj-plugin" className="w-full">
                <SelectValue placeholder="Select a plugin…" />
              </SelectTrigger>
              <SelectContent>
                {plugins.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name} ({p.kind})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="new-proj-localpath">Local checkout path</Label>
            <Input
              id="new-proj-localpath"
              placeholder="/home/.../repo (required for agents to actually work on it)"
              value={localPath}
              onChange={(e) => setLocalPath(e.target.value)}
            />
            <p className="text-xs text-muted-foreground">
              Leave empty to configure later — but tasks will stay Blocked until it's set.
            </p>
          </div>
        </div>

        <DialogFooter>
          <Button disabled={!canSubmit || createProject.isPending} onClick={() => createProject.mutate()}>
            Create project
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
