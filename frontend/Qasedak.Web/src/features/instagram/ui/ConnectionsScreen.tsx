import { Button } from "@/shared/design/Button";
import { Card, StatusAlert, StatusBadge } from "@/shared/design/Feedback";
import { PageHeader } from "@/shared/design/PageHeader";
import styles from "@/shared/design/ContentCards.module.css";

export function ConnectionsScreen() {
  return <><PageHeader title="اتصال اینستاگرام" description="مدیریت اتصال امن حساب حرفه‌ای اینستاگرام." /><StatusAlert tone="warning" title="اتصال هنوز فعال نشده است">امکان اتصال و مشاهده حساب‌ها در حال حاضر در دسترس نیست؛ هیچ اتصال نمایشی ساخته نمی‌شود.</StatusAlert><div className={styles.afterAlert}><Card><div className={styles.connection}><span className={styles.connectionLogo}>IG</span><div className={styles.connectionCopy}><h2>حساب متصل وجود ندارد</h2><p>پس از فعال‌شدن اتصال امن، حساب‌های تأییدشده اینجا نمایش داده می‌شوند.</p></div><div className={styles.connectionAction}><Button disabled>اتصال اینستاگرام</Button></div><StatusBadge tone="neutral">قطع</StatusBadge></div></Card></div></>;
}
