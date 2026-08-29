/*
 * Contacts (CRM) presentation helpers. Pure mapping of backend rules to Persian copy and
 * client-side validation mirrors; unknown backend values fail closed. Bounds mirror the
 * Contacts domain aggregate (Contact.cs): 32-char tags, 12 tags per contact, 2000-char notes.
 */

/** Mirrors Contact.MaxTagLength. */
export const TAG_MAX_LENGTH = 32;

/** Mirrors Contact.MaxNoteLength. */
export const NOTE_MAX_LENGTH = 2000;

export const CONTACT_FAILURE_COPY: Record<string, string> = {
  "contact.notFound": "مخاطب پیدا نشد.",
  "contact.identityRequired": "شناسهٔ مخاطب الزامی است.",
  "contact.tagRequired": "برچسب نمی‌تواند خالی باشد.",
  "contact.tagTooLong": `برچسب بیش از ${TAG_MAX_LENGTH} کاراکتر است.`,
  "contact.tooManyTags": "حداکثر ۱۲ برچسب برای هر مخاطب ممکن است.",
  "contact.noteRequired": "یادداشت نمی‌تواند خالی باشد.",
  "contact.noteTooLong": `یادداشت بیش از ${NOTE_MAX_LENGTH} کاراکتر است.`,
  "contact.notActive": "این مخاطب فعال نیست و قابل ویرایش نیست.",
};

export function describeContactFailure(code: string | null): string {
  return (code && CONTACT_FAILURE_COPY[code]) ?? "بروزرسانی مخاطب ناموفق بود؛ دوباره تلاش کنید.";
}

export function validateTagInput(tag: string): string | null {
  if (tag.trim().length === 0) return "contact.tagRequired";
  if (tag.trim().length > TAG_MAX_LENGTH) return "contact.tagTooLong";
  return null;
}

export function validateNoteInput(note: string): string | null {
  if (note.trim().length === 0) return "contact.noteRequired";
  if (note.trim().length > NOTE_MAX_LENGTH) return "contact.noteTooLong";
  return null;
}

/**
 * Empty-state copy shown when a conversation has no CRM contact yet. The panel is live —
 * editing is disabled only because there is no aggregate to edit, not because the CRM is
 * incomplete (M07 shipped; the old «تا تکمیل M07» warning no longer applies).
 */
export const CONTACT_PANEL_EMPTY_TITLE = "مخاطب CRM ثبت نشده است";
export const CONTACT_PANEL_EMPTY_BODY =
  "با شروع یک گفتگو، مخاطب به‌صورت خودکار ساخته می‌شود و می‌توانید برایش برچسب و یادداشت ثبت کنید.";