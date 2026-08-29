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
  new Function("module", "exports", "require", js)(module_, module_.exports, () => {
    throw new Error("unexpected require");
  });
  return module_.exports;
}

test("browser API requests use same-origin relative URLs", async () => {
  const previousWindow = globalThis.window;
  globalThis.window = {};
  try {
    const http = loadHttpModule();
    const calls = [];
    http.setTransport(async (input, init) => {
      calls.push({ input, init });
      return { ok: true, status: 200, json: async () => ({ accessToken: "token", expiresAtUtc: "2099-01-01T00:00:00Z" }) };
    });

    await http.api().login({ email: "user@example.com", password: "StrongPassword123!" });

    assert.equal(http.apiBaseUrl, "");
    assert.equal(calls[0].input, "/api/v1/identity/login");
    assert.equal(new URL(calls[0].input, "https://qasedak.example").origin, "https://qasedak.example");
    assert.notEqual(calls[0].input, "http://localhost:8080/api/v1/identity/login");
  } finally {
    globalThis.window = previousWindow;
  }
});

test("register remains on the same-origin API path", async () => {
  const previousWindow = globalThis.window;
  globalThis.window = {};
  try {
    const http = loadHttpModule();
    let input;
    http.setTransport(async (requestInput) => {
      input = requestInput;
      return { ok: true, status: 200, json: async () => ({ userId: "user-1" }) };
    });

    await http.api().register({
      email: "user@example.com",
      displayName: "User",
      password: "StrongPassword123!",
    });

    assert.equal(input, "/api/v1/identity/register");
  } finally {
    globalThis.window = previousWindow;
  }
});

test("production browser source has no localhost API fallback or Docker hostname", () => {
  const source = readFileSync(path.join(root, "src/shared/api/http.ts"), "utf8");
  assert.doesNotMatch(source, /http:\/\/localhost:8080/);
  assert.doesNotMatch(source, /http:\/\/api:8080/);
  assert.match(source, /: \"\";/);
});
