import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { KeyRound } from 'lucide-react'
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

// docs/adr/ADR-0006's noted gap - self-service only, no Admin-driven reset. The backend
// verifies `currentPassword` via BCrypt before touching PasswordHash, so a wrong current
// password 401s (surfaced here, not silently ignored).
export function ChangePasswordDialog() {
  const [open, setOpen] = useState(false)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')

  const reset = () => {
    setCurrentPassword('')
    setNewPassword('')
    setConfirmPassword('')
  }

  const changePassword = useMutation({
    mutationFn: () => api.changePassword(currentPassword, newPassword),
    onSuccess: () => {
      toast.success('Password changed.')
      reset()
      setOpen(false)
    },
    onError: () => toast.error('Current password is incorrect.'),
  })

  const canSubmit =
    currentPassword && newPassword && newPassword === confirmPassword && newPassword.length > 0

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) reset()
      }}
    >
      <DialogTrigger asChild>
        <Button variant="ghost" size="sm" className="w-fit gap-1.5 text-muted-foreground">
          <KeyRound className="size-3.5" />
          Change password
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>Change password</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-4 py-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="cur-password">Current password</Label>
            <Input
              id="cur-password"
              type="password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              autoFocus
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="new-password">New password</Label>
            <Input
              id="new-password"
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="confirm-password">Confirm new password</Label>
            <Input
              id="confirm-password"
              type="password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && canSubmit && changePassword.mutate()}
            />
            {confirmPassword && newPassword !== confirmPassword && (
              <p className="text-xs text-destructive">Passwords don't match.</p>
            )}
          </div>
        </div>

        <DialogFooter>
          <Button disabled={!canSubmit || changePassword.isPending} onClick={() => changePassword.mutate()}>
            Change password
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
