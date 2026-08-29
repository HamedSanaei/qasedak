/*
 * Application-owned API client for the workspace inbox
 * (GET /conversations, GET /conversations/{id}, POST /conversations/{id}/replies).
 */
import { request } from "./http";

export interface ConversationListItem {
  id: string;
  channel: string;
  participantId: string;
  status: string;
  lastMessageAtUtc: string | null;
  unreadCount: number;
  lastMessagePreview: string | null;
}

export interface ConversationMessage {
  id: string;
  direction: string;
  providerMessageId: string | null;
  senderId: string | null;
  body: string;
  occurredAtUtc: string;
}

export interface ConversationDetail {
  id: string;
  channel: string;
  participantId: string;
  status: string;
  lastMessageAtUtc: string | null;
  unreadCount: number;
  messages: ConversationMessage[];
}

export interface ConversationsApi {
  list(
    token: string,
    workspaceId: string,
    options?: { status?: string | null; search?: string | null; page?: number },
  ): Promise<{ page: number; pageSize: number; totalCount: number; items: ConversationListItem[] }>;
  get(token: string, workspaceId: string, conversationId: string): Promise<ConversationDetail>;
  reply(token: string, workspaceId: string, conversationId: string, text: string): Promise<{ messageId: string }>;
}

export function conversationsApi(): ConversationsApi {
  const base = (workspaceId: string) => `/api/v1/workspaces/${workspaceId}/conversations`;
  return {
    list: (token, workspaceId, options = {}) => {
      const params = new URLSearchParams();
      if (options.status) params.set("status", options.status);
      if (options.search) params.set("search", options.search);
      if (options.page) params.set("page", String(options.page));
      const qs = params.toString();
      return request(`${base(workspaceId)}${qs ? `?${qs}` : ""}`, { bearerToken: token });
    },
    get: (token, workspaceId, conversationId) =>
      request(`${base(workspaceId)}/${conversationId}`, { bearerToken: token }),
    reply: (token, workspaceId, conversationId, text) =>
      request(`${base(workspaceId)}/${conversationId}/replies`, {
        method: "POST",
        body: { text },
        bearerToken: token,
      }),
  };
}
