/* Application-owned Automation Builder V2 HTTP contract (M13-014 over M13-012). */
import { request } from "./http";

export type AutomationTriggerKind = "CommentCreated" | "InboundDirectMessage";
export type AutomationTextMatchMode = "EveryEvent" | "Keywords";
export type AutomationSourceScope = "AnySource" | "SpecificSource";
export type AutomationActionKind =
  | "SendDirectMessage" // persisted legacy value-1; presentation depends on trigger
  | "SendPrivateReply"
  | "DirectMessage"
  | "StartRevealFlow"
  | "SendPublicReply"
  | "ScheduleFollowUp";

export interface RevealActionDto {
  gatePromptText: string;
  postbackButtonTitle: string;
  revealText: string;
  followUrl: string | null;
  followButtonTitle: string | null;
  followGateMode: "Disabled" | "EnabledWhenSupported";
}

export interface AutomationActionDto {
  kind: string;
  messageText: string;
  delayMinutes?: number | null;
  reveal?: RevealActionDto | null;
  extras?: { delayMinutes: number | null; reveal: RevealActionDto | null } | null;
}
export interface AutomationDefinitionDto {
  triggerKind: string;
  keywordFilters: string[];
  textMatchMode?: string;
  wholeWord?: boolean;
  sourceScope?: string;
  sourceMediaId?: string | null;
  conditions: Array<{ field: string; operator: string; expectedValue: string }>;
  actions: AutomationActionDto[];
}

export interface AutomationSummary {
  id: string;
  name: string;
  channelAccountId: string | null;
  status: string;
  currentVersionNumber: number;
  triggerKind: string;
  keywordFilters: string[];
  actionCount: number;
  createdAtUtc: string;
  activatedAtUtc: string | null;
}

export interface AutomationDetail {
  id: string;
  name: string;
  channelAccountId: string | null;
  status: string;
  currentVersionNumber: number;
  currentVersionFrozen: boolean;
  createdAtUtc: string;
  activatedAtUtc: string | null;
  disabledAtUtc: string | null;
  definition: AutomationDefinitionDto;
}
export interface AutomationsApi {
  list(token: string, workspaceId: string): Promise<{ items: AutomationSummary[] }>;
  get(token: string, workspaceId: string, automationId: string): Promise<AutomationDetail>;
  create(token: string, workspaceId: string, body: { name: string; channelAccountId: string | null; definition: AutomationDefinitionDto }): Promise<AutomationDetail>;
  update(token: string, workspaceId: string, automationId: string, body: { name?: string; channelAccountId?: string | null; definition: AutomationDefinitionDto }): Promise<AutomationDetail>;
  activate(token: string, workspaceId: string, automationId: string): Promise<AutomationDetail>;
  deactivate(token: string, workspaceId: string, automationId: string): Promise<AutomationDetail>;
  remove(token: string, workspaceId: string, automationId: string): Promise<void>;
}

export function automationsApi(): AutomationsApi {
  const base = (workspaceId: string) => `/api/v1/workspaces/${workspaceId}/automations`;
  return {
    list: (token, workspaceId) => request(`${base(workspaceId)}`, { bearerToken: token }),
    get: (token, workspaceId, automationId) => request(`${base(workspaceId)}/${automationId}`, { bearerToken: token }),
    create: (token, workspaceId, body) => request(`${base(workspaceId)}`, { method: "POST", body, bearerToken: token }),
    update: (token, workspaceId, automationId, body) => request(`${base(workspaceId)}/${automationId}`, { method: "PUT", body, bearerToken: token }),
    activate: (token, workspaceId, automationId) => request(`${base(workspaceId)}/${automationId}/activate`, { method: "POST", bearerToken: token }),
    deactivate: (token, workspaceId, automationId) => request(`${base(workspaceId)}/${automationId}/deactivate`, { method: "POST", bearerToken: token }),
    remove: async (token, workspaceId, automationId) => {
      await request(`${base(workspaceId)}/${automationId}`, { method: "DELETE", bearerToken: token });
    },
  };
}
