import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import ts from "typescript";
import { test } from "node:test";
import assert from "node:assert/strict";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const read = (relPath) => readFileSync(path.join(root, relPath), "utf8");

function loadTsModule(relPath) {
  const source = read(relPath);
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const module_ = { exports: {} };
  new Function("module", "exports", "require", "TextEncoder", "URL", js)(
    module_, module_.exports,
    (requestPath) => { throw new Error(`unexpected require: ${requestPath}`); },
    TextEncoder, URL,
  );
  return module_.exports;
}

const selection = loadTsModule("src/features/instagram/selection.ts");
const presentation = loadTsModule("src/features/automations/presentation.ts");
const history = loadTsModule("src/features/instagram/history.ts");
const capabilities = loadTsModule("src/features/instagram/capabilities.ts");
function account(id, health = "Connected") {
  return {
    accountId: id, providerIdentity: `ig-${id}`, path: "InstagramLogin", scopes: [], health,
    healthDetail: null, tokenExpiresAtUtc: null, connectedAtUtc: "2026-09-08T00:00:00Z",
    disconnectedAtUtc: null, username: null, displayName: null, profilePictureUrl: null,
    accountType: null, profileUpdatedAtUtc: null, subscriptionHealth: "Healthy",
    subscriptionDetail: null, lastSubscriptionCheckUtc: null,
  };
}

test("exact-account selection never falls back to the first account", () => {
  const rows = [account("A"), account("B")];
  assert.equal(selection.resolveExplicitAccountId(rows, null), null);
  assert.equal(selection.resolveExplicitAccountId(rows, "missing"), null);
  assert.equal(selection.resolveExplicitAccountId(rows, "A"), "A");
  assert.equal(selection.resolveExplicitAccountId(rows, "B"), "B");
  assert.equal(selection.resolveExplicitAccountId([account("A", "Disconnected"), account("B")], "A"), null);
});

test("workspace replacement invalidates the old exact-account selection", () => {
  assert.equal(selection.selectionAfterWorkspaceChange("w1", "w1", "A"), "A");
  assert.equal(selection.selectionAfterWorkspaceChange("w1", "w2", "A"), null);
  assert.equal(selection.selectionAfterWorkspaceChange(null, "w2", null), null);
});

test("capability states fail closed and FollowGate cannot be inferred", () => {
  assert.equal(capabilities.normalizeCapabilityState("Available"), "Available");
  assert.equal(capabilities.normalizeCapabilityState("future-state"), "Unsupported");
  assert.equal(capabilities.capabilityAvailable(null, "FollowGate"), false);
});
test("Builder V2 exposes only verified trigger/action combinations", () => {
  assert.deepEqual(presentation.allowedActions("CommentCreated"), [
    "SendPrivateReply", "SendPublicReply", "StartRevealFlow", "ScheduleFollowUp",
  ]);
  assert.deepEqual(presentation.allowedActions("InboundDirectMessage"), ["DirectMessage", "ScheduleFollowUp"]);
  assert.equal(presentation.actionAllowed("CommentCreated", "DirectMessage"), false);
  assert.equal(presentation.actionAllowed("InboundDirectMessage", "SendPrivateReply"), false);
});

test("legacy SendDirectMessage displays as Private Reply for comment trigger without rewrite", () => {
  assert.match(presentation.legacyActionLabel("CommentCreated"), /پاسخ خصوصی/);
  assert.equal(presentation.actionAllowed("CommentCreated", "SendDirectMessage"), true);
  const builder = read("src/features/automations/AutomationBuilderForm.tsx");
  assert.match(builder, /initialDefinition\.actions\.map/);
  assert.match(builder, /kind: action\.kind/);
});

test("unknown future automation values fail closed", () => {
  const builder = read("src/features/automations/AutomationBuilderForm.tsx");
  assert.match(builder, /unsupportedDefinition/);
  assert.match(builder, /automation\.unsupportedConfiguration/);
  assert.match(builder, /KNOWN_TRIGGERS/);
  assert.match(builder, /KNOWN_ACTIONS/);
});

test("plain-text validation is UTF-8 byte based for Persian and emoji", () => {
  assert.equal(presentation.utf8ByteLength("الف"), 6);
  assert.equal(presentation.validatePlainText("ا".repeat(499)), null);
  assert.equal(presentation.validatePlainText("ا".repeat(501)), "automation.actionTextTooLong");
  assert.equal(presentation.validatePlainText("😀".repeat(250)), null);
  assert.equal(presentation.validatePlainText("😀".repeat(251)), "automation.actionTextTooLong");
});
test("Public Reply keeps a 1000-character bound instead of UTF-8 bytes", () => {
  assert.equal(presentation.validatePublicReply("ا".repeat(1000)), null);
  assert.equal(presentation.validatePublicReply("ا".repeat(1001)), "automation.actionTextTooLong");
});

test("Reveal validation enforces opening/final UTF-8 and template/url bounds without truncation", () => {
  const valid = {
    openingText: "سلام", gatePromptText: "ادامه دهید", postbackButtonTitle: "ادامه",
    revealText: "نتیجه", followUrl: "https://instagram.com/example", followButtonTitle: "مشاهده پیج",
  };
  assert.equal(presentation.validateReveal(valid), null);
  assert.equal(presentation.validateReveal({ ...valid, gatePromptText: "x".repeat(641) }), "automation.gatePromptTooLong");
  assert.equal(presentation.validateReveal({ ...valid, postbackButtonTitle: "x".repeat(21) }), "automation.revealButtonTitleTooLong");
  assert.equal(presentation.validateReveal({ ...valid, followButtonTitle: "x".repeat(21) }), "automation.revealButtonTitleTooLong");
  assert.equal(presentation.validateReveal({ ...valid, followUrl: "ftp://example.com" }), "automation.revealFollowUrlScheme");
  assert.equal(presentation.validateReveal({ ...valid, followUrl: "https://x.test/" + "a".repeat(2000) }), "automation.revealFollowUrlTooLong");
  assert.equal(presentation.validateReveal({ ...valid, openingText: "😀".repeat(251) }), "automation.actionTextTooLong");
  assert.equal(presentation.validateReveal({ ...valid, revealText: "😀".repeat(251) }), "automation.revealTextTooLong");
});

test("Delayed Follow-up is byte-bounded and delay-bounded", () => {
  assert.equal(presentation.validateFollowUp("سلام", 1), null);
  assert.equal(presentation.validateFollowUp("سلام", 7 * 24 * 60), null);
  assert.equal(presentation.validateFollowUp("سلام", 0), "automation.followUpDelayInvalid");
  assert.equal(presentation.validateFollowUp("😀".repeat(251), 60), "automation.actionTextTooLong");
});
test("history status model is bounded and never claims full-history completion", () => {
  assert.equal(history.isTerminalHistoryStatus("Queued"), false);
  assert.equal(history.isTerminalHistoryStatus("Running"), false);
  assert.equal(history.isTerminalHistoryStatus("RateLimitedRetrying"), false);
  assert.equal(history.isTerminalHistoryStatus("CompletedWithinProviderLimits"), true);
  assert.equal(history.isTerminalHistoryStatus("Failed"), true);
  assert.match(history.historyStatusLabel("CompletedWithinProviderLimits"), /محدوده پوشش/);
  for (const forbidden of ["Fully Synced", "All History Synced", "Complete Inbox"]) {
    assert.equal(history.HISTORY_COVERAGE_COPY.includes(forbidden), false);
  }
  assert.match(history.HISTORY_COVERAGE_COPY, /Requests/);
  assert.doesNotMatch(history.HISTORY_COVERAGE_COPY, /حذف/);
});

test("History polling stops on terminal state and effect cleanup", () => {
  const page = read("src/app/dashboard/instagram/history/page.tsx");
  assert.match(page, /setInterval/);
  assert.match(page, /isTerminalHistoryStatus\(current\.status\).*stopPolling\(\)/s);
  assert.match(page, /return \(\) => \{ cancelled = true;.*stopPolling\(\); \}/s);
  assert.match(page, /account\.selectedAccountId, account\.accessToken, account\.workspaceId/);
});

test("Media account changes clear selection and never expose raw cursor/manual provider-id entry", () => {
  const page = read("src/app/dashboard/instagram/media/page.tsx");
  assert.match(page, /setSelectedMediaId\(null\)/);
  assert.match(page, /account\.selectedAccountId/);
  assert.doesNotMatch(page, /<input[^>]+ProviderMediaId/i);
  assert.doesNotMatch(page, /nextCursor\}/);
});
test("existing Automation account binding is immutable in the editor", () => {
  const builder = read("src/features/automations/AutomationBuilderForm.tsx");
  const editPage = read("src/app/dashboard/automations/[automationId]/page.tsx");
  assert.match(builder, /lockAccount \? initialChannelAccountId : accounts\.selectedAccountId/);
  assert.match(builder, /disabled=\{lockAccount/);
  assert.match(editPage, /initialChannelAccountId=\{detail\.channelAccountId\}/);
  assert.match(editPage, /lockAccount/);
  assert.doesNotMatch(editPage, /channelAccountId:\s*definition/);
});

test("Follow Gate remains server-owned Unsupported with no active usable toggle", () => {
  const builder = read("src/features/automations/AutomationBuilderForm.tsx");
  assert.match(builder, /FollowGate/);
  assert.match(builder, /capabilityState\(capabilities, "FollowGate"\)/);
  assert.doesNotMatch(builder, /setFollowGateMode/);
  assert.doesNotMatch(builder, /type="checkbox"[^>]*follow/i);
});

test("new Instagram product browser source contains no direct Meta/token/provider paging leak", () => {
  const files = [
    "src/app/dashboard/instagram/insights/page.tsx",
    "src/app/dashboard/instagram/media/page.tsx",
    "src/app/dashboard/instagram/history/page.tsx",
    "src/features/automations/AutomationBuilderForm.tsx",
    "src/shared/api/capabilities.ts",
    "src/shared/api/history-sync.ts",
  ];
  const combined = files.map(read).join("\n");
  for (const forbidden of ["graph.instagram.com", "graph.facebook.com", "access_token", "fbtrace_id", "paging.next"]) {
    assert.equal(combined.includes(forbidden), false, `provider leak: ${forbidden}`);
  }
  assert.match(combined, /\/api\/v1\/workspaces/);
});

test("M13-014 field errors are programmatically associated and connection loading is announced", () => {
  const primitives = read("src/shared/design/ui/index.tsx");
  const connections = read("src/app/dashboard/settings/instagram/page.tsx");
  assert.match(primitives, /id=\{`\$\{id\}-error`\}/);
  assert.match(primitives, /aria-describedby=\{error \? `\$\{rest\.id\}-error`/);
  assert.match(primitives, /aria-describedby=\{error \? `\$\{id\}-error`/);
  assert.match(connections, /role="status" aria-live="polite"/);
});

test("Connections keeps a stable API client and cannot re-enter loading from render identity churn", () => {
  const page = read("src/app/dashboard/settings/instagram/page.tsx");
  const componentAt = page.indexOf("export default function InstagramConnectionPage");
  const clientAt = page.indexOf("const client: ConnectionsApi = connectionsApi()");
  assert.ok(clientAt >= 0 && clientAt < componentAt, "connections client must be module-scoped");
  assert.doesNotMatch(page.slice(componentAt), /const client: ConnectionsApi = connectionsApi\(\)/);
});
