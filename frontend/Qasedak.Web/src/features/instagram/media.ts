/*
 * Post-picker data contract for the Instagram media catalog (M13-006).
 * Pure types + normalization only; no UI. The picker item carries enough to
 * display a post/reel and later select its opaque media id
 * (ConnectedAccountId + ProviderMediaId). Unknown backend values fail closed
 * to "Unknown" — never crash, never invent.
 */

export type MediaKind = "Image" | "Video" | "Reel" | "Carousel" | "Unknown";

export interface MediaPickerChild {
  mediaId: string;
  kind: MediaKind;
  mediaUrl: string | null;
  thumbnailUrl: string | null;
  permalink: string | null;
}

export interface MediaPickerItem {
  mediaId: string;
  caption: string | null;
  kind: MediaKind;
  mediaProductType: string | null;
  timestampUtc: string | null;
  permalink: string | null;
  mediaUrl: string | null;
  thumbnailUrl: string | null;
  hasMediaPreview: boolean;
  hasThumbnail: boolean;
  likeCount: number | null;
  commentCount: number | null;
  children: MediaPickerChild[] | null;
}

export interface MediaPickerPage {
  items: MediaPickerItem[];
  nextCursor: string | null;
  hasMore: boolean;
}

const KNOWN_KINDS: readonly string[] = ["Image", "Video", "Reel", "Carousel"];

/** Unknown/future backend kinds fail closed to "Unknown" without translation invention. */
export function normalizeKind(kind: string | null | undefined): MediaKind {
  return kind && KNOWN_KINDS.includes(kind) ? (kind as MediaKind) : "Unknown";
}

interface RawMediaPickerChild {
  mediaId?: string | null;
  kind?: string | null;
  mediaUrl?: string | null;
  thumbnailUrl?: string | null;
  permalink?: string | null;
}

interface RawMediaPickerItem {
  mediaId?: string | null;
  caption?: string | null;
  kind?: string | null;
  mediaProductType?: string | null;
  timestampUtc?: string | null;
  permalink?: string | null;
  mediaUrl?: string | null;
  thumbnailUrl?: string | null;
  hasMediaPreview?: boolean;
  hasThumbnail?: boolean;
  likeCount?: number | null;
  commentCount?: number | null;
  children?: RawMediaPickerChild[] | null;
}

interface RawMediaPickerPage {
  items?: RawMediaPickerItem[] | null;
  nextCursor?: string | null;
  hasMore?: boolean;
}

/** Normalizes an unknown server payload into the picker contract (fail-closed). */
export function normalizePage(raw: unknown): MediaPickerPage {
  const page = (raw ?? {}) as RawMediaPickerPage;
  return {
    items: (page.items ?? []).map((item) => ({
      mediaId: item.mediaId ?? "",
      caption: item.caption ?? null,
      kind: normalizeKind(item.kind),
      mediaProductType: item.mediaProductType ?? null,
      timestampUtc: item.timestampUtc ?? null,
      permalink: item.permalink ?? null,
      mediaUrl: item.mediaUrl ?? null,
      thumbnailUrl: item.thumbnailUrl ?? null,
      hasMediaPreview: item.hasMediaPreview ?? false,
      hasThumbnail: item.hasThumbnail ?? false,
      likeCount: item.likeCount ?? null,
      commentCount: item.commentCount ?? null,
      children:
        item.children?.map((child) => ({
          mediaId: child.mediaId ?? "",
          kind: normalizeKind(child.kind),
          mediaUrl: child.mediaUrl ?? null,
          thumbnailUrl: child.thumbnailUrl ?? null,
          permalink: child.permalink ?? null,
        })) ?? null,
    })),
    nextCursor: page.nextCursor ?? null,
    hasMore: page.hasMore ?? false,
  };
}