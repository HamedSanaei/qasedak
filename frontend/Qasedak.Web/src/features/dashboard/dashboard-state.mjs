export function workspaceState({ selected, ok, status }) {
  if (ok) return "ready";
  if (!selected) return "missing";
  if (status === 503) return "service-error";
  return "unavailable";
}

export function inboxState({ workspaceReady, ok, totalCount, status }) {
  if (!workspaceReady) return "needs-workspace";
  if (ok && totalCount === 0) return "empty";
  if (ok && totalCount > 0) return "has-items";
  if (status === 503) return "service-error";
  return "unavailable";
}
