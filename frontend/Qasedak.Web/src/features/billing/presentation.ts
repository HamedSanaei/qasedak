/*
 * Presentation helpers for the Billing & Payments screens, synchronized from canonical
 * Penpot boards (page c48311ed-e700-80f8-8008-8820a6cf5187). Pure functions only.
 */

export type ResultState = "pending" | "success" | "failed" | "cancelled" | "alreadyVerified" | "error";

export interface ResultPresentation {
  state: ResultState;
  tone: "success" | "warning" | "danger" | "info";
  title: string;
  body: string;
}

/** Maps a callback query `state` hint plus server status/failure code to result copy. */
export function paymentResultPresentation(
  stateHint: string | null,
  status: string | null,
  failureCode: string | null,
): ResultPresentation {
  const normalized = (stateHint ?? "").toLowerCase();
  if (status === "Verified") {
    return {
      state: "success",
      tone: "success",
      title: "پرداخت موفق",
      body: "اشتراک شما فعال شد و سطح دسترسی به‌روزرسانی گردید.",
    };
  }
  if (status === "Failed") {
    if (failureCode === "payment.canceledByUser") {
      return {
        state: "cancelled",
        tone: "warning",
        title: "پرداخت لغو شد",
        body: "پرداخت در درگاه تکمیل نشد. می‌توانید دوباره تلاش کنید.",
      };
    }
    if (failureCode === "payment.verifyRejected") {
      return {
        state: "failed",
        tone: "danger",
        title: "پرداخت ناموفق",
        body: "تأیید تراکنش نزد درگاه انجام نشد. مبلغی از شما برداشت نشده است؛ دوباره تلاش کنید.",
      };
    }
    return {
      state: "failed",
      tone: "danger",
      title: "پرداخت ناموفق",
      body: "پرداخت با خطا متوقف شد. می‌توانید دوباره تلاش کنید.",
    };
  }
  if (normalized === "success") {
    // Callback said success but verification is still settling; keep polling.
    return {
      state: "pending",
      tone: "info",
      title: "در حال بررسی پرداخت",
      body: "نتیجه نهایی پس از تأیید سرور با درگاه نمایش داده می‌شود.",
    };
  }
  if (normalized === "cancelled" || normalized === "canceled") {
    return {
      state: "cancelled",
      tone: "warning",
      title: "پرداخت لغو شد",
      body: "پرداخت در درگاه تکمیل نشد. می‌توانید دوباره تلاش کنید.",
    };
  }
  if (normalized === "failed") {
    return {
      state: "failed",
      tone: "danger",
      title: "پرداخت ناموفق",
      body: "پرداخت با خطا متوقف شد. می‌توانید دوباره تلاش کنید.",
    };
  }
  return {
    state: "pending",
    tone: "info",
    title: "در حال بررسی پرداخت",
    body: "وضعیت پرداخت از سرور دریافت می‌شود…",
  };
}

export function subscriptionStatusTone(status: string): "success" | "warning" | "danger" | "neutral" {
  switch (status) {
    case "Active":
      return "success";
    case "Trial":
      return "neutral";
    case "PastDue":
      return "warning";
    case "Canceled":
    case "Expired":
      return "danger";
    default:
      return "neutral";
  }
}

const PERSIAN_DIGITS = ["۰", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹"];

function toPersianDigits(value: string): string {
  return value.replace(/[0-9]/g, (d) => PERSIAN_DIGITS[Number(d)]);
}

/**
 * Formats a server-owned IRR amount for display only — grouping separators plus
 * «ریال». Never converts or multiplies; the amount stays exactly what the API sent.
 */
export function formatIrr(amountIrr: number): string {
  const grouped = Math.trunc(amountIrr)
    .toString()
    .replace(/\B(?=(\d{3})+(?!\d))/g, "٬");
  return `${toPersianDigits(grouped)} ریال`;
}

export function providerLabel(providerId: string): string {
  const known: Record<string, string> = { zarinpal: "زرین‌پال", mellat: "به‌پرداخت ملت" };
  return known[providerId] ?? providerId;
}

export function featureLimitLabel(limit: number): string {
  if (limit === -1) return "نامحدود";
  if (limit === 0) return "غیرفعال";
  return toPersianDigits(String(limit));
}
