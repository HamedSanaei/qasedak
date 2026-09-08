import type { ConnectionState } from "./health";

export function eligibleAccounts(accounts: readonly ConnectionState[]): ConnectionState[] {
  return accounts.filter((account) => account.health !== "Disconnected");
}

/**
 * Resolves only an explicit deep-link/account choice that belongs to the current workspace list.
 * No fallback-to-first behavior is permitted.
 */
export function resolveExplicitAccountId(accounts: readonly ConnectionState[], requestedId: string | null | undefined): string | null {
  if (!requestedId) return null;
  return eligibleAccounts(accounts).some((account) => account.accountId === requestedId) ? requestedId : null;
}

/** A workspace replacement always invalidates an account selection from the old workspace. */
export function selectionAfterWorkspaceChange(previousWorkspaceId: string | null, nextWorkspaceId: string | null, currentAccountId: string | null): string | null {
  return previousWorkspaceId === nextWorkspaceId ? currentAccountId : null;
}

export function accountLabel(account: ConnectionState): string {
  if (account.username) return `@${account.username}`;
  if (account.displayName) return account.displayName;
  return account.providerIdentity;
}
