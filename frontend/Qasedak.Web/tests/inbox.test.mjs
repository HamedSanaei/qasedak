// M08-004 — inbox presentation + conversations API contract tests (offline).
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

const presentation = loadTsModule("src/features/inbox/presentation.ts");

test("conversation statuses and every stable reply failure code have Persian copy", () => {
  assert.equal(presentation.statusLabel("open"), "باز");
  assert.equal(presentation.statusLabel("archived"), "بایگانی");
  for (const code of [
    "conversation.notFound",
    "reply.emptyText",
    "reply.tooLong",
    "reply.archivedThread",
    "reply.messagingWindowClosed",
    "channel.unsupported",
    "instagram.noConnectedAccount",
    "instagram.tokenMissing",
  ]) {
    assert.notEqual(presentation.describeReplyFailure(code), presentation.describeReplyFailure(null), code);
  }
});

test("reply validation mirrors backend empty/length rules", () => {
  assert.equal(presentation.validateReplyText("   "), "reply.emptyText");
  assert.equal(presentation.validateReplyText("سلام"), null);
  assert.equal(
    presentation.validateReplyText("x".repeat(presentation.REPLY_MAX_LENGTH + 1)),
    "reply.tooLong",
  );
});

test("fa relative time formatting is deterministic and Persian-digited", () => {
  const now = Date.parse("2026-08-24T12:00:00Z");
  assert.equal(presentation.formatRelativeFa("2026-08-24T11:30:00Z", now), "۳۰ دقیقه پیش");
  assert.equal(presentation.formatRelativeFa("2026-08-24T10:00:00Z", now), "۲ ساعت پیش");
  assert.equal(presentation.formatRelativeFa("2026-08-20T12:00:00Z", now), "۴ روز پیش");
  assert.equal(presentation.formatRelativeFa("2026-08-24T12:00:00Z", now), "همین حالا");
});

test("conversations client targets list/detail/reply surface with bearer auth", async () => {
  const calls = [];
  const http = loadTsModule("src/shared/api/http.ts");
  http.setTransport(async (input, init) => {
    calls.push({ input, init });
    return {
      ok: true,
      status: String(input).includes("/replies") ? 201 : 200,
      json: async () => {
        const url = String(input);
        if (url.includes("/replies")) return { messageId: "m-9" };
        if (/\/conversations\/[0-9a-f-]{36}$/.test(url)) return { id: "c-1", messages: [] };
        return { page: 1, pageSize: 20, totalCount: 1, items: [{ id: "c-1" }] };
      },
    };
  });
  const mod = loadTsModule("src/shared/api/conversations.ts", { "./http": http });

  const api = mod.conversationsApi();
  const list = await api.list("tok", "22222222-2222-2222-2222-222222222222", { status: "open", page: 2 });
  assert.equal(list.items[0].id, "c-1");
  assert.ok(String(calls[0].input).endsWith("/conversations?status=open&page=2"));
  assert.equal(calls[0].init.headers.authorization, "Bearer tok");

  await api.get("tok", "22222222-2222-2222-2222-222222222222", "33333333-3333-3333-3333-333333333333");
  assert.ok(String(calls[1].input).endsWith("/conversations/33333333-3333-3333-3333-333333333333"));

  const reply = await api.reply("tok", "22222222-2222-2222-2222-222222222222", "33333333-3333-3333-3333-333333333333", "سلام");
  assert.equal(reply.messageId, "m-9");
  assert.deepEqual(JSON.parse(calls[2].init.body), { text: "سلام" });
});
