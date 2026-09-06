/*
 * Overview + follower-history data contract for the Instagram analytics surface
 * (M13-007). Pure types + normalization only; no UI. Mirrors the backend
 * exact-account read models. Availability is first-class: a real zero is
 * "Available" with value 0, while missing metrics are "NoData" — normalization
 * never collapses the difference, never invents values, and unknown backend
 * states fail closed. No provider DTO/token fields are ever carried.
 */

export type MetricAvailability =
  | "Available"
  | "NoData"
  | "Unsupported"
  | "PermissionRequired"
  | "TemporarilyUnavailable";

export interface InsightMetricObservation {
  metric: string;
  state: MetricAvailability;
  value: number | null;
}

export type AnalyticsAvailability = "Available" | "PermissionRequired" | "TemporarilyUnavailable";

export type CurrentFollowerState = "Available" | "Stale" | "NoData";

export type FollowerProvenance = "Observed" | "Derived" | "Backfilled";

export interface CurrentFollowersModel {
  value: number | null;
  state: CurrentFollowerState;
  observedAtUtc: string | null;
  provenance: FollowerProvenance | null;
}

export interface FollowerHistoryPoint {
  date: string; // yyyy-MM-dd (UTC day)
  value: number;
  provenance: FollowerProvenance;
}

export type MediaOverviewState = "Available" | "PermissionDenied" | "TemporarilyUnavailable" | "NoData";

export interface OverviewMediaItem {
  mediaId: string;
  kind: string;
  likeCount: number | null;
  commentCount: number | null;
  insights: InsightMetricObservation[];
}

export interface OverviewMediaModel {
  state: MediaOverviewState;
  count: number | null;
  likeTotal: number | null;
  commentTotal: number | null;
  likeTotalComplete: boolean;
  commentTotalComplete: boolean;
  items: OverviewMediaItem[] | null;
}

export interface InstagramOverview {
  accountId: string;
  analyticsAvailability: AnalyticsAvailability;
  currentFollowers: CurrentFollowersModel;
  followerHistory: FollowerHistoryPoint[];
  media: OverviewMediaModel;
  accountInsights: InsightMetricObservation[];
}

const KNOWN_METRIC_STATES: readonly string[] = [
  "Available",
  "NoData",
  "Unsupported",
  "PermissionRequired",
  "TemporarilyUnavailable",
];

const KNOWN_ANALYTICS_AVAILABILITY: readonly string[] = ["Available", "PermissionRequired", "TemporarilyUnavailable"];

const KNOWN_CURRENT_FOLLOWER_STATES: readonly string[] = ["Available", "Stale", "NoData"];

const KNOWN_MEDIA_OVERVIEW_STATES: readonly string[] = [
  "Available",
  "PermissionDenied",
  "TemporarilyUnavailable",
  "NoData",
];

const KNOWN_PROVENANCE: readonly string[] = ["Observed", "Derived", "Backfilled"];

/** UTC-day keys are yyyy-MM-dd; anything else is not a snapshot date and fails closed. */
function isSnapshotDate(value: unknown): value is string {
  return typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value);
}

/**
 * Availability is first-class: unknown/future backend states fail closed to
 * "NoData" (value already null) — never to "Available" and never to a guessed 0.
 */
export function normalizeMetricState(state: string | null | undefined): MetricAvailability {
  return state && KNOWN_METRIC_STATES.includes(state) ? (state as MetricAvailability) : "NoData";
}

function normalizeProvenance(provenance: string | null | undefined): FollowerProvenance | null {
  return provenance && KNOWN_PROVENANCE.includes(provenance) ? (provenance as FollowerProvenance) : null;
}

interface RawInsightMetricObservation {
  metric?: string | null;
  state?: string | null;
  value?: number | null;
}

interface RawCurrentFollowers {
  value?: number | null;
  state?: string | null;
  observedAtUtc?: string | null;
  provenance?: string | null;
}

interface RawFollowerHistoryPoint {
  date?: string | null;
  value?: number | null;
  provenance?: string | null;
}

interface RawOverviewMediaItem {
  mediaId?: string | null;
  kind?: string | null;
  likeCount?: number | null;
  commentCount?: number | null;
  insights?: RawInsightMetricObservation[] | null;
}

interface RawOverviewMedia {
  state?: string | null;
  count?: number | null;
  likeTotal?: number | null;
  commentTotal?: number | null;
  likeTotalComplete?: boolean;
  commentTotalComplete?: boolean;
  items?: RawOverviewMediaItem[] | null;
}

interface RawInstagramOverview {
  accountId?: string | null;
  analyticsAvailability?: string | null;
  currentFollowers?: RawCurrentFollowers | null;
  followerHistory?: RawFollowerHistoryPoint[] | null;
  media?: RawOverviewMedia | null;
  accountInsights?: RawInsightMetricObservation[] | null;
}

function normalizeMetric(raw: RawInsightMetricObservation | null | undefined): InsightMetricObservation {
  return {
    metric: raw?.metric ?? "",
    state: normalizeMetricState(raw?.state),
    value: raw?.value ?? null,
  };
}

/** Normalizes an unknown overview payload into the exact-account read model (fail-closed). */
export function normalizeOverview(raw: unknown): InstagramOverview {
  const overview = (raw ?? {}) as RawInstagramOverview;
  const availability = overview.analyticsAvailability ?? "";
  const followerState = overview.currentFollowers?.state ?? "";
  const mediaState = overview.media?.state ?? "";
  return {
    accountId: overview.accountId ?? "",
    analyticsAvailability: KNOWN_ANALYTICS_AVAILABILITY.includes(availability)
      ? (availability as AnalyticsAvailability)
      : "TemporarilyUnavailable",
    currentFollowers: {
      value: overview.currentFollowers?.value ?? null,
      state: KNOWN_CURRENT_FOLLOWER_STATES.includes(followerState)
        ? (followerState as CurrentFollowerState)
        : "NoData",
      observedAtUtc: overview.currentFollowers?.observedAtUtc ?? null,
      provenance: normalizeProvenance(overview.currentFollowers?.provenance),
    },
    followerHistory: (overview.followerHistory ?? [])
      .filter((point) => typeof point?.value === "number" && isSnapshotDate(point?.date))
      .map((point) => ({
        date: point.date as string,
        value: point.value as number,
        provenance: normalizeProvenance(point.provenance) ?? "Backfilled",
      })),
    media: {
      state: KNOWN_MEDIA_OVERVIEW_STATES.includes(mediaState) ? (mediaState as MediaOverviewState) : "NoData",
      count: overview.media?.count ?? null,
      likeTotal: overview.media?.likeTotal ?? null,
      commentTotal: overview.media?.commentTotal ?? null,
      likeTotalComplete: overview.media?.likeTotalComplete ?? false,
      commentTotalComplete: overview.media?.commentTotalComplete ?? false,
      items:
        overview.media?.items?.map((item) => ({
          mediaId: item.mediaId ?? "",
          kind: item.kind ?? "Unknown",
          likeCount: item.likeCount ?? null,
          commentCount: item.commentCount ?? null,
          insights: (item.insights ?? []).map(normalizeMetric),
        })) ?? null,
    },
    accountInsights: (overview.accountInsights ?? []).map(normalizeMetric),
  };
}

/** Normalizes the follower-history payload into newest-first points (fail-closed). */
export function normalizeFollowerHistory(raw: unknown): FollowerHistoryPoint[] {
  const payload = (raw ?? {}) as { items?: RawFollowerHistoryPoint[] | null };
  return (payload.items ?? [])
    .filter((point) => typeof point?.value === "number" && isSnapshotDate(point?.date))
    .map((point) => ({
      date: point.date as string,
      value: point.value as number,
      provenance: normalizeProvenance(point.provenance) ?? "Backfilled",
    }));
}