"use client";

/*
 * Automation list — synchronized from canonical board
 * "Comment Automation — List" (f5bf3c2c-b970-8002-8008-874ebb85c7c2) on page
 * f5bf3c2c-b970-8002-8008-874eb9e5a3b1, live-inspected in the approved design pass:
 * breadcrumb 13/400, title 24/800, help banner (accentSoft ؟ chip, 14/700 title,
 * 12/400 body, accent 12/700 link), primary «＋ اضافه کردن دستور», search field,
 * cards with thumbnail ▧ / name 15/700 / «کامنت ← دایرکت» 12/500 / keyword chips
 * 10/600 / ویرایش + حذف 12/700. Lifecycle controls extend the design for the
 * backend's Draft/Active/Disabled model.
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { Button, Card, StatusPill } from "../../../shared/design/ui";
import { automationsApi, type AutomationSummary } from "../../../shared/api/automations";
import { readSession, readWorkspaceId } from "../../../shared/api/identity";
import {
  AUTOMATION_STATUS_LABELS,
  describeAutomationFailure,
} from "../../../features/automations/presentation";

export default function AutomationsPage() {
  const router = useRouter();
  const [items, setItems] = useState<AutomationSummary[] | null>(null);
  const [state, setState] = useState<"loading" | "error" | "ready">("loading");
  const [search, setSearch] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setState("loading");
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) {
        router.replace("/login");
        return;
      }
      const result = await automationsApi().list(session.accessToken, workspaceId);
      setItems(result.items);
      setState("ready");
    } catch {
      setState("error");
    }
  }, [router]);

  useEffect(() => {
    // Defer so the first setState happens outside the effect body (react-hooks lint).
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const filtered = useMemo(() => {
    if (!items) return [];
    const query = search.trim().toLowerCase();
    return query.length === 0 ? items : items.filter((a) => a.name.toLowerCase().includes(query));
  }, [items, search]);

  async function lifecycle(automation: AutomationSummary, action: "activate" | "deactivate" | "remove") {
    setActionError(null);
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) return;
      const api = automationsApi();
      if (action === "remove") await api.remove(session.accessToken, workspaceId, automation.id);
      else await api[action](session.accessToken, workspaceId, automation.id);
      await load();
    } catch (error) {
      const code =
        error && typeof error === "object" && "code" in error
          ? String((error as { code: unknown }).code)
          : null;
      setActionError(describeAutomationFailure(code));
    }
  }

  return (
    <main style={{ padding: "1.5rem 2rem", maxWidth: 900 }}>
      <nav aria-label="مسیر" style={{ fontSize: 13, color: "#88828E", marginBottom: ".25rem" }}>
        داشبورد&nbsp;&nbsp;/&nbsp;&nbsp;کامنت و لایو هوشمند
      </nav>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "1rem" }}>
        <h1 style={{ fontSize: 24, fontWeight: 800, color: "var(--color-text-primary)", margin: 0 }}>
          کامنت و لایو هوشمند
        </h1>
        <Button onClick={() => router.push("/dashboard/automations/new")}>
          <span aria-hidden="true">＋</span> اضافه کردن دستور
        </Button>
      </div>

      <Card>
        <div style={{ display: "flex", gap: ".75rem", alignItems: "flex-start" }}>
          <span
            aria-hidden="true"
            style={{
              display: "inline-flex",
              alignItems: "center",
              justifyContent: "center",
              width: 28,
              height: 28,
              borderRadius: "50%",
              background: "var(--color-accent-softer)",
              color: "var(--color-brand-accent)",
              fontWeight: 800,
              fontSize: 14,
            }}
          >
            ?
          </span>
          <span style={{ flex: 1 }}>
            <span style={{ display: "block", fontSize: 14, fontWeight: 700, color: "#141414" }}>
              آموزش کامنت و لایو هوشمند
            </span>
            <span style={{ display: "block", fontSize: 12, color: "#737373" }}>
              دستورات خودکار برای کامنت پست، ریپلای لایو و ارسال دایرکت تنظیم کنید.
            </span>
          </span>
          <a href="/docs" style={{ fontSize: 12, fontWeight: 700, color: "var(--color-brand-accent)", textDecoration: "none" }}>
            مشاهده آموزش
          </a>
        </div>
      </Card>

      <input
        type="search"
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        placeholder="⌕ دستورات خود را جستجو کنید"
        aria-label="جستجوی اتوماسیون"
        style={{
          width: "100%",
          marginTop: "1rem",
          border: "1px solid var(--color-border-input)",
          borderRadius: "var(--radius-control)",
          padding: ".625rem .875rem",
          font: "inherit",
          fontSize: 13,
        }}
      />

      {state === "loading" ? (
        <p style={{ color: "var(--color-text-secondary)", fontSize: 14 }}>در حال دریافت دستورها…</p>
      ) : null}

      {state === "error" ? (
        <Card>
          <div role="alert" style={{ color: "var(--color-status-danger)", fontSize: 14 }}>
            دریافت دستورها ناموفق بود.
          </div>
          <div style={{ marginTop: ".75rem" }}>
            <Button variant="outline" size="small" onClick={() => void load()}>تلاش مجدد</Button>
          </div>
        </Card>
      ) : null}

      {actionError ? (
        <div role="alert" style={{ marginTop: ".75rem", fontSize: 13, color: "var(--color-status-danger)" }}>
          {actionError}
        </div>
      ) : null}

      {state === "ready" && filtered.length === 0 ? (
        <p style={{ color: "var(--color-text-secondary)", fontSize: 14 }}>
          هنوز دستوری ثبت نشده است؛ با «اضافه کردن دستور» اولین پاسخ خودکار را بسازید.
        </p>
      ) : null}

      <ul style={{ listStyle: "none", margin: "1rem 0 0", padding: 0, display: "grid", gap: ".75rem" }}>
        {filtered.map((automation) => (
          <li key={automation.id}>
            <Card>
              <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
                <span
                  aria-hidden="true"
                  style={{
                    width: 44,
                    height: 44,
                    borderRadius: "var(--radius-chip)",
                    background: "var(--color-accent-soft)",
                    color: "var(--color-brand-accent)",
                    display: "inline-flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontSize: 22,
                  }}
                >
                  ▧
                </span>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ display: "flex", alignItems: "center", gap: ".5rem", flexWrap: "wrap" }}>
                    <strong style={{ fontSize: 15, fontWeight: 700, color: "#141414" }}>{automation.name}</strong>
                    <StatusPill tone={automation.status === "Active" ? "success" : automation.status === "Draft" ? "info" : "neutral"}>
                      {AUTOMATION_STATUS_LABELS[automation.status] ?? automation.status}
                    </StatusPill>
                  </div>
                  <div style={{ fontSize: 12, fontWeight: 500, color: "#737373", marginTop: ".15rem" }}>
                    کامنت ← دایرکت
                  </div>
                  <div style={{ display: "flex", gap: ".375rem", marginTop: ".4rem", flexWrap: "wrap" }}>
                    {(automation.keywordFilters ?? []).slice(0, 6).map((keyword) => (
                      <span
                        key={keyword}
                        style={{
                          fontSize: 10,
                          fontWeight: 600,
                          color: "#514D5E",
                          background: "var(--color-surface-subtle)",
                          border: "1px solid var(--color-border-default)",
                          borderRadius: "999px",
                          padding: ".125rem .5rem",
                        }}
                      >
                        {keyword}
                      </span>
                    ))}
                  </div>
                </div>
                <div style={{ display: "flex", gap: ".5rem" }}>
                  <a
                    href={`/dashboard/automations/${automation.id}`}
                    style={{ fontSize: 12, fontWeight: 700, color: "var(--color-brand-accent)", textDecoration: "none" }}
                  >
                    ویرایش
                  </a>
                  {automation.status === "Active" ? (
                    <button
                      onClick={() => void lifecycle(automation, "deactivate")}
                      className="row-action"
                      style={{ fontSize: 12, fontWeight: 700, color: "#514D5E", border: "none", background: "none", cursor: "pointer" }}
                    >
                      توقف
                    </button>
                  ) : automation.status === "Draft" ? (
                    <button
                      onClick={() => void lifecycle(automation, "activate")}
                      className="row-action"
                      style={{ fontSize: 12, fontWeight: 700, color: "var(--color-status-success)", border: "none", background: "none", cursor: "pointer" }}
                    >
                      فعال‌سازی
                    </button>
                  ) : null}
                  <button
                    onClick={() => void lifecycle(automation, "remove")}
                    className="row-action"
                    style={{ fontSize: 12, fontWeight: 700, color: "var(--color-status-danger)", border: "none", background: "none", cursor: "pointer" }}
                  >
                    حذف
                  </button>
                </div>
              </div>
            </Card>
          </li>
        ))}
      </ul>
    </main>
  );
}
