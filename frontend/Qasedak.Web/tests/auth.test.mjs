// M08-002 — auth/workspace frontend behavior tests (offline, deterministic).
// The pure validation module is compiled on the fly with the TypeScript compiler
// already available as a devDependency.
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
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const module_ = { exports: {} };
  new Function("module", "exports", js)(module_, module_.exports, () => ({}));
  return module_.exports;
}

const v = loadTsModule("src/features/auth/validation.ts");

test("auth validation mirrors backend PasswordPolicy (10..128, non-alphanumeric required)", () => {
  assert.equal(v.validatePassword("short1!"), v.FAILURE_CODES.weakPassword);
  assert.equal(v.validatePassword("a".repeat(9) + "!"), null);
  assert.equal(v.validatePassword("a".repeat(129) + "!"), v.FAILURE_CODES.weakPassword);
  assert.equal(v.validatePassword("OnlyLetters123456"), v.FAILURE_CODES.weakPassword);
  assert.equal(v.validatePassword("Str0ng#Passphrase"), null);
});

test("auth validation mirrors backend email/displayName/workspace rules", () => {
  assert.equal(v.validateEmail("user@example.com"), null);
  assert.equal(v.validateEmail("nope"), v.FAILURE_CODES.invalidEmail);
  assert.equal(v.validateDisplayName(""), v.FAILURE_CODES.invalidDisplayName);
  assert.equal(v.validateDisplayName("  "), v.FAILURE_CODES.invalidDisplayName);
  assert.equal(v.validateWorkspaceName("ab"), v.FAILURE_CODES.invalidName);
  assert.equal(v.validateWorkspaceName("فروشگاه من"), null);
});

test("every stable backend failure code has a Persian description", () => {
  for (const code of Object.values(v.FAILURE_CODES)) {
    const described = v.describeFailure(code);
    assert.notEqual(described, "خطایی رخ داد؛ دوباره تلاش کنید.", `untranslated failure code ${code}`);
  }
});

test("active auth pages use server session handlers and keep visible failure states", () => {
  const login = readFileSync(path.join(root, "src/app/login/page.tsx"), "utf8");
  const register = readFileSync(path.join(root, "src/app/register/page.tsx"), "utf8");
  const loginRoute = readFileSync(path.join(root, "src/app/web-api/auth/login/route.ts"), "utf8");
  const registerRoute = readFileSync(path.join(root, "src/app/web-api/auth/register/route.ts"), "utf8");

  assert.match(login, /fetch\("\/web-api\/auth\/login"/);
  assert.match(login, /credentials:\s*"same-origin"/);
  assert.match(login, /setFormError\(/);
  assert.doesNotMatch(login, /api\(\)\.login/);
  assert.match(register, /fetch\("\/web-api\/auth\/register"/);
  assert.match(register, /fetch\("\/web-api\/workspace"/);
  assert.match(register, /setFormError\(/);
  assert.doesNotMatch(register, /api\(\)\.(register|login|createWorkspace)/);
  assert.match(loginRoute, /accessToken:\s*body\.accessToken/);
  assert.match(loginRoute, /clearSessionCookies\(response\)/);
  assert.match(registerRoute, /accessToken:\s*body\.accessToken/);
  assert.doesNotMatch(login, /fetch\("\/api\//, "web-owned auth must stay outside the production API proxy prefix");
  assert.doesNotMatch(register, /fetch\("\/api\//, "web-owned registration must stay outside the production API proxy prefix");
});
