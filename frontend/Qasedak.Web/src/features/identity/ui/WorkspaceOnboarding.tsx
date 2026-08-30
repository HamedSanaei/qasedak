"use client";

import { useRouter } from "next/navigation";
import { FormEvent, useState } from "react";
import { Button, ButtonLink } from "@/shared/design/Button";
import { FormField } from "@/shared/design/FormField";
import { StatusAlert } from "@/shared/design/Feedback";
import styles from "./WorkspaceOnboarding.module.css";

export function WorkspaceOnboarding({ workspaceReady }: { workspaceReady: boolean }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const form = new FormData(event.currentTarget);
    try {
      const response = await fetch("/web-api/workspace", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ name: form.get("name") }) });
      const body = await response.json() as { message?: string };
      if (!response.ok) { setError(body.message ?? "فضای کاری ساخته نشد."); return; }
      router.replace("/dashboard");
      router.refresh();
    } catch { setError("ارتباط با سرویس برقرار نشد."); }
    finally { setBusy(false); }
  }

  return (
    <main className={styles.page}>
      <section className={styles.card}>
        <div className={styles.brand}><span className={styles.mark}>ق</span>قاصدک</div>
        <p className={styles.step}>آماده‌سازی فضای کاری</p>
        <h1 className={styles.title}>{workspaceReady ? "فضای کاری آماده است" : "فضای کاری خود را بسازید"}</h1>
        <p className={styles.copy}>{workspaceReady ? "می‌توانید وارد داشبورد شوید و صندوق گفتگو را بررسی کنید." : "نامی انتخاب کنید که اعضای تیم آن را به‌راحتی تشخیص دهند. سازنده به‌عنوان مالک ثبت می‌شود."}</p>
        {workspaceReady ? <><StatusAlert tone="success" title="آماده برای شروع">فضای کاری فعال در این مرورگر انتخاب شده است.</StatusAlert><div className={styles.readyAction}><ButtonLink href="/dashboard" fullWidth>ورود به داشبورد</ButtonLink></div></> : (
          <form className={styles.form} onSubmit={submit}>
            {error ? <StatusAlert tone="danger" title="ساخت فضای کاری انجام نشد">{error}</StatusAlert> : null}
            <FormField id="workspace-name" name="name" label="نام فضای کاری" placeholder="مثلاً تیم فروش" required maxLength={128} disabled={busy} />
            <Button type="submit" fullWidth disabled={busy}>{busy ? "در حال ساخت…" : "ایجاد فضای کاری"}</Button>
            <ButtonLink href="/dashboard" variant="secondary" fullWidth>بعداً انجام می‌دهم</ButtonLink>
          </form>
        )}
      </section>
    </main>
  );
}
