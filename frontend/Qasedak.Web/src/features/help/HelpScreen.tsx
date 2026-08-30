import { Card } from "@/shared/design/Feedback";
import { PageHeader } from "@/shared/design/PageHeader";
import styles from "@/shared/design/ContentCards.module.css";

const faqs = [
  ["چطور فضای کاری بسازم؟", "از داشبورد یا صفحه حساب من، گزینه ایجاد فضای کاری را انتخاب کنید. سازنده به‌عنوان مالک ثبت می‌شود."],
  ["چرا صندوق گفتگو خالی است؟", "گفتگو فقط پس از دریافت پیام واقعی در فضای کاری نمایش داده می‌شود. قاصدک برای پر کردن صفحه داده نمایشی نمی‌سازد."],
  ["چرا امکان پاسخ‌گویی بسته است؟", "ارسال پاسخ به وضعیت گفتگو، اتصال حساب و پنجره مجاز پیام‌رسانی وابسته است. دلیل قابل استفاده در همان صفحه نشان داده می‌شود."],
  ["اطلاعات ورود کجا نگهداری می‌شود؟", "نشست در cookie امن و فقط سمت سرور نگهداری می‌شود؛ access token در اختیار کد مرورگر قرار نمی‌گیرد."],
];

export function HelpScreen() {
  return <><PageHeader title="راهنما و پشتیبانی" description="پاسخ‌های کوتاه برای کار با حساب، فضای کاری و صندوق گفتگو." /><div className={styles.grid}><Card><h2 className={styles.cardTitle}>شروع سریع</h2><p className={styles.cardCopy}>۱. حساب بسازید یا وارد شوید.<br />۲. فضای کاری را ایجاد کنید.<br />۳. گفتگوهای دریافت‌شده را در صندوق بررسی کنید.</p></Card><Card><h2 className={styles.cardTitle}>حریم خصوصی</h2><p className={styles.cardCopy}>توکن‌های سرویس‌های خارجی در مرورگر نمایش داده نمی‌شوند و کنترل دسترسی در مرز سرور انجام می‌شود.</p></Card><section className={`${styles.faq} ${styles.full}`} aria-label="پرسش‌های رایج">{faqs.map(([question, answer]) => <details key={question}><summary>{question}</summary><p>{answer}</p></details>)}</section></div></>;
}
