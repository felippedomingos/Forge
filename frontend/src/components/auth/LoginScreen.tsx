import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Flame } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card } from '@/components/ui/card'
import { api } from '@/lib/api'
import { setToken } from '@/lib/auth'

// docs/adr/ADR-0006 - the whole app is gated behind this until there's a valid,
// unexpired JWT (App.tsx). Two modes: bootstrap (first-ever run, no User rows exist
// yet - creates the founder's own Admin account) or plain login (every run after).
// No self-signup - accounts past the first are created via POST /users by an Admin.
export function LoginScreen({ onAuthenticated }: { onAuthenticated: () => void }) {
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  const needsBootstrapQuery = useQuery({ queryKey: ['needs-bootstrap'], queryFn: api.needsBootstrap })
  const needsBootstrap = needsBootstrapQuery.data?.needsBootstrap ?? false

  const bootstrap = useMutation({
    mutationFn: () => api.bootstrap(name, email, password),
    onSuccess: ({ token }) => {
      setToken(token)
      toast.success('Admin account created.')
      onAuthenticated()
    },
    onError: () => toast.error('Could not create the admin account.'),
  })

  const login = useMutation({
    mutationFn: () => api.login(email, password),
    onSuccess: ({ token }) => {
      setToken(token)
      onAuthenticated()
    },
    onError: () => toast.error('Invalid email or password.'),
  })

  const submit = () => (needsBootstrap ? bootstrap.mutate() : login.mutate())
  const canSubmit = needsBootstrap ? name && email && password : email && password
  const pending = bootstrap.isPending || login.isPending

  return (
    <div className="flex h-screen items-center justify-center bg-background text-foreground">
      <Card className="w-full max-w-sm p-6">
        <div className="mb-5 flex items-center gap-2">
          <Flame className="size-5 text-primary" />
          <h1 className="text-base font-semibold">Forge</h1>
        </div>

        {needsBootstrapQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <div className="flex flex-col gap-4">
            {needsBootstrap && (
              <p className="text-xs text-muted-foreground">
                First run — create the admin account. Every account after this one is
                created by an Admin, not by self-signup.
              </p>
            )}

            {needsBootstrap && (
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="login-name">Name</Label>
                <Input id="login-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
              </div>
            )}

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="login-email">Email</Label>
              <Input
                id="login-email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoFocus={!needsBootstrap}
              />
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="login-password">Password</Label>
              <Input
                id="login-password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && canSubmit && submit()}
              />
            </div>

            <Button disabled={!canSubmit || pending} onClick={submit}>
              {needsBootstrap ? 'Create admin account' : 'Log in'}
            </Button>
          </div>
        )}
      </Card>
    </div>
  )
}
