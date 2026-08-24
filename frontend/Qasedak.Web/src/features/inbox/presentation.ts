/*
 * Inbox presentation helpers. Pure mapping; unknown backend values fail closed.
 * NOTE (M08-004): no inbox/conversation design exists in the canonical Penpot file
 * (all 24 pages swept). This screen uses only already-approved foundation tokens and
 * primitives; its visual sync is BLOCKED pending a human-approved design — see
 * docs/design/sync/M08-004-conversation-inbox.md and SCREEN-INVENTORY.md.
 */

export const CONVERSATION_STATUS_LABELS: Record<string, string> = {
  open: "باز",
  pending: "در انتظار",
  archived: "بایگانی",
};

export function statusLabel(status: string): string {
  return CONVERSATION_STATUS_LABELS[status] ?? status;
}

/** Reply failure copy for every stable ReplyFailures/channel code surfaced by the API. */
export const REPLY_FAILURE_COPY: Record<string, string> = {
  "conversation.notFound": "گفتگو پیدا نشد.",
  "reply.emptyText": "متن پاسخ نمی‌تواند خالی باشد.",
  "reply.tooLong": "متن پاسخ بیش از حد مجاز است.",
  "reply.archivedThread": "این گفتگو بایگانی شده و پاسخی دریافت نمی‌کند.",
  "reply.messagingWindowClosed": "پنجره پیام‌رسانی اینستاگرام بسته است؛ پاسخ ممکن نیست.",
  "channel.unsupported": "کانال گفتگو پشتیبانی نمی‌شود.",
  "instagram.noConnectedAccount": "هیچ پیج اینستاگرامی متصل نیست.",
  "instagram.tokenMissing": "توکن دسترسی پیج در دسترس نیست؛ اتصال را به‌روزرسانی کنید.",
};

export function describeReplyFailure(code: string | null): string {
  return (code && REPLY_FAILURE_COPY[code]) ?? "ارسال پاسخ ناموفق بود؛ دوباره تلاش کنید.";
}

export const REPLY_MAX_LENGTH = 1000;

export function validateReplyText(text: string): string | null {
  if (text.trim().length === 0) return "reply.emptyText";
  if (text.length > REPLY_MAX_LENGTH) return "reply.tooLong";
  return null;
}

const FA_DIGITS = ["۰", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹"];

/** Deterministic fa-IR relative-time label for tests and UI (no Intl dependency). */
export function formatRelativeFa(iso: string, nowUtcMs: number): string {
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return "";
  const diffMinutes = Math.round((nowUtcMs - then) / 60000);
  const toFa = (n: number) =>
    String(n)
      .split("")
      .map((ch) => (/\d/.test(ch) ? FA_DIGITS[Number(ch)] : ch))
      .join("");
  if (diffMinutes < 1) return "همین حالا";
  if (diffMinutes < 60) return `${toFa(diffMinutes)} دقیقه پیش`;
  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return `${toFa(diffHours)} ساعت پیش`;
  const diffDays = Math.floor(diffHours / 24);
  return `${toFa(diffDays)} روز پیش`;
}
