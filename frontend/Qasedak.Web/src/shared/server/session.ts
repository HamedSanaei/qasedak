import "server-only";
import { cookies } from "next/headers";
import type { NextResponse } from "next/server";

export const SESSION_COOKIE = "qasedak_session";
export const WORKSPACE_COOKIE = "qasedak_workspace";

const cookieBase = {
  httpOnly: true,
  sameSite: "lax" as const,
  secure: process.env.NODE_ENV === "production",
  path: "/",
};

export async function readSession() {
  const store = await cookies();
  return {
    token: store.get(SESSION_COOKIE)?.value ?? null,
    workspaceId: store.get(WORKSPACE_COOKIE)?.value ?? null,
  };
}

export function attachSessionCookie(response: NextResponse, token: string, expiresAtUtc: string) {
  const expires = new Date(expiresAtUtc);
  response.cookies.set(SESSION_COOKIE, token, { ...cookieBase, expires: Number.isNaN(expires.getTime()) ? undefined : expires });
}

export function attachWorkspaceCookie(response: NextResponse, workspaceId: string) {
  response.cookies.set(WORKSPACE_COOKIE, workspaceId, { ...cookieBase, maxAge: 60 * 60 * 24 * 180 });
}

export function clearSessionCookies(response: NextResponse) {
  response.cookies.set(SESSION_COOKIE, "", { ...cookieBase, maxAge: 0 });
  response.cookies.set(WORKSPACE_COOKIE, "", { ...cookieBase, maxAge: 0 });
}
