import { ButtonLink } from "./Button";
import { Card, StatusBadge } from "./Feedback";
import { Icon } from "./Icons";
import { PageHeader } from "./PageHeader";
import styles from "./ContentCards.module.css";

export function CapabilityScreen({ title, description, kind = "features" }: { title: string; description: string; kind?: "features" | "instagram" | "billing" | "inbox" }) {
  const icon = kind === "features" ? "features" : kind;
  return <><PageHeader title={title} description={description} /><Card className={styles.capability}><div className={styles.capabilityInner}><span className={styles.capabilityIcon}><Icon name={icon} size={30} /></span><StatusBadge tone="warning">در این حساب فعال نیست</StatusBadge><h2>عملیاتی برای انجام وجود ندارد</h2><p>این صفحه عمداً داده یا نتیجه ساختگی نمایش نمی‌دهد. وقتی قابلیت از سمت سرویس برای فضای کاری فعال شود، کنترل‌های واقعی همین‌جا در دسترس خواهند بود.</p><ButtonLink href="/dashboard" variant="secondary">بازگشت به داشبورد</ButtonLink></div></Card></>;
}
