// docs/adr/ADR-0006 - a plain JWT held in localStorage, no server-side session.
// Decoding is done client-side (no round-trip needed just to read the claims already
// on the token) - this is NOT verification, just reading what a trusted-by-construction
// token (issued by our own backend, sent over HTTPS in production) already says.
const TOKEN_KEY = 'forge-auth-token'

export interface AuthUser {
  id: string
  email: string
  name: string
  role: string
  exp: number
}

function decodeToken(token: string): AuthUser | null {
  try {
    const payload = token.split('.')[1]
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
    const claims = JSON.parse(json)
    return {
      id: claims.sub,
      email: claims.email,
      name: claims.name,
      role: claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ?? claims.role,
      exp: claims.exp,
    }
  } catch {
    return null
  }
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function setToken(token: string) {
  localStorage.setItem(TOKEN_KEY, token)
}

export function clearToken() {
  localStorage.removeItem(TOKEN_KEY)
}

// Returns null if there's no token, it's malformed, or it's expired - all three mean
// "not authenticated" as far as the UI is concerned.
export function getCurrentUser(): AuthUser | null {
  const token = getToken()
  if (!token) return null
  const user = decodeToken(token)
  if (!user || user.exp * 1000 < Date.now()) return null
  return user
}

// Dispatched by api.ts's request() on a 401 - lets App.tsx drop back to the login
// screen the instant a token is rejected (expired mid-session, revoked, etc.) without
// every call site needing to handle it individually.
export const AUTH_INVALID_EVENT = 'forge-auth-invalid'

export function logout() {
  clearToken()
  window.dispatchEvent(new Event(AUTH_INVALID_EVENT))
}
