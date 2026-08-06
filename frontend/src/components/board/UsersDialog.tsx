import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Pencil, Trash2, Users as UsersIcon } from 'lucide-react'
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
// check already gating POST/PUT/GET /users on the backend; this dialog is itself only
// mounted for Admins, see ProjectSidebar). Self-service password changes go through the
// separate /users/me/change-password flow (ChangePasswordDialog) - the "Reset password"
// action below is the Admin-driven counterpart, for when the target user doesn't know
// their current password.
function UserRow({ user }: { user: { id: string; name: string; email: string; role: string } }) {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [resetting, setResetting] = useState(false)
  const [name, setName] = useState(user.name)
  const [email, setEmail] = useState(user.email)
  const [role, setRole] = useState(user.role)
  const [newPassword, setNewPassword] = useState('')

  const save = useMutation({
    mutationFn: () => api.updateUser(user.id, { name, email, role }),
    onSuccess: () => {
      toast.success('User updated.')
      setEditing(false)
      queryClient.invalidateQueries({ queryKey: ['users'] })
    },
    onError: () => toast.error('Could not update the user.'),
  })

  const remove = useMutation({
    mutationFn: () => api.deleteUser(user.id),
    onSuccess: () => {
      toast.success('User deleted.')
      queryClient.invalidateQueries({ queryKey: ['users'] })
    },
    onError: (error: Error) => {
      toast.error(
        error.message.includes('409') ? 'Cannot delete the last Admin user.' : 'Could not delete the user.'
      )
    },
  })

  const reset = useMutation({
    mutationFn: () => api.resetPassword(user.id, newPassword),
    onSuccess: () => {
      toast.success(`Password reset for ${user.name}.`)
      setResetting(false)
      setNewPassword('')
    },
    onError: () => toast.error('Could not reset the password.'),
  })

  if (resetting) {
    return (
      <div className="flex flex-col gap-2 rounded-lg border border-border/60 bg-card/40 p-2">
        <Label>Reset password for {user.name}</Label>
        <Input
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          placeholder="New password"
          type="password"
        />
        <div className="flex justify-end gap-2">
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              setResetting(false)
              setNewPassword('')
            }}
          >
            Cancel
          </Button>
          <Button size="sm" disabled={!newPassword || reset.isPending} onClick={() => reset.mutate()}>
            Reset password
          </Button>
        </div>
      </div>
    )
  }

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
          onClick={() => setResetting(true)}
          className="shrink-0 text-muted-foreground/50 hover:text-foreground"
          aria-label={`Reset password for ${user.name}`}
        >
          <KeyRound className="size-3.5" />
        </button>
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
        <button
          onClick={() => {
            if (window.confirm(`Delete ${user.name}? This cannot be undone.`)) remove.mutate()
          }}
          disabled={remove.isPending}
          className="shrink-0 text-muted-foreground/50 hover:text-destructive disabled:opacity-50"
          aria-label={`Delete ${user.name}`}
        >
          <Trash2 className="size-3.5" />
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
