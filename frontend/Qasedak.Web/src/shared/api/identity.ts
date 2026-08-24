/*
 * Identity/workspace contracts mirroring the backend HTTP surface
 * (register/login/me, workspace creation and member listing endpoints).
 * Application-owned; never touched by Penpot re-syncs.
 */

export interface RegisterRequest {
  email: string;
  displayName: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface CreateWorkspaceRequest {
  name: string;
}

export interface IdentityApi {
  register(body: RegisterRequest): Promise<{ userId: string }>;
  login(body: LoginRequest): Promise<{ accessToken: string; expiresAtUtc: string }>;
  me(
    token: string,
  ): Promise<{ userId: string; email: string }>;
  createWorkspace(token: string, body: CreateWorkspaceRequest): Promise<{ workspaceId: string; name: string }>;
  listMembers(
    token: string,
    workspaceId: string,
  ): Promise<{ workspaceName: string; members: { userId: string; role: string }[] }>;
}

/** Session persistence keys — the token never leaves localStorage except as a header. */
const TOKEN_KEY = "qasedak.accessToken";
const EXPIRY_KEY = "qasedak.expiresAtUtc";

function safeStorage(): Storage | null {
  try {
    if (typeof window === "undefined") return null;
    return window.localStorage;
  } catch {
    return null;
  }
}

export function saveSession(accessToken: string, expiresAtUtc: string): void {
  safeStorage()?.setItem(TOKEN_KEY, accessToken);
  safeStorage()?.setItem(EXPIRY_KEY, expiresAtUtc);
}

export function clearSession(): void {
  safeStorage()?.removeItem(TOKEN_KEY);
  safeStorage()?.removeItem(EXPIRY_KEY);
}

export function readSession(): { accessToken: string } | null {
  const storage = safeStorage();
  const token = storage?.getItem(TOKEN_KEY) ?? null;
  const expiry = storage?.getItem(EXPIRY_KEY) ?? null;
  if (!token || !expiry) return null;
  if (Date.parse(expiry) <= Date.now()) {
    clearSession();
    return null;
  }
  return { accessToken: token };
}

export function readWorkspaceId(): string | null {
  return safeStorage()?.getItem("qasedak.workspaceId") ?? null;
}

export function saveWorkspaceId(workspaceId: string): void {
  safeStorage()?.setItem("qasedak.workspaceId", workspaceId);
}
