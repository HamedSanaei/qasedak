// Billing UI behavior tests (offline, deterministic) — presentation helpers + API client.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import ts from "typescript";
import { test } from "node:test";
import assert from "node:assert/strict";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function loadTsModule(relPath, requireMap = {}) {
  const source = readFileSync(path.join(root, relPath), "utf8");
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const module_ = { exports: {} };
  new Function("module", "exports", "require", js)(
    module_,
    module_.exports,
    (requestPath) => {
      if (requireMap[requestPath]) return requireMap[requestPath];
      throw new Error(`unexpected require: ${requestPath}`);
    },
  );
  return module_.exports;
}

const presentation = loadTsModule("src/features/billing/presentation.ts");

test("verified payments always present as success regardless of callback hint", () => {
  for (const hint of [null, "success", "failed", "cancelled"]) {
    const result = presentation.paymentResultPresentation(hint, "Verified", null);
    assert.equal(result.state, "success");
    assert.equal(result.tone, "success");
    assert.equal(result.title, "پرداخت موفق");
  }
});

test("canceled and verify-rejected failures map to distinct honest copy", () => {
  const cancelled = presentation.paymentResultPresentation(null, "Failed", "payment.canceledByUser");
  assert.equal(cancelled.state, "cancelled");
  assert.equal(cancelled.tone, "warning");

  const rejected = presentation.paymentResultPresentation(null, "Failed", "payment.verifyRejected");
  assert.equal(rejected.state, "failed");
  assert.equal(rejected.tone, "danger");

  const unknown = presentation.paymentResultPresentation(null, "Failed", "payment.somethingNew");
  assert.equal(unknown.state, "failed");
});

test("callback hints alone never claim success — they resolve to pending/cancelled/failed", () => {
  const successHint = presentation.paymentResultPresentation("success", null, null);
  assert.notEqual(successHint.state, "success");
  assert.equal(successHint.state, "pending"); // server verification still authoritative

  const cancelledHint = presentation.paymentResultPresentation("cancelled", null, null);
  assert.equal(cancelledHint.state, "cancelled");

  const failedHint = presentation.paymentResultPresentation("failed", null, null);
  assert.equal(failedHint.state, "failed");
});

test("amounts render exactly as received — grouped IRR without any conversion", () => {
  assert.equal(presentation.formatIrr(1500000), "۱٬۵۰۰٬۰۰۰ ریال");
  assert.equal(presentation.formatIrr(99000), "۹۹٬۰۰۰ ریال");
  assert.equal(presentation.formatIrr(1000), "۱٬۰۰۰ ریال");
  // No silent multiplication/division ever happens.
  const raw = 1234567;
  assert.ok(presentation.formatIrr(raw).includes("۱٬۲۳۴٬۵۶۷"));
});

test("feature limits use the design's -1/0 semantics in Persian", () => {
  assert.equal(presentation.featureLimitLabel(-1), "نامحدود");
  assert.equal(presentation.featureLimitLabel(0), "غیرفعال");
  assert.equal(presentation.featureLimitLabel(5), "۵");
});

test("subscription status maps to pill tones with fail-closed neutral", () => {
  assert.equal(presentation.subscriptionStatusTone("Active"), "success");
  assert.equal(presentation.subscriptionStatusTone("Trial"), "neutral");
  assert.equal(presentation.subscriptionStatusTone("PastDue"), "warning");
  assert.equal(presentation.subscriptionStatusTone("Canceled"), "danger");
  assert.equal(presentation.subscriptionStatusTone("Expired"), "danger");
  assert.equal(presentation.subscriptionStatusTone("SomethingElse"), "neutral");
});

test("billing api client targets workspace-scoped surface with bearer auth and no price input", async () => {
  const calls = [];
  const http = loadTsModule("src/shared/api/http.ts");
  http.setTransport(async (input, init) => {
    calls.push({ input: String(input), init });
    if (String(input).endsWith("/billing/plans")) {
      return {
        ok: true,
        status: 200,
        json: async () => ({ providers: ["zarinpal"], items: [{ code: "pro", name: "Pro", amountIrr: 1500000, purchasable: true, features: [] }] }),
      };
    }
    if (String(input).includes("/billing/payments/pay-1")) {
      return {
        ok: true,
        status: 200,
        json: async () => ({ attemptId: "pay-1", status: "Verified", failureCode: null, amountIrr: 1500000, provider: "zarinpal", createdAtUtc: "2026-08-24T00:00:00Z", verifiedAtUtc: "2026-08-24T00:05:00Z" }),
      };
    }
    if (String(input).endsWith("/checkout")) {
      return {
        ok: true,
        status: 202,
        json: async () => ({ attemptId: "pay-1", provider: "zarinpal", redirectUrl: "https://payment.zarinpal.com/pg/StartPay/A1" }),
      };
    }
    if (String(input).includes("/billing/subscription")) {
      return { ok: false, status: 404, json: async () => ({ code: "billing.subscriptionNotFound" }) };
    }
    return { ok: true, status: 200, json: async () => ({ items: [] }) };
  });
  const billing = loadTsModule("src/shared/api/billing.ts", { "./http": http });
  const client = billing.billingApi();

  await client.plans("tok");
  await client.checkout("tok", "ws-1", "pro", "zarinpal");
  const status = await client.paymentStatus("tok", "ws-1", "pay-1");
  const subscription = await client.subscription("tok", "ws-1");

  const checkoutCall = calls.find((c) => c.input.endsWith("/checkout"));
  const body = JSON.parse(checkoutCall.init.body);
  // The client may only submit plan code + provider id; never an amount or price.
  assert.deepEqual(Object.keys(body).sort(), ["planCode", "providerId"]);
  assert.ok(checkoutCall.init.headers.authorization === "Bearer tok");
  assert.ok(checkoutCall.input.includes("/workspaces/ws-1/billing/checkout"));
  assert.equal(status.attemptId, "pay-1");
  assert.equal(subscription, null); // 404 → normal "no subscription yet"
});
