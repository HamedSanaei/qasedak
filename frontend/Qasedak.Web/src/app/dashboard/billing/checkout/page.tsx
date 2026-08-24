"use client";

/*
 * Checkout — synchronized from the canonical Penpot board
 * "Billing / Checkout / Desktop" (c48311ed-e700-80f8-8008-8820b1f8bfe9), page
 * c48311ed-e700-80f8-8008-8820a6cf5187. The plan amount shown is fetched from the
 * server catalog; the checkout POST re-resolves it server-side, so nothing in the
 * query string can influence what is charged.
 */
import { Suspense, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Button, Card, PageHeader } from "../../../../shared/design/ui";
import { billingApi, type PlanSummary } from "../../../../shared/api/billing";
import { readSession, readWorkspaceId } from "../../../../shared/api/identity";
import { formatIrr, providerLabel } from "../../../../features/billing/presentation";

function CheckoutInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const planCode = searchParams.get("plan") ?? "";

  const [state, setState] = useState<"loading" | "error" | "ready">("loading");
  const [plan, setPlan] = useState<PlanSummary | null>(null);
  const [providers, setProviders] = useState<string[]>([]);
  const [providerId, setProviderId] = useState<string>("");
  const [redirecting, setRedirecting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const client = billingApi();

  const load = useCallback(async () => {
    setState("loading");
    setErrorMessage(null);
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) {
        router.replace("/login");
        return;
      }
      if (!planCode) {
        router.replace("/dashboard/billing");
        return;
      }
      const catalog = await client.plans(session.accessToken);
      const found = catalog.items.find((p) => p.code === planCode.toLowerCase()) ?? null;
      if (!found || !found.purchasable) {
        router.replace("/dashboard/billing");
        return;
      }
      setPlan(found);
      setProviders(catalog.providers);
      // Default to the first enabled provider; Melli stays inert until its official contract lands.
      setProviderId(catalog.providers[0] ?? "");
      setState("ready");
    } catch {
      setErrorMessage("دریافت اطلاعات پلن ناموفق بود. دوباره تلاش کنید.");
      setState("error");
    }
  }, [client, planCode, router]);

  useEffect(() => {
    // Deferred so the first setState happens outside the effect body (react-hooks lint).
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  async function startPayment() {
    if (redirecting || !providerId) return;
    setRedirecting(true);
    setErrorMessage(null);
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) {
        router.replace("/login");
        return;
      }
      const result = await client.checkout(session.accessToken, workspaceId, plan!.code, providerId);
      // External provider host — full browser navigation is intentional and expected.
      window.location.href = result.redirectUrl;
    } catch (error) {
      setRedirecting(false);
      const code = error && typeof error === "object" && "code" in error ? String((error as { code: unknown }).code) : null;
      setErrorMessage(
        code === "payment.providerDisabled"
          ? "این درگاه فعلاً غیرفعال است."
          : code === "payment.providerUnknown"
            ? "درگاه انتخابی پشتیبانی نمی‌شود."
            : "ایجاد پرداخت ناموفق بود. دوباره تلاش کنید.",
      );
    }
  }

  return (
    <main dir="rtl">
      <PageHeader title="تکمیل پرداخت" subtitle="پرداخت یک‌باره از طریق درگاه انتخابی انجام می‌شود." />
      <div style={{ display: "flex", flexDirection: "column", gap: "1rem", maxWidth: 720 }}>
        {state === "loading" ? <p style={{ fontSize: 13, color: "#7d7887" }}>در حال آماده‌سازی…</p> : null}
        {state === "error" ? (
          <div role="alert" style={{ color: "#c93c54", fontSize: 13 }}>
            {errorMessage}
          </div>
        ) : null}

        {state === "ready" && plan ? (
          <>
            <Card>
              <h2 style={{ fontSize: 18, color: "#2e2938", margin: 0 }}>خلاصه سفارش</h2>
              <dl style={{ display: "grid", gap: ".4rem", fontSize: 14, margin: ".75rem 0 0" }}>
                <div style={{ display: "flex", justifyContent: "space-between" }}>
                  <dt style={{ color: "#7d7887" }}>پلن انتخابی</dt>
                  <dd style={{ margin: 0, color: "#2e2938" }}>{plan.name}</dd>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between" }}>
                  <dt style={{ color: "#7d7887" }}>مبلغ قابل پرداخت</dt>
                  <dd style={{ margin: 0, fontWeight: 600, color: "#be0183" }}>{formatIrr(plan.amountIrr)}</dd>
                </div>
              </dl>
              <p style={{ fontSize: 12, color: "#7d7887", margin: ".6rem 0 0" }}>
                این مبلغ از سرور خوانده شده و پیش از ایجاد تراکنش دوباره تأیید می‌شود؛ مقدار ارسالی از مرورگر یا آدرس بازگشت نادیده گرفته می‌شود.
              </p>
              <div style={{ background: "#edf3ff", borderRadius: 10, padding: ".6rem .8rem", marginTop: ".75rem" }}>
                <p style={{ fontSize: 12, color: "#2f6fed", margin: 0 }}>پرداخت در محیط امن درگاه بانکی انجام می‌شود.</p>
              </div>
            </Card>

            <Card>
              <h2 style={{ fontSize: 18, color: "#2e2938", margin: 0 }}>انتخاب درگاه پرداخت</h2>
              <div role="radiogroup" aria-label="درگاه پرداخت" style={{ display: "grid", gap: ".6rem", marginTop: ".75rem" }}>
                {["zarinpal", "melli"].map((id) => {
                  const enabled = providers.includes(id);
                  return (
                    <label
                      key={id}
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: ".65rem",
                        border: `1px solid ${providerId === id && enabled ? "#be0183" : "#e3e5e8"}`,
                        borderRadius: 12,
                        padding: ".7rem .9rem",
                        background: providerId === id && enabled ? "#fcebf6" : "#ffffff",
                        cursor: enabled ? "pointer" : "not-allowed",
                        opacity: enabled ? 1 : 0.6,
                      }}
                    >
                      <input
                        type="radio"
                        name="provider"
                        value={id}
                        checked={providerId === id}
                        disabled={!enabled || redirecting}
                        onChange={() => setProviderId(id)}
                      />
                      <span style={{ width: 36, height: 36, borderRadius: 10, background: enabled ? "#fcebf6" : "#ecedef", color: "#be0183", fontSize: 13, display: "inline-flex", alignItems: "center", justifyContent: "center" }}>
                        {id === "zarinpal" ? "زر" : "ملی"}
                      </span>
                      <span>
                        <span style={{ display: "block", fontSize: 14, color: "#2e2938" }}>{providerLabel(id)}</span>
                        <span style={{ display: "block", fontSize: 12, color: "#7d7887" }}>
                          {enabled
                            ? id === "zarinpal"
                              ? "انتقال به صفحه پرداخت زرین‌پال"
                              : "نیازمند قرارداد رسمی بانک ملی"
                            : "غیرفعال — قرارداد رسمی تأیید نشده"}
                        </span>
                      </span>
                    </label>
                  );
                })}
              </div>
              <Button
                variant="primary"
                style={{ width: "100%", marginTop: "1rem" }}
                disabled={!providerId || redirecting}
                onClick={() => void startPayment()}
              >
                {redirecting ? "در حال انتقال به درگاه…" : "ادامه به درگاه"}
              </Button>
              <p style={{ fontSize: 12, color: "#7d7887", margin: ".6rem 0 0" }}>
                با ادامه، به صفحه پرداخت درگاه منتقل می‌شوید و پس از بازگشت، نتیجه از سرور استعلام می‌شود.
              </p>
            </Card>

            <p style={{ fontSize: 13 }}>
              <Link href="/dashboard/billing" style={{ color: "#be0183" }}>بازگشت به انتخاب پلن</Link>
            </p>
          </>
        ) : null}

        {errorMessage && state === "ready" ? (
          <div role="alert" style={{ color: "#c93c54", fontSize: 13 }}>{errorMessage}</div>
        ) : null}
      </div>
    </main>
  );
}

export default function CheckoutPage() {
  return (
    <Suspense fallback={<main dir="rtl"><PageHeader title="تکمیل پرداخت" /></main>}>
      <CheckoutInner />
    </Suspense>
  );
}
