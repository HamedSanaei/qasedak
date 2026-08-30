import { NextRequest, NextResponse } from "next/server";
import { BackendUnavailableError, requestBackend } from "@/shared/server/backend";
import { attachSessionCookie, attachWorkspaceCookie, clearSessionCookies, readSession } from "@/shared/server/session";

function mutationOriginIsSafe(request: NextRequest) {
  if (request.method === "GET" || request.method === "HEAD" || request.method === "OPTIONS") return true;
  const origin = request.headers.get("origin");
  return !origin || origin === request.nextUrl.origin;
}

async function proxy(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  if (!mutationOriginIsSafe(request)) return NextResponse.json({ message: "درخواست نامعتبر است." }, { status: 403 });
  const { path } = await context.params;
  const safePath = path.map((segment) => encodeURIComponent(segment)).join("/");
  const target = `/api/v1/${safePath}${request.nextUrl.search}`;
  const session = await readSession();
  const headers = new Headers();
  headers.set("accept", request.headers.get("accept") ?? "application/json");
  const contentType = request.headers.get("content-type");
  if (contentType) headers.set("content-type", contentType);
  // Keep compatibility with the original client-side API contract while the
  // richer server-side shell uses HttpOnly cookies. Cookie auth wins when it is
  // available; otherwise forward the bearer token supplied by legacy screens.
  const authorization = session.token ? `Bearer ${session.token}` : request.headers.get("authorization");
  if (authorization) headers.set("authorization", authorization);
  try {
    const body = request.method === "GET" || request.method === "HEAD" ? undefined : await request.arrayBuffer();
    const backend = await requestBackend(target, { method: request.method, headers, body });
    const responseBody = await backend.arrayBuffer();
    const response = new NextResponse(responseBody, { status: backend.status, headers: { "content-type": backend.headers.get("content-type") ?? "application/json" } });
    if (backend.status === 401) clearSessionCookies(response);
    if (backend.ok && request.method === "POST") {
      try {
        const data = JSON.parse(new TextDecoder().decode(responseBody)) as { accessToken?: string; expiresAtUtc?: string; workspaceId?: string };
        if (path.length === 2 && path[0] === "identity" && path[1] === "login" && data.accessToken && data.expiresAtUtc) {
          attachSessionCookie(response, data.accessToken, data.expiresAtUtc);
        } else if (path.length === 1 && path[0] === "workspaces" && data.workspaceId) {
          attachWorkspaceCookie(response, data.workspaceId);
        }
      } catch {
        // Non-JSON success responses keep their original body and status.
      }
    }
    return response;
  } catch (error) {
    if (error instanceof BackendUnavailableError) return NextResponse.json({ message: "ارتباط با سرویس برقرار نشد." }, { status: 503 });
    return NextResponse.json({ message: "درخواست قابل پردازش نیست." }, { status: 502 });
  }
}

export const GET = proxy;
export const POST = proxy;
export const PUT = proxy;
export const PATCH = proxy;
export const DELETE = proxy;
