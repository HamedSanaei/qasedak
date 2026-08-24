// M08-002 — identity API client contract tests with an injected transport
// (mirrors the backend IdentityEndpoints responses; no network access).
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import ts from "typescript";
import { test } from "node:test";
import assert from "node:assert/strict";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function loadHttpModule() {
  const source = readFileSync(path.join(root, "src/shared/api/http.ts"), "utf8");
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const module_ = { exports: {} };
  new Function("module", "exports", js)(module_, module_.exports, () => ({}));
  return module_.exports;
}

function jsonResponse(status, body) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  };
}

test("api client posts login credentials and returns the bearer session", async () => {
  const mod = loadHttpModule();
  const calls = [];
  mod.setTransport(async (input, init) => {
    calls.push({ input, init });
    return jsonResponse(200, { accessToken: "jwt-1", expiresAtUtc: "2026-12-01T00:00:00Z" });
  });
  const session = await mod.api().login({ email: "u@e.com", password: "Str0ng#Passphrase" });
  assert.equal(session.accessToken, "jwt-1");
  assert.equal(calls.length, 1);
  assert.ok(String(calls[0].input).endsWith("/api/v1/identity/login"));
  assert.deepEqual(JSON.parse(calls[0].init.body), { email: "u@e.com", password: "Str0ng#Passphrase" });
});

test("api client surfaces stable failure codes as ApiError", async () => {
  const mod = loadHttpModule();
  mod.setTransport(async () => jsonResponse(401, { code: "auth.invalidCredentials" }));
  await assert.rejects(
    () => mod.api().login({ email: "u@e.com", password: "wrong" }),
    (error) =>
      error instanceof mod.ApiError &&
      error.code === "auth.invalidCredentials" &&
      error.status === 401,
  );
});

test("workspace endpoints attach the bearer token header", async () => {
  const mod = loadHttpModule();
  const calls = [];
  mod.setTransport(async (input, init) => {
    calls.push({ input, headers: init.headers });
    if (calls.length === 1) return jsonResponse(201, { workspaceId: "w-1", name: "فروشگاه من" });
    return jsonResponse(200, { workspaceName: "فروشگاه من", members: [{ userId: "u-1", role: "Owner" }] });
  });
  const created = await mod.api().createWorkspace("tok", { name: "فروشگاه من" });
  const members = await mod.api().listMembers("tok", created.workspaceId);
  assert.equal(created.workspaceId, "w-1");
  assert.equal(members.workspaceName, "فروشگاه من");
  for (const c of calls) {
    assert.equal(c.headers.authorization, "Bearer tok");
  }
});
