export type WorkspaceState = "ready" | "missing" | "service-error" | "unavailable";
export type InboxState = "needs-workspace" | "empty" | "has-items" | "service-error" | "unavailable";
export function workspaceState(input: { selected: boolean; ok: boolean; status?: number }): WorkspaceState;
export function inboxState(input: { workspaceReady: boolean; ok: boolean; totalCount: number; status?: number }): InboxState;
