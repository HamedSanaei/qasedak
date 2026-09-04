"use client";

import Link from "next/link";
import { useState } from "react";
import { EducationBanner, FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

export default function SmartAnswerPage() {
  const [query, setQuery] = useState("");

  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "پاسخ هوشمند"]} />

      <EducationBanner
        title="نیاز به دیدن آموزش دارید؟"
        body="با یک آموزش کوتاه، سریع‌تر با این بخش آشنا شوید"
        actionLabel="مشاهده آموزش"
        primary
      />

      <section className={styles.card} aria-label="جستجوی دستورات">
        <h1 className={styles.title} style={{ textAlign: "center" }}>دستورات خود را جستجو کنید</h1>
        <div className={styles.searchRow} style={{ marginTop: "1rem", maxWidth: 560, marginInline: "auto", width: "100%" }}>
          <div className={styles.searchWrap}>
            <input
              type="search"
              className={styles.searchInput}
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="عنوان دستور را وارد کنید"
              aria-label="دنبال چی می‌گردی؟"
            />
            <span className={styles.searchIcon} aria-hidden="true">⌕</span>
          </div>
        </div>
      </section>

      <section className={styles.card} aria-label="وضعیت دستورات">
        <div className={styles.emptyCanvas}>
          <span className={styles.emptyCanvasTitle}>هنوز دستوری ساخته نشده</span>
          <span className={styles.emptyCanvasBody}>برای شروع، اولین دستور پاسخ هوشمند خود را اضافه کنید.</span>
        </div>
      </section>

      <div style={{ display: "flex", justifyContent: "flex-start" }}>
        <Link href="/dashboard/features/smart-answer/new" className={styles.addButton}>
          <span aria-hidden="true">＋</span> اضافه کردن دستور
        </Link>
      </div>
    </main>
  );
}
