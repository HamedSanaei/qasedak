"use client";

/*
 * Billing plans + current subscription — synchronized from canonical Penpot boards
 * "Billing / Plans / Desktop" (c48311ed-e700-80f8-8008-8820a7020aa1) and
 * "Billing / Current Subscription / Desktop" (c48311ed-e700-80f8-8008-8820adebc780),
 * page c48311ed-e700-80f8-8008-8820a6cf5187. Visual layer only; amounts, plan data,
 * entitlements and payment status are server-authoritative.
 */
import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Button, Card, PageHeader, StatusPill } from "../../../shared/design/ui";
import { billingApi, type PlansResponse, type SubscriptionOverview } from "../../../shared/api/billing";
import { readSession, readWorkspaceId } from "../../../shared/api/identity";
import {
  featureLimitLabel,
  formatIrr,
  providerLabel,
  subscriptionStatusTone,
} from "../../../features/billing/presentation";

const FEATURE_LABELS: Record<string, string> = {
  "automations.active": "اتوماسیون فعال",
  "inbox.ai-replies": "پاسخ هوشمند صندوق گفتگو",
  "contacts.total": "مخاطبان",
};

export default function BillingPage() {
  const router = useRouter();
  const [state, setState] = useState<"loading" | "error" | "ready">("loading");
  const [catalog, setCatalog] = useState<PlansResponse | null>(null);
  const [subscription, setSubscription] = useState<SubscriptionOverview | null>(null);
  const [selectedPlan, setSelectedPlan] = useState<string | null>(null);
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
      const [plansResult, subscriptionResult] = await Promise.all([
        client.plans(session.accessToken),
        client.subscription(session.accessToken, workspaceId),
      ]);
      setCatalog(plansResult);
      setSubscription(subscriptionResult);
      setState("ready");
    } catch {
      setErrorMessage("دریافت اطلاعات اشتراک ناموفق بود. دوباره تلاش کنید.");
      setState("error");
    }
  }, [client, router]);

  useEffect(() => {
    // Deferred so the first setState happens outside the effect body (react-hooks lint).
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  return (
    <main dir="rtl">
      <PageHeader title="انتخاب اشتراک" subtitle="اشتراک قاصدک یک‌باره و از طریق درگاه پرداخت انجام می‌شود." />

      <div style={{ display: "flex", flexDirection: "column", gap: "1rem", maxWidth: 960 }}>
        {subscription ? (
          <Card>
            <div style={{ display: "flex", alignItems: "center", gap: ".75rem", flexWrap: "wrap" }}>
              <span aria-hidden style={{ width: 4, height: 28, background: "#be0183", borderRadius: 4 }} />
              <StatusPill tone={subscriptionStatusTone(subscription.status)}>
                {subscription.status === "Active"
                  ? "فعال"
                  : subscription.status === "Trial"
                    ? "آزمایشی"
                    : subscription.status === "PastDue"
                      ? "سرسید گذشته"
                      : subscription.status === "Canceled"
                        ? "لغو شده"
                        : subscription.status === "Expired"
                          ? "منقضی"
                          : subscription.status}
              </StatusPill>
              <strong style={{ fontSize: 24, color: "#2e2938" }}>
                {subscription.plan ? subscription.plan.name : "بدون پلن"}
              </strong>
              {subscription.entitled ? (
                <span style={{ fontSize: 12, color: "#168b5b" }}>دسترسی برقرار است</span>
              ) : (
                <span style={{ fontSize: 12, color: "#c93c54" }}>دسترسی فعال نیست</span>
              )}
            </div>
            <p style={{ fontSize: 13, color: "#7d7887", marginTop: ".5rem" }}>
              دوره، تاریخ شروع و پایان از سرور
              {subscription.currentPeriodEndUtc
                ? ` — پایان دوره: ${new Date(subscription.currentPeriodEndUtc).toLocaleDateString("fa-IR")}`
                : ""}
            </p>
          </Card>
        ) : null}

        <Card>
          <h2 style={{ fontSize: 18, color: "#2e2938", margin: 0 }}>اشتراک آزمایشی به پرداخت نیاز دارد</h2>
          <p style={{ fontSize: 13, color: "#7d7887", margin: ".35rem 0 0" }}>
            قیمت‌ها و امکانات هر پلن از سرور خوانده می‌شود؛ هیچ عددی در این صفحه منبع حقیقت نیست.
          </p>
        </Card>

        {state === "loading" ? <p style={{ fontSize: 13, color: "#7d7887" }}>در حال دریافت پلن‌ها…</p> : null}
        {state === "error" ? (
          <div role="alert" style={{ color: "#c93c54", fontSize: 13 }}>
            {errorMessage}{" "}
            <button type="button" onClick={() => void load()} style={{ color: "#be0183" }}>
              تلاش دوباره
            </button>
          </div>
        ) : null}

        {state === "ready" && catalog ? (
          <>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))", gap: "1rem" }}>
              {catalog.items.map((plan) => {
                const selected = selectedPlan === plan.code;
                return (
                  <Card key={plan.code}>
                    <h3 style={{ fontSize: 20, color: "#2e2938", margin: 0 }}>{plan.name}</h3>
                    <p style={{ fontSize: 15, color: plan.purchasable ? "#2e2938" : "#a09ba8", margin: ".4rem 0 0" }}>
                      {plan.purchasable ? formatIrr(plan.amountIrr) : "قیمت و دوره از سرور"}
                    </p>
                    <ul style={{ listStyle: "none", padding: 0, margin: ".75rem 0 1rem", display: "grid", gap: ".35rem" }}>
                      {plan.features.map((feature) => (
                        <li key={feature.key} style={{ display: "flex", alignItems: "center", gap: ".45rem", fontSize: 13, color: "#514d5e" }}>
                          <span aria-hidden style={{ width: 18, height: 18, borderRadius: "50%", background: "#e9f7f1", color: "#168b5b", fontSize: 11, display: "inline-flex", alignItems: "center", justifyContent: "center" }}>
                            ✓
                          </span>
                          {FEATURE_LABELS[feature.key] ?? feature.key}: {featureLimitLabel(feature.limit)}
                        </li>
                      ))}
                    </ul>
                    {plan.purchasable ? (
                      selected ? (
                        <Link href={`/dashboard/billing/checkout?plan=${encodeURIComponent(plan.code)}`}>
                          <Button variant="primary" style={{ width: "100%" }}>ادامه به پرداخت</Button>
                        </Link>
                      ) : (
                        <Button variant="outline" style={{ width: "100%" }} onClick={() => setSelectedPlan(plan.code)}>
                          انتخاب پلن
                        </Button>
                      )
                    ) : (
                      <Button variant="outline" disabled style={{ width: "100%" }}>
                        غیرقابل خرید
                      </Button>
                    )}
                  </Card>
                );
              })}
            </div>

            <Card>
              <p style={{ fontSize: 12, color: "#7d7887", margin: 0 }}>
                تمدید با پرداخت درگاه انجام می‌شود؛ برداشت خودکار از کارت در نسخه اول وجود ندارد.
                درگاه‌های فعال: {catalog.providers.map(providerLabel).join("، ") || "—"}.
              </p>
            </Card>
          </>
        ) : null}
      </div>
    </main>
  );
}
