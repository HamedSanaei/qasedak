/*
 * Pure client-side validation mirroring the backend identity rules so users get
 * early feedback. The BACKEND REMAINS AUTHORITATIVE — these helpers only prevent
 * obviously doomed requests; every server failure code is still surfaced.
 * Failure-code constants mirror the identity module's stable failure codes.
 */
export const FAILURE_CODES = {
  invalidEmail: "auth.invalidEmail",
  invalidDisplayName: "auth.invalidDisplayName",
  emailTaken: "auth.emailTaken",
  weakPassword: "auth.weakPassword",
  invalidCredentials: "auth.invalidCredentials",
  invalidName: "workspace.invalidName",
} as const;

export const PASSWORD_MIN_LENGTH = 10;
export const PASSWORD_MAX_LENGTH = 128;
export const WORKSPACE_NAME_MIN_LENGTH = 3;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Returns null when valid, else the stable backend failure code. */
export function validateEmail(email: string): string | null {
  return EMAIL_PATTERN.test(email.trim()) ? null : FAILURE_CODES.invalidEmail;
}

/** Backend: not null/whitespace; treated as display name rule (auth.invalidDisplayName). */
export function validateDisplayName(name: string): string | null {
  return name.trim().length > 0 ? null : FAILURE_CODES.invalidDisplayName;
}

/**
 * Mirrors PasswordPolicy.Validate: length 10..128 and at least one character that is
 * NOT a letter/digit (all alphanumeric ⇒ weak).
 */
export function validatePassword(password: string): string | null {
  if (
    password.length < PASSWORD_MIN_LENGTH ||
    password.length > PASSWORD_MAX_LENGTH ||
    /^[\p{L}\p{N}]+$/u.test(password)
  ) {
    return FAILURE_CODES.weakPassword;
  }
  return null;
}

/** Mirrors WorkspaceName: 3..64 chars after trim (workspace.invalidName). */
export function validateWorkspaceName(name: string): string | null {
  const trimmed = name.trim();
  if (trimmed.length < WORKSPACE_NAME_MIN_LENGTH || trimmed.length > 64) {
    return FAILURE_CODES.invalidName;
  }
  return null;
}

/** Persian labels for stable backend failure codes. */
export function describeFailure(code: string | null): string {
  switch (code) {
    case FAILURE_CODES.invalidEmail:
      return "ایمیل معتبر نیست.";
    case FAILURE_CODES.invalidDisplayName:
      return "نام نمایشی نمی‌تواند خالی باشد.";
    case FAILURE_CODES.emailTaken:
      return "این ایمیل قبلاً ثبت شده است.";
    case FAILURE_CODES.weakPassword:
      return `رمز عبور باید حداقل ${PASSWORD_MIN_LENGTH} کاراکتر و شامل نمادی غیرحرفی باشد.`;
    case FAILURE_CODES.invalidCredentials:
      return "ایمیل یا رمز عبور نادرست است.";
    case FAILURE_CODES.invalidName:
      return "نام ورک‌اسپیس باید بین ۳ تا ۶۴ کاراکتر باشد.";
    default:
      return "خطایی رخ داد؛ دوباره تلاش کنید.";
  }
}
