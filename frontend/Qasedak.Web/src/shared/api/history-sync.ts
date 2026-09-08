import type { HistorySyncOperation } from "../../features/instagram/history";
import { normalizeHistoryStatus } from "../../features/instagram/history";
import { request } from "./http";

interface RawOperation {
  operationId?: string | null;
  kind?: string | null;
  status?: string | null;
  stage?: number | null;
  conversationsObserved?: number | null;
  messageIdsObserved?: number | null;
  detailsFetched?: number | null;
  messagesImported?: number | null;
  duplicates?: number | null;
  unsupported?: number | null;
  historyWindowLimited?: number | null;
  failureCategory?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  createdAtUtc?: string | null;
}

function normalizeOperation(raw: RawOperation): HistorySyncOperation {
  return {
    operationId: raw.operationId ?? "",
    kind: raw.kind ?? "Unknown",
    status: normalizeHistoryStatus(raw.status),
    stage: raw.stage ?? 0,
    conversationsObserved: raw.conversationsObserved ?? 0,
    messageIdsObserved: raw.messageIdsObserved ?? 0,
    detailsFetched: raw.detailsFetched ?? 0,
    messagesImported: raw.messagesImported ?? 0,
    duplicates: raw.duplicates ?? 0,
    unsupported: raw.unsupported ?? 0,
    historyWindowLimited: raw.historyWindowLimited ?? 0,
    failureCategory: raw.failureCategory ?? null,
    startedAtUtc: raw.startedAtUtc ?? null,
    completedAtUtc: raw.completedAtUtc ?? null,
    createdAtUtc: raw.createdAtUtc ?? "",
  };
}

export function historySyncApi() {
  const base = (workspaceId: string, accountId: string) =>
    `/api/v1/workspaces/${workspaceId}/instagram/connections/${accountId}/history-sync`;
  return {
    get: async (token: string, workspaceId: string, accountId: string): Promise<{ operations: HistorySyncOperation[] }> => {
      const raw = await request<{ operations?: RawOperation[] | null }>(base(workspaceId, accountId), { bearerToken: token });
      return { operations: (raw.operations ?? []).map(normalizeOperation) };
    },
    enqueue: (token: string, workspaceId: string, accountId: string) =>
      request<{ operationId: string; alreadyActive: boolean }>(base(workspaceId, accountId), {
        method: "POST", bearerToken: token,
      }),
  };
}
