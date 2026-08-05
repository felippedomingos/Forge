import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Users as UsersIcon } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Separator } from '@/components/ui/separator'
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { api } from '@/lib/api'

// docs/adr/ADR-0006 - Admin-only account management (mirrors the `Role == "Admin"`
// check already gating POST/PUT/GET /users on the backend). No password reset here -
// that's the self-service /users/me/change-password flow (ChangePasswordDialog), the
// only way a PasswordHash ever changes post-creation per the ADR.
function UserRow({ user }: { user: { id: string; name: string; email: string; role: string } }) {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(user.name)
  const [email, setEmail] = useState(user.email)
  const [role, setRole] = useState(user.role)

  const save = useMutation({
    mutationFn: () => api.updateUser(user.id, { name, email, role }),
    onSuccess: () => {
      toast.success('User updated.')
      setEditing(false)
      queryClient.invalidateQueries({ queryKey: ['users'] })
    },
    onError: () => toast.error('Could not update the user.'),
  })

  if (!editing) {
    return (
      <div className="flex items-center gap-2 rounded-lg border border-border/60 bg-card/40 p-2">
        <div className="min-w-0 flex-1">
          <p className="truncate text-xs font-medium text-foreground">{user.name}</p>
          <p className="truncate text-xs text-muted-foreground">{user.email}</p>
        </div>
        <Badge variant="secondary" className="rounded-full px-1.5 text-[10px]">
          {user.role}
        </Badge>
        <button
          onClick={() => {
            setName(user.name)
            setEmail(user.email)
            setRole(user.role)
            setEditing(true)
          }}
          className="shrink-0 text-muted-foreground/50 hover:text-foreground"
          aria-label={`Edit ${user.name}`}
        >
          <Pencil className="size-3.5" />
        </button>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-2 rounded-lg border border-border/60 bg-card/40 p-2">
      <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Name" />
      <Input value={email} onChange={(e) => setEmail(e.target.value)} placeholder="Email" type="email" />
      <Input value={role} onChange={(e) => setRole(e.target.value)} placeholder='Role (e.g. "Admin")' />
      <div className="flex justify-end gap-2">
        <Button size="sm" variant="ghost" onClick={() => setEditing(false)}>
          Cancel
        </Button>
        <Button
          size="sm"
          disabled={!name || !email || !role || save.isPending}
          onClick={() => save.mutate()}
        >
          Save
        </Button>
      </div>
    </div>
  )
}

export function UsersDialog() {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [newName, setNewName] = useState('')
  const [newEmail, setNewEmail] = useState('')
  const [newRole, setNewRole] = useState('')
  const [newPassword, setNewPassword] = useState('')

  const usersQuery = useQuery({ queryKey: ['users'], queryFn: api.listUsers, enabled: open })
  const users = usersQuery.data ?? []

  const createUser = useMutation({
    mutationFn: () => api.createUser(newName, newEmail, newRole, newPassword),
    onSuccess: () => {
      toast.success('User created.')
      setNewName('')
      setNewEmail('')
      setNewRole('')
      setNewPassword('')
      queryClient.invalidateQueries({ queryKey: ['users'] })
    },
    onError: () => toast.error('Could not create the user.'),
  })

  const canCreate = newName && newEmail && newRole && newPassword

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="ghost" size="sm" className="w-fit gap-1.5 text-muted-foreground">
          <UsersIcon className="size-3.5" />
          Users
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Users</DialogTitle>
        </DialogHeader>

        <div className="flex max-h-[65vh] flex-col gap-4 overflow-y-auto py-2 pr-1">
          <div className="flex flex-col gap-2">
            {usersQuery.isLoading && <p className="text-xs text-muted-foreground">Loading…</p>}
            {!usersQuery.isLoading && users.length === 0 && (
              <p className="text-xs text-muted-foreground/60">No users yet.</p>
            )}
            {users.map((u) => (
              <UserRow key={u.id} user={u} />
            ))}
          </div>

          <Separator />

          <div className="flex flex-col gap-1.5 rounded-lg border border-dashed border-border/60 p-2">
            <Label>New user</Label>
            <Input placeholder="Name" value={newName} onChange={(e) => setNewName(e.target.value)} />
            <Input
              placeholder="Email"
              type="email"
              value={newEmail}
              onChange={(e) => setNewEmail(e.target.value)}
            />
            <Input
              placeholder='Role (e.g. "Admin")'
              value={newRole}
              onChange={(e) => setNewRole(e.target.value)}
            />
            <Input
              placeholder="Password"
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
            />
            <Button
              size="sm"
              variant="secondary"
              className="w-fit"
              disabled={!canCreate || createUser.isPending}
              onClick={() => createUser.mutate()}
            >
              Create user
            </Button>
          </div>
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={() => setOpen(false)}>
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
