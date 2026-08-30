import assert from "node:assert/strict";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

import { inboxState, workspaceState } from "../src/features/dashboard/dashboard-state.mjs";
import { routeIsActive } from "../src/shared/navigation/route-policy.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const sourceRoot = path.join(root, "src");

function read(relativePath) {
  return readFileSync(path.join(root, relativePath), "utf8");
}

function walk(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const absolute = path.join(directory, entry.name);
    return entry.isDirectory() ? walk(absolute) : [absolute];
  });
}

test("navigation policy distinguishes the dashboard root from nested routes", () => {
  assert.equal(routeIsActive("/dashboard", "/dashboard"), true);
  assert.equal(routeIsActive("/dashboard/inbox", "/dashboard"), false);
  assert.equal(routeIsActive("/dashboard/inbox", "/dashboard/inbox"), true);
  assert.equal(routeIsActive("/dashboard/inbox/3d9a", "/dashboard/inbox"), true);
  assert.equal(routeIsActive("/dashboard/accounts", "/dashboard/inbox"), false);
});

test("dashboard state model covers first use, populated, empty, and failures", () => {
  assert.equal(workspaceState({ selected: false, ok: false, status: 401 }), "missing");
  assert.equal(workspaceState({ selected: true, ok: true, status: 200 }), "ready");
  assert.equal(workspaceState({ selected: true, ok: false, status: 503 }), "service-error");
  assert.equal(workspaceState({ selected: true, ok: false, status: 404 }), "unavailable");
  assert.equal(inboxState({ workspaceReady: false, ok: false, totalCount: 0, status: 401 }), "needs-workspace");
  assert.equal(inboxState({ workspaceReady: true, ok: true, totalCount: 0, status: 200 }), "empty");
  assert.equal(inboxState({ workspaceReady: true, ok: true, totalCount: 4, status: 200 }), "has-items");
  assert.equal(inboxState({ workspaceReady: true, ok: false, totalCount: 0, status: 503 }), "service-error");
});

test("every visible and compatibility route has a concrete App Router page", () => {
  const pages = [
    "src/app/page.tsx",
    "src/app/login/page.tsx",
    "src/app/register/page.tsx",
    "src/app/onboarding/workspace/page.tsx",
    "src/app/dashboard/page.tsx",
    "src/app/dashboard/inbox/page.tsx",
    "src/app/dashboard/inbox/[conversationId]/page.tsx",
    "src/app/dashboard/automations/page.tsx",
    "src/app/dashboard/automations/new/page.tsx",
    "src/app/dashboard/automations/[automationId]/page.tsx",
    "src/app/dashboard/settings/instagram/page.tsx",
    "src/app/dashboard/billing/page.tsx",
    "src/app/dashboard/billing/plans/page.tsx",
    "src/app/dashboard/billing/checkout/page.tsx",
    "src/app/dashboard/billing/result/page.tsx",
    "src/app/dashboard/billing/payment-result/page.tsx",
    "src/app/dashboard/accounts/page.tsx",
    "src/app/dashboard/help/page.tsx",
    "src/app/dashboard/smart-sms/page.tsx",
    "src/app/dashboard/features/cards/page.tsx",
    "src/app/dashboard/features/cards/new/page.tsx",
    "src/app/dashboard/features/follow-up/page.tsx",
    "src/app/dashboard/features/follow-up/new/page.tsx",
    "src/app/dashboard/features/comment-automation/page.tsx",
    "src/app/dashboard/features/comment-automation/new/page.tsx",
    "src/app/dashboard/features/form-maker/page.tsx",
    "src/app/dashboard/features/form-maker/new/page.tsx",
    "src/app/dashboard/features/ice-breakers/page.tsx",
    "src/app/dashboard/features/smart-answer/page.tsx",
    "src/app/dashboard/features/smart-answer/new/page.tsx",
  ];
  for (const page of pages) assert.ok(existsSync(path.join(root, page)), `missing route page: ${page}`);
});

test("public root composes the Penpot-synced landing instead of redirecting", () => {
  const page = read("src/app/page.tsx");
  const landing = read("src/features/landing/ui/LandingPage.tsx");
  const styles = read("src/features/landing/ui/LandingPage.module.css");
  assert.match(page, /<LandingPage\s*\/>/);
  assert.doesNotMatch(page, /redirect\(/);
  assert.match(landing, /id="features"/);
  assert.match(landing, /id="pricing"/);
  assert.match(landing, /id="faq"/);
  assert.match(landing, /href="\/register"/);
  assert.match(landing, /href="\/login"/);
  assert.match(landing, /<details/);
  assert.match(landing, /سرویس دایرکتم، ۱۴ روز رایگان شد/);
  assert.match(landing, /directam-team\.webp/);
  assert.match(landing, /۱۵٬۴۱۸٬۰۰۰/);
  assert.match(styles, /@media \(max-width: 640px\)/);
});

test("browser components use same-origin API calls only", () => {
  const browserFiles = walk(sourceRoot)
    .filter((file) => /\.(?:tsx|ts)$/.test(file))
    .filter((file) => readFileSync(file, "utf8").startsWith('"use client"'));

  for (const file of browserFiles) {
    const source = readFileSync(file, "utf8");
    assert.doesNotMatch(source, /https?:\/\/(?:localhost|127\.0\.0\.1|api)(?::\d+)?/i, `browser host fallback in ${file}`);
    for (const match of source.matchAll(/fetch\(\s*["'`]([^"'`]+)["'`]/g)) {
      assert.match(match[1], /^\/api\//, `non same-origin fetch in ${file}: ${match[1]}`);
    }
  }
});

test("session cookies remain server-owned and production-secure", () => {
  const session = read("src/shared/server/session.ts");
  assert.match(session, /httpOnly:\s*true/);
  assert.match(session, /sameSite:\s*"lax"/);
  assert.match(session, /secure:\s*process\.env\.NODE_ENV\s*===\s*"production"/);
  assert.match(session, /path:\s*"\/"/);
  assert.doesNotMatch(session, /localStorage|sessionStorage/);
});

test("responsive dashboard shell exposes keyboard and modal drawer contracts", () => {
  const shell = read("src/shared/design/DashboardShell.tsx");
  assert.match(shell, /event\.key === "Escape"/);
  assert.match(shell, /aria-modal="true"/);
  assert.match(shell, /onNavigate=\{\(\) => setMenuOpen\(false\)\}/);
  assert.match(shell, /sidebarCollapsed/);
  assert.match(shell, /onToggle=/);
});

test("customer-facing component copy has no internal milestone or design-source labels", () => {
  const customerFiles = walk(sourceRoot)
    .filter((file) => file.endsWith(".tsx"))
    .filter((file) => /[\\/](?:app|features|shared[\\/]design)[\\/]/.test(file));
  const joined = customerFiles.map((file) => readFileSync(file, "utf8")).join("\n");
  assert.doesNotMatch(joined, /M08-\d{3}/);
  assert.doesNotMatch(joined, /IB Side Note|Instance \/ Global User Menu/);
});

test("loading, empty, and error UI remain present for primary data screens", () => {
  assert.ok(existsSync(path.join(root, "src/app/dashboard/loading.tsx")));
  assert.ok(existsSync(path.join(root, "src/app/dashboard/inbox/loading.tsx")));
  assert.ok(existsSync(path.join(root, "src/app/dashboard/error.tsx")));
  assert.match(read("src/features/dashboard/DashboardOverview.tsx"), /inboxState/);
  assert.match(read("src/features/dashboard/DashboardOverview.tsx"), /هنوز گفتگویی دریافت نشده است/);
  assert.match(read("src/features/dashboard/DashboardOverview.tsx"), /StatusAlert/);
  // The functional inbox predates the newer shared component names but keeps
  // the same observable contracts: a dedicated empty branch and an alert role.
  assert.match(read("src/app/dashboard/inbox/page.tsx"), /items\.length === 0|EmptyState/);
  assert.match(read("src/app/dashboard/inbox/page.tsx"), /role="alert"|StatusAlert/);
});
