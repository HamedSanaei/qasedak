import { NextResponse } from "next/server";
import { requestBackend, BackendUnavailableError } from "@/shared/server/backend";
import { attachSessionCookie } from "@/shared/server/session";

type LoginResponse = { accessToken?: string; expiresAtUtc?: string };

export async function POST(request: Request) {
  try {
    const payload = await request.text();
    const backend = await requestBackend("/api/v1/identity/login", { method: "POST", headers: { "content-type": "application/json", accept: "application/json" }, body: payload });
    const body = await backend.json() as LoginResponse;
    if (!backend.ok || !body.accessToken || !body.expiresAtUtc) return NextResponse.json({ message: "ایمیل یا گذرواژه درست نیست." }, { status: backend.status });
    const response = NextResponse.json({ authenticated: true, expiresAtUtc: body.expiresAtUtc });
    attachSessionCookie(response, body.accessToken, body.expiresAtUtc);
    return response;
  } catch (error) {
    if (error instanceof BackendUnavailableError) return NextResponse.json({ message: "ارتباط با سرویس برقرار نشد." }, { status: 503 });
    return NextResponse.json({ message: "درخواست ورود قابل پردازش نیست." }, { status: 400 });
  }
}
