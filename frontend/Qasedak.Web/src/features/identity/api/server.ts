import "server-only";
import { cache } from "react";
import { backendJson } from "@/shared/server/backend";

export type IdentityMe = { userId: string; email: string };
export type WorkspaceMembers = { workspaceName: string; members: { userId: string; role: string }[] };

export const getIdentity = cache((token: string) => backendJson<IdentityMe>("/api/v1/identity/me", token));
export const getWorkspaceMembers = cache((token: string, workspaceId: string) => backendJson<WorkspaceMembers>(`/api/v1/workspaces/${workspaceId}/members`, token));
