"use client";

/*
 * Payment result — synchronized from the canonical Penpot board
 * "Billing / Payment Results / Desktop" (c48311ed-e700-80f8-8008-8820b826931b), page
 * c48311ed-e700-80f8-8008-8820a6cf5187. The callback redirect lands here with a
 * coarse state hint only; the authoritative status is always polled from
 * GET /workspaces/{id}/billing/payments/{attemptId}. Pending states keep polling.
 */
import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Button, Card, PageHeader, StatusPill } from "../../../../shared/design/ui";
import { billingApi, type PaymentStatus } from "../../../../shared/api/billing";
import { readSession, readWorkspaceId } from "../../../../shared/api/identity";
import { formatIrr, paymentResultPresentation } from "../../../../features/billing/presentation";

const POLL_INTERVAL_MS = 2500;
const MAX_POLLS = 12;

function ResultInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const stateHint = searchParams.get("state");
  const attemptIdParam = searchParams.get("attempt");

  const [status, setStatus] = useState<PaymentStatus | null>(null);
  const [loadState, setLoadState] = useState<"loading" | "ready" | "unavailable">("loading");
  const [pollsExhausted, setPollsExhausted] = useState(false);
  const pollCountRef = useRef(0);

  const client = billingApi();

  const fetchStatus = useCallback(async () => {
    const session = readSession();
    const workspaceId = readWorkspaceId();
    if (!session || !workspaceId) {
      router.replace("/login");
      return null;
    }
    if (!attemptIdParam) return null;
    return client.paymentStatus(session.accessToken, workspaceId, attemptIdParam);
  }, [attemptIdParam, client, router]);

  const refresh = useCallback(async () => {
    try {
      const result = await fetchStatus();
      setStatus(result);
      setLoadState(result || !attemptIdParam ? "ready" : "unavailable");
      return result;
    } catch {
      setLoadState("unavailable");
      return null;
    }
  }, [attemptIdParam, fetchStatus]);

  useEffect(() => {
    // Deferred initial load (react-hooks lint) + bounded polling while Pending.
    let timer: number | undefined;
    let cancelled = false;
    const tick = async () => {
      const result = await refresh();
      if (cancelled) return;
      if (!result || result.status !== "Pending") return;
      pollCountRef.current += 1;
      if (pollCountRef.current >= MAX_POLLS) setPollsExhausted(true);
      timer = window.setTimeout(() => void tick(), POLL_INTERVAL_MS);
    };
    timer = window.setTimeout(() => void tick(), 0);
    return () => {
      cancelled = true;
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [refresh]);

  const presentation = paymentResultPresentation(
    stateHint,
    status?.status ?? (pollsExhausted ? "Pending" : null),
    status?.failureCode ?? null,
  );

  const toneColor =
    presentation.tone === "success"
      ? "#168b5b"
      : presentation.tone === "danger"
        ? "#c93c54"
        : presentation.tone === "warning"
          ? "#a8640a"
          : "#2f6fed";
  const toneBg =
    presentation.tone === "success"
      ? "#e9f7f1"
      : presentation.tone === "danger"
        ? "#fff0f3"
        : presentation.tone === "warning"
          ? "#fff6e6"
          : "#edf3ff";

  return (
    <main dir="rtl">
      <PageHeader title="نتیجه پرداخت" subtitle="وضعیت نهایی همیشه از سرور استعلام می‌شود." />
      <div style={{ display: "flex", flexDirection: "column", gap: "1rem", maxWidth: 640 }}>
        <Card>
          <div style={{ background: toneBg, borderRadius: 14, padding: "1.1rem 1.2rem", textAlign: "center" }}>
            <span
              aria-hidden
              style={{
                width: 44,
                height: 44,
                borderRadius: "50%",
                background: "#ffffff",
                color: toneColor,
                fontSize: 20,
                fontWeight: 700,
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                marginBottom: ".5rem",
              }}
            >
              {presentation.tone === "success" ? "✓" : presentation.tone === "info" ? "…" : "×"}
            </span>
            <h2 style={{ fontSize: 22, color: "#2e2938", margin: ".25rem 0 .35rem" }}>{presentation.title}</h2>
            <p style={{ fontSize: 13, color: "#7d7887", margin: 0 }}>{presentation.body}</p>
          </div>

          {loadState === "loading" ? (
            <p style={{ fontSize: 13, color: "#7d7887", marginTop: ".75rem" }}>در حال دریافت وضعیت…</p>
          ) : null}
          {loadState === "unavailable" ? (
            <div role="alert" style={{ color: "#c93c54", fontSize: 13, marginTop: ".75rem" }}>
              وضعیت این پرداخت پیدا نشد؛ از تاریخچه پرداخت وضعیت دقیق را ببینید.
            </div>
          ) : null}

          {status ? (
            <dl style={{ display: "grid", gap: ".4rem", fontSize: 13, marginTop: ".9rem" }}>
              <div style={{ display: "flex", justifyContent: "space-between" }}>
                <dt style={{ color: "#7d7887" }}>مبلغ</dt>
                <dd style={{ margin: 0, color: "#2e2938" }}>{formatIrr(status.amountIrr)}</dd>
              </div>
              <div style={{ display: "flex", justifyContent: "space-between" }}>
                <dt style={{ color: "#7d7887" }}>درگاه</dt>
                <dd style={{ margin: 0, color: "#2e2938" }}>{status.provider}</dd>
              </div>
              <div style={{ display: "flex", justifyContent: "space-between" }}>
                <dt style={{ color: "#7d7887" }}>وضعیت سرور</dt>
                <dd style={{ margin: 0 }}>
                  <StatusPill tone={presentation.tone}>{presentation.title}</StatusPill>
                </dd>
              </div>
            </dl>
          ) : null}

          {presentation.state === "pending" && loadState === "ready" ? (
            <div aria-hidden style={{ height: 8, borderRadius: 5, background: "#ecedef", overflow: "hidden", marginTop: ".9rem" }}>
              <div style={{ width: "40%", height: "100%", background: "#2f6fed", borderRadius: 5 }} />
            </div>
          ) : null}
        </Card>

        <div style={{ display: "flex", gap: ".6rem", flexWrap: "wrap" }}>
          {presentation.state === "success" ? (
            <Link href="/dashboard/billing">
              <Button variant="primary">مشاهده اشتراک</Button>
            </Link>
          ) : null}
          {(presentation.state === "failed" || presentation.state === "cancelled") && status ? (
            <Link href={`/dashboard/billing/checkout?plan=`}>
              <Button variant="primary">تلاش دوباره</Button>
            </Link>
          ) : null}
          {presentation.state === "alreadyVerified" ? (
            <span style={{ fontSize: 12, color: "#7d7887", alignSelf: "center" }}>
              این پرداخت قبلاً بررسی شده؛ اشتراک دوباره تمدید نمی‌شود.
            </span>
          ) : null}
          <Link href="/dashboard/billing">
            <Button variant="outline">بازگشت به صورتحساب</Button>
          </Link>
        </div>
        <p style={{ fontSize: 12, color: "#a09ba8" }}>
          اگر پرداخت موفق بوده باشد، سطح دسترسی فضای کاری پس از تأیید سرور به‌صورت خودکار به‌روز می‌شود.
        </p>
      </div>
    </main>
  );
}

export default function PaymentResultPage() {
  return (
    <Suspense fallback={<main dir="rtl"><PageHeader title="نتیجه پرداخت" /></main>}>
      <ResultInner />
    </Suspense>
  );
}
