"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useState } from "react";
import { Button } from "@/shared/design/Button";
import { FormField } from "@/shared/design/FormField";
import { StatusAlert } from "@/shared/design/Feedback";
import styles from "./AuthShell.module.css";

type FormErrors = Record<string, string>;

export function LoginForm({ sessionExpired = false }: { sessionExpired?: boolean }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const form = new FormData(event.currentTarget);
    try {
      const response = await fetch("/web-api/auth/login", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ email: form.get("email"), password: form.get("password") }) });
      const body = await response.json() as { message?: string };
      if (!response.ok) { setError(body.message ?? "ورود انجام نشد."); return; }
      router.replace("/dashboard");
      router.refresh();
    } catch { setError("ارتباط با سرویس برقرار نشد. دوباره تلاش کنید."); }
    finally { setBusy(false); }
  }

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      {sessionExpired ? <StatusAlert tone="warning" title="نشست شما پایان یافته است">برای ادامه دوباره وارد شوید.</StatusAlert> : null}
      {error ? <StatusAlert tone="danger" title="ورود ناموفق">{error}</StatusAlert> : null}
      <FormField id="login-email" name="email" label="ایمیل" type="email" autoComplete="email" placeholder="name@example.com" required disabled={busy} />
      <FormField id="login-password" name="password" label="گذرواژه" type="password" autoComplete="current-password" placeholder="حداقل ۱۰ نویسه" required minLength={10} disabled={busy} hint="در صورت فراموشی گذرواژه با مدیر فضای کاری تماس بگیرید." />
      <Button type="submit" fullWidth disabled={busy}>{busy ? "در حال ورود…" : "ورود"}</Button>
      <p className={styles.switch}>حساب ندارید؟ <Link href="/register">ثبت‌نام</Link></p>
      <p className={styles.security}>نشانی صفحه را بررسی کنید. اطلاعات ورود فقط در دامنه رسمی قاصدک دریافت می‌شود.</p>
    </form>
  );
}

export function RegisterForm() {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [errors, setErrors] = useState<FormErrors>({});

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const password = String(form.get("password") ?? "");
    const confirmPassword = String(form.get("confirmPassword") ?? "");
    const nextErrors: FormErrors = {};
    if (password.length < 10 || /^[\p{L}\p{N}]+$/u.test(password)) nextErrors.password = "گذرواژه باید حداقل ۱۰ نویسه و شامل یک نماد باشد.";
    if (password !== confirmPassword) nextErrors.confirmPassword = "تکرار گذرواژه یکسان نیست.";
    setErrors(nextErrors);
    if (Object.keys(nextErrors).length) return;
    setBusy(true);
    setError(null);
    try {
      const response = await fetch("/web-api/auth/register", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ displayName: form.get("displayName"), email: form.get("email"), password }) });
      const body = await response.json() as { message?: string };
      if (!response.ok) { setError(body.message ?? "ساخت حساب انجام نشد."); return; }
      router.replace("/onboarding/workspace");
      router.refresh();
    } catch { setError("ارتباط با سرویس برقرار نشد. دوباره تلاش کنید."); }
    finally { setBusy(false); }
  }

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      {error ? <StatusAlert tone="danger" title="ثبت‌نام انجام نشد">{error}</StatusAlert> : null}
      <FormField id="register-name" name="displayName" label="نام و نام خانوادگی" autoComplete="name" placeholder="نام شما" required maxLength={128} disabled={busy} />
      <FormField id="register-email" name="email" label="ایمیل" type="email" autoComplete="email" placeholder="name@example.com" required disabled={busy} />
      <FormField id="register-password" name="password" label="گذرواژه" type="password" autoComplete="new-password" required minLength={10} maxLength={128} disabled={busy} error={errors.password} />
      <FormField id="register-confirm" name="confirmPassword" label="تکرار گذرواژه" type="password" autoComplete="new-password" required disabled={busy} error={errors.confirmPassword} />
      <Button type="submit" fullWidth disabled={busy}>{busy ? "در حال ساخت حساب…" : "ساخت حساب"}</Button>
      <p className={styles.switch}>قبلاً ثبت‌نام کرده‌اید؟ <Link href="/login">ورود</Link></p>
    </form>
  );
}
