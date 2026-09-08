import type { AccountCapabilities } from "../../features/instagram/capabilities";
import { normalizeCapabilityState } from "../../features/instagram/capabilities";
import { request } from "./http";

interface RawCapabilityItem {
  capability?: string | null;
  state?: string | null;
  reasonCode?: string | null;
}

interface RawCapabilities {
  accountId?: string | null;
  capabilities?: RawCapabilityItem[] | null;
}

const CAPABILITY_NAMES = new Set([
  "MediaCatalog", "Analytics", "Messaging", "CommentAutomation", "PrivateReply",
  "PublicReply", "RevealFlow", "FollowGate", "ConversationHistorySync",
]);

export function normalizeCapabilities(raw: unknown): AccountCapabilities {
  const value = (raw ?? {}) as RawCapabilities;
  return {
    accountId: value.accountId ?? "",
    capabilities: (value.capabilities ?? [])
      .filter((item) => item.capability && CAPABILITY_NAMES.has(item.capability))
      .map((item) => ({
        capability: item.capability as AccountCapabilities["capabilities"][number]["capability"],
        state: normalizeCapabilityState(item.state),
        reasonCode: item.reasonCode ?? null,
      })),
  };
}

export function capabilitiesApi() {
  return {
    get: async (token: string, workspaceId: string, accountId: string): Promise<AccountCapabilities> =>
      normalizeCapabilities(await request<unknown>(
        `/api/v1/workspaces/${workspaceId}/instagram/connections/${accountId}/capabilities`,
        { bearerToken: token },
      )),
  };
}
