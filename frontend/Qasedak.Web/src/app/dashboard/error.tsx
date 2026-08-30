"use client";

import { Button, ButtonLink } from "@/shared/design/Button";
import { Card } from "@/shared/design/Feedback";
import styles from "@/shared/design/RouteState.module.css";

export default function DashboardError({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return <div className={styles.errorPage}><Card className={styles.errorCard}><h2>نمایش این صفحه ممکن نیست</h2><p>اطلاعات خام خطا نمایش داده نمی‌شود. دوباره تلاش کنید یا به داشبورد برگردید.</p><div className={styles.actions}><Button type="button" onClick={reset}>تلاش دوباره</Button><ButtonLink href="/dashboard" variant="secondary">داشبورد</ButtonLink></div></Card></div>;
}
