import Link from "next/link";
import { Card, StatusAlert } from "@/shared/design/Feedback";
import { Icon } from "@/shared/design/Icons";
import type { ApiResult } from "@/shared/server/backend";
import type { IdentityMe, WorkspaceMembers } from "@/features/identity/api/server";
import type { InboxPageData } from "@/features/conversations/api/server";
import { inboxState, workspaceState } from "./dashboard-state.mjs";
import styles from "./DashboardOverview.module.css";

type FeatureCard = {
  title: string;
  tag: string;
  description: string;
  href: string;
  glyph: string;
  actionLabel: "شروع" | "مشاهده وضعیت";
  wide?: boolean;
};

const features: FeatureCard[] = [
  { title: "پاسخ هوشمند", tag: "پاسخ به استوری", description: "استوری‌های خود را تعاملی کنید و به پرسش‌های دنبال‌کنندگان پاسخ دهید.", href: "/dashboard/features/smart-answer", glyph: "•••", actionLabel: "شروع" },
  { title: "کامنت و لایو هوشمند", tag: "پاسخ به کامنت", description: "هیچ کامنتی بدون پاسخ نمی‌ماند؛ خودکار پاسخ دهید یا در دایرکت پیام ارسال کنید.", href: "/dashboard/features/comment-automation", glyph: "≡", actionLabel: "شروع" },
  { title: "ویترین‌ساز", tag: "ارائه محصولات و خدمات", description: "دایرکت خود را شبیه یک ویترین واقعی کنید و محصولات و خدمات را ارائه دهید.", href: "/dashboard/features/cards", glyph: "▦", actionLabel: "مشاهده وضعیت" },
  { title: "پشتیبان هوشمند", tag: "تعامل ۲۴ ساعته", description: "در طول شبانه‌روز به‌صورت خودکار با مخاطبان خود در ارتباط بمانید.", href: "/dashboard/features/follow-up", glyph: "◷", actionLabel: "مشاهده وضعیت" },
  { title: "فرم‌ساز", tag: "جمع‌آوری اطلاعات مخاطبان", description: "سؤال طراحی کنید و پاسخ‌های دنبال‌کنندگان را در یک مسیر مشخص دریافت کنید.", href: "/dashboard/features/form-maker", glyph: "▣", actionLabel: "مشاهده وضعیت" },
  { title: "پیامک هوشمند", tag: "ثبت و نگهداری شماره تماس", description: "شماره تماس دنبال‌کنندگان را دریافت کنید و مسیر ارسال پیامک را بسازید.", href: "/dashboard/smart-sms", glyph: "▯", actionLabel: "مشاهده وضعیت" },
  { title: "پیام خوش‌آمدگویی", tag: "معرفی کسب‌وکار به مخاطبان", description: "خود را معرفی کنید و مسیر پاسخ به پرسش‌های متداول دنبال‌کنندگان را آماده کنید.", href: "/dashboard/features/ice-breakers", glyph: "♧", actionLabel: "مشاهده وضعیت", wide: true },
];

function DashboardNotice({ icon, children, actionHref, actionLabel, tone = "brand" }: { icon: "features" | "accounts" | "inbox"; children: React.ReactNode; actionHref: string; actionLabel: string; tone?: "brand" | "warning" | "danger" }) {
  const toneClass = { brand: styles.noticeBrand, warning: styles.noticeWarning, danger: styles.noticeDanger }[tone];
  return (
    <div className={`${styles.notice} ${toneClass}`}>
      <span className={styles.noticeIcon}><Icon name={icon} size={20} /></span>
      <p>{children}</p>
      <Link href={actionHref}>{actionLabel}<span aria-hidden="true">←</span></Link>
    </div>
  );
}

function ProductFeatureCard({ feature }: { feature: FeatureCard }) {
  return (
    <Card padded={false} className={`${styles.featureCard} ${feature.wide ? styles.featureCardWide : ""}`}>
      <div className={styles.featureHeader}>
        <span className={styles.featureIcon} aria-hidden="true">{feature.glyph}</span>
        <div>
          <h2>{feature.title}</h2>
          <span className={styles.featureTag}>{feature.tag}</span>
        </div>
      </div>
      <p className={styles.featureDescription}>{feature.description}</p>
      <div className={styles.featureActions}>
        <Link className={styles.introLink} href="/dashboard/help"><span aria-hidden="true">▷</span> مشاهده راهنما</Link>
        <Link className={styles.startLink} href={feature.href}>{feature.actionLabel} <span aria-hidden="true">‹</span></Link>
      </div>
    </Card>
  );
}

export function DashboardOverview({ identity, workspace, inbox, workspaceSelected }: { identity: ApiResult<IdentityMe>; workspace: ApiResult<WorkspaceMembers> | null; inbox: ApiResult<InboxPageData> | null; workspaceSelected: boolean }) {
  const workspaceData = workspace?.ok ? workspace.data : null;
  const workspaceReady = workspaceData !== null;
  const inboxCount = inbox?.ok ? inbox.data.totalCount : null;
  const currentWorkspaceState = workspaceState({ selected: workspaceSelected, ok: workspaceReady, status: workspace?.status });
  const currentInboxState = inboxState({ workspaceReady, ok: inbox?.ok ?? false, totalCount: inboxCount ?? 0, status: inbox?.status });

  const workspaceCopy = currentWorkspaceState === "ready"
    ? `${workspaceData!.workspaceName} با ${workspaceData!.members.length.toLocaleString("fa-IR")} عضو آماده استفاده است.`
    : currentWorkspaceState === "missing"
      ? "هنوز فضای کاری فعالی انتخاب نشده است. برای شروع، فضای کاری تیم را بسازید."
      : currentWorkspaceState === "service-error"
        ? "بارگذاری فضای کاری با خطا روبه‌رو شد. کمی بعد دوباره تلاش کنید."
        : "فضای کاری انتخاب‌شده در دسترس نیست. یک فضای کاری معتبر انتخاب کنید.";
  const inboxCopy = currentInboxState === "needs-workspace"
    ? "پس از ساخت فضای کاری، گفتگوها در صندوق گفتگو نمایش داده می‌شوند."
    : currentInboxState === "has-items"
      ? `${inboxCount!.toLocaleString("fa-IR")} گفتگو در فضای کاری ثبت شده است.`
      : currentInboxState === "empty"
        ? "هنوز گفتگویی دریافت نشده است؛ صندوق گفتگو آماده دریافت پیام است."
        : currentInboxState === "service-error"
          ? "بارگذاری گفتگوها با خطا روبه‌رو شد. کمی بعد دوباره تلاش کنید."
          : "اطلاعات گفتگو در دسترس نیست.";
  const workspaceIsDanger = currentWorkspaceState === "service-error" || currentWorkspaceState === "unavailable";
  const inboxIsDanger = currentInboxState === "service-error" || currentInboxState === "unavailable";

  return (
    <div className={styles.dashboard}>
      {!identity.ok && identity.status === 503 ? <StatusAlert tone="danger" title="ارتباط با سرویس برقرار نشد">اطلاعات حساب فعلاً بارگذاری نشد. صفحه را دوباره باز کنید.</StatusAlert> : null}

      <section className={styles.notices} aria-label="وضعیت شروع کار">
        <DashboardNotice icon="features" actionHref="/dashboard/automations/new" actionLabel="از اینجا شروع کنید">
          برای ساخت دستور جدید و استفاده بهتر از امکانات قاصدک آماده‌اید؟
        </DashboardNotice>
        <DashboardNotice icon="accounts" actionHref={workspaceReady ? "/dashboard/accounts" : "/onboarding/workspace"} actionLabel={workspaceReady ? "مشاهده حساب" : "ساخت فضای کاری"} tone={workspaceIsDanger ? "danger" : workspaceReady ? "brand" : "warning"}>
          {workspaceCopy}
        </DashboardNotice>
        <DashboardNotice icon="inbox" actionHref="/dashboard/inbox" actionLabel="باز کردن صندوق" tone={inboxIsDanger ? "danger" : "brand"}>
          {inboxCopy}
        </DashboardNotice>
      </section>

      <section className={styles.features} aria-labelledby="dashboard-features-title">
        <header className={styles.sectionHeader}>
          <h1 id="dashboard-features-title">امکانات قاصدک</h1>
          <p>از این بخش می‌توانید به‌سرعت وارد هر قابلیت شوید و استفاده را آغاز کنید.</p>
        </header>
        <div className={styles.featureGrid}>
          {features.map((feature) => <ProductFeatureCard key={feature.title} feature={feature} />)}
        </div>
      </section>

      <section className={styles.quickAccess} aria-labelledby="dashboard-quick-access-title">
        <header className={styles.sectionHeader}>
          <h2 id="dashboard-quick-access-title">دسترسی‌های سریع قاصدک</h2>
          <p>وضعیت واقعی فضای کاری را ببینید و مستقیماً به بخش موردنیاز بروید.</p>
        </header>
        <div className={styles.quickGrid}>
          <Card padded={false} className={styles.quickCard}>
            <span className={`${styles.quickIcon} ${styles.quickIconInbox}`}><Icon name="inbox" size={24} /></span>
            <div><h3>صندوق گفتگو</h3><p>{inboxCopy}</p></div>
            <Link href="/dashboard/inbox">ورود به صندوق گفتگو</Link>
          </Card>
          <Card padded={false} className={styles.quickCard}>
            <span className={`${styles.quickIcon} ${styles.quickIconAccounts}`}><Icon name="accounts" size={24} /></span>
            <div><h3>حساب‌های من</h3><p>{workspaceCopy}</p></div>
            <Link href={workspaceReady ? "/dashboard/accounts" : "/onboarding/workspace"}>{workspaceReady ? "مدیریت حساب و اعضا" : "ایجاد فضای کاری"}</Link>
          </Card>
          <Card padded={false} className={styles.quickCard}>
            <span className={`${styles.quickIcon} ${styles.quickIconHelp}`}><Icon name="help" size={24} /></span>
            <div><h3>راهنما و پشتیبانی</h3><p>پاسخ پرسش‌های رایج درباره حساب، فضای کاری و امکانات محصول را بخوانید.</p></div>
            <Link href="/dashboard/help">مشاهده راهنمای قاصدک</Link>
          </Card>
        </div>
      </section>
    </div>
  );
}
