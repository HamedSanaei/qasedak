import { getIdentity, getWorkspaceMembers } from "@/features/identity/api/server";
import { ButtonLink } from "@/shared/design/Button";
import { Card, StatusAlert, StatusBadge } from "@/shared/design/Feedback";
import { PageHeader } from "@/shared/design/PageHeader";
import { readSession } from "@/shared/server/session";
import styles from "@/shared/design/ContentCards.module.css";

const roleNames: Record<string, string> = { Owner: "مالک", Admin: "مدیر", Member: "عضو" };

export async function AccountsScreen() {
  const session = await readSession();
  const identity = await getIdentity(session.token!);
  const workspace = session.workspaceId ? await getWorkspaceMembers(session.token!, session.workspaceId) : null;
  return <><PageHeader title="حساب من" description="اطلاعات واقعی نشست و فضای کاری فعال." /><div className={styles.grid}>
    <Card><h2 className={styles.cardTitle}>اطلاعات حساب</h2><p className={styles.cardCopy}>این داده‌ها مستقیماً از سرویس هویت خوانده می‌شوند.</p>{identity.ok ? <dl className={styles.details}><div className={styles.detail}><dt>ایمیل</dt><dd dir="ltr">{identity.data.email}</dd></div><div className={styles.detail}><dt>شناسه کاربر</dt><dd dir="ltr">{identity.data.userId}</dd></div><div className={styles.detail}><dt>وضعیت نشست</dt><dd><StatusBadge tone="success">فعال</StatusBadge></dd></div></dl> : <StatusAlert tone="danger" title="اطلاعات حساب بارگذاری نشد">ارتباط با سرویس هویت برقرار نشد.</StatusAlert>}</Card>
    <Card><h2 className={styles.cardTitle}>فضای کاری فعال</h2><p className={styles.cardCopy}>عضویت‌ها توسط سرور کنترل می‌شوند.</p>{workspace?.ok ? <dl className={styles.details}><div className={styles.detail}><dt>نام</dt><dd>{workspace.data.workspaceName}</dd></div><div className={styles.detail}><dt>تعداد اعضا</dt><dd>{workspace.data.members.length.toLocaleString("fa-IR")}</dd></div>{identity.ok ? <div className={styles.detail}><dt>نقش من</dt><dd>{roleNames[workspace.data.members.find((member) => member.userId === identity.data.userId)?.role ?? ""] ?? "عضو"}</dd></div> : null}</dl> : <><StatusAlert tone={workspace?.status === 503 ? "danger" : "info"} title={session.workspaceId ? "فضای کاری در دسترس نیست" : "فضای کاری انتخاب نشده است"}>{session.workspaceId ? "دسترسی یا ارتباط با فضای کاری قابل تأیید نیست." : "برای ادامه یک فضای کاری بسازید."}</StatusAlert><div className={styles.actions}><ButtonLink href="/onboarding/workspace">مدیریت فضای کاری</ButtonLink></div></>}</Card>
    <Card className={styles.full}><h2 className={styles.cardTitle}>میانبرهای حساب</h2><div className={styles.actions}><ButtonLink href="/dashboard/settings/instagram" variant="secondary">اتصال اینستاگرام</ButtonLink><ButtonLink href="/dashboard/billing" variant="secondary">اشتراک من</ButtonLink><ButtonLink href="/dashboard/help" variant="secondary">راهنما</ButtonLink></div></Card>
  </div></>;
}
