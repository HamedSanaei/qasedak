"use client";

/*
 * Registration + workspace creation — the approved workspace experience available
 * from the current backend surface (register → login → createWorkspace).
 * See the design-source note in ../login/page.tsx regarding auth boards.
 */
import { useState } from "react";
import { useRouter } from "next/navigation";
import { Button, Card, TextField } from "../../shared/design/ui";
import { api, ApiError } from "../../shared/api/http";
import { saveSession, saveWorkspaceId } from "../../shared/api/identity";
import {
  describeFailure,
  validateDisplayName,
  validateEmail,
  validatePassword,
  validateWorkspaceName,
} from "../../features/auth/validation";

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
      await api().register({
        email: email.trim(),
        displayName: displayName.trim(),
        password,
      });
      const session = await api().login({ email: email.trim(), password });
      saveSession(session.accessToken, session.expiresAtUtc);
      const workspace = await api().createWorkspace(session.accessToken, { name: workspaceName.trim() });
      saveWorkspaceId(workspace.workspaceId);
      router.replace("/dashboard");
    } catch (error) {
      const code = error instanceof ApiError ? error.code : null;
      if (code === "auth.emailTaken") {
        setErrors((prev) => ({ ...prev, email: describeFailure(code) }));
      } else {
        setFormError(describeFailure(code));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main style={{ minHeight: "100vh", display: "grid", placeItems: "center", padding: "2rem" }}>
      <Card className="auth-card">
        <h1 style={{ fontSize: 30, fontWeight: 800, color: "var(--color-heading-plum)", margin: "0 0 .25rem" }}>
          ساخت حساب کاربری
        </h1>
        <p style={{ fontSize: 14, color: "var(--color-text-secondary)", marginTop: 0 }}>
          حساب شما و اولین ورک‌اسپیس در یک مرحله ساخته می‌شود.
        </p>
        <form onSubmit={handleSubmit} noValidate style={{ display: "grid", gap: "1rem", marginTop: "1.25rem" }}>
          <TextField
            id="displayName"
            label="نام نمایشی"
            autoComplete="name"
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
            placeholder="you@example.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            error={errors.email}
          />
          <TextField
            id="password"
            label="رمز عبور"
            type="password"
            dir="ltr"
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            error={errors.password}
          />
          <TextField
            id="workspaceName"
            label="نام ورک‌اسپیس"
            placeholder="مثلاً فروشگاه من"
            value={workspaceName}
            onChange={(e) => setWorkspaceName(e.target.value)}
            error={errors.workspaceName}
          />
          {formError ? (
            <div role="alert" style={{ fontSize: 13, color: "var(--color-status-danger)" }}>
              {formError}
            </div>
          ) : null}
          <Button type="submit" disabled={submitting}>
            {submitting ? "در حال ثبت‌نام…" : "ثبت‌نام و ورود"}
          </Button>
        </form>
        <p style={{ fontSize: 13, color: "var(--color-text-secondary)", marginTop: "1rem" }}>
          قبلاً ثبت‌نام کرده‌اید؟{" "}
          <a href="/login" style={{ color: "var(--color-brand-accent)", fontWeight: 700 }}>
            وارد شوید
          </a>
        </p>
      </Card>
    </main>
  );
}
