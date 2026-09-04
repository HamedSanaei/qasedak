import Link from "next/link";
import { FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const FEATURES = [
  { href: "/dashboard/features/smart-answer", name: "پاسخ هوشمند", desc: "دستورات پاسخ خودکار به پیام‌های دایرکت بر اساس کلمه یا عبارت." },
  { href: "/dashboard/features/cards", name: "ویترین‌ساز", desc: "چند محصول یا خدمت را در یک پیام تعاملی نمایش دهید." },
  { href: "/dashboard/features/follow-up", name: "پشتیبان هوشمند", desc: "ارسال خودکار پیام در زمان مشخص پس از تعامل کاربر." },
  { href: "/dashboard/features/comment-automation", name: "کامنت / لایو هوشمند", desc: "دستورات خودکار برای کامنت پست، ریپلای لایو و ارسال دایرکت." },
  { href: "/dashboard/features/form-maker", name: "فرم‌ساز", desc: "فرم تعاملی برای دریافت مرحله‌ای اطلاعات کاربران در دایرکت." },
  { href: "/dashboard/features/ice-breakers", name: "پیام خوش‌آمدگویی", desc: "عبارت‌هایی که اولین گفت‌وگوی کاربر با پیج شما را شروع می‌کنند." },
];

export default function FeaturesPage() {
  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "امکانات"]} />
      <h1 className={styles.title}>امکانات</h1>
      <p className={styles.subtitle}>یکی از روش‌های زیر را برای ارتباط سریع و هوشمند با مخاطبین انتخاب کنید.</p>
      <div className={styles.hubGrid}>
        {FEATURES.map((feature) => (
          <Link key={feature.href} href={feature.href} className={styles.hubCard}>
            <span className={styles.hubName}>{feature.name}</span>
            <span className={styles.hubDesc}>{feature.desc}</span>
            <span className={styles.hubLink}>ورود به بخش</span>
          </Link>
        ))}
      </div>
    </main>
  );
}
