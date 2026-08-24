// M08-001 — deterministic checks for the Penpot-derived design foundation.
// Mirrors the offline style of penpot-sync.test.mjs: file-content assertions, no build.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { test } from "node:test";
import assert from "node:assert/strict";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const read = (p) => readFileSync(path.join(root, p), "utf8");

test("design foundation: icon set is extracted verbatim from the canonical sidebar board", () => {
  const icon = read("src/shared/design/SidebarIcon.tsx");
  // All 14 live icons from board f5bf3c2c-…-8752c6768b24 must be present.
  const expected = [
    "SourceSVG", "Dashboard", "Features", "SmartSMS", "Instagram", "Pricing",
    "Accounts", "Help", "SmartAnswering", "Cards", "Followup",
    "CommentAutomation", "FormMaker", "IceBreakers",
  ];
  for (const name of expected) {
    assert.ok(icon.includes(`${name}: { size:`), `missing icon ${name}`);
  }
  assert.match(icon, /c269caa0-e456-818c-8008-85a77340be64/, "must cite canonical file id");
  assert.ok(!/\bH\b|\bV\b/.test(icon.match(/d: '([^']*)'/)?.[1] ?? ""), "paths must stay M/L/C/Z only");
});

test("design foundation: extended tokens carry their Penpot origin annotations", () => {
  const css = read("src/app/globals.css");
  for (const token of [
    "--color-accent-soft", "--color-accent-softer", "--color-heading-plum",
    "--color-border-input", "--color-text-placeholder", "--color-status-success",
    "--color-status-warning", "--color-status-danger", "--color-status-error",
    "--color-accent-violet", "--shadow-menu", "--radius-control", "--radius-chip",
  ]) {
    assert.ok(css.includes(token), `missing token ${token}`);
  }
  // Every extended token line keeps a Penpot provenance comment (contract: no invented values).
  const lines = css.split("\n").filter((l) => l.includes("--color-status") || l.includes("--shadow-menu"));
  for (const line of lines) {
    assert.ok(/Penpot/.test(line), `token without Penpot origin comment: ${line.trim()}`);
  }
});

test("design foundation: primitives expose the documented component surface", () => {
  const ui = read("src/shared/design/ui/index.tsx");
  for (const symbol of ["Button", "Card", "TextField", "TextAreaField", "SelectField", "StatusPill", "PageHeader"]) {
    assert.ok(new RegExp(`export (function|interface|type) ${symbol}\\b`).test(ui), `missing ${symbol}`);
  }
});

test("design foundation: active-state treatment stays an explicit open question", () => {
  const css = read("src/shared/design/Sidebar.module.css");
  assert.match(css, /OPEN QUESTION/);
});
