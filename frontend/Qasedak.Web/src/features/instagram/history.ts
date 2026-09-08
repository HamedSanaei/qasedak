export type HistorySyncStatus =
  | "Queued"
  | "Running"
  | "CompletedWithinProviderLimits"
  | "RateLimitedRetrying"
  | "Failed"
  | "Unknown";

export interface HistorySyncOperation {
  operationId: string;
  kind: string;
  status: HistorySyncStatus;
  stage: number;
  conversationsObserved: number;
  messageIdsObserved: number;
  detailsFetched: number;
  messagesImported: number;
  duplicates: number;
  unsupported: number;
  historyWindowLimited: number;
  failureCategory: string | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  createdAtUtc: string;
}

const KNOWN = new Set<HistorySyncStatus>([
  "Queued", "Running", "CompletedWithinProviderLimits", "RateLimitedRetrying", "Failed",
]);

export function normalizeHistoryStatus(value: string | null | undefined): HistorySyncStatus {
  return value && KNOWN.has(value as HistorySyncStatus) ? (value as HistorySyncStatus) : "Unknown";
}

export function isTerminalHistoryStatus(status: HistorySyncStatus): boolean {
  return status === "CompletedWithinProviderLimits" || status === "Failed" || status === "Unknown";
}

export function historyStatusLabel(status: HistorySyncStatus): string {
  switch (status) {
    case "Queued": return "در صف";
    case "Running": return "در حال همگام‌سازی";
    case "CompletedWithinProviderLimits": return "پایان‌یافته در محدوده پوشش ارائه‌دهنده";
    case "RateLimitedRetrying": return "محدودیت نرخ؛ تلاش مجدد زمان‌بندی شده";
    case "Failed": return "ناموفق";
    case "Unknown": return "وضعیت ناشناخته";
  }
}

export const HISTORY_COVERAGE_COPY =
  "پوشش تاریخچه محدود به داده‌ای است که اینستاگرام در API ارائه می‌کند؛ جزئیات فقط برای پیام‌های جدیدتر قابل بازیابی است و گفتگوهای قدیمیِ غیرفعال در Requests ممکن است در پاسخ ارائه‌دهنده وجود نداشته باشند.";
