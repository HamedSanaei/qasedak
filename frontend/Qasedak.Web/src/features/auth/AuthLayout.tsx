/*
 * Auth split-hero layout — synchronized from the canonical Penpot boards
 * "Identity / Login / Desktop" + "Identity / Register / Desktop" (page
 * c48311ed-e700-80f8-8008-881f0352eb6a). Presentation only: pages own all behavior.
 */
import type { ReactNode } from "react";
import styles from "./AuthLayout.module.css";

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className={styles.canvas}>
      <aside className={styles.brandPanel} aria-hidden="true">
        <div className={styles.decorOne} />
        <div className={styles.decorTwo} />
        <div className={styles.brandRow}>
          <span className={styles.mark}>ق</span>
          <span className={styles.wordmark}>قاصدک</span>
        </div>
        <p className={styles.promise}>ارتباط‌های ارزشمند، ساده و یکپارچه</p>
        <p className={styles.brandBody}>
          با قاصدک پیام‌ها، مشتریان و اتوماسیون‌های اینستاگرام را از یک فضای کاری مدیریت کنید.
        </p>
        <ul className={styles.benefits}>
          <li>
            <span className={styles.benefitCheck}>✓</span>
            ورود امن با ایمیل و گذرواژه
          </li>
          <li>
            <span className={styles.benefitCheck}>✓</span>
            داده‌های هر فضای کاری جدا و محافظت‌شده
          </li>
          <li>
            <span className={styles.benefitCheck}>✓</span>
            تجربه کاملاً فارسی و راست‌چین
          </li>
        </ul>
      </aside>
      <section className={styles.card}>{children}</section>
    </div>
  );
}

export function AuthBrandRow() {
  return (
    <div className={styles.cardBrandRow}>
      <span className={styles.cardMark}>ق</span>
      <span className={styles.cardWordmark}>قاصدک</span>
    </div>
  );
}
