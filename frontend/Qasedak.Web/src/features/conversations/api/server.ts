import "server-only";
import { cache } from "react";
import { backendJson } from "@/shared/server/backend";

export type InboxConversation = {
  id: string;
  channel: string;
  participantId: string;
  status: string;
  lastMessageAtUtc: string;
  unreadCount: number;
  lastMessagePreview: string | null;
};

export type InboxPageData = { page: number; pageSize: number; totalCount: number; items: InboxConversation[] };
export type ConversationMessage = { id: string; direction: "Inbound" | "Outbound"; providerMessageId: string | null; senderId: string; body: string; occurredAtUtc: string };
export type ConversationDetail = { id: string; channel: string; participantId: string; status: string; lastMessageAtUtc: string; unreadCount: number; messages: ConversationMessage[] };

export const getInboxPage = cache((token: string, workspaceId: string, status = "", page = 1) => {
  const query = new URLSearchParams({ page: String(page), pageSize: "25" });
  if (status) query.set("status", status);
  return backendJson<InboxPageData>(`/api/v1/workspaces/${workspaceId}/conversations?${query}`, token);
});

export const getConversation = cache((token: string, workspaceId: string, conversationId: string) => backendJson<ConversationDetail>(`/api/v1/workspaces/${workspaceId}/conversations/${conversationId}`, token));
