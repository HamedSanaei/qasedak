// M13-006 — Instagram media catalog frontend contract tests (offline, deterministic).
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

const media = loadTsModule("src/features/instagram/media.ts");

test("picker kind normalization maps known kinds and fails closed to Unknown", () => {
  assert.equal(media.normalizeKind("Image"), "Image");
  assert.equal(media.normalizeKind("Video"), "Video");
  assert.equal(media.normalizeKind("Reel"), "Reel");
  assert.equal(media.normalizeKind("Carousel"), "Carousel");
  assert.equal(media.normalizeKind("SomethingNew"), "Unknown");
  assert.equal(media.normalizeKind(null), "Unknown");
  assert.equal(media.normalizeKind(undefined), "Unknown");
});

test("picker page normalization preserves nullable fields and never invents values", () => {
  const page = media.normalizePage({
    items: [
      {
        mediaId: "m-1",
        caption: null,
        kind: "Image",
        mediaProductType: null,
        timestampUtc: "2026-08-01T10:30:00+00:00",
        permalink: null,
        mediaUrl: null,
        thumbnailUrl: null,
        likeCount: null,
        commentCount: 3,
        children: null,
      },
      {
        mediaId: "m-2",
        kind: "Carousel",
        children: [
          { mediaId: "c-1", kind: "Image", mediaUrl: "https://media.example/c1.jpg", thumbnailUrl: null, permalink: null },
          { mediaId: "c-2", kind: "FutureChildType" },
        ],
      },
    ],
    nextCursor: "opaque-cursor",
    hasMore: true,
  });

  assert.equal(page.items.length, 2);
  assert.equal(page.items[0].caption, null);
  assert.equal(page.items[0].mediaUrl, null);
  assert.equal(page.items[0].likeCount, null);
  assert.equal(page.items[0].commentCount, 3);
  assert.equal(page.items[0].hasMediaPreview, false);
  assert.equal(page.items[1].kind, "Carousel");
  assert.equal(page.items[1].children.length, 2);
  assert.equal(page.items[1].children[0].mediaUrl, "https://media.example/c1.jpg");
  // Unknown child kind fails closed, never crashes.
  assert.equal(page.items[1].children[1].kind, "Unknown");
  assert.equal(page.nextCursor, "opaque-cursor");
  assert.equal(page.hasMore, true);
});

test("picker page normalization handles missing payloads and empty pages", () => {
  const empty = media.normalizePage({});
  assert.deepEqual(empty.items, []);
  assert.equal(empty.nextCursor, null);
  assert.equal(empty.hasMore, false);

  const noItems = media.normalizePage(null);
  assert.deepEqual(noItems.items, []);
});

test("media api client targets the exact-account workspace-scoped surface with bearer auth", async () => {
  const calls = [];
  const http = loadTsModule("src/shared/api/http.ts");
  assert.equal(http.apiBaseUrl, "");
  http.setTransport(async (input, init) => {
    calls.push({ input, init });
    return {
      ok: true,
      status: 200,
      json: async () => ({
        items: [{ mediaId: "m-1", kind: "Image" }],
        nextCursor: "cursor-2",
        hasMore: true,
      }),
    };
  });
  const mediaModule = loadTsModule("src/shared/api/media.ts", {
    "./http": http,
    "../../features/instagram/media": media,
  });

  const api = mediaModule.mediaApi();
  const page = await api.listMedia(
    "tok-secret",
    "11111111-1111-1111-1111-111111111111",
    "22222222-2222-2222-2222-222222222222",
    { limit: 25, cursor: "cursor-1" },
  );

  assert.equal(page.items[0].mediaId, "m-1");
  assert.equal(page.nextCursor, "cursor-2");
  assert.equal(page.hasMore, true);
  assert.ok(
    String(calls[0].input).includes(
      "/api/v1/workspaces/11111111-1111-1111-1111-111111111111/instagram/connections/22222222-2222-2222-2222-222222222222/media?limit=25&cursor=cursor-1",
    ),
    `unexpected url ${String(calls[0].input)}`,
  );
  assert.equal(calls[0].init.headers.authorization, "Bearer tok-secret");
  // The token is never placed in the URL.
  assert.ok(!String(calls[0].input).includes("tok-secret"));
});

test("media api client omits optional query parameters when absent", async () => {
  const calls = [];
  const http = loadTsModule("src/shared/api/http.ts");
  http.setTransport(async (input, init) => {
    calls.push({ input, init });
    return { ok: true, status: 200, json: async () => ({ items: [], nextCursor: null, hasMore: false }) };
  });
  const mediaModule = loadTsModule("src/shared/api/media.ts", {
    "./http": http,
    "../../features/instagram/media": media,
  });

  await mediaModule.mediaApi().listMedia("tok", "w-1", "a-1");

  assert.equal(String(calls[0].input), "/api/v1/workspaces/w-1/instagram/connections/a-1/media");
});

test("every media failure code has Persian copy", () => {
  const health = loadTsModule("src/features/instagram/health.ts");
  for (const code of [
    "media.invalidCursor",
    "media.invalidLimit",
    "media.permissionDenied",
    "media.rateLimited",
    "media.malformed",
    "media.unavailable",
  ]) {
    const described = health.describeConnectionFailure(code);
    assert.notEqual(described, "خطایی رخ داد؛ دوباره تلاش کنید.", `untranslated media code ${code}`);
  }
});