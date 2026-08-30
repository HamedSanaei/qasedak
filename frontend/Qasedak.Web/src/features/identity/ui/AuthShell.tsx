import type { ReactNode } from "react";
import styles from "./AuthShell.module.css";

export function AuthShell({ children, mode }: { children: ReactNode; mode: "login" | "register" }) {
  return (
    <main className={styles.page}>
      <div className={styles.formColumn}>
        <section className={styles.card}>
          <div className={styles.brand}><span className={styles.mark}>ق</span><span>قاصدک</span></div>
          <p className={styles.eyebrow}>{mode === "login" ? "ورود به حساب" : "ساخت حساب"}</p>
          <h1 className={styles.title}>{mode === "login" ? "خوش آمدید" : "به قاصدک بپیوندید"}</h1>
          <p className={styles.subtitle}>{mode === "login" ? "برای ادامه، اطلاعات حساب خود را وارد کنید." : "اطلاعات اولیه را وارد کنید؛ سپس فضای کاری خود را می‌سازید."}</p>
          {children}
        </section>
      </div>
      <aside className={styles.side} aria-label="مزایای قاصدک">
        <div className={styles.sideBrand}><span className={styles.sideMark}>ق</span><span>قاصدک</span></div>
        <h2>ارتباط‌های ارزشمند، ساده و یکپارچه</h2>
        <p className={styles.sideCopy}>پیام‌های اینستاگرام و گفتگوهای فضای کاری خود را از یک پنل آرام و یکپارچه مدیریت کنید.</p>
        <ul className={styles.benefits}>
          <li><span className={styles.check}>✓</span>ورود امن با ایمیل و گذرواژه</li>
          <li><span className={styles.check}>✓</span>داده‌های هر فضای کاری جدا و محافظت‌شده</li>
          <li><span className={styles.check}>✓</span>تجربه کاملاً فارسی و واکنش‌گرا</li>
        </ul>
      </aside>
    </main>
  );
}
