// M13-007 — Instagram insights overview + follower history frontend contract tests
// (offline, deterministic). Mirrors the backend exact-account read models:
// availability is first-class (real zero vs NoData), provenance is preserved,
// unknown backend states fail closed, and no token/provider fields are carried.
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

const insights = loadTsModule("src/features/instagram/insights.ts");

test("metric state normalization keeps every verified state and fails closed", () => {
  assert.equal(insights.normalizeMetricState("Available"), "Available");
  assert.equal(insights.normalizeMetricState("NoData"), "NoData");
  assert.equal(insights.normalizeMetricState("Unsupported"), "Unsupported");
  assert.equal(insights.normalizeMetricState("PermissionRequired"), "PermissionRequired");
  assert.equal(insights.normalizeMetricState("TemporarilyUnavailable"), "TemporarilyUnavailable");
  // Unknown future backend states must never become Available or 0.
  assert.equal(insights.normalizeMetricState("SomethingNew"), "NoData");
  assert.equal(insights.normalizeMetricState(null), "NoData");
  assert.equal(insights.normalizeMetricState(undefined), "NoData");
});

test("overview normalization preserves a real zero and keeps NoData null", () => {
  const overview = insights.normalizeOverview({
    accountId: "acc-1",
    analyticsAvailability: "Available",
    currentFollowers: {
      value: 10000,
      state: "Available",
      observedAtUtc: "2026-09-06T12:00:00+00:00",
      provenance: "Observed",
    },
    followerHistory: [
      { date: "2026-09-06", value: 10000, provenance: "Observed" },
      { date: "2026-09-05", value: 9950, provenance: "Backfilled" },
    ],
    media: {
      state: "Available",
      count: 1,
      likeTotal: 0,
      commentTotal: null,
      likeTotalComplete: true,
      commentTotalComplete: false,
      items: [
        {
          mediaId: "m-1",
          kind: "Image",
          likeCount: 0,
          commentCount: null,
          insights: [
            { metric: "likes", state: "Available", value: 0 },
            { metric: "comments", state: "NoData", value: null },
            { metric: "saves", state: "PermissionRequired", value: null },
          ],
        },
      ],
    },
    accountInsights: [
      { metric: "reach", state: "Available", value: 1200 },
      { metric: "follows_and_unfollows", state: "NoData", value: null },
    ],
  });

  assert.equal(overview.accountId, "acc-1");
  assert.equal(overview.analyticsAvailability, "Available");
  // Real zero stays 0; NoData stays null; permission loss carries no value.
  assert.equal(overview.accountInsights[0].value, 1200);
  assert.equal(overview.media.items[0].insights[0].value, 0);
  assert.equal(overview.media.items[0].insights[0].state, "Available");
  assert.equal(overview.media.items[0].insights[1].value, null);
  assert.equal(overview.media.items[0].insights[1].state, "NoData");
  assert.equal(overview.media.items[0].insights[2].state, "PermissionRequired");
  // Media totals: complete flag tells the truth (0 total is a real zero).
  assert.equal(overview.media.likeTotal, 0);
  assert.equal(overview.media.likeTotalComplete, true);
  assert.equal(overview.media.commentTotal, null);
  assert.equal(overview.media.commentTotalComplete, false);
  // Current followers carry value, state and provenance.
  assert.equal(overview.currentFollowers.value, 10000);
  assert.equal(overview.currentFollowers.state, "Available");
  assert.equal(overview.currentFollowers.provenance, "Observed");
  assert.equal(overview.followerHistory.length, 2);
  assert.equal(overview.followerHistory[0].provenance, "Observed");
  assert.equal(overview.followerHistory[1].provenance, "Backfilled");
});

test("follower history normalization is newest-first with provenance preserved", () => {
  const points = insights.normalizeFollowerHistory({
    items: [
      { date: "2026-09-06", value: 10000, provenance: "Observed" },
      { date: "2026-09-05", value: 9950, provenance: "Backfilled" },
      { date: "garbage", value: 1, provenance: "Observed" },
      { date: "2026-09-04" },
    ],
  });

  assert.equal(points.length, 2);
  assert.equal(points[0].date, "2026-09-06");
  assert.equal(points[0].value, 10000);
  assert.equal(points[0].provenance, "Observed");
  assert.equal(points[1].provenance, "Backfilled");
});

test("unknown or missing payloads fail closed without inventing analytics", () => {
  const empty = insights.normalizeOverview({});
  assert.equal(empty.analyticsAvailability, "TemporarilyUnavailable");
  assert.equal(empty.currentFollowers.state, "NoData");
  assert.equal(empty.currentFollowers.value, null);
  assert.equal(empty.media.state, "NoData");
  assert.equal(empty.media.items, null);
  assert.equal(empty.followerHistory.length, 0);
  assert.equal(empty.accountInsights.length, 0);

  const nullPayload = insights.normalizeOverview(null);
  assert.equal(nullPayload.currentFollowers.state, "NoData");

  const unknownState = insights.normalizeOverview({
    analyticsAvailability: "BrandNewState",
    currentFollowers: { value: 5, state: "BrandNewState", provenance: "BrandNewProvenance" },
    media: { state: "BrandNewState" },
  });
  // Fail closed: availability never claims success; unknown provenance stays null.
  assert.equal(unknownState.analyticsAvailability, "TemporarilyUnavailable");
  assert.equal(unknownState.currentFollowers.state, "NoData");
  assert.equal(unknownState.currentFollowers.provenance, null);
  assert.equal(unknownState.media.state, "NoData");
  assert.equal(insights.normalizeFollowerHistory(null).length, 0);
});

test("overview normalization never carries token or provider paging fields", () => {
  const overview = insights.normalizeOverview({
    accountId: "acc-1",
    accessToken: "LONG-SECRET",
    access_token: "LONG-SECRET",
    paging: { cursors: { after: "provider-token" } },
    currentFollowers: { value: 1, state: "Available" },
    media: { state: "Available", count: 0 },
  });

  assert.equal("accessToken" in overview, false);
  assert.equal("paging" in overview, false);
  assert.equal("cursors" in overview.media, false);
  assert.equal(overview.currentFollowers.value, 1);
});