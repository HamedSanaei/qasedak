// M08-005 — automation builder presentation + API contract tests (offline).
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
    (requestPath) => requireMap[requestPath] ?? (() => { throw new Error(`unexpected require: ${requestPath}`); })(),
  );
  return module_.exports;
}

const presentation = loadTsModule("src/features/automations/presentation.ts");

test("automation name validation mirrors backend rules (required, ≤200)", () => {
  assert.equal(presentation.validateAutomationName(""), "automation.nameRequired");
  assert.equal(presentation.validateAutomationName("   "), "automation.nameRequired");
  assert.equal(presentation.validateAutomationName("x".repeat(201)), "automation.nameTooLong");
  assert.equal(presentation.validateAutomationName("ارسال قیمت"), null);
});

test("definition validation enforces keyword/action-text rules per match mode", () => {
  assert.equal(
    presentation.validateDefinition("contains", ["قیمت"], "سلام 👋"),
    null,
  );
  assert.equal(presentation.validateDefinition("contains", [], "سلام"), "automation.keywordRequired");
  assert.equal(presentation.validateDefinition("anyReply", [], ""), "automation.actionTextRequired");
  assert.equal(
    presentation.validateDefinition("anyReply", [], "x".repeat(presentation.AUTOMATION_MAX_MESSAGE_LENGTH + 1)),
    "automation.actionTextTooLong",
  );
  // anyReply intentionally allows an empty keyword list (backend: empty filters match all).
  assert.equal(presentation.validateDefinition("anyReply", [], "سلام"), null);
});

test("entitlement denials are recognized and every stable code has Persian copy", () => {
  assert.ok(presentation.isEntitlementDenial("billing.subscriptionRequired"));
  assert.ok(presentation.isEntitlementDenial("billing.limitExceeded"));
  assert.ok(!presentation.isEntitlementDenial("automation.notFound"));
  for (const code of [
    "automation.notFound",
    "automation.nameRequired",
    "automation.nameTooLong",
    "automation.keywordRequired",
    "automation.tooManyKeywordFilters",
    "automation.conditionInvalid",
    "automation.actionRequired",
    "automation.actionTextRequired",
    "automation.actionTextTooLong",
    "automation.definitionRequired",
    "automation.triggerKindInvalid",
    "automation.alreadyActive",
    "automation.notActive",
    "automation.alreadyDisabled",
    "automation.disabled",
    "automation.versionFrozen",
    "billing.subscriptionRequired",
    "billing.limitExceeded",
  ]) {
    assert.notEqual(
      presentation.describeAutomationFailure(code),
      presentation.describeAutomationFailure(null),
      `untranslated code ${code}`,
    );
  }
});

test("automations client targets list/create/lifecycle surface with bearer auth", async () => {
  const calls = [];
  const http = loadTsModule("src/shared/api/http.ts");
  http.setTransport(async (input, init) => {
    calls.push({ input, init });
    return {
      ok: true,
      status: String(input).endsWith("/automations") && (!init.method || init.method === "GET") ? 200 : 201,
      json: async () =>
        init.method === "DELETE" || String(input).includes("/activate") || String(input).includes("/deactivate")
          ? { id: "a-1", status: "Active" }
          : { items: [{ id: "a-1", status: "Draft" }] },
    };
  });
  const mod = loadTsModule("src/shared/api/automations.ts", { "./http": http });

  const api = mod.automationsApi();
  const list = await api.list("tok", "44444444-4444-4444-4444-444444444444");
  assert.equal(list.items[0].id, "a-1");
  assert.ok(String(calls[0].input).endsWith("/api/v1/workspaces/44444444-4444-4444-4444-444444444444/automations"));

  await api.activate("tok", "44444444-4444-4444-4444-444444444444", "55555555-5555-5555-5555-555555555555");
  const activateCall = calls[calls.length - 1];
  assert.ok(String(activateCall.input).endsWith("/55555555-5555-5555-5555-555555555555/activate"));
  assert.equal(activateCall.init.method, "POST");
  assert.equal(activateCall.init.headers.authorization, "Bearer tok");

  await api.remove("tok", "44444444-4444-4444-4444-444444444444", "55555555-5555-5555-5555-555555555555");
  const deleteCall = calls[calls.length - 1];
  assert.equal(deleteCall.init.method, "DELETE");

  const definition = {
    triggerKind: "CommentCreated",
    keywordFilters: ["قیمت"],
    conditions: [{ field: "CommentText", operator: "Contains", expectedValue: "قیمت" }],
    actions: [{ kind: "SendDirectMessage", messageText: "سلام 👋" }],
  };
  await api.create("tok", "44444444-4444-4444-4444-444444444444", { name: "ارسال قیمت", definition });
  const createCall = calls[calls.length - 1];
  assert.equal(createCall.init.method, "POST");
  assert.deepEqual(JSON.parse(createCall.init.body).definition.keywordFilters, ["قیمت"]);
});
