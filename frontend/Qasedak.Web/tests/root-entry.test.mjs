// M12-001 — root application entry route regression tests.
// Asserts that `/` forwards to /dashboard when a session exists and to /login
// otherwise, that expired sessions are handled by readSession(), and that the starter
// placeholder ("اسکلت مهندسی آماده است.") can no longer appear on the root page.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import ts from "typescript";
import { test } from "node:test";
import assert from "node:assert/strict";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function loadTsModule(relPath) {
  const source = readFileSync(path.join(root, relPath), "utf8");
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  const module_ = { exports: {} };
  new Function("module", "exports", js)(module_, module_.exports, () => ({}));
  return module_.exports;
}

test("root redirects authenticated sessions to /dashboard", () => {
  const { resolveRootTarget } = loadTsModule("src/features/auth/rootRedirect.ts");
  assert.equal(resolveRootTarget({ accessToken: "jwt" }), "/dashboard");
});

test("root redirects missing sessions to /login", () => {
  const { resolveRootTarget } = loadTsModule("src/features/auth/rootRedirect.ts");
  assert.equal(resolveRootTarget(null), "/login");
});

test("readSession ignores expired sessions", () => {
  // identity.ts reads localStorage; simulate an in-memory storage to drive readSession.
  const store = new Map();
  globalThis.window = {
    localStorage: {
      getItem: (k) => (store.has(k) ? store.get(k) : null),
      setItem: (k, v) => store.set(k, String(v)),
      removeItem: (k) => store.delete(k),
    },
  };
  try {
    const { saveSession, clearSession, readSession } = loadTsModule("src/shared/api/identity.ts");
    const past = "2000-01-01T00:00:00Z";
    saveSession("jwt-expired", past);
    assert.equal(readSession(), null, "expired token must not yield a session");
    assert.equal(store.get("qasedak.accessToken"), undefined, "expired session storage must be cleared");
    // A live (future) session must be returned and NOT cleared.
    const future = "2999-01-01T00:00:00Z";
    saveSession("jwt-valid", future);
    assert.deepEqual(readSession(), { accessToken: "jwt-valid" });
    assert.equal(store.get("qasedak.accessToken"), "jwt-valid");
    clearSession();
    assert.equal(readSession(), null);
  } finally {
    delete globalThis.window;
  }
});

test("root page is a client entry route that uses readSession and resolveRootTarget", () => {
  const pageSource = readFileSync(path.join(root, "src/app/page.tsx"), "utf8");
  assert.match(pageSource, /"use client"/, "root page must be a client component");
  assert.match(pageSource, /resolveRootTarget\(/);
  assert.match(pageSource, /readSession\(\)/);
  assert.match(pageSource, /router\.replace/);
});

test("starter placeholder text is no longer emitted by the root page", () => {
  const pageSource = readFileSync(path.join(root, "src/app/page.tsx"), "utf8");
  assert.ok(
    !pageSource.includes("اسکلت مهندسی آماده است."),
    "root page must not contain the engineering placeholder",
  );
});