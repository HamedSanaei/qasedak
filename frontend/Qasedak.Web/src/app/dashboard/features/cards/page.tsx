"use client";

import Link from "next/link";
import { useState } from "react";
import { EducationBanner, FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const INITIAL_SHOWCASES = [
  { id: "special-offers", name: "پیشنهادهای ویژه", scope: "تخفیف، پیشنهاد", thumb: "✦", tone: "thumbPeach" },
  { id: "service-catalog", name: "کاتالوگ خدمات", scope: "خدمات، مشاوره", thumb: "◇", tone: "thumbBlue" },
  { id: "summer-products", name: "ویترین محصولات تابستانی", scope: "سلام، محصولات، قیمت", thumb: "▦", tone: "thumbPink" },
];

export default function CardsPage() {
  const [query, setQuery] = useState("");
  const [showcases, setShowcases] = useState(INITIAL_SHOWCASES);
  const normalized = query.trim();
  const visible = normalized.length === 0
    ? showcases
    : showcases.filter((item) => `${item.name} ${item.scope}`.includes(normalized));

  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "ویترین‌ساز"]} />
      <h1 className={styles.title}>ویترین‌ساز</h1>

      <EducationBanner
        title="آموزش ویترین‌ساز"
        body="با ساخت ویترین، چند محصول یا خدمت را در یک پیام تعاملی نمایش دهید."
        actionLabel="مشاهده آموزش"
      />

      <div className={styles.searchRow}>
        <div className={styles.searchWrap}>
          <input
            type="search"
            className={styles.searchInput}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="ویترین‌های خود را جستجو کنید"
            aria-label="جستجوی ویترین"
          />
          <span className={styles.searchIcon} aria-hidden="true">⌕</span>
        </div>
        <Link href="/dashboard/features/cards/new" className={styles.addButton}>
          <span aria-hidden="true">＋</span> اضافه کردن ویترین
        </Link>
      </div>

      {visible.length === 0 ? (
        <section className={styles.card} aria-label="نتیجه جستجو">
          <p className={styles.infoBody}>ویترینی با این عبارت پیدا نشد.</p>
        </section>
      ) : (
        <div className={styles.grid3}>
          {visible.map((item) => (
            <article key={item.id} className={styles.featureCard} aria-label={item.name}>
              <div className={styles.featureCardTop}>
                <div style={{ flex: 1 }}>
                  <div className={styles.featureName}>{item.name}</div>
                  <div className={styles.featureMeta}>فعال‌کننده‌ها</div>
                  <div className={styles.featureMeta}>{item.scope}</div>
                </div>
                <span className={`${styles.thumb} ${styles[item.tone as keyof typeof styles]}`} aria-hidden="true">{item.thumb}</span>
              </div>
              <div className={styles.cardActions}>
                <button
                  type="button"
                  className={styles.deleteButton}
                  onClick={() => setShowcases((list) => list.filter((entry) => entry.id !== item.id))}
                >
                  حذف
                </button>
                <Link href="/dashboard/features/cards/new" className={styles.editButton}>ویرایش</Link>
              </div>
            </article>
          ))}
        </div>
      )}

      <section className={styles.infoCard} aria-label="راهنمای نمایش">
        <h2 className={styles.infoTitle}>ویترین‌ها در دایرکت چه شکلی نمایش داده می‌شوند؟</h2>
        <p className={styles.infoBody}>هر ویترین می‌تواند شامل چند کارت، تصویر، ویدیو، متن و دکمه باشد.</p>
      </section>
    </main>
  );
}
