// M08-003 — Instagram connection UI behavior tests (offline, deterministic).
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

const health = loadTsModule("src/features/instagram/health.ts");

test("every backend AccountHealth value maps to a Persian label and pill tone", () => {
  const expected = {
    Healthy: ["سالم", "success"],
    ExpiringSoon: ["نزدیک انقضا", "warning"],
    Expired: ["توکن منقضی", "danger"],
    Revoked: ["دسترسی لغو شده", "danger"],
    Unhealthy: ["ناسالم", "danger"],
    Disconnected: ["قطع شده", "neutral"],
  };
  for (const [value, [label, tone]] of Object.entries(expected)) {
    const presentation = health.healthPresentation(value);
    assert.equal(presentation.label, label);
    assert.equal(presentation.tone, tone);
  }
});

test("unknown health values fail closed to neutral tone without translation invention", () => {
  const presentation = health.healthPresentation("SomethingNew");
  assert.equal(presentation.label, "SomethingNew");
  assert.equal(presentation.tone, "neutral");
});

test("connection failure copy covers every stable account failure code", () => {
  for (const code of [
    "account.notFound",
    "account.alreadyConnected",
    "account.alreadyConnectedElsewhere",
    "account.alreadyDisconnected",
    "account.oauthRejected",
    "account.oauthUnavailable",
    "account.tokenMissing",
    "account.tokenExpired",
    "oauth.invalidState",
    "oauth.expiredState",
    "oauth.replayedState",
    "oauth.workspaceMismatch",
    "oauth.redirectMismatch",
    "profile.identityMismatch",
    "profile.unavailable",
    "subscription.unavailable",
    "subscription.permissionDenied",
  ]) {
    const described = health.describeConnectionFailure(code);
    assert.notEqual(described, health.describeConnectionFailure(null), `untranslated code ${code}`);
  }
});

test("subscription helpers distinguish repairable states without inventing health", () => {
  assert.equal(health.subscriptionNeedsRepair("NeedsRepair"), true);
  assert.equal(health.subscriptionNeedsRepair("Partial"), true);
  assert.equal(health.subscriptionNeedsRepair("Healthy"), false);
  assert.equal(health.subscriptionNeedsRepair("Unknown"), false);
  assert.equal(health.describeSubscriptionHealth("Healthy"), "اعلان‌ها فعال");
  assert.equal(health.describeSubscriptionHealth("SomethingNew"), "وضعیت اعلان‌ها نامشخص");
});

test("connections api client targets the workspace-scoped surface with bearer auth", async () => {
  const calls = [];
  const http = loadTsModule("src/shared/api/http.ts");
  assert.equal(http.apiBaseUrl, "");
  http.setTransport(async (input, init) => {
    calls.push({ input, init });
    return {
      ok: true,
      status: 200,
      json: async () =>
        String(input).includes("/authorize-url")
          ? { url: "https://instagram.com/oauth/authorize?x=1" }
          : { items: [{ accountId: "a-1", health: "Healthy" }] },
    };
  });
  const connectionsModule = loadTsModule("src/shared/api/connections.ts", { "./http": http });

  const api = connectionsModule.connectionsApi();
  const list = await api.list("tok", "11111111-1111-1111-1111-111111111111", true);
  assert.equal(list.items[0].accountId, "a-1");
  const auth = await api.authorizeUrl("tok", "11111111-1111-1111-1111-111111111111", "http://localhost:3000/cb");
  assert.ok(auth.url.startsWith("https://instagram.com/oauth/authorize"));

  assert.ok(String(calls[0].input).includes("/api/v1/workspaces/11111111-1111-1111-1111-111111111111/instagram/connections?includeDisconnected=true"));
  assert.equal(calls[0].init.headers.authorization, "Bearer tok");
  await api.disconnect("tok", "11111111-1111-1111-1111-111111111111", "a-1");
  assert.equal(calls[calls.length - 1].init.method, "DELETE");
});

test("oauth state round-trips from authorize-url into connect without storage", async () => {
  const bodies = [];
  const http = loadTsModule("src/shared/api/http.ts");
  http.setTransport(async (input, init) => {
    if (init?.body) bodies.push(JSON.parse(init.body));
    return {
      ok: true,
      status: 200,
      json: async () =>
        String(input).includes("/authorize-url")
          ? { url: "https://instagram.com/oauth/authorize?state=st-1", state: "st-1" }
          : { accountId: "a-9" },
    };
  });
  const connectionsModule = loadTsModule("src/shared/api/connections.ts", { "./http": http });

  const api = connectionsModule.connectionsApi();
  const auth = await api.authorizeUrl("tok", "22222222-2222-2222-2222-222222222222", "http://localhost:3000/cb");
  assert.equal(auth.state, "st-1");
  const connected = await api.connect("tok", "22222222-2222-2222-2222-222222222222", {
    authorizationCode: "code-1",
    redirectUri: "http://localhost:3000/cb",
    state: auth.state,
  });
  assert.equal(connected.accountId, "a-9");
  assert.equal(bodies[0].state, "st-1");
  assert.equal(bodies[0].authorizationCode, "code-1");
});

test("subscription repair posts to the exact account endpoint", async () => {
  const seen = [];
  const http = loadTsModule("src/shared/api/http.ts");
  http.setTransport(async (input, init) => {
    seen.push({ input: String(input), init });
    return { ok: true, status: 200, json: async () => ({ subscriptionHealth: "Healthy" }) };
  });
  const connectionsModule = loadTsModule("src/shared/api/connections.ts", { "./http": http });

  const api = connectionsModule.connectionsApi();
  const result = await api.repairSubscription("tok", "33333333-3333-3333-3333-333333333333", "a-7");
  assert.equal(result.subscriptionHealth, "Healthy");
  assert.ok(seen[0].input.endsWith("/instagram/connections/a-7/repair-subscription"));
  assert.equal(seen[0].init.method, "POST");
  assert.equal(seen[0].init.headers.authorization, "Bearer tok");
});
