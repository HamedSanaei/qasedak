"use client";

import Link from "next/link";
import { EducationBanner, FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const INITIAL_FORMS = [
  { id: "newsletter", name: "عضویت در خبرنامه", answers: "۴۸ پاسخ", questions: "۳ سوال" },
  { id: "consult", name: "درخواست مشاوره", answers: "۱۲ پاسخ", questions: "۴ سوال" },
  { id: "order", name: "فرم ثبت سفارش", answers: "۲۳ پاسخ", questions: "۵ سوال" },
];

export default function FormMakerPage() {
  const visible = INITIAL_FORMS;

  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "فرم‌ساز"]} />
      <h1 className={styles.title}>فرم‌ساز</h1>

      <EducationBanner
        title="آموزش فرم‌ساز"
        body="فرم تعاملی برای دریافت مرحله‌ای اطلاعات کاربران در دایرکت."
        actionLabel="مشاهده آموزش"
      />

      <div style={{ display: "flex", justifyContent: "flex-start" }}>
        <Link href="/dashboard/features/form-maker/new" className={styles.addButton}>
          <span aria-hidden="true">＋</span> فرم جدید
        </Link>
      </div>

      {visible.length === 0 ? (
        <section className={styles.card} aria-label="نتیجه جستجو">
          <p className={styles.infoBody}>فرمی با این عبارت پیدا نشد.</p>
        </section>
      ) : (
        <div className={styles.grid3}>
          {visible.map((form) => (
            <article key={form.id} className={styles.featureCard} aria-label={form.name}>
              <div className={styles.featureCardTop}>
                <div style={{ flex: 1 }}>
                  <div className={styles.featureName}>{form.name}</div>
                  <div className={styles.featureMeta}>{form.answers} &nbsp; {form.questions}</div>
                </div>
                <span className={`${styles.thumb} ${styles.thumbPink}`} aria-hidden="true">☰</span>
              </div>
              <div className={styles.cardActions}>
                <button type="button" className={styles.deleteButton} aria-label={`حذف ${form.name}`}>⌫</button>
                <Link href="/dashboard/features/form-maker/new" className={styles.editButton}>ویرایش</Link>
                <button type="button" className={styles.successButton}>نتایج</button>
              </div>
            </article>
          ))}
        </div>
      )}

      <section className={styles.infoCard} aria-label="خروجی پاسخ‌ها">
        <div style={{ display: "flex", gap: "1rem", alignItems: "flex-start" }}>
          <div style={{ flex: 1 }}>
            <h2 className={styles.infoTitle}>خروجی پاسخ‌ها</h2>
            <p className={styles.infoBody}>نتایج هر فرم را می‌توانید مشاهده و در قالب فایل اکسل دریافت کنید.</p>
          </div>
          <span className={styles.thumb} style={{ background: "#e7f6ee", color: "#147a4c" }} aria-hidden="true">X</span>
        </div>
      </section>
    </main>
  );
}
