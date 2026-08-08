import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Settings as SettingsIcon } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { api } from '@/lib/api'

// Founder-requested (2026-08-08, docs/001-Requirements.md NFR-1) - the global
// cross-project concurrency ceiling, the one requirement re-audit turned up as
// genuinely unimplemented. Admin-only, same gating as Users/PublishRecipe editing
// elsewhere - this affects every project's task throughput, not just one.
export function SettingsDialog() {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [maxGlobal, setMaxGlobal] = useState('')

  const settingsQuery = useQuery({ queryKey: ['settings'], queryFn: api.getSettings, enabled: open })

  useEffect(() => {
    if (settingsQuery.data) setMaxGlobal(String(settingsQuery.data.maxGlobalConcurrentExecuting))
  }, [settingsQuery.data])

  const parsedMax = Number.parseInt(maxGlobal, 10)
  const maxValid = Number.isInteger(parsedMax) && parsedMax > 0

  const save = useMutation({
    mutationFn: () => api.updateSettings({ maxGlobalConcurrentExecuting: parsedMax }),
    onSuccess: () => {
      toast.success('Settings updated.')
      queryClient.invalidateQueries({ queryKey: ['settings'] })
    },
    onError: () => toast.error('Could not update settings.'),
  })

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="ghost" size="sm" className="w-fit gap-1.5 text-muted-foreground">
          <SettingsIcon className="size-3.5" />
          Settings
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>Settings</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-1.5 py-2">
          <Label htmlFor="max-global-concurrent">Max global concurrent executing tasks</Label>
          <p className="text-xs text-muted-foreground">
            Ceiling across every project's `Executing` tasks combined, on top of each
            project's own limit (docs/006-Scheduler.md §2) - enforced atomically
            alongside it, so a burst of promotions across many projects at once can't
            overshoot this either.
          </p>
          {settingsQuery.isLoading ? (
            <p className="text-xs text-muted-foreground">Loading…</p>
          ) : (
            <Input
              id="max-global-concurrent"
              type="number"
              min={1}
              step={1}
              value={maxGlobal}
              onChange={(e) => setMaxGlobal(e.target.value)}
            />
          )}
          {!maxValid && <p className="text-xs text-destructive">Must be a positive integer.</p>}
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={() => setOpen(false)}>
            Close
          </Button>
          <Button size="sm" disabled={!maxValid || save.isPending} onClick={() => save.mutate()}>
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
