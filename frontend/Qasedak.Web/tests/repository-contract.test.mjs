import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const packageJson = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));

test("frontend is Next.js and not a Vite application", () => {
  assert.ok(packageJson.dependencies.next);
  assert.ok(packageJson.dependencies.react);
  assert.equal(packageJson.devDependencies?.vite, undefined);
  assert.equal(packageJson.scripts?.build, "next build");
});

test("quality scripts remain part of the frontend contract", () => {
  for (const script of ["lint", "typecheck", "test", "verify"]) {
    assert.equal(typeof packageJson.scripts?.[script], "string", `missing ${script} script`);
  }
});
