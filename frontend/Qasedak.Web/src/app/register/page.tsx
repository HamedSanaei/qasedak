"use client";

/*
 * Registration + workspace creation — synchronized from the canonical Penpot board
 * "Identity / Register / Desktop" (c48311ed-e700-80f8-8008-881f075bc2f7, page
 * c48311ed-e700-80f8-8008-881f0352eb6a). Visual layer only; the register → login →
 * createWorkspace flow and validation remain application-owned while the server
 * auth/workspace handlers own session state and transport errors.
 */
import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { Button, TextField } from "../../shared/design/ui";
import { saveSession, saveWorkspaceId } from "../../shared/api/identity";
import {
  validateDisplayName,
  validateEmail,
  validatePassword,
  validateWorkspaceName,
} from "../../features/auth/validation";
import { AuthBrandRow, AuthLayout } from "../../features/auth/AuthLayout";

export default function RegisterPage() {
  const router = useRouter();
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [workspaceName, setWorkspaceName] = useState("");
  const [errors, setErrors] = useState<Record<string, string | null>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);
    const nextErrors: Record<string, string | null> = {
      displayName: validateDisplayName(displayName),
      email: validateEmail(email),
      password: validatePassword(password),
      workspaceName: validateWorkspaceName(workspaceName),
    };
    setErrors(nextErrors);
    if (Object.values(nextErrors).some(Boolean)) return;

    setSubmitting(true);
    try {
      const authResponse = await fetch("/web-api/auth/register", {
        method: "POST",
        headers: { "content-type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({ email: email.trim(), displayName: displayName.trim(), password }),
      });
      const authBody = await authResponse.json() as { accessToken?: string; expiresAtUtc?: string; message?: string };
      if (!authResponse.ok || !authBody.accessToken || !authBody.expiresAtUtc) {
        setFormError(authBody.message ?? "ساخت حساب انجام نشد. دوباره تلاش کنید.");
        return;
      }
      saveSession(authBody.accessToken, authBody.expiresAtUtc);

      const workspaceResponse = await fetch("/web-api/workspace", {
        method: "POST",
        headers: { "content-type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({ name: workspaceName.trim() }),
      });
      const workspaceBody = await workspaceResponse.json() as { workspaceId?: string; message?: string };
      if (!workspaceResponse.ok || !workspaceBody.workspaceId) {
        setFormError(workspaceBody.message ?? "حساب ساخته شد؛ ساخت فضای کاری انجام نشد.");
        return;
      }
      saveWorkspaceId(workspaceBody.workspaceId);
      router.replace("/dashboard");
    } catch {
      setFormError("ارتباط با سرویس برقرار نشد. دوباره تلاش کنید.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthLayout>
      <AuthBrandRow />
      <p style={{ fontSize: 14, fontWeight: 700, color: "var(--color-brand-accent)", margin: "1.5rem 0 .1rem" }}>
        ساخت حساب
      </p>
      <h1 style={{ fontSize: 28, fontWeight: 800, color: "var(--color-text-primary)", margin: "0 0 .25rem" }}>
        شروع با قاصدک
      </h1>
      <p style={{ fontSize: 14, color: "var(--color-text-secondary)", margin: 0 }}>
        یک حساب بسازید؛ سپس فضای کاری خود را ایجاد می‌کنید.
      </p>
      <form onSubmit={handleSubmit} noValidate style={{ display: "grid", gap: "1rem", marginTop: "1.25rem" }}>
        <TextField
          id="displayName"
          label="نام نمایشی"
          autoComplete="name"
          placeholder="مثلاً حامد محمودی"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          error={errors.displayName}
        />
        <TextField
          id="email"
          label="ایمیل"
          type="email"
          dir="ltr"
          autoComplete="email"
          placeholder="name@example.com"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={errors.email}
        />
        <TextField
          id="password"
          label="گذرواژه"
          type="password"
          dir="ltr"
          autoComplete="new-password"
          placeholder="حداقل ۱۰ کاراکتر"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          error={errors.password}
        />
        <div
          style={{
            background: "var(--qs-canvas)",
            border: "1px solid var(--qs-card-border)",
            borderRadius: 12,
            padding: ".7rem .9rem",
            marginTop: "-.25rem",
          }}
        >
          <p style={{ fontSize: 13, fontWeight: 700, color: "var(--color-text-primary)", margin: 0 }}>
            گذرواژه باید:
          </p>
          <div style={{ display: "grid", gap: ".25rem", marginTop: ".35rem" }}>
            <span style={{ display: "flex", alignItems: "center", gap: ".45rem", fontSize: 12, color: "var(--color-text-secondary)" }}>
              <span aria-hidden style={{ width: 6, height: 6, borderRadius: "50%", background: "var(--color-brand-accent)", flexShrink: 0 }} />
              بین ۱۰ تا ۱۲۸ کاراکتر باشد
            </span>
            <span style={{ display: "flex", alignItems: "center", gap: ".45rem", fontSize: 12, color: "var(--color-text-secondary)" }}>
              <span aria-hidden style={{ width: 6, height: 6, borderRadius: "50%", background: "var(--color-brand-accent)", flexShrink: 0 }} />
              فقط از حروف و اعداد تشکیل نشده باشد
            </span>
          </div>
        </div>
        <TextField
          id="workspaceName"
          label="نام ورک‌اسپیس"
          placeholder="مثلاً فروشگاه من"
          value={workspaceName}
          onChange={(e) => setWorkspaceName(e.target.value)}
          error={errors.workspaceName}
        />
        {formError ? (
          <div
            role="alert"
            style={{
              fontSize: 13,
              color: "var(--qs-status-danger)",
              background: "var(--qs-status-danger-bg)",
              borderRadius: 10,
              padding: ".55rem .8rem",
            }}
          >
            {formError}
          </div>
        ) : null}
        <Button type="submit" disabled={submitting} style={{ height: 52 }}>
          {submitting ? "در حال ثبت‌نام…" : "ساخت حساب"}
        </Button>
      </form>
      <p style={{ fontSize: 14, color: "var(--color-text-secondary)", marginTop: "1.25rem", marginBottom: 0 }}>
        قبلاً ثبت‌نام کرده‌اید؟{" "}
        <Link href="/login" style={{ color: "var(--color-brand-accent)", fontWeight: 700 }}>
          ورود
        </Link>
      </p>
    </AuthLayout>
  );
}
