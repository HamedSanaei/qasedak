// M12-002 — contacts CRM client + panel presentation contract tests (offline).
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

const WS = "22222222-2222-2222-2222-222222222222";

test("contacts presentation validates tag/note bounds and exposes stable Persian copy", () => {
  const p = loadTsModule("src/features/contacts/presentation.ts");
  assert.equal(p.validateTagInput("   "), "contact.tagRequired");
  assert.equal(p.validateTagInput("VIP"), null);
  assert.equal(p.validateTagInput("x".repeat(p.TAG_MAX_LENGTH + 1)), "contact.tagTooLong");
  assert.equal(p.validateNoteInput("   "), "contact.noteRequired");
  assert.equal(p.validateNoteInput("یادداشت"), null);
  assert.equal(p.validateNoteInput("n".repeat(p.NOTE_MAX_LENGTH + 1)), "contact.noteTooLong");
  for (const code of [
    "contact.notFound",
    "contact.tagRequired",
    "contact.tagTooLong",
    "contact.tooManyTags",
    "contact.noteRequired",
    "contact.noteTooLong",
    "contact.notActive",
  ]) {
    assert.notEqual(p.describeContactFailure(code), p.describeContactFailure(null), code);
  }
  assert.equal(typeof p.CONTACT_PANEL_EMPTY_TITLE, "string");
  assert.equal(typeof p.CONTACT_PANEL_EMPTY_BODY, "string");
  // The old «تا تکمیل M07» warning is gone from the copy.
  assert.ok(!p.CONTACT_PANEL_EMPTY_BODY.includes("M07"));
});

test("contacts client resolves by identity, treats 404 as null, and mutates tags/notes", async () => {
  const calls = [];
  const http = loadTsModule("src/shared/api/http.ts");
  http.setTransport(async (input, init) => {
    const url = String(input);
    calls.push({ url, method: init?.method ?? "GET" });
    if (url.endsWith("/contacts/by-identity?channel=instagram&identity=ep-1")) {
      return {
        ok: true,
        status: 200,
        json: async () => ({
          id: "33333333-3333-3333-3333-333333333333",
          displayName: "الیزه نور",
          status: "active",
          createdAtUtc: "2026-08-23T00:00:00Z",
          lastSeenAtUtc: "2026-08-23T00:00:00Z",
          interactionCount: 3,
          mergedIntoId: null,
          identities: [{ channel: "instagram", providerIdentity: "ep-1" }],
          tags: [],
          notes: [],
        }),
      };
    }
    if (url.includes("identity=nobody")) {
      return { ok: false, status: 404, json: async () => ({ code: "contact.notFound" }) };
    }
    if (url.endsWith(`/contacts/33333333-3333-3333-3333-333333333333/notes`)) {
      return { ok: true, status: 201, json: async () => ({ noteId: "n-1" }) };
    }
    return { ok: true, status: 204, json: async () => ({}), text: async () => "" };
  });

  const mod = loadTsModule("src/shared/api/contacts.ts", { "./http": http });
  const api = mod.contactsApi();

  // Same-origin by-identity URL with Bearer auth.
  const resolved = await api.getByIdentity("tok", WS, "instagram", "ep-1");
  assert.equal(resolved.displayName, "الیزه نور");
  assert.equal(calls[0].method, "GET");
  assert.ok(calls[0].url.endsWith("/contacts/by-identity?channel=instagram&identity=ep-1"));

  // Unknown identity resolves to null (404 swallowed by the client).
  const missing = await api.getByIdentity("tok", WS, "instagram", "nobody");
  assert.equal(missing, null);

  // Mutations hit the workspace-scoped paths with the right verbs.
  await api.addTag("tok", WS, "33333333-3333-3333-3333-333333333333", "VIP");
  assert.equal(calls[2].method, "POST");
  assert.ok(calls[2].url.endsWith(`/contacts/33333333-3333-3333-3333-333333333333/tags`));
  await api.removeTag("tok", WS, "33333333-3333-3333-3333-333333333333", "VIP");
  assert.equal(calls[3].method, "DELETE");
  assert.ok(calls[3].url.endsWith(`/contacts/33333333-3333-3333-3333-333333333333/tags/VIP`));
  const note = await api.addNote("tok", WS, "33333333-3333-3333-3333-333333333333", "ترجیح میدهد ایمیل بزند.");
  assert.equal(note.noteId, "n-1");
  assert.equal(calls[4].method, "POST");
  assert.ok(calls[4].url.endsWith(`/contacts/33333333-3333-3333-3333-333333333333/notes`));
});