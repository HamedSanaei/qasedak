"use client";

import { useEffect, useState } from "react";
import { capabilitiesApi } from "@/shared/api/capabilities";
import { insightsApi } from "@/shared/api/insights";
import type { AccountCapabilities } from "@/features/instagram/capabilities";
import { capabilityExplanation, capabilityState } from "@/features/instagram/capabilities";
import type { InstagramOverview, InsightMetricObservation } from "@/features/instagram/insights";
import { AccountSelector } from "@/features/instagram/ui/AccountSelector";
import { InstagramSurfaceNav } from "@/features/instagram/ui/InstagramSurfaceNav";
import { useInstagramAccountContext } from "@/features/instagram/useInstagramAccountContext";
import styles from "@/features/instagram/ui/InstagramSurface.module.css";

const METRIC_LABELS: Record<string, string> = {
  reach: "دسترسی", accounts_engaged: "حساب‌های درگیر", likes: "پسندها", comments: "کامنت‌ها",
  saves: "ذخیره‌ها", shares: "اشتراک‌گذاری", views: "بازدیدها", total_interactions: "کل تعامل‌ها",
  reposts: "بازنشرها", follows_and_unfollows: "دنبال‌کردن و لغو دنبال‌کردن",
};

function metricText(metric: InsightMetricObservation): string {
  if (metric.state === "Available") return new Intl.NumberFormat("fa-IR").format(metric.value ?? 0);
  if (metric.state === "NoData") return "داده‌ای ثبت نشده";
  if (metric.state === "Unsupported") return "پشتیبانی نمی‌شود";
  if (metric.state === "PermissionRequired") return "نیازمند دسترسی";
  return "موقتاً در دسترس نیست";
}
function provenanceLabel(value: string | null): string {
  if (value === "Observed") return "مشاهده مستقیم";
  if (value === "Derived") return "محاسبه‌شده";
  if (value === "Backfilled") return "بازسازی‌شده";
  return "نامشخص";
}

export default function InstagramInsightsPage() {
  const account = useInstagramAccountContext();
  const [capabilities, setCapabilities] = useState<AccountCapabilities | null>(null);
  const [overview, setOverview] = useState<InstagramOverview | null>(null);
  const [loadingData, setLoadingData] = useState(false);
  const [dataError, setDataError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const timer = window.setTimeout(() => {
      setCapabilities(null);
      setOverview(null);
      setDataError(null);
      if (!account.selectedAccountId || !account.accessToken || !account.workspaceId) return;

      const selected = account.selectedAccountId;
      const run = async () => {
        setLoadingData(true);
        try {
          const caps = await capabilitiesApi().get(account.accessToken!, account.workspaceId!, selected);
          if (cancelled) return;
          setCapabilities(caps);
          if (capabilityState(caps, "Analytics") !== "Available") return;
          const value = await insightsApi().getOverview(account.accessToken!, account.workspaceId!, selected);
          if (!cancelled && value.accountId === selected) setOverview(value);
        } catch (cause) {
          if (!cancelled) setDataError(cause && typeof cause === "object" && "code" in cause ? String((cause as { code: unknown }).code) : "insights.unavailable");
        } finally {
          if (!cancelled) setLoadingData(false);
        }
      };
      void run();
    }, 0);
    return () => { cancelled = true; window.clearTimeout(timer); };
  }, [account.selectedAccountId, account.accessToken, account.workspaceId]);
  const analyticsState = capabilityState(capabilities, "Analytics");
  return (
    <main className={styles.page}>
      <div className={styles.headerRow}>
        <div><h1 className={styles.title}>آمار و اینسایت اینستاگرام</h1><p className={styles.subtitle}>نمای دقیق حساب انتخاب‌شده با تفکیک داده موجود، بدون‌داده و دسترسی‌نداشتن.</p></div>
      </div>
      <InstagramSurfaceNav accountId={account.selectedAccountId} />
      <AccountSelector accounts={account.accounts} selectedAccountId={account.selectedAccountId} onChange={account.selectAccount} disabled={account.loading} />

      {account.loading ? <div className={styles.statusBox} role="status" aria-live="polite">در حال دریافت حساب‌ها…</div> : null}
      {account.error ? <div className={`${styles.statusBox} ${styles.error}`} role="alert">دریافت حساب‌ها ناموفق بود.</div> : null}
      {!account.loading && account.accounts.length === 0 ? <div className={styles.statusBox}>حساب متصلی در این ورک‌اسپیس وجود ندارد.</div> : null}
      {!account.selectedAccountId && account.accounts.length > 0 ? <div className={styles.statusBox}>برای نمایش آمار، یک حساب را صریحاً انتخاب کنید.</div> : null}
      {loadingData ? <div className={styles.statusBox} role="status" aria-live="polite">در حال دریافت وضعیت آمار…</div> : null}
      {dataError ? <div className={`${styles.statusBox} ${styles.error}`} role="alert">دریافت آمار ناموفق بود؛ دوباره تلاش کنید.</div> : null}
      {capabilities && analyticsState !== "Available" ? <div className={styles.statusBox}>{capabilityExplanation(analyticsState)}</div> : null}

      {overview ? (
        <div className={styles.grid}>
          <section className={`${styles.card} ${styles.third}`} aria-labelledby="followers-heading">
            <h2 id="followers-heading" className={styles.cardTitle}>دنبال‌کنندگان فعلی</h2>
            <div className={styles.metricValue}>{overview.currentFollowers.state === "Available" || overview.currentFollowers.state === "Stale" ? new Intl.NumberFormat("fa-IR").format(overview.currentFollowers.value ?? 0) : "—"}</div>
            <div className={styles.metricMeta}>وضعیت: {overview.currentFollowers.state === "Stale" ? "قدیمی" : overview.currentFollowers.state === "Available" ? "موجود" : "بدون داده"} · منبع: {provenanceLabel(overview.currentFollowers.provenance)}</div>
          </section>
          <section className={`${styles.card} ${styles.third}`}><h2 className={styles.cardTitle}>پسند رسانه‌ها</h2><div className={styles.metricValue}>{overview.media.likeTotal === null ? "—" : new Intl.NumberFormat("fa-IR").format(overview.media.likeTotal)}</div><div className={styles.metricMeta}>{overview.media.likeTotalComplete ? "جمع کامل در محدوده پاسخ" : "جمع جزئی؛ همه رسانه‌ها/داده‌ها در دسترس نبوده‌اند"}</div></section>
          <section className={`${styles.card} ${styles.third}`}><h2 className={styles.cardTitle}>کامنت رسانه‌ها</h2><div className={styles.metricValue}>{overview.media.commentTotal === null ? "—" : new Intl.NumberFormat("fa-IR").format(overview.media.commentTotal)}</div><div className={styles.metricMeta}>{overview.media.commentTotalComplete ? "جمع کامل در محدوده پاسخ" : "جمع جزئی؛ مقدار را کل قطعی تلقی نکنید"}</div></section>
          <section className={`${styles.card} ${styles.half}`} aria-labelledby="metrics-heading">
            <h2 id="metrics-heading" className={styles.cardTitle}>متریک‌های حساب</h2>
            {overview.accountInsights.length === 0 ? <p className={styles.cardText}>متریکی ثبت نشده است.</p> : (
              <dl className={styles.dataList}>
                {overview.accountInsights.map((metric) => <div key={metric.metric}><dt>{METRIC_LABELS[metric.metric] ?? metric.metric}</dt><dd>{metricText(metric)}</dd></div>)}
              </dl>
            )}
          </section>
          <section className={`${styles.card} ${styles.half}`} aria-labelledby="history-heading">
            <h2 id="history-heading" className={styles.cardTitle}>تاریخچه دنبال‌کنندگان</h2>
            <p className={styles.cardText}>منشأ هر نقطه همیشه به‌صورت متن نمایش داده می‌شود و فقط با رنگ یا hover منتقل نمی‌شود.</p>
            {overview.followerHistory.length === 0 ? <p className={styles.notice}>تاریخچه‌ای ثبت نشده است.</p> : (
              <ol className={styles.chartList} aria-label="متن جایگزین نمودار تاریخچه دنبال‌کنندگان">
                {overview.followerHistory.map((point) => <li key={`${point.date}-${point.provenance}`}>{point.date}: {new Intl.NumberFormat("fa-IR").format(point.value)} — {provenanceLabel(point.provenance)}</li>)}
              </ol>
            )}
          </section>
          <section className={`${styles.card} ${styles.full}`}>
            <h2 className={styles.cardTitle}>رسانه‌های نمونه در Overview</h2>
            <p className={styles.cardText}>وضعیت رسانه: {overview.media.state}. اعداد ناموجود به‌صورت صفر نمایش داده نمی‌شوند.</p>
            {overview.media.items?.length ? <div className={styles.tableScroll}><table className={styles.historyTable}><thead><tr><th>شناسه رسانه</th><th>نوع</th><th>پسند</th><th>کامنت</th></tr></thead><tbody>{overview.media.items.map((item) => <tr key={item.mediaId}><td>{item.mediaId}</td><td>{item.kind}</td><td>{item.likeCount ?? "—"}</td><td>{item.commentCount ?? "—"}</td></tr>)}</tbody></table></div> : <p className={styles.notice}>رسانه‌ای در Overview موجود نیست.</p>}
          </section>
        </div>
      ) : null}
    </main>
  );
}
