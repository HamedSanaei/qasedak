/*
 * Application-owned API client for workspace automations (M08-005 surface).
 */
import { request } from "./http";

export interface AutomationDefinitionDto {
  triggerKind: string;
  keywordFilters: string[];
  conditions: Array<{ field: string; operator: string; expectedValue: string }>;
  actions: Array<{ kind: string; messageText: string }>;
}

export interface AutomationSummary {
  id: string;
  name: string;
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
  create(
    token: string,
    workspaceId: string,
    body: { name: string; definition: AutomationDefinitionDto },
  ): Promise<AutomationDetail>;
  update(
    token: string,
    workspaceId: string,
    automationId: string,
    body: { name?: string; definition: AutomationDefinitionDto },
  ): Promise<AutomationDetail>;
  activate(token: string, workspaceId: string, automationId: string): Promise<AutomationDetail>;
  deactivate(token: string, workspaceId: string, automationId: string): Promise<AutomationDetail>;
  remove(token: string, workspaceId: string, automationId: string): Promise<void>;
}

export function automationsApi(): AutomationsApi {
  const base = (workspaceId: string) => `/api/v1/workspaces/${workspaceId}/automations`;
  return {
    list: (token, workspaceId) => request(`${base(workspaceId)}`, { bearerToken: token }),
    get: (token, workspaceId, automationId) =>
      request(`${base(workspaceId)}/${automationId}`, { bearerToken: token }),
    create: (token, workspaceId, body) =>
      request(`${base(workspaceId)}`, { method: "POST", body, bearerToken: token }),
    update: (token, workspaceId, automationId, body) =>
      request(`${base(workspaceId)}/${automationId}`, { method: "PUT", body, bearerToken: token }),
    activate: (token, workspaceId, automationId) =>
      request(`${base(workspaceId)}/${automationId}/activate`, { method: "POST", bearerToken: token }),
    deactivate: (token, workspaceId, automationId) =>
      request(`${base(workspaceId)}/${automationId}/deactivate`, { method: "POST", bearerToken: token }),
    remove: async (token, workspaceId, automationId) => {
      await request(`${base(workspaceId)}/${automationId}`, { method: "DELETE", bearerToken: token });
    },
  };
}
