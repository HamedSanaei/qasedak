import Link from "next/link";
import { ButtonLink } from "@/shared/design/Button";
import { Card, StatusAlert, StatusBadge } from "@/shared/design/Feedback";
import { Icon } from "@/shared/design/Icons";
import type { ApiResult } from "@/shared/server/backend";
import type { IdentityMe, WorkspaceMembers } from "@/features/identity/api/server";
import type { InboxPageData } from "@/features/conversations/api/server";
import { inboxState, workspaceState } from "./dashboard-state.mjs";
import styles from "./DashboardOverview.module.css";

export function DashboardOverview({ identity, workspace, inbox, workspaceSelected }: { identity: ApiResult<IdentityMe>; workspace: ApiResult<WorkspaceMembers> | null; inbox: ApiResult<InboxPageData> | null; workspaceSelected: boolean }) {
  const workspaceData = workspace?.ok ? workspace.data : null;
  const workspaceReady = workspaceData !== null;
  const inboxCount = inbox?.ok ? inbox.data.totalCount : null;
  const currentWorkspaceState = workspaceState({ selected: workspaceSelected, ok: workspaceReady, status: workspace?.status });
  const currentInboxState = inboxState({ workspaceReady, ok: inbox?.ok ?? false, totalCount: inboxCount ?? 0, status: inbox?.status });
  return (
    <>
      {!identity.ok && identity.status === 503 ? <StatusAlert tone="danger" title="ارتباط با سرویس برقرار نشد">اطلاعات حساب فعلاً بارگذاری نشد. صفحه را دوباره باز کنید.</StatusAlert> : null}
      <section className={styles.hero} aria-labelledby="dashboard-welcome">
        <div className={styles.heroCopy}>
          <h2 id="dashboard-welcome">{workspaceReady ? `خوش آمدید به ${workspaceData.workspaceName}` : "شروع کار با قاصدک"}</h2>
          <p>{workspaceReady ? "وضعیت واقعی فضای کاری و صندوق گفتگو را از همین‌جا دنبال کنید." : "برای نگهداری گفتگوها و دسترسی تیمی، ابتدا یک فضای کاری بسازید."}</p>
          <div className={styles.heroActions}>{workspaceReady ? <><ButtonLink href="/dashboard/inbox">رفتن به صندوق گفتگو</ButtonLink><ButtonLink href="/dashboard/accounts" variant="secondary">مشاهده حساب</ButtonLink></> : <ButtonLink href="/onboarding/workspace">ایجاد فضای کاری</ButtonLink>}</div>
        </div>
      </section>

      <div className={styles.grid} aria-label="خلاصه فضای کاری">
        <Card className={styles.summary}>
          <div className={styles.summaryTop}><span className={styles.summaryIcon}><Icon name="accounts" /></span><StatusBadge tone={workspaceReady ? "success" : "warning"}>{workspaceReady ? "آماده" : "نیازمند اقدام"}</StatusBadge></div>
          <h3>فضای کاری</h3>
          <p>{currentWorkspaceState === "ready" ? `${workspaceData!.workspaceName} با ${workspaceData!.members.length.toLocaleString("fa-IR")} عضو` : currentWorkspaceState === "missing" ? "هنوز فضای کاری فعالی انتخاب نشده است." : currentWorkspaceState === "service-error" ? "بارگذاری فضای کاری با خطا روبه‌رو شد." : "فضای کاری انتخاب‌شده در دسترس نیست."}</p>
          <Link className={styles.summaryLink} href={workspaceReady ? "/dashboard/accounts" : "/onboarding/workspace"}>{workspaceReady ? "جزئیات حساب" : "ساخت فضای کاری"}</Link>
        </Card>

        <Card className={styles.summary}>
          <div className={styles.summaryTop}><span className={styles.summaryIcon}><Icon name="inbox" /></span><StatusBadge tone={inboxCount === null ? "neutral" : inboxCount > 0 ? "info" : "success"}>{inboxCount === null ? "بدون داده" : inboxCount > 0 ? "دارای گفتگو" : "آماده"}</StatusBadge></div>
          <h3>صندوق گفتگو</h3>
          <p>{currentInboxState === "needs-workspace" ? "پس از ساخت فضای کاری، گفتگوها در این بخش نمایش داده می‌شوند." : currentInboxState === "has-items" ? `${inboxCount!.toLocaleString("fa-IR")} گفتگو در فضای کاری ثبت شده است.` : currentInboxState === "empty" ? "هنوز گفتگویی دریافت نشده است." : currentInboxState === "service-error" ? "بارگذاری گفتگوها با خطا روبه‌رو شد." : "اطلاعات گفتگو در دسترس نیست."}</p>
          <Link className={styles.summaryLink} href="/dashboard/inbox">باز کردن صندوق گفتگو</Link>
        </Card>

        <Card className={styles.summary}>
          <div className={styles.summaryTop}><span className={styles.summaryIcon}><Icon name="help" /></span><StatusBadge tone="info">راهنما</StatusBadge></div>
          <h3>راهنمای محصول</h3>
          <p>پاسخ پرسش‌های رایج درباره حساب، فضای کاری و صندوق گفتگو را بخوانید.</p>
          <Link className={styles.summaryLink} href="/dashboard/help">مشاهده راهنما</Link>
        </Card>
      </div>

      <Card className={styles.onboarding}>
        <h2>مسیر شروع</h2>
        <p className={styles.onboardingIntro}>هر مرحله به یک قابلیت واقعی و قابل استفاده متصل است.</p>
        <ol className={styles.steps}>
          <li className={styles.step}><div className={styles.stepHeader}><span className={styles.stepNumber}>۱</span><strong>فضای کاری</strong></div><p>{workspaceReady ? "فضای کاری آماده است." : "فضای کاری تیم را ایجاد کنید."}</p><Link href={workspaceReady ? "/dashboard/accounts" : "/onboarding/workspace"}>{workspaceReady ? "مشاهده" : "شروع"}</Link></li>
          <li className={styles.step}><div className={styles.stepHeader}><span className={styles.stepNumber}>۲</span><strong>حساب و دسترسی</strong></div><p>ایمیل و نقش فعلی خود را بررسی کنید.</p><Link href="/dashboard/accounts">بررسی حساب</Link></li>
          <li className={styles.step}><div className={styles.stepHeader}><span className={styles.stepNumber}>۳</span><strong>صندوق گفتگو</strong></div><p>پیام‌های واقعی دریافت‌شده را دنبال کنید.</p><Link href="/dashboard/inbox">ورود به صندوق</Link></li>
        </ol>
      </Card>
    </>
  );
}
