/*
 * Pure decision helper for the application entry route (`/`).
 *
 * The frontend keeps auth state in localStorage (see shared/api/identity.ts), so the
 * root redirect must be resolved on the client. readSession() already treats expired or
 * missing sessions as "no session" and clears stale storage, so the only input this
 * helper needs is the session presence. Keeping the decision pure makes the entry
 * behavior deterministically testable without a browser.
 *
 * There is no approved Penpot public landing page yet (design/penpot-sync.json has none),
 * so `/` is strictly an application entry route: it forwards to the dashboard when a
 * valid session exists, otherwise to /login.
 */
export function resolveRootTarget(session: { accessToken: string } | null): string {
  return session ? "/dashboard" : "/login";
}