// Deterministic validation of the Penpot ↔ Next.js sync contract.
// Runs offline (no Penpot access) in both CI lanes:
//   - `npm test` / `npm run verify` (frontend gate)
//   - `python scripts/verify.py --full` via scripts/validate_penpot_sync.py
import { readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { test } from "node:test";
import assert from "node:assert/strict";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const manifestPath = path.join(root, "design", "penpot-sync.json");
const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));

const APPROVAL_STATUSES = new Set(["draft", "provisional", "approved", "superseded"]);
const SYNC_STATUSES = new Set(["synced", "stale", "pending"]);

function resolveToken(name) {
  const [group, ...rest] = name.split(".");
  return manifest.tokens?.[group]?.[rest.join(".")];
}

function requireMappingFields(m) {
  const required = [
    "id", "designName", "penpotPageId", "penpotPageName", "penpotBoardId",
    "penpotComponentId", "approval", "nextRoutes", "componentPath",
    "compositionPaths", "responsiveTargets", "tokenDependencies",
    "assetDependencies", "lastSyncedAtUtc", "penpotRevision", "syncStatus",
  ];
  for (const field of required) {
    assert.ok(Object.hasOwn(m, field), `${m.id ?? "?"}: missing field ${field}`);
  }
}

test("penpot sync manifest: schema and source block", () => {
  assert.equal(manifest.$schema, "penpot-sync-v1");
  assert.ok(manifest.source, "missing source block");
  assert.match(manifest.source.tool, /penpot/i);
  // fileId must be a string or explicitly null — never a fabricated placeholder.
  assert.ok(manifest.source.fileId === null || typeof manifest.source.fileId === "string");
  assert.ok(Array.isArray(manifest.mappings) && manifest.mappings.length > 0, "no mappings");
});

test("penpot sync manifest: mapping fields are complete and enums valid", () => {
  for (const m of manifest.mappings) {
    requireMappingFields(m);
    assert.ok(APPROVAL_STATUSES.has(m.approval.status), `${m.id}: bad approval.status`);
    if (m.approval.status === "approved") {
      assert.ok(typeof m.approval.basis === "string" && m.approval.basis.length > 0);
    }
    assert.ok(SYNC_STATUSES.has(m.syncStatus), `${m.id}: bad syncStatus`);
    assert.ok(!Number.isNaN(Date.parse(m.lastSyncedAtUtc)), `${m.id}: lastSyncedAtUtc not ISO`);
    assert.match(m.lastSyncedAtUtc, /Z$/, `${m.id}: timestamp must be UTC`);
    // penpotRevision: null or a non-empty string — never invented numbers.
    assert.ok(m.penpotRevision === null || (typeof m.penpotRevision === "string" && m.penpotRevision.length > 0),
      `${m.id}: penpotRevision must be null or a string`);
    assert.ok(Array.isArray(m.nextRoutes) && m.nextRoutes.length > 0, `${m.id}: no routes`);
  }
});

test("penpot sync manifest: identifiers and routes are unique", () => {
  const ids = manifest.mappings.map((m) => m.id);
  const boards = manifest.mappings.map((m) => m.penpotBoardId);
  const routes = manifest.mappings.flatMap((m) => m.nextRoutes);
  assert.equal(new Set(ids).size, ids.length, "duplicate mapping id");
  assert.equal(new Set(boards).size, boards.length, "duplicate penpotBoardId");
  const components = manifest.mappings.filter((m) => m.penpotComponentId !== null).map((m) => m.penpotComponentId);
  assert.equal(new Set(components).size, components.length, "duplicate penpotComponentId");
  assert.equal(new Set(routes).size, routes.length, `duplicate route mapping: ${routes.join(", ")}`);
});

test("penpot sync manifest: referenced paths exist in the app", () => {
  for (const m of manifest.mappings) {
    assert.ok(existsSync(path.join(root, m.componentPath)), `${m.id}: componentPath missing`);
    for (const p of m.compositionPaths) {
      assert.ok(existsSync(path.join(root, p)), `${m.id}: compositionPath missing (${p})`);
    }
  }
});

test("penpot sync manifest: token dependencies resolve", () => {
  for (const m of manifest.mappings) {
    for (const token of m.tokenDependencies) {
      assert.notEqual(resolveToken(token), undefined, `${m.id}: unresolved token ${token}`);
    }
  }
});

test("penpot sync manifest: approved screens are fully mapped", () => {
  for (const m of manifest.mappings) {
    if (m.approval.status === "approved") {
      assert.ok(existsSync(path.join(root, m.componentPath)), `${m.id}: approved without component`);
      assert.ok(m.compositionPaths.length > 0, `${m.id}: approved without composition`);
    }
  }
});
