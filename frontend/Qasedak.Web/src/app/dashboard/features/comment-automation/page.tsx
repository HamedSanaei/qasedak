"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { EducationBanner, FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";
import { automationsApi, type AutomationSummary } from "@/shared/api/automations";
import { readSession, readWorkspaceId } from "@/shared/api/identity";

export default function CommentAutomationPage() {
  const router = useRouter();
  const [items, setItems] = useState<AutomationSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [query, setQuery] = useState("");

  const load = useCallback(async () => {
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) {
        router.replace("/login");
        return;
      }
      const result = await automationsApi().list(session.accessToken, workspaceId);
      setItems(result.items);
    } catch {
      setFailed(true);
    }
  }, [router]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const filtered = useMemo(() => {
    if (!items) return [];
    const q = query.trim();
    return q.length === 0 ? items : items.filter((a) => a.name.includes(q));
  }, [items, query]);

  async function remove(id: string) {
    const session = readSession();
    const workspaceId = readWorkspaceId();
    if (!session || !workspaceId) return;
    await automationsApi().remove(session.accessToken, workspaceId, id);
    await load();
  }

  const active = (items ?? []).filter((a) => a.status === "Active").length;
  const draft = (items ?? []).filter((a) => a.status === "Draft").length;

  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "کامنت و لایو هوشمند"]} />
      <h1 className={styles.title}>کامنت و لایو هوشمند</h1>

      <EducationBanner
        title="آموزش کامنت و لایو هوشمند"
        body="دستورات خودکار برای کامنت پست، ریپلای لایو و ارسال دایرکت."
        actionLabel="مشاهده آموزش"
      />

      <div className={styles.searchRow}>
        <div className={styles.searchWrap}>
          <input
            type="search"
            className={styles.searchInput}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="دستورات خود را جستجو کنید"
            aria-label="جستجوی دستور"
          />
          <span className={styles.searchIcon} aria-hidden="true">⌕</span>
        </div>
        <Link href="/dashboard/features/comment-automation/new" className={styles.addButton}>
          <span aria-hidden="true">＋</span> اضافه کردن دستور
        </Link>
      </div>

      {failed ? (
        <div role="alert" className={styles.card}>دریافت دستورها ناموفق بود؛ لطفاً دوباره تلاش کنید.</div>
      ) : null}

      {items !== null && filtered.length === 0 ? (
        <section className={styles.card} aria-label="نتیجه">
          <p className={styles.infoBody}>هنوز دستوری ثبت نشده است؛ با «اضافه کردن دستور» اولین دستور را بسازید.</p>
        </section>
      ) : (
        <div className={styles.grid3}>
          {filtered.map((automation) => (
            <article key={automation.id} className={styles.featureCard} aria-label={automation.name}>
              <div className={styles.featureCardTop}>
                <div style={{ flex: 1 }}>
                  <div className={styles.featureName}>{automation.name}</div>
                  <div className={styles.featureMeta}>کامنت ← دایرکت</div>
                  <span className={styles.scopePill}>همه پست‌ها</span>
                </div>
                <span className={`${styles.thumb} ${styles.thumbGrey}`} aria-hidden="true">▨</span>
              </div>
              <div className={styles.cardActions}>
                <button type="button" className={styles.deleteButton} onClick={() => void remove(automation.id)}>
                  حذف
                </button>
                <Link href={`/dashboard/automations/${automation.id}`} className={styles.editButton}>ویرایش</Link>
              </div>
            </article>
          ))}
        </div>
      )}

      <section className={styles.statRow} aria-label="آمار دستورات">
        <div className={styles.statCell}>
          <div className={styles.statValue}>{items === null ? "–" : active}</div>
          <div className={styles.statLabel}>دستور فعال</div>
        </div>
        <div className={styles.statCell}>
          <div className={styles.statValue}>{items === null ? "–" : draft}</div>
          <div className={styles.statLabel}>پیش‌نویس</div>
        </div>
        <div className={styles.statCell}>
          <div className={styles.statValue}>{items === null ? "–" : items.length}</div>
          <div className={styles.statLabel}>کل دستورها</div>
        </div>
      </section>
    </main>
  );
}
