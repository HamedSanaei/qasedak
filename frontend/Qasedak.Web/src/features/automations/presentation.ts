/*
 * Automation-builder presentation helpers. Pure mapping/validation; the backend stays
 * authoritative (domain limits: name ≤200, ≤10 conditions, 1..5 actions, message ≤1000).
 */

export type MatchMode = "anyReply" | "contains" | "equals";

export const MATCH_MODE_OPTIONS: Array<{ value: MatchMode; label: string; hint: string }> = [
  { value: "contains", label: "شامل", hint: "وجود عبارت در پیام" },
  { value: "equals", label: "برابر", hint: "تطبیق کامل عبارت" },
  { value: "anyReply", label: "هر ریپلایی", hint: "پاسخ به تمام ریپلای‌ها" },
];

export const AUTOMATION_STATUS_LABELS: Record<string, string> = {
  Draft: "پیش‌نویس",
  Active: "فعال",
  Disabled: "غیرفعال",
};

export const AUTOMATION_MAX_NAME_LENGTH = 200;
export const AUTOMATION_MAX_MESSAGE_LENGTH = 1000;

export function validateAutomationName(name: string): string | null {
  const trimmed = name.trim();
  if (trimmed.length === 0) return "automation.nameRequired";
  if (trimmed.length > AUTOMATION_MAX_NAME_LENGTH) return "automation.nameTooLong";
  return null;
}

/** Returns the stable failure code the wire payload would produce, or null when valid. */
export function validateDefinition(matchMode: MatchMode, keywords: string[], messageText: string): string | null {
  if ((matchMode !== "anyReply") && keywords.filter((k) => k.trim().length > 0).length === 0) {
    return "automation.keywordRequired";
  }
  if (messageText.trim().length === 0) return "automation.actionTextRequired";
  if (messageText.length > AUTOMATION_MAX_MESSAGE_LENGTH) return "automation.actionTextTooLong";
  return null;
}

const FAILURE_COPY: Record<string, string> = {
  "automation.notFound": "اتوماسیون پیدا نشد.",
  "automation.nameRequired": "نام اتوماسیون الزامی است.",
  "automation.nameTooLong": `نام اتوماسیون حداکثر ${AUTOMATION_MAX_NAME_LENGTH} کاراکتر است.`,
  "automation.keywordRequired": "حداقل یک واژه فعال‌کننده وارد کنید یا «هر ریپلایی» را انتخاب کنید.",
  "automation.tooManyKeywordFilters": "تعداد واژه‌های فعال‌کننده بیش از حد مجاز است.",
  "automation.conditionInvalid": "شرط انتخابی معتبر نیست.",
  "automation.actionRequired": "حداقل یک پاسخ برای اتوماسیون لازم است.",
  "automation.actionTextRequired": "متن پاسخ نمی‌تواند خالی باشد.",
  "automation.actionTextTooLong": `متن پاسخ حداکثر ${AUTOMATION_MAX_MESSAGE_LENGTH} کاراکتر است.`,
  "automation.definitionRequired": "تعریف اتوماسیون نامعتبر است.",
  "automation.triggerKindInvalid": "نوع رویداد فعال‌ساز معتبر نیست.",
  "automation.alreadyActive": "این اتوماسیون از قبل فعال است.",
  "automation.notActive": "فقط اتوماسیون فعال قابل توقف است.",
  "automation.alreadyDisabled": "این اتوماسیون حذف (غیرفعال نهایی) شده است.",
  "automation.disabled": "اتوماسیون غیرفعال‌شده دیگر قابل تغییر نیست.",
  "automation.versionFrozen": "نسخه فعال اتوماسیون قفل است؛ ابتدا آن را متوقف کنید.",
  // Server-owned entitlement denials surface verbatim from the activation policy:
  "billing.subscriptionRequired": "برای فعال‌سازی، اشتراک فعال لازم است.",
  "billing.limitExceeded": "سقف اتوماسیون‌های فعال پلن شما پر است.",
};

export function describeAutomationFailure(code: string | null): string {
  return (code && FAILURE_COPY[code]) ?? "عملیات ناموفق بود؛ دوباره تلاش کنید.";
}

export function isEntitlementDenial(code: string | null): boolean {
  return code === "billing.subscriptionRequired" || code === "billing.limitExceeded";
}
