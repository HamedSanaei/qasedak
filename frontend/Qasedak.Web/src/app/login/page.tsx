"use client";

/*
 * Sign-in — Qasedak product UI built on the M08-001 design foundation.
 *
 * Design-source note (docs/design/sync/M08-002-auth-workspace.md): the canonical
 * Penpot file's only auth boards are GetCode-branded OTP screens (phone credentials,
 * cyan #13A9D4 identity) that do not match this backend's email+password contracts.
 * This screen therefore uses the approved Qasedak token set and is mapped as
 * `draft` pending a human-approved Qasedak auth design.
 */
import { useState } from "react";
import { useRouter } from "next/navigation";
import { Button, Card, TextField } from "../../shared/design/ui";
import { api, ApiError } from "../../shared/api/http";
import { saveSession } from "../../shared/api/identity";
import {
  describeFailure,
  validateEmail,
  validatePassword,
} from "../../features/auth/validation";

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
      const session = await api().login({ email: email.trim(), password });
      saveSession(session.accessToken, session.expiresAtUtc);
      router.replace("/dashboard");
    } catch (error) {
      const code = error instanceof ApiError ? error.code : null;
      setFormError(describeFailure(code));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main
      style={{
        minHeight: "100vh",
        display: "grid",
        placeItems: "center",
        padding: "2rem",
      }}
    >
      <Card className="auth-card">
        <h1 style={{ fontSize: 30, fontWeight: 800, color: "var(--color-heading-plum)", margin: "0 0 .25rem" }}>
          ورود یا ثبت‌نام
        </h1>
        <p style={{ fontSize: 14, color: "var(--color-text-secondary)", marginTop: 0 }}>
          برای مدیریت دایرکت هوشمند وارد حساب خود شوید.
        </p>
        <form onSubmit={handleSubmit} noValidate style={{ display: "grid", gap: "1rem", marginTop: "1.25rem" }}>
          <TextField
            id="email"
            label="ایمیل"
            type="email"
            dir="ltr"
            autoComplete="email"
            placeholder="you@example.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            error={fieldErrors.email}
          />
          <TextField
            id="password"
            label="رمز عبور"
            type="password"
            dir="ltr"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            error={fieldErrors.password}
          />
          {formError ? (
            <div role="alert" style={{ fontSize: 13, color: "var(--color-status-danger)" }}>
              {formError}
            </div>
          ) : null}
          <Button type="submit" disabled={submitting}>
            {submitting ? "در حال ورود…" : "ورود"}
          </Button>
        </form>
        <p style={{ fontSize: 13, color: "var(--color-text-secondary)", marginTop: "1rem" }}>
          حساب ندارید؟{" "}
          <a href="/register" style={{ color: "var(--color-brand-accent)", fontWeight: 700 }}>
            ثبت‌نام کنید
          </a>
        </p>
      </Card>
    </main>
  );
}
