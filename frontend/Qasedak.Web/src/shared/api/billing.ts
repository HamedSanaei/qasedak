/*
 * Application-owned API client for the workspace Billing surface
 * (GET /billing/plans, GET /workspaces/{id}/billing/subscription,
 *  POST /workspaces/{id}/billing/checkout, GET /workspaces/{id}/billing/payments[/{id}]).
 * Amounts are server-authoritative IRR integers; the UI never mutates them.
 */
import { request } from "./http";

export interface PlanFeature {
  key: string;
  limit: number;
}

export interface PlanSummary {
  code: string;
  name: string;
  amountIrr: number;
  purchasable: boolean;
  features: PlanFeature[];
}

export interface PlansResponse {
  providers: string[];
  items: PlanSummary[];
}

export interface SubscriptionOverview {
  status: string;
  startedAtUtc: string | null;
  currentPeriodEndUtc: string | null;
  entitled: boolean;
  plan: { code: string; name: string; amountIrr: number } | null;
}

export interface CheckoutResult {
  attemptId: string;
  provider: string;
  redirectUrl: string;
}

export interface PaymentStatus {
  attemptId: string;
  status: "Pending" | "Verified" | "Failed";
  failureCode: string | null;
  amountIrr: number;
  provider: string;
  createdAtUtc: string;
  verifiedAtUtc: string | null;
}

export interface PaymentsResponse {
  items: PaymentStatus[];
}

export interface BillingApi {
  plans(token: string): Promise<PlansResponse>;
  subscription(token: string, workspaceId: string): Promise<SubscriptionOverview | null>;
  checkout(
    token: string,
    workspaceId: string,
    planCode: string,
    providerId: string,
  ): Promise<CheckoutResult>;
  paymentStatus(token: string, workspaceId: string, attemptId: string): Promise<PaymentStatus | null>;
  payments(token: string, workspaceId: string): Promise<PaymentsResponse>;
}

export const PROVIDER_LABELS: Record<string, string> = {
  zarinpal: "زرین‌پال",
  melli: "پرداخت مستقیم بانک ملی",
};

export function billingApi(): BillingApi {
  return {
    plans: (token) => request<PlansResponse>("/api/v1/billing/plans", { bearerToken: token }),
    subscription: async (token, workspaceId) => {
      try {
        return await request<SubscriptionOverview>(
          `/api/v1/workspaces/${workspaceId}/billing/subscription`,
          { bearerToken: token },
        );
      } catch (error) {
        // No subscription yet is a normal state, not an error surface.
        if (error instanceof Error && "status" in error && (error as { status?: number }).status === 404) {
          return null;
        }
        throw error;
      }
    },
    checkout: (token, workspaceId, planCode, providerId) =>
      request<CheckoutResult>(`/api/v1/workspaces/${workspaceId}/billing/checkout`, {
        method: "POST",
        body: { planCode, providerId },
        bearerToken: token,
      }),
    paymentStatus: async (token, workspaceId, attemptId) => {
      try {
        return await request<PaymentStatus>(
          `/api/v1/workspaces/${workspaceId}/billing/payments/${attemptId}`,
          { bearerToken: token },
        );
      } catch (error) {
        if (error instanceof Error && "status" in error && (error as { status?: number }).status === 404) {
          return null;
        }
        throw error;
      }
    },
    payments: (token, workspaceId) =>
      request<PaymentsResponse>(`/api/v1/workspaces/${workspaceId}/billing/payments`, {
        bearerToken: token,
      }),
  };
}
