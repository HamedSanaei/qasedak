import Link from "next/link";
import { EducationBanner, FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const SCENARIOS = [
  { id: "welcome-back", name: "بازگشت مشتری", meta: "۲ پیام زمان‌بندی‌شده" },
  { id: "satisfaction", name: "رضایت‌سنجی", meta: "۳ پیام زمان‌بندی‌شده" },
  { id: "payment-reminder", name: "یادآوری پرداخت", meta: "۱ پیام زمان‌بندی‌شده" },
  { id: "purchase-chase", name: "پیگیری خرید", meta: "۲ پیام زمان‌بندی‌شده" },
];

export default function FollowUpPage() {
  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "پشتیبان هوشمند"]} />
      <h1 className={styles.title}>پشتیبان هوشمند</h1>

      <EducationBanner
        title="آموزش پشتیبان هوشمند"
        body="ارسال خودکار پیام در زمان مشخص پس از تعامل کاربر."
        actionLabel="مشاهده آموزش"
      />

      <div style={{ display: "flex", justifyContent: "flex-start" }}>
        <Link href="/dashboard/features/follow-up/new" className={styles.addButton}>
          <span aria-hidden="true">＋</span> فالوآپ جدید
        </Link>
      </div>

      <div className={styles.grid4}>
        {SCENARIOS.map((scenario) => (
          <article key={scenario.id} className={styles.featureCard} aria-label={scenario.name}>
            <div className={styles.featureCardTop}>
              <div style={{ flex: 1 }}>
                <div className={styles.featureName}>{scenario.name}</div>
                <div className={styles.featureMeta}>{scenario.meta}</div>
              </div>
              <span className={`${styles.thumb} ${styles.thumbPink}`} aria-hidden="true">↻</span>
            </div>
            <div className={styles.cardActions}>
              <button type="button" className={styles.ghostButton} aria-label={`مشاهده ${scenario.name}`}>👁</button>
              <Link href="/dashboard/features/follow-up/new" className={styles.editButton}>ویرایش</Link>
            </div>
          </article>
        ))}
      </div>

      <section className={styles.infoCard} aria-label="راهنمای فالوآپ">
        <div style={{ display: "flex", gap: "1rem", alignItems: "flex-start" }}>
          <div style={{ flex: 1 }}>
            <h2 className={styles.infoTitle}>فالوآپ چطور کار می‌کند؟</h2>
            <p className={styles.infoBody}>پس از ارسال فعال‌کننده توسط کاربر، پیام‌های تنظیم‌شده با فاصله زمانی موردنظر به‌صورت خودکار ارسال می‌شوند.</p>
          </div>
          <span className={`${styles.thumb} ${styles.thumbPink}`} aria-hidden="true">↻</span>
        </div>
      </section>
    </main>
  );
}
