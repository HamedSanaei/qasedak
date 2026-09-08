/* Pure Automation Builder V2 presentation and validation helpers. */
import type { AutomationActionKind, AutomationTriggerKind } from "../../shared/api/automations";

export type MatchMode = "anyReply" | "contains" | "equals";
export const MATCH_MODE_OPTIONS: Array<{ value: MatchMode; label: string; hint: string }> = [
  { value: "contains", label: "شامل", hint: "وجود عبارت در پیام" },
  { value: "equals", label: "برابر", hint: "تطبیق کامل عبارت" },
  { value: "anyReply", label: "هر ریپلایی", hint: "پاسخ به تمام ریپلای‌ها" },
];

export const AUTOMATION_STATUS_LABELS: Record<string, string> = { Draft: "پیش‌نویس", Active: "فعال", Disabled: "غیرفعال" };
export const AUTOMATION_MAX_NAME_LENGTH = 200;
export const AUTOMATION_MAX_MESSAGE_LENGTH = 1000;
export const PUBLIC_REPLY_MAX_CHARACTERS = 1000;
export const GATE_PROMPT_MAX_CHARACTERS = 640;
export const BUTTON_TITLE_MAX_CHARACTERS = 20;
export const FOLLOW_URL_MAX_CHARACTERS = 2000;
export const FOLLOW_UP_MAX_MINUTES = 7 * 24 * 60;

export function utf8ByteLength(value: string): number {
  return new TextEncoder().encode(value).length;
}

export function validateAutomationName(name: string): string | null {
  const trimmed = name.trim();
  if (trimmed.length === 0) return "automation.nameRequired";
  if (trimmed.length > AUTOMATION_MAX_NAME_LENGTH) return "automation.nameTooLong";
  return null;
}
export function validatePlainText(text: string): string | null {
  if (text.trim().length === 0) return "automation.actionTextRequired";
  if (utf8ByteLength(text) > AUTOMATION_MAX_MESSAGE_LENGTH) return "automation.actionTextTooLong";
  return null;
}

export function validatePublicReply(text: string): string | null {
  if (text.trim().length === 0) return "automation.actionTextRequired";
  if (text.length > PUBLIC_REPLY_MAX_CHARACTERS) return "automation.actionTextTooLong";
  return null;
}

export function validateDefinition(matchMode: MatchMode, keywords: string[], messageText: string): string | null {
  if (matchMode !== "anyReply" && keywords.filter((keyword) => keyword.trim().length > 0).length === 0) return "automation.keywordRequired";
  return validatePlainText(messageText);
}

const ACTIONS_BY_TRIGGER: Record<AutomationTriggerKind, readonly AutomationActionKind[]> = {
  CommentCreated: ["SendPrivateReply", "SendPublicReply", "StartRevealFlow", "ScheduleFollowUp"],
  InboundDirectMessage: ["DirectMessage", "ScheduleFollowUp"],
};

export function allowedActions(trigger: AutomationTriggerKind): readonly AutomationActionKind[] {
  return ACTIONS_BY_TRIGGER[trigger];
}

export function actionAllowed(trigger: AutomationTriggerKind, action: string): boolean {
  if (action === "SendDirectMessage") return true; // persisted legacy only; never offered for new authoring
  return allowedActions(trigger).includes(action as AutomationActionKind);
}

export function legacyActionLabel(trigger: string): string {
  return trigger === "CommentCreated" ? "پاسخ خصوصی (تعریف قدیمی)" : "دایرکت (تعریف قدیمی)";
}
export interface RevealValidationInput {
  openingText: string;
  gatePromptText: string;
  postbackButtonTitle: string;
  revealText: string;
  followUrl: string;
  followButtonTitle: string;
}

export function validateReveal(input: RevealValidationInput): string | null {
  const opening = validatePlainText(input.openingText);
  if (opening) return opening;
  if (input.gatePromptText.trim().length === 0) return "automation.revealTextRequired";
  if (input.gatePromptText.length > GATE_PROMPT_MAX_CHARACTERS) return "automation.gatePromptTooLong";
  if (input.postbackButtonTitle.trim().length === 0) return "automation.revealButtonTitleRequired";
  if (input.postbackButtonTitle.length > BUTTON_TITLE_MAX_CHARACTERS) return "automation.revealButtonTitleTooLong";
  if (validatePlainText(input.revealText)) return "automation.revealTextTooLong";
  if (input.followButtonTitle.length > BUTTON_TITLE_MAX_CHARACTERS) return "automation.revealButtonTitleTooLong";
  if (input.followUrl.length > FOLLOW_URL_MAX_CHARACTERS) return "automation.revealFollowUrlTooLong";
  if (input.followUrl.trim()) {
    try {
      const parsed = new URL(input.followUrl);
      if ((parsed.protocol !== "http:" && parsed.protocol !== "https:") || /[\u0000-\u001F\u007F]/.test(input.followUrl)) return "automation.revealFollowUrlScheme";
    } catch { return "automation.revealFollowUrlScheme"; }
  }
  return null;
}

export function validateFollowUp(message: string, delayMinutes: number): string | null {
  const messageError = validatePlainText(message);
  if (messageError) return messageError;
  if (!Number.isInteger(delayMinutes) || delayMinutes < 1 || delayMinutes > FOLLOW_UP_MAX_MINUTES) return "automation.followUpDelayInvalid";
  return null;
}
const FAILURE_COPY: Record<string, string> = {
  "automation.notFound": "اتوماسیون پیدا نشد.",
  "automation.nameRequired": "نام اتوماسیون الزامی است.",
  "automation.nameTooLong": `نام اتوماسیون حداکثر ${AUTOMATION_MAX_NAME_LENGTH} کاراکتر است.`,
  "automation.accountInvalid": "حساب انتخاب‌شده معتبر نیست.",
  "automation.bindingImmutable": "حساب این اتوماسیون پس از ساخت قابل تغییر نیست؛ برای حساب دیگر اتوماسیون جدید بسازید.",
  "automation.keywordRequired": "در حالت کلمات کلیدی حداقل یک کلمه لازم است.",
  "automation.tooManyKeywordFilters": "تعداد کلمات کلیدی بیش از حد مجاز است.",
  "automation.wholeWordRequiresKeywords": "تطبیق کلمه کامل فقط در حالت کلمات کلیدی قابل استفاده است.",
  "automation.sourceMediaIdRequired": "برای منبع مشخص باید یک رسانه انتخاب شود.",
  "automation.dmSourceScopeNotApplicable": "برای دایرکت ورودی، محدودکردن به رسانه کاربرد ندارد.",
  "automation.actionRequired": "حداقل یک عمل لازم است.",
  "automation.tooManyActions": "حداکثر پنج عمل در هر اتوماسیون مجاز است.",
  "automation.capabilityUnavailable": "قابلیت لازم برای این تنظیم روی حساب انتخاب‌شده در دسترس نیست.",
  "automation.unsupportedConfiguration": "این اتوماسیون شامل مقدار ناشناخته/آینده است و بدون تغییر صریح قابل ذخیره نیست.",
  "automation.actionKindInvalid": "نوع عمل پشتیبانی نمی‌شود.",
  "automation.actionNotAllowedForTrigger": "این عمل برای رویداد انتخاب‌شده مجاز نیست.",
  "automation.conflictingPrivateReplyEffects": "در یک رویداد کامنت فقط یک عمل مصرف‌کننده پاسخ خصوصی می‌تواند وجود داشته باشد.",
  "automation.actionTextRequired": "متن پیام الزامی است.",
  "automation.actionTextTooLong": "متن پیام از حد مجاز عبور کرده است.",
  "automation.followUpDelayInvalid": "زمان پیگیری باید بین یک دقیقه تا هفت روز باشد.",
  "automation.revealTextRequired": "متن‌های لازم جریان نمایش تکمیل نشده‌اند.",
  "automation.gatePromptTooLong": "متن پیام دروازه حداکثر ۶۴۰ کاراکتر است.",
  "automation.revealButtonTitleRequired": "عنوان دکمه الزامی است.",
  "automation.revealButtonTitleTooLong": "عنوان دکمه حداکثر ۲۰ کاراکتر است.",
  "automation.revealTextTooLong": "متن نمایش از سقف ۱۰۰۰ بایت UTF-8 عبور کرده است.",
  "automation.revealFollowUrlTooLong": "نشانی دنبال‌کردن بیش از حد طولانی است.",
  "automation.revealFollowUrlScheme": "نشانی دنبال‌کردن باید URL کامل http یا https باشد.",
};
Object.assign(FAILURE_COPY, {
  "automation.definitionRequired": "تعریف اتوماسیون نامعتبر است.",
  "automation.triggerKindInvalid": "نوع رویداد فعال‌ساز معتبر نیست.",
  "automation.conditionInvalid": "شرط انتخابی معتبر نیست.",
  "automation.alreadyActive": "این اتوماسیون از قبل فعال است.",
  "automation.notActive": "فقط اتوماسیون فعال قابل توقف است.",
  "automation.alreadyDisabled": "این اتوماسیون حذف (غیرفعال نهایی) شده است.",
  "automation.disabled": "اتوماسیون غیرفعال‌شده دیگر قابل تغییر نیست.",
  "automation.versionFrozen": "نسخه فعال اتوماسیون قفل است؛ ابتدا آن را متوقف کنید.",
  "billing.subscriptionRequired": "برای فعال‌سازی، اشتراک فعال لازم است.",
  "billing.limitExceeded": "سقف اتوماسیون‌های فعال پلن شما پر است.",
});

export function describeAutomationFailure(code: string | null): string {
  return (code && FAILURE_COPY[code]) ?? "عملیات ناموفق بود؛ دوباره تلاش کنید.";
}

export function isEntitlementDenial(code: string | null): boolean {
  return code === "billing.subscriptionRequired" || code === "billing.limitExceeded";
}
