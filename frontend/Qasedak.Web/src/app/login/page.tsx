"use client";

/*
 * Sign-in — synchronized from the canonical Penpot board
 * "Identity / Login / Desktop" (c48311ed-e700-80f8-8008-881f0372388a, page
 * c48311ed-e700-80f8-8008-881f0352eb6a) via the extracted contract in
 * docs/design/sync/2026-08-24-qasedak-final-designs.md. Visual layer only;
 * email+password behavior and validation remain application-owned while the
 * server-owned auth handler now establishes the session cookie.
 */
import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { Button, TextField } from "../../shared/design/ui";
import { saveSession } from "../../shared/api/identity";
import {
  validateEmail,
  validatePassword,
} from "../../features/auth/validation";
import { AuthBrandRow, AuthLayout } from "../../features/auth/AuthLayout";

export default function LoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [fieldErrors, setFieldErrors] = useState<{ email?: string | null; password?: string | null }>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);
    const errors = { email: validateEmail(email), password: validatePassword(password) };
    setFieldErrors(errors);
    if (errors.email || errors.password) return;

    setSubmitting(true);
    try {
      const response = await fetch("/web-api/auth/login", {
        method: "POST",
        headers: { "content-type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({ email: email.trim(), password }),
      });
      const body = await response.json() as { accessToken?: string; expiresAtUtc?: string; message?: string };
      if (!response.ok || !body.accessToken || !body.expiresAtUtc) {
        setFormError(body.message ?? "ورود انجام نشد. دوباره تلاش کنید.");
        return;
      }
      // Keep the legacy feature clients working while the HttpOnly cookie is used
      // by server components and guards.
      saveSession(body.accessToken, body.expiresAtUtc);
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
        ورود به حساب
      </p>
      <h1 style={{ fontSize: 28, fontWeight: 800, color: "var(--color-text-primary)", margin: "0 0 .25rem" }}>
        خوش آمدید
      </h1>
      <p style={{ fontSize: 14, color: "var(--color-text-secondary)", margin: 0 }}>
        برای ادامه، اطلاعات حساب قاصدک خود را وارد کنید.
      </p>
      <form onSubmit={handleSubmit} noValidate style={{ display: "grid", gap: "1rem", marginTop: "1.25rem" }}>
        <TextField
          id="email"
          label="ایمیل"
          type="email"
          dir="ltr"
          autoComplete="email"
          placeholder="name@example.com"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={fieldErrors.email}
        />
        <TextField
          id="password"
          label="گذرواژه"
          type="password"
          dir="ltr"
          autoComplete="current-password"
          placeholder="حداقل ۱۰ کاراکتر"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          error={fieldErrors.password}
        />
        <p style={{ fontSize: 12, color: "var(--color-text-secondary)", margin: 0 }}>
          در صورت فراموشی گذرواژه با مدیر فضای کاری تماس بگیرید.
        </p>
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
          {submitting ? "در حال ورود…" : "ورود"}
        </Button>
      </form>
      <div
        style={{
          background: "var(--qs-info-bg)",
          borderRadius: 12,
          padding: ".7rem .9rem",
          marginTop: "1.25rem",
        }}
      >
        <p style={{ fontSize: 13, fontWeight: 700, color: "var(--qs-info)", margin: 0 }}>
          نشانی صفحه را بررسی کنید
        </p>
        <p style={{ fontSize: 12, color: "var(--color-text-secondary)", margin: ".15rem 0 0" }}>
          اطلاعات ورود فقط در دامنه رسمی قاصدک وارد شود.
        </p>
      </div>
      <p style={{ fontSize: 14, color: "var(--color-text-secondary)", marginTop: "1.25rem", marginBottom: 0 }}>
        حساب ندارید؟{" "}
        <Link href="/register" style={{ color: "var(--color-brand-accent)", fontWeight: 700 }}>
          ثبت‌نام
        </Link>
      </p>
    </AuthLayout>
  );
}
