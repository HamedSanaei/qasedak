"use client";

/*
 * Edit automation — draft-only revision via PUT; frozen/terminal states surface the
 * backend's stable codes (automation.versionFrozen / automation.disabled) as 409s.
 */
import { useCallback, useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { AutomationBuilderForm } from "../../../../features/automations/AutomationBuilderForm";
import { Button, Card } from "../../../../shared/design/ui";
import {
  automationsApi,
  type AutomationDetail,
} from "../../../../shared/api/automations";
import { readSession, readWorkspaceId } from "../../../../shared/api/identity";

export default function EditAutomationPage() {
  const params = useParams<{ automationId: string }>();
  const router = useRouter();
  const automationId = params.automationId;
  const [detail, setDetail] = useState<AutomationDetail | null>(null);
  const [state, setState] = useState<"loading" | "error" | "ready">("loading");
  const [frozenNotice, setFrozenNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setState("loading");
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) {
        router.replace("/login");
        return;
      }
      setDetail(await automationsApi().get(session.accessToken, workspaceId, automationId));
      setState("ready");
    } catch {
      setState("error");
    }
  }, [automationId, router]);

  useEffect(() => {
    // Defer so the first setState happens outside the effect body (react-hooks lint).
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  return (
    <main style={{ padding: "1.5rem 2rem", maxWidth: 760 }}>
      <nav aria-label="مسیر" style={{ fontSize: 12, color: "#88828E", marginBottom: ".25rem" }}>
        داشبورد&nbsp;&nbsp;/&nbsp;&nbsp;کامنت و لایو هوشمند&nbsp;&nbsp;/&nbsp;&nbsp;ویرایش
      </nav>
      <h1 style={{ fontSize: 23, fontWeight: 800, color: "var(--color-text-primary)", margin: "0 0 1rem" }}>
        ویرایش دستور
      </h1>

      {state === "loading" ? (
        <p style={{ color: "var(--color-text-secondary)", fontSize: 14 }}>در حال دریافت دستور…</p>
      ) : null}

      {state === "error" ? (
        <Card>
          <div role="alert" style={{ color: "var(--color-status-danger)", fontSize: 14 }}>
            دریافت دستور ناموفق بود.
          </div>
          <div style={{ marginTop: ".75rem" }}>
            <Button variant="outline" size="small" onClick={() => void load()}>تلاش مجدد</Button>
          </div>
        </Card>
      ) : null}

      {state === "ready" && detail ? (
        <>
          {detail.currentVersionFrozen || detail.status !== "Draft" ? (
            <Card>
              <p style={{ margin: 0, fontSize: 13, color: "var(--color-status-warning, #F47B20)" }}>
                این اتوماسیون در وضعیت «{detail.status === "Active" ? "فعال" : "غیرفعال"}» است؛ برای ویرایش ابتدا آن را از فهرست متوقف کنید. نسخه‌های فعال همیشه بدون تغییر باقی می‌مانند.
              </p>
            </Card>
          ) : null}
          {frozenNotice ? (
            <div role="alert" style={{ marginTop: ".75rem", fontSize: 13, color: "var(--color-status-danger)" }}>
              {frozenNotice}
            </div>
          ) : null}
          <AutomationBuilderForm
            initialName={detail.name}
            initialDefinition={detail.definition}
            submitLabel="ذخیره تغییرات"
            onSubmit={async (name, definition) => {
              const session = readSession();
              const workspaceId = readWorkspaceId();
              if (!session || !workspaceId) {
                router.replace("/login");
                return { ok: false, code: null };
              }
              try {
                await automationsApi().update(session.accessToken, workspaceId, automationId, {
                  name,
                  definition,
                });
                router.push("/dashboard/automations");
                return { ok: true };
              } catch (error) {
                const code =
                  error && typeof error === "object" && "code" in error
                    ? String((error as { code: unknown }).code)
                    : null;
                if (code === "automation.versionFrozen") setFrozenNotice("نسخه فعال قفل است؛ ابتدا توقف کنید.");
                return { ok: false, code };
              }
            }}
          />
        </>
      ) : null}
    </main>
  );
}
