import { NextResponse } from "next/server";
import { requestBackend, BackendUnavailableError } from "@/shared/server/backend";
import { attachSessionCookie } from "@/shared/server/session";

type RegistrationPayload = { email?: string; displayName?: string; password?: string };
type LoginResponse = { accessToken?: string; expiresAtUtc?: string };

const registrationMessages: Record<string, string> = {
  "auth.invalidEmail": "ایمیل واردشده معتبر نیست.",
  "auth.invalidDisplayName": "نام نمایشی معتبر نیست.",
  "auth.emailTaken": "برای این ایمیل قبلاً حساب ساخته شده است.",
  "auth.weakPassword": "گذرواژه باید حداقل ۱۰ نویسه و شامل یک نماد باشد.",
};

export async function POST(request: Request) {
  try {
    const payload = await request.json() as RegistrationPayload;
    const register = await requestBackend("/api/v1/identity/register", { method: "POST", headers: { "content-type": "application/json", accept: "application/json" }, body: JSON.stringify(payload) });
    if (!register.ok) {
      const failure = await register.json().catch(() => ({})) as { code?: string };
      return NextResponse.json({ message: registrationMessages[failure.code ?? ""] ?? "ساخت حساب انجام نشد." }, { status: register.status });
    }
    const login = await requestBackend("/api/v1/identity/login", { method: "POST", headers: { "content-type": "application/json", accept: "application/json" }, body: JSON.stringify({ email: payload.email, password: payload.password }) });
    const body = await login.json() as LoginResponse;
    if (!login.ok || !body.accessToken || !body.expiresAtUtc) return NextResponse.json({ message: "حساب ساخته شد؛ برای ادامه وارد شوید." }, { status: 409 });
    const response = NextResponse.json({ authenticated: true, accessToken: body.accessToken, expiresAtUtc: body.expiresAtUtc }, { status: 201 });
    attachSessionCookie(response, body.accessToken, body.expiresAtUtc);
    return response;
  } catch (error) {
    if (error instanceof BackendUnavailableError) return NextResponse.json({ message: "ارتباط با سرویس برقرار نشد." }, { status: 503 });
    return NextResponse.json({ message: "درخواست ثبت‌نام قابل پردازش نیست." }, { status: 400 });
  }
}
