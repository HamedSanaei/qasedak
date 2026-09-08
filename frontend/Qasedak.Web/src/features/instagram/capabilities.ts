export type InstagramCapabilityName =
  | "MediaCatalog"
  | "Analytics"
  | "Messaging"
  | "CommentAutomation"
  | "PrivateReply"
  | "PublicReply"
  | "RevealFlow"
  | "FollowGate"
  | "ConversationHistorySync";

export type InstagramCapabilityState =
  | "Available"
  | "PermissionRequired"
  | "Disconnected"
  | "Unhealthy"
  | "TemporarilyUnavailable"
  | "Unsupported";

export interface InstagramCapabilityItem {
  capability: InstagramCapabilityName;
  state: InstagramCapabilityState;
  reasonCode: string | null;
}

export interface AccountCapabilities {
  accountId: string;
  capabilities: InstagramCapabilityItem[];
}

const KNOWN_STATES = new Set<InstagramCapabilityState>([
  "Available",
  "PermissionRequired",
  "Disconnected",
  "Unhealthy",
  "TemporarilyUnavailable",
  "Unsupported",
]);

export function normalizeCapabilityState(value: string | null | undefined): InstagramCapabilityState {
  return value && KNOWN_STATES.has(value as InstagramCapabilityState)
    ? (value as InstagramCapabilityState)
    : "Unsupported";
}

export function capabilityState(model: AccountCapabilities | null, name: InstagramCapabilityName): InstagramCapabilityState {
  const raw = model?.capabilities.find((item) => item.capability === name)?.state;
  return normalizeCapabilityState(raw);
}

export function capabilityAvailable(model: AccountCapabilities | null, name: InstagramCapabilityName): boolean {
  return capabilityState(model, name) === "Available";
}

export function capabilityExplanation(state: InstagramCapabilityState): string {
  switch (state) {
    case "Available": return "در دسترس";
    case "PermissionRequired": return "دسترسی لازم برای این قابلیت ثبت نشده است.";
    case "Disconnected": return "حساب اینستاگرام قطع شده است.";
    case "Unhealthy": return "اتصال حساب سالم نیست؛ ابتدا اتصال را بررسی کنید.";
    case "TemporarilyUnavailable": return "این قابلیت فعلاً به دلیل وضعیت محلی حساب یا اعلان‌ها در دسترس نیست.";
    case "Unsupported": return "این قابلیت در قرارداد فعلی قاصدک فعال نیست.";
  }
}
