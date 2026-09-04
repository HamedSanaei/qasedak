import Link from "next/link";
import { FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const METHODS = [
  {
    id: "bulk",
    name: "پیامک انبوه",
    desc: "ارسال پیام به تعداد زیادی از مخاطبین به‌صورت همزمان.",
    thumb: "▤",
    tone: "thumbPink",
    action: "ورود به بخش",
    href: "/dashboard/help",
    soon: false,
  },
  {
    id: "sync",
    name: "پیامک همگام",
    desc: "ارسال همزمان پیام به مخاطب در اینستاگرام و پیامک.",
    thumb: "❐",
    tone: "thumbPink",
    action: "ورود به بخش",
    href: "/dashboard/help",
    soon: false,
  },
  {
    id: "interactive",
    name: "دستورات پیامک تعاملی",
    desc: "تنظیم دستورات برای پاسخ خودکار به پیام‌ها.",
    thumb: "💬",
    tone: "thumbGrey",
    action: "غیرفعال",
    href: null,
    soon: true,
  },
];

const STEPS = [
  { n: "۱", name: "پیامک انبوه", desc: "برای کمپین‌ها و اطلاع‌رسانی به لیست مخاطبان" },
  { n: "۲", name: "پیامک همگام", desc: "همزمان با تعامل کاربر در اینستاگرام" },
  { n: "۳", name: "پیامک تعاملی", desc: "اجرای سناریو و پاسخ خودکار بر اساس پیام" },
];

export default function SmartSmsPage() {
  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "پیامک به مخاطبین"]} />
      <h1 className={styles.title}>ارسال پیامک به مخاطبین</h1>
      <p className={styles.subtitle}>یکی از روش‌های زیر را برای ارسال پیامک به مخاطبین انتخاب کنید.</p>

      <div className={styles.infoCard} role="note" style={{ background: "var(--color-accent-softer)", borderColor: "#f3d3e8" }}>
        <p className={styles.infoBody} style={{ color: "var(--color-brand-accent)", fontWeight: 700 }}>
          ⓘ معرفی سرویس پیامک دایرکتم
        </p>
      </div>

      <div className={styles.grid3}>
        {METHODS.map((method) => (
          <article key={method.id} className={styles.featureCard} aria-label={method.name}>
            {method.soon ? <span className={styles.scopePill} style={{ alignSelf: "flex-start", background: "#e23b3b", color: "#ffffff" }}>به‌زودی</span> : null}
            <div style={{ display: "flex", justifyContent: "center", padding: "0.5rem 0" }}>
              <span className={`${styles.thumb} ${styles[method.tone as keyof typeof styles]}`} style={{ width: 120, height: 120, fontSize: 54, borderRadius: "50%" }} aria-hidden="true">
                {method.thumb}
              </span>
            </div>
            <div className={styles.featureName} style={{ textAlign: "center" }}>{method.name}</div>
            <p className={styles.featureMeta} style={{ textAlign: "center" }}>{method.desc}</p>
            <div className={styles.cardActions} style={{ borderTop: "1px solid var(--color-border-default)", paddingTop: "0.75rem" }}>
              {method.href ? (
                <Link href={method.href} className={styles.editButton} style={{ background: "none" }}>{method.action}</Link>
              ) : (
                <span className={styles.footerHint} style={{ flex: 1, textAlign: "center" }}>{method.action}</span>
              )}
            </div>
          </article>
        ))}
      </div>

      <section className={styles.infoCard} aria-label="مسیرهای ارتباطی">
        <h2 className={styles.infoTitle}>سه مسیر برای ارتباط سریع و هوشمند</h2>
        <div style={{ display: "flex", flexDirection: "column", marginTop: "0.75rem" }}>
          {STEPS.map((step) => (
            <div key={step.n} style={{ display: "flex", gap: "0.75rem", alignItems: "flex-start", padding: "0.7rem 0", borderTop: "1px solid var(--color-border-default)" }}>
              <span className={`${styles.thumb} ${styles.thumbPink}`} style={{ width: 36, height: 36, fontSize: 14, borderRadius: "50%" }} aria-hidden="true">{step.n}</span>
              <div>
                <div className={styles.featureName} style={{ fontSize: 14 }}>{step.name}</div>
                <div className={styles.featureMeta}>{step.desc}</div>
              </div>
            </div>
          ))}
        </div>
      </section>
    </main>
  );
}
