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
  "account.alreadyDisconnected": "این اتصال از قبل قطع شده بود.",
  "account.oauthRejected": "اتصال توسط اینستاگرام تأیید نشد.",
  "account.oauthUnavailable": "سرویس اتصال در دسترس نیست؛ بعداً تلاش کنید.",
};

export function describeConnectionFailure(code: string | null): string {
  return (code && FAILURE_COPY[code]) ?? "خطایی رخ داد؛ دوباره تلاش کنید.";
}
