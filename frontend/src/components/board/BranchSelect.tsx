import { useQuery } from '@tanstack/react-query'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { api } from '@/lib/api'
import { useDebouncedValue } from '@/lib/useDebouncedValue'

// Founder-requested: a project's root branch isn't always "main"/"develop"/"dev" - list
// whatever the repo actually has instead of guessing. Provider-agnostic (`git
// ls-remote` under the hood - GET /git/branches), so this works the same for GitHub,
// Azure DevOps, or anything else without per-provider API wiring. Falls back to free
// text if the URL can't be reached yet (private repo, typo mid-edit, no network) -
// never blocks the form on a fetch that hasn't succeeded.
export function BranchSelect({
  id,
  repositoryUrl,
  value,
  onChange,
}: {
  id: string
  repositoryUrl: string
  value: string
  onChange: (value: string) => void
}) {
  const debouncedUrl = useDebouncedValue(repositoryUrl.trim(), 500)

  const branchesQuery = useQuery({
    queryKey: ['branches', debouncedUrl],
    queryFn: () => api.listBranches(debouncedUrl),
    enabled: debouncedUrl.length > 0,
    retry: false,
  })

  if (debouncedUrl.length === 0) {
    return (
      <Input id={id} value={value} onChange={(e) => onChange(e.target.value)} placeholder="main" disabled />
    )
  }

  if (branchesQuery.isError) {
    return (
      <div className="flex flex-col gap-1">
        <Input id={id} value={value} onChange={(e) => onChange(e.target.value)} placeholder="main" />
        <p className="text-[11px] text-muted-foreground/70">
          Couldn't list branches for this URL — type it manually.
        </p>
      </div>
    )
  }

  const branches = branchesQuery.data?.branches ?? []
  // Never drop the current value even if the fetch hasn't returned it (yet, or ever) -
  // e.g. editing a project whose branch was set before this feature existed.
  const options = value && !branches.includes(value) ? [value, ...branches] : branches

  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger id={id} className="w-full">
        <SelectValue placeholder={branchesQuery.isFetching ? 'Loading branches…' : 'Select a branch…'} />
      </SelectTrigger>
      <SelectContent>
        {options.map((b) => (
          <SelectItem key={b} value={b}>
            {b}
          </SelectItem>
        ))}
        {options.length === 0 && !branchesQuery.isFetching && (
          <div className="px-2 py-1.5 text-xs text-muted-foreground">No branches found</div>
        )}
      </SelectContent>
    </Select>
  )
}
