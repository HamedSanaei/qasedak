"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Button } from "@/shared/design/ui";
import { capabilitiesApi } from "@/shared/api/capabilities";
import { historySyncApi } from "@/shared/api/history-sync";
import type { AccountCapabilities } from "@/features/instagram/capabilities";
import { capabilityExplanation, capabilityState } from "@/features/instagram/capabilities";
import { HISTORY_COVERAGE_COPY, historyStatusLabel, isTerminalHistoryStatus, type HistorySyncOperation } from "@/features/instagram/history";
import { AccountSelector } from "@/features/instagram/ui/AccountSelector";
import { InstagramSurfaceNav } from "@/features/instagram/ui/InstagramSurfaceNav";
import { useInstagramAccountContext } from "@/features/instagram/useInstagramAccountContext";
import styles from "@/features/instagram/ui/InstagramSurface.module.css";

const POLL_INTERVAL_MS = 3000;

export default function InstagramHistoryPage() {
  const account = useInstagramAccountContext();
  const [capabilities, setCapabilities] = useState<AccountCapabilities | null>(null);
  const [operations, setOperations] = useState<HistorySyncOperation[]>([]);
  const [loadingData, setLoadingData] = useState(false);
  const [dataError, setDataError] = useState<string | null>(null);
  const pollTimer = useRef<number | null>(null);

  const stopPolling = useCallback(() => {
    if (pollTimer.current !== null) window.clearInterval(pollTimer.current);
    pollTimer.current = null;
  }, []);
  const loadStatus = useCallback(async (selectedAccountId: string) => {
    if (!account.accessToken || !account.workspaceId) return [];
    const result = await historySyncApi().get(account.accessToken, account.workspaceId, selectedAccountId);
    if (account.selectedAccountId !== selectedAccountId) return [];
    setOperations(result.operations);
    return result.operations;
  }, [account.accessToken, account.workspaceId, account.selectedAccountId]);

  const schedulePolling = useCallback((selectedAccountId: string, rows: HistorySyncOperation[]) => {
    stopPolling();
    const latest = rows[0];
    if (!latest || isTerminalHistoryStatus(latest.status)) return;
    pollTimer.current = window.setInterval(() => {
      void loadStatus(selectedAccountId)
        .then((next) => {
          const current = next[0];
          if (!current || isTerminalHistoryStatus(current.status)) stopPolling();
        })
        .catch(() => { stopPolling(); setDataError("history.statusUnavailable"); });
    }, POLL_INTERVAL_MS);
  }, [loadStatus, stopPolling]);

  useEffect(() => {
    let cancelled = false;
    stopPolling();
    const timer = window.setTimeout(() => {
      setCapabilities(null);
      setOperations([]);
      setDataError(null);
      if (!account.selectedAccountId || !account.accessToken || !account.workspaceId) return;
      const selected = account.selectedAccountId;
      const run = async () => {
        setLoadingData(true);
        try {
          const caps = await capabilitiesApi().get(account.accessToken!, account.workspaceId!, selected);
          if (cancelled) return;
          setCapabilities(caps);
          if (capabilityState(caps, "ConversationHistorySync") !== "Available") return;
          const rows = await loadStatus(selected);
          if (!cancelled) schedulePolling(selected, rows);
        } catch (cause) {
          if (!cancelled) setDataError(cause && typeof cause === "object" && "code" in cause ? String((cause as { code: unknown }).code) : "history.unavailable");
        } finally {
          if (!cancelled) setLoadingData(false);
        }
      };
      void run();
    }, 0);
    return () => { cancelled = true; window.clearTimeout(timer); stopPolling(); };
  }, [account.selectedAccountId, account.accessToken, account.workspaceId, loadStatus, schedulePolling, stopPolling]);
  async function enqueue() {
    if (!account.selectedAccountId || !account.accessToken || !account.workspaceId) return;
    const selected = account.selectedAccountId;
    stopPolling();
    setLoadingData(true);
    setDataError(null);
    try {
      await historySyncApi().enqueue(account.accessToken, account.workspaceId, selected);
      const rows = await loadStatus(selected);
      schedulePolling(selected, rows);
    } catch (cause) {
      setDataError(cause && typeof cause === "object" && "code" in cause ? String((cause as { code: unknown }).code) : "history.enqueueFailed");
    } finally {
      setLoadingData(false);
    }
  }

  const latest = operations[0] ?? null;
  const historyState = capabilityState(capabilities, "ConversationHistorySync");
  const active = latest ? !isTerminalHistoryStatus(latest.status) : false;
  const counters = useMemo(() => latest ? [
    ["گفتگوهای مشاهده‌شده", latest.conversationsObserved],
    ["شناسه پیام‌های مشاهده‌شده", latest.messageIdsObserved],
    ["جزئیات بازیابی‌شده", latest.detailsFetched],
    ["پیام‌های واردشده", latest.messagesImported],
    ["تکراری‌ها", latest.duplicates],
    ["پیام‌های پشتیبانی‌نشده", latest.unsupported],
    ["خارج از پنجره جزئیات", latest.historyWindowLimited],
  ] as const : [], [latest]);

  return (
    <main className={styles.page}>
      <div className={styles.headerRow}><div><h1 className={styles.title}>همگام‌سازی تاریخچه گفتگوها</h1><p className={styles.subtitle}>عملیات فقط در Qasedak صف می‌شود و وضعیت Qasedak با فاصله محدود polling می‌شود؛ مرورگر هرگز Meta را poll نمی‌کند.</p></div></div>
      <InstagramSurfaceNav accountId={account.selectedAccountId} />
      <AccountSelector accounts={account.accounts} selectedAccountId={account.selectedAccountId} onChange={account.selectAccount} disabled={account.loading} />
      <div className={styles.statusBox}>{HISTORY_COVERAGE_COPY}</div>
      {account.loading ? <div className={styles.statusBox} role="status" aria-live="polite">در حال دریافت حساب‌ها…</div> : null}
      {!account.selectedAccountId && account.accounts.length > 0 ? <div className={styles.statusBox}>برای مشاهده یا شروع همگام‌سازی، یک حساب را انتخاب کنید.</div> : null}
      {capabilities && historyState !== "Available" ? <div className={styles.statusBox}>{capabilityExplanation(historyState)}</div> : null}
      {dataError ? <div className={`${styles.statusBox} ${styles.error}`} role="alert">عملیات تاریخچه ناموفق بود. کد: {dataError}</div> : null}

      {account.selectedAccountId && historyState === "Available" ? (
        <div className={styles.grid}>
          <section className={`${styles.card} ${styles.full}`}>
            <div className={styles.headerRow}>
              <div><h2 className={styles.cardTitle}>آخرین عملیات</h2><p className={styles.cardText}>{latest ? historyStatusLabel(latest.status) : "هنوز عملیات ثبت نشده است."}</p></div>
              <div className={styles.actions}><Button disabled={loadingData || active} onClick={() => void enqueue()}>{active ? "عملیات فعال است" : "همگام‌سازی مجدد"}</Button></div>
            </div>
            {latest ? <dl className={styles.dataList}>{counters.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{new Intl.NumberFormat("fa-IR").format(value)}</dd></div>)}</dl> : null}
            {latest?.failureCategory ? <p className={`${styles.notice} ${styles.error}`} role="alert">دسته خطا: {latest.failureCategory}</p> : null}
            {latest?.status === "RateLimitedRetrying" ? <p className={styles.notice} role="status">به‌دلیل محدودیت نرخ، ادامه عملیات توسط صف Qasedak دوباره تلاش خواهد شد.</p> : null}
          </section>
          {operations.length > 0 ? <section className={`${styles.card} ${styles.full}`}><h2 className={styles.cardTitle}>عملیات اخیر</h2><div className={styles.tableScroll}><table className={styles.historyTable}><thead><tr><th>زمان ایجاد</th><th>نوع</th><th>وضعیت</th><th>واردشده</th><th>محدودیت تاریخچه</th></tr></thead><tbody>{operations.map((operation) => <tr key={operation.operationId}><td>{operation.createdAtUtc ? new Date(operation.createdAtUtc).toLocaleString("fa-IR") : "—"}</td><td>{operation.kind}</td><td>{historyStatusLabel(operation.status)}</td><td>{operation.messagesImported}</td><td>{operation.historyWindowLimited}</td></tr>)}</tbody></table></div></section> : null}
        </div>
      ) : null}
    </main>
  );
}
