"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { EmptyState } from "@/shared/design/Feedback";
import { Icon } from "@/shared/design/Icons";
import { PageHeader } from "@/shared/design/PageHeader";
import styles from "./HelpScreen.module.css";

const quickLinks = [
  { label: "اتصال حساب اینستاگرام", description: "وضعیت اتصال و شروع فرایند امن اتصال", href: "/dashboard/settings/instagram", icon: "instagram" as const },
  { label: "ساخت پاسخ خودکار", description: "ایجاد و مدیریت اتوماسیون‌های واقعی فضای کاری", href: "/dashboard/automations", icon: "features" as const },
  { label: "بررسی حساب و فضای کاری", description: "ایمیل، نقش و عضویت فعال خود را ببینید", href: "/dashboard/accounts", icon: "accounts" as const },
];

const categories = [
  { title: "شروع کار با قاصدک", description: "حساب، فضای کاری و راه‌اندازی اولیه", href: "/dashboard/accounts", icon: "dashboard" as const },
  { title: "صندوق گفتگو", description: "دریافت پیام، جست‌وجو و ادامه گفتگو", href: "/dashboard/inbox", icon: "inbox" as const },
  { title: "امکانات و اتوماسیون", description: "پاسخ‌های خودکار پشتیبانی‌شده در قاصدک", href: "/dashboard/automations", icon: "features" as const },
  { title: "اشتراک و پرداخت", description: "پلن‌ها، پرداخت و نتیجه تراکنش", href: "/dashboard/billing", icon: "billing" as const },
  { title: "حساب کاربری و دسترسی", description: "نشست، نقش و عضویت فضای کاری", href: "/dashboard/accounts", icon: "accounts" as const },
  { title: "رفع مشکلات رایج", description: "پاسخ‌های شفاف و بدون داده نمایشی", href: "#frequent-questions", icon: "help" as const },
];

const faqs = [
  { question: "چطور فضای کاری بسازم؟", answer: "از داشبورد یا صفحه حساب من، گزینه ایجاد فضای کاری را انتخاب کنید. سازنده به‌عنوان مالک ثبت می‌شود.", keywords: "فضای کاری ساخت مالک onboarding" },
  { question: "چرا صندوق گفتگو خالی است؟", answer: "گفتگو فقط پس از دریافت پیام واقعی در فضای کاری نمایش داده می‌شود. قاصدک برای پر کردن صفحه داده نمایشی نمی‌سازد.", keywords: "صندوق گفتگو پیام خالی inbox" },
  { question: "چرا امکان پاسخ‌گویی بسته است؟", answer: "ارسال پاسخ به وضعیت گفتگو، اتصال حساب و پنجره مجاز پیام‌رسانی وابسته است. دلیل قابل استفاده در همان صفحه نشان داده می‌شود.", keywords: "پاسخ ارسال اتصال اینستاگرام" },
  { question: "چطور پاسخ خودکار بسازم؟", answer: "از بخش امکانات وارد پاسخ‌های خودکار شوید. قاصدک فقط فرایندهایی را فعال می‌کند که backend و اتصال فضای کاری از آن‌ها پشتیبانی می‌کنند.", keywords: "اتوماسیون پاسخ خودکار کامنت automation" },
  { question: "اطلاعات ورود کجا نگهداری می‌شود؟", answer: "نشست در cookie امن و فقط سمت سرور نگهداری می‌شود؛ access token در اختیار کد مرورگر قرار نمی‌گیرد.", keywords: "ورود نشست امنیت کوکی رمز" },
  { question: "وضعیت پرداخت را از کجا ببینم؟", answer: "صفحه اشتراک، پلن‌های دریافت‌شده از سرور را نمایش می‌دهد و نتیجه پرداخت فقط پس از بررسی سمت سرور معتبر است.", keywords: "اشتراک پرداخت پلن تراکنش billing" },
];

function normalize(value: string) {
  return value.trim().toLocaleLowerCase("fa-IR").replaceAll("ي", "ی").replaceAll("ك", "ک");
}

export function HelpScreen() {
  const [query, setQuery] = useState("");
  const filteredFaqs = useMemo(() => {
    const needle = normalize(query);
    if (!needle) return faqs;
    return faqs.filter(({ question, answer, keywords }) => normalize(`${question} ${answer} ${keywords}`).includes(needle));
  }, [query]);

  return <>
    <PageHeader title="راهنما و پشتیبانی" description="راهنمای واقعی حساب، فضای کاری، گفتگو، اتوماسیون و پرداخت در قاصدک." />

    <section className={styles.searchPanel} aria-labelledby="help-search-title">
      <div className={styles.searchCopy}>
        <span className={styles.searchIcon} aria-hidden="true"><Icon name="help" size={28} /></span>
        <div><h2 id="help-search-title">چطور می‌توانیم کمکتان کنیم؟</h2><p>در پاسخ‌های همین صفحه جست‌وجو کنید یا مستقیماً به بخش موردنظر بروید.</p></div>
      </div>
      <label className={styles.searchField}>
        <span className={styles.srOnly}>جست‌وجوی راهنما</span>
        <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="مثلاً فضای کاری، پاسخ خودکار یا پرداخت" />
        <span aria-hidden="true">⌕</span>
      </label>
    </section>

    <section className={styles.section} aria-labelledby="quick-help-title">
      <div className={styles.sectionHeading}><h2 id="quick-help-title">سریع‌تر به جواب برسید</h2><p>سه مسیر پرکاربرد با مقصد واقعی در برنامه</p></div>
      <div className={styles.quickGrid}>{quickLinks.map((item) => <Link className={styles.quickCard} href={item.href} key={item.href}><span className={styles.iconBubble} aria-hidden="true"><Icon name={item.icon} size={22} /></span><span><strong>{item.label}</strong><small>{item.description}</small></span><span className={styles.arrow} aria-hidden="true">‹</span></Link>)}</div>
    </section>

    <section className={styles.section} aria-labelledby="help-categories-title">
      <div className={styles.sectionHeading}><h2 id="help-categories-title">دسته‌بندی راهنما</h2><p>هر کارت به یک قابلیت موجود یا پرسش‌های همین صفحه متصل است.</p></div>
      <div className={styles.categoryGrid}>{categories.map((item) => <Link className={styles.categoryCard} href={item.href} key={item.title}><span className={styles.iconBubble} aria-hidden="true"><Icon name={item.icon} size={22} /></span><span><strong>{item.title}</strong><small>{item.description}</small></span><span className={styles.arrow} aria-hidden="true">‹</span></Link>)}</div>
    </section>

    <section className={styles.section} id="frequent-questions" aria-labelledby="faq-title">
      <div className={styles.sectionHeading}><h2 id="faq-title">پرسش‌های متداول</h2><p aria-live="polite">{query ? `${filteredFaqs.length.toLocaleString("fa-IR")} پاسخ پیدا شد` : "پاسخ‌های کوتاه و قابل اتکا"}</p></div>
      {filteredFaqs.length ? <div className={styles.faq}>{filteredFaqs.map(({ question, answer }) => <details key={question}><summary>{question}</summary><p>{answer}</p></details>)}</div> : <div className={styles.empty}><EmptyState icon="؟" title="پاسخی پیدا نشد" description="عبارت دیگری جست‌وجو کنید یا از دسته‌بندی‌های بالا وارد بخش مرتبط شوید." /></div>}
    </section>

    <section className={styles.supportNotice} aria-label="روش دریافت راهنمایی بیشتر"><span className={styles.supportMark} aria-hidden="true"><Icon name="help" size={28} /></span><div><h2>پاسخ را پیدا نکردید؟</h2><p>قاصدک هنوز سامانه تیکت یا چت پشتیبانی درون‌برنامه‌ای ندارد. برای جلوگیری از ثبت درخواست ساختگی، این صفحه فقط راهنما و مسیرهای واقعی محصول را نمایش می‌دهد.</p></div></section>
  </>;
}
