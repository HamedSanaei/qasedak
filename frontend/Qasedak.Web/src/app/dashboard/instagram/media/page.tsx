"use client";

import { useEffect, useState } from "react";
import { Button } from "@/shared/design/ui";
import { capabilitiesApi } from "@/shared/api/capabilities";
import { mediaApi } from "@/shared/api/media";
import type { AccountCapabilities } from "@/features/instagram/capabilities";
import { capabilityExplanation, capabilityState } from "@/features/instagram/capabilities";
import type { MediaPickerItem } from "@/features/instagram/media";
import { AccountSelector } from "@/features/instagram/ui/AccountSelector";
import { InstagramSurfaceNav } from "@/features/instagram/ui/InstagramSurfaceNav";
import { useInstagramAccountContext } from "@/features/instagram/useInstagramAccountContext";
import styles from "@/features/instagram/ui/InstagramSurface.module.css";

function kindLabel(kind: string): string {
  if (kind === "Image") return "تصویر";
  if (kind === "Video") return "ویدیو";
  if (kind === "Reel") return "ریل";
  if (kind === "Carousel") return "آلبوم";
  return "نوع ناشناخته";
}

export default function InstagramMediaPage() {
  const account = useInstagramAccountContext();
  const [capabilities, setCapabilities] = useState<AccountCapabilities | null>(null);
  const [items, setItems] = useState<MediaPickerItem[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [selectedMediaId, setSelectedMediaId] = useState<string | null>(null);
  const [loadingData, setLoadingData] = useState(false);
  const [dataError, setDataError] = useState<string | null>(null);
  useEffect(() => {
    let cancelled = false;
    const timer = window.setTimeout(() => {
      setCapabilities(null);
      setItems([]);
      setNextCursor(null);
      setHasMore(false);
      setSelectedMediaId(null);
      setDataError(null);
      if (!account.selectedAccountId || !account.accessToken || !account.workspaceId) return;

      const selectedAccountId = account.selectedAccountId;
      const run = async () => {
        setLoadingData(true);
        try {
          const caps = await capabilitiesApi().get(account.accessToken!, account.workspaceId!, selectedAccountId);
          if (cancelled) return;
          setCapabilities(caps);
          if (capabilityState(caps, "MediaCatalog") !== "Available") return;
          const page = await mediaApi().listMedia(account.accessToken!, account.workspaceId!, selectedAccountId, { limit: 24 });
          if (cancelled || account.selectedAccountId !== selectedAccountId) return;
          setItems(page.items);
          setNextCursor(page.nextCursor);
          setHasMore(page.hasMore);
        } catch (cause) {
          if (!cancelled) setDataError(cause && typeof cause === "object" && "code" in cause ? String((cause as { code: unknown }).code) : "media.unavailable");
        } finally {
          if (!cancelled) setLoadingData(false);
        }
      };
      void run();
    }, 0);
    return () => { cancelled = true; window.clearTimeout(timer); };
  }, [account.selectedAccountId, account.accessToken, account.workspaceId]);

  async function loadMore() {
    if (!account.selectedAccountId || !account.accessToken || !account.workspaceId || !nextCursor || loadingData) return;
    const selectedAccountId = account.selectedAccountId;
    setLoadingData(true);
    setDataError(null);
    try {
      const page = await mediaApi().listMedia(account.accessToken, account.workspaceId, selectedAccountId, { limit: 24, cursor: nextCursor });
      if (account.selectedAccountId !== selectedAccountId) return;
      setItems((current) => [...current, ...page.items.filter((candidate) => !current.some((item) => item.mediaId === candidate.mediaId))]);
      setNextCursor(page.nextCursor);
      setHasMore(page.hasMore);
    } catch (cause) {
      setDataError(cause && typeof cause === "object" && "code" in cause ? String((cause as { code: unknown }).code) : "media.unavailable");
    } finally {
      setLoadingData(false);
    }
  }
  const mediaState = capabilityState(capabilities, "MediaCatalog");
  return (
    <main className={styles.page}>
      <div className={styles.headerRow}>
        <div><h1 className={styles.title}>رسانه‌های اینستاگرام</h1><p className={styles.subtitle}>رسانه را بصری انتخاب کنید؛ شناسه ProviderMediaId فقط درون قرارداد قاصدک نگهداری می‌شود.</p></div>
      </div>
      <InstagramSurfaceNav accountId={account.selectedAccountId} />
      <AccountSelector accounts={account.accounts} selectedAccountId={account.selectedAccountId} onChange={account.selectAccount} disabled={account.loading} />

      {account.loading ? <div className={styles.statusBox} role="status" aria-live="polite">در حال دریافت حساب‌ها…</div> : null}
      {!account.loading && account.accounts.length === 0 ? <div className={styles.statusBox}>حساب متصلی وجود ندارد.</div> : null}
      {!account.selectedAccountId && account.accounts.length > 0 ? <div className={styles.statusBox}>برای دیدن رسانه‌ها یک حساب را انتخاب کنید.</div> : null}
      {capabilities && mediaState !== "Available" ? <div className={styles.statusBox}>{capabilityExplanation(mediaState)}</div> : null}
      {dataError ? <div className={`${styles.statusBox} ${styles.error}`} role="alert">دریافت رسانه ناموفق بود. کد: {dataError}</div> : null}
      {loadingData && items.length === 0 ? <div className={styles.statusBox} role="status" aria-live="polite">در حال دریافت رسانه‌ها…</div> : null}

      {account.selectedAccountId && mediaState === "Available" && !loadingData && items.length === 0 && !dataError ? <div className={styles.statusBox}>رسانه‌ای برای این حساب برگردانده نشد.</div> : null}
      {items.length > 0 ? (
        <section className={styles.card} aria-labelledby="media-grid-heading">
          <div className={styles.headerRow}><div><h2 id="media-grid-heading" className={styles.cardTitle}>انتخاب رسانه</h2><p className={styles.cardText}>{selectedMediaId ? `رسانه انتخاب‌شده: ${selectedMediaId}` : "هنوز رسانه‌ای انتخاب نشده است."}</p></div></div>
          <div className={styles.mediaGrid}>
            {items.map((item) => {
              const preview = item.thumbnailUrl ?? item.mediaUrl;
              const selected = selectedMediaId === item.mediaId;
              return (
                <button key={item.mediaId} type="button" className={`${styles.mediaButton} ${selected ? styles.mediaSelected : ""}`} aria-pressed={selected} onClick={() => setSelectedMediaId(item.mediaId)}>
                  <span className={styles.mediaPreview} aria-hidden="true" style={preview ? { backgroundImage: `url(${JSON.stringify(preview).slice(1, -1)})`, backgroundSize: "cover", backgroundPosition: "center" } : undefined}>{preview ? null : kindLabel(item.kind)}</span>
                  <span className={styles.mediaInfo}><span className={styles.mediaCaption}>{item.caption?.trim() || "بدون کپشن"}</span><span className={styles.mediaMeta}>{kindLabel(item.kind)} · {item.timestampUtc ? new Date(item.timestampUtc).toLocaleString("fa-IR") : "زمان نامشخص"}</span></span>
                </button>
              );
            })}
          </div>
          {hasMore ? <div className={styles.actions} style={{ marginTop: 16 }}><Button variant="secondary" disabled={loadingData} onClick={() => void loadMore()}>{loadingData ? "در حال دریافت…" : "نمایش بیشتر"}</Button></div> : null}
        </section>
      ) : null}
    </main>
  );
}
