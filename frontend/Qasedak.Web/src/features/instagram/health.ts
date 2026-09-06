/*
 * Connection-state presentation helpers for the Instagram account UI.
 * Pure mapping only; all connection decisions stay server-side.
 */

export type AccountHealth =
  | "Healthy"
  | "ExpiringSoon"
  | "Expired"
  | "Revoked"
  | "Unhealthy"
  | "Disconnected";

export interface ConnectionState {
  accountId: string;
  providerIdentity: string;
  path: string;
  scopes: string[];
  health: string;
  healthDetail: string | null;
  tokenExpiresAtUtc: string | null;
  connectedAtUtc: string;
  disconnectedAtUtc: string | null;
  username: string | null;
  displayName: string | null;
  profilePictureUrl: string | null;
  accountType: string | null;
  profileUpdatedAtUtc: string | null;
  subscriptionHealth: string;
  subscriptionDetail: string | null;
  lastSubscriptionCheckUtc: string | null;
}

export type PillTone = "success" | "warning" | "danger" | "info" | "neutral";

const HEALTH_PRESENTATION: Record<string, { label: string; tone: PillTone }> = {
  Healthy: { label: "سالم", tone: "success" },
  ExpiringSoon: { label: "نزدیک انقضا", tone: "warning" },
  Expired: { label: "توکن منقضی", tone: "danger" },
  Revoked: { label: "دسترسی لغو شده", tone: "danger" },
  Unhealthy: { label: "ناسالم", tone: "danger" },
  Disconnected: { label: "قطع شده", tone: "neutral" },
};

/** Unknown backend values fail closed to a neutral, clearly-untranslated presentation. */
export function healthPresentation(health: string): { label: string; tone: PillTone } {
  return HEALTH_PRESENTATION[health] ?? { label: health, tone: "neutral" };
}

export const FAILURE_COPY: Record<string, string> = {
  "account.notFound": "اتصال مورد نظر پیدا نشد.",
  "account.alreadyConnected": "این پیج قبلاً متصل شده است.",
  "account.alreadyConnectedElsewhere": "این پیج در ورک‌اسپیس دیگری متصل است.",
  "account.alreadyDisconnected": "این اتصال از قبل قطع شده بود.",
  "account.oauthRejected": "اتصال توسط اینستاگرام تأیید نشد.",
  "account.oauthUnavailable": "سرویس اتصال در دسترس نیست؛ بعداً تلاش کنید.",
  "account.tokenMissing": "توکن این اتصال موجود نیست؛ دوباره متصل شوید.",
  "account.tokenExpired": "توکن این اتصال منقضی شده؛ دوباره متصل شوید.",
  "oauth.invalidState": "نشست اتصال معتبر نیست؛ فرایند اتصال را از نو آغاز کنید.",
  "oauth.expiredState": "مهلت اتصال تمام شد؛ فرایند اتصال را از نو آغاز کنید.",
  "oauth.replayedState": "این اتصال قبلاً استفاده شده؛ فرایند اتصال را از نو آغاز کنید.",
  "oauth.workspaceMismatch": "اتصال به ورک‌اسپیس دیگری تعلق دارد.",
  "oauth.redirectMismatch": "مسیر بازگشت اتصال معتبر نیست.",
  "profile.identityMismatch": "هویت پیج تأیید نشد؛ اتصال لغو شد.",
  "profile.unavailable": "دریافت اطلاعات پیج ممکن نشد؛ بعداً تلاش کنید.",
  "subscription.unavailable": "فعال‌سازی اعلان‌ها ممکن نشد؛ بعداً تعمیر کنید.",
  "subscription.permissionDenied": "مجوز اعلان‌ها داده نشد؛ دسترسی را بازبینی کنید.",
};

/** Subscription states that the user can recover without a new OAuth flow. */
export function subscriptionNeedsRepair(subscriptionHealth: string): boolean {
  return subscriptionHealth === "NeedsRepair" || subscriptionHealth === "Partial";
}

export function describeSubscriptionHealth(subscriptionHealth: string): string {
  switch (subscriptionHealth) {
    case "Healthy":
      return "اعلان‌ها فعال";
    case "Partial":
      return "اعلان‌ها ناقص";
    case "NeedsRepair":
      return "اعلان‌ها نیاز به تعمیر";
    default:
      return "وضعیت اعلان‌ها نامشخص";
  }
}

export function describeConnectionFailure(code: string | null): string {
  return (code && FAILURE_COPY[code]) ?? "خطایی رخ داد؛ دوباره تلاش کنید.";
}
