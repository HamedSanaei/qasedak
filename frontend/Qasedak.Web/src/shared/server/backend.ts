import "server-only";

export class BackendUnavailableError extends Error {
  constructor() {
    super("Qasedak backend is unavailable");
    this.name = "BackendUnavailableError";
  }
}

function backendBaseUrl() {
  const value = process.env.QASEDAK_API_INTERNAL_URL?.trim();
  if (!value) throw new BackendUnavailableError();
  return value.endsWith("/") ? value.slice(0, -1) : value;
}

export async function requestBackend(path: string, init: RequestInit = {}) {
  if (!/^\/api\/v1(?:\/|$)/.test(path)) throw new Error("Backend path must stay under /api/v1");
  try {
    return await fetch(`${backendBaseUrl()}${path}`, { ...init, cache: "no-store" });
  } catch (error) {
    if (error instanceof BackendUnavailableError) throw error;
    throw new BackendUnavailableError();
  }
}

export type ApiResult<T> = { ok: true; status: number; data: T } | { ok: false; status: number; data: null };

export async function backendJson<T>(path: string, token?: string, init: RequestInit = {}): Promise<ApiResult<T>> {
  try {
    const headers = new Headers(init.headers);
    headers.set("accept", "application/json");
    if (token) headers.set("authorization", `Bearer ${token}`);
    const response = await requestBackend(path, { ...init, headers });
    if (!response.ok) return { ok: false, status: response.status, data: null };
    return { ok: true, status: response.status, data: await response.json() as T };
  } catch (error) {
    if (error instanceof BackendUnavailableError) return { ok: false, status: 503, data: null };
    throw error;
  }
}
