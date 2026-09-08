"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { connectionsApi } from "../../shared/api/connections";
import { readSession, readWorkspaceId, WORKSPACE_CHANGED_EVENT } from "../../shared/api/identity";
import type { ConnectionState } from "./health";
import { resolveExplicitAccountId } from "./selection";

export interface InstagramAccountContextState {
  loading: boolean;
  error: string | null;
  accounts: ConnectionState[];
  selectedAccountId: string | null;
  workspaceId: string | null;
  accessToken: string | null;
  selectAccount(accountId: string | null): void;
  reload(): Promise<void>;
}

function queryAccountId(): string | null {
  if (typeof window === "undefined") return null;
  return new URLSearchParams(window.location.search).get("accountId");
}
function replaceAccountQuery(accountId: string | null): void {
  if (typeof window === "undefined") return;
  const url = new URL(window.location.href);
  if (accountId) url.searchParams.set("accountId", accountId);
  else url.searchParams.delete("accountId");
  window.history.replaceState(null, "", `${url.pathname}${url.search}${url.hash}`);
}

export function useInstagramAccountContext(): InstagramAccountContextState {
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [accounts, setAccounts] = useState<ConnectionState[]>([]);
  const [selectedAccountId, setSelectedAccountId] = useState<string | null>(null);
  const [workspaceId, setWorkspaceId] = useState<string | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const workspaceRef = useRef<string | null>(null);

  const reload = useCallback(async () => {
    const session = readSession();
    const nextWorkspaceId = readWorkspaceId();
    if (!session || !nextWorkspaceId) {
      router.replace("/login");
      return;
    }
    const workspaceChanged = workspaceRef.current !== null && workspaceRef.current !== nextWorkspaceId;
    if (workspaceChanged) {
      setSelectedAccountId(null);
      setAccounts([]);
      replaceAccountQuery(null);
    }
    workspaceRef.current = nextWorkspaceId;
    setWorkspaceId(nextWorkspaceId);
    setAccessToken(session.accessToken);
    setLoading(true);
    setError(null);
    try {
      const result = await connectionsApi().list(session.accessToken, nextWorkspaceId, false);
      setAccounts(result.items);
      const requested = workspaceChanged ? null : queryAccountId();
      const resolved = resolveExplicitAccountId(result.items, requested);
      setSelectedAccountId(resolved);
      if (requested && !resolved) replaceAccountQuery(null);
    } catch (cause) {
      const code = cause && typeof cause === "object" && "code" in cause
        ? String((cause as { code: unknown }).code)
        : null;
      setError(code ?? "connections.unavailable");
      setAccounts([]);
      setSelectedAccountId(null);
    } finally {
      setLoading(false);
    }
  }, [router]);
  useEffect(() => {
    const initialTimer = window.setTimeout(() => { void reload(); }, 0);
    const onWorkspaceChanged = () => { void reload(); };
    const onStorage = (event: StorageEvent) => {
      if (event.key === "qasedak.workspaceId") void reload();
    };
    window.addEventListener(WORKSPACE_CHANGED_EVENT, onWorkspaceChanged);
    window.addEventListener("storage", onStorage);
    return () => {
      window.clearTimeout(initialTimer);
      window.removeEventListener(WORKSPACE_CHANGED_EVENT, onWorkspaceChanged);
      window.removeEventListener("storage", onStorage);
    };
  }, [reload]);

  const selectAccount = useCallback((accountId: string | null) => {
    const resolved = resolveExplicitAccountId(accounts, accountId);
    setSelectedAccountId(resolved);
    replaceAccountQuery(resolved);
  }, [accounts]);

  return {
    loading,
    error,
    accounts,
    selectedAccountId,
    workspaceId,
    accessToken,
    selectAccount,
    reload,
  };
}
