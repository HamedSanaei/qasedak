import { ButtonLink } from "@/shared/design/Button";
import { Card } from "@/shared/design/Feedback";
import styles from "@/shared/design/RouteState.module.css";

export default function NotFound() {
  return <main className={styles.errorPage}><Card className={styles.errorCard}><h1>صفحه پیدا نشد</h1><p>نشانی واردشده معتبر نیست یا این بخش برای حساب شما وجود ندارد.</p><div className={styles.actions}><ButtonLink href="/dashboard">بازگشت به داشبورد</ButtonLink></div></Card></main>;
}
