import { Button } from "@/shared/design/Button";
import { Card, StatusAlert, StatusBadge } from "@/shared/design/Feedback";
import { PageHeader } from "@/shared/design/PageHeader";
import styles from "@/shared/design/ContentCards.module.css";

export function SubscriptionScreen({ mode = "subscription" }: { mode?: "subscription" | "plans" | "checkout" | "result" }) {
  const titles = { subscription: "اشتراک من", plans: "انتخاب اشتراک", checkout: "تأیید و پرداخت", result: "نتیجه پرداخت" };
  const descriptions = { subscription: "وضعیت اشتراک و سطح دسترسی فضای کاری.", plans: "پلن‌ها و قیمت‌های معتبر فضای کاری.", checkout: "بازبینی مبلغ و روش پرداخت پیش از انتقال.", result: "نمایش نتیجه نهایی و تأییدشده پرداخت." };
  return <><PageHeader title={titles[mode]} description={descriptions[mode]} /><StatusAlert tone="warning" title="اطلاعات پرداخت در دسترس نیست">در حال حاضر اطلاعات معتبری برای نمایش مبلغ، پلن یا نتیجه پرداخت دریافت نشده است.</StatusAlert><div className={styles.afterAlert}><Card><div className={styles.connection}><span className={styles.connectionLogo}>ق</span><div className={styles.connectionCopy}><h2>{mode === "result" ? "نتیجه قابل تأیید نیست" : "اشتراک فعالی ثبت نشده است"}</h2><p>{mode === "checkout" ? "تا دریافت مبلغ و درخواست پرداخت معتبر، انتقالی انجام نمی‌شود." : "پس از فعال‌شدن اشتراک، وضعیت معتبر همین‌جا نمایش داده می‌شود."}</p></div><div className={styles.connectionAction}><Button disabled>{mode === "result" ? "بررسی وضعیت" : mode === "checkout" ? "ادامه به درگاه" : "انتخاب پلن"}</Button></div><StatusBadge tone="neutral">بدون داده</StatusBadge></div></Card></div></>;
}
