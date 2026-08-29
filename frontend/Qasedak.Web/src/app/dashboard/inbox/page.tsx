"use client";

/*
 * Workspace inbox — conversation list.
 * Synchronized from the canonical Penpot board "Conversations / Inbox / Desktop"
 * (c48311ed-e700-80f8-8008-88200ed6b9fc, page c48311ed-e700-80f8-8008-88200ec40bf3)
 * via docs/design/sync/2026-08-24-qasedak-final-designs.md. Visual layer only;
 * list/filter/navigation behavior is unchanged. Search was disabled BY DESIGN until the
 * backend shipped a search query capability (M12-001) — it is now a live, debounced
 * server-side search over participant + message bodies.
 */
import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "../../../shared/design/ui";
import { conversationsApi, type ConversationListItem } from "../../../shared/api/conversations";
import { readSession, readWorkspaceId } from "../../../shared/api/identity";
import { useNowMs } from "../../../shared/hooks/useNowMs";
import { formatRelativeFa, statusLabel } from "../../../features/inbox/presentation";

const FILTERS: Array<{ key: string; label: string }> = [
  { key: "", label: "همه" },
  { key: "open", label: "باز" },
  { key: "pending", label: "در انتظار" },
];

export default function InboxPage() {
  const router = useRouter();
  const [items, setItems] = useState<ConversationListItem[] | null>(null);
  const [state, setState] = useState<"loading" | "error" | "ready">("loading");
  const [filter, setFilter] = useState("");
  const [search, setSearch] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const nowMs = useNowMs();

  // Debounce typed terms so each keystroke does not fire a request; blank terms are
  // normalized to no search (the backend treats them identically).
  useEffect(() => {
    const timer = window.setTimeout(() => setSearch(searchInput.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [searchInput]);

  const load = useCallback(async () => {
    setState("loading");
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) {
        router.replace("/login");
        return;
      }
      const result = await conversationsApi().list(session.accessToken, workspaceId, {
        status: filter || null,
        search: search || null,
      });
      setItems(result.items);
      setState("ready");
    } catch {
      setState("error");
    }
  }, [filter, search, router]);

  useEffect(() => {
    // Defer so the first setState happens outside the effect body (react-hooks lint).
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  return (
    <main style={{ padding: "1.5rem 2rem", maxWidth: 900 }}>
      <div
        style={{
          background: "#ffffff",
          border: "1px solid var(--qs-card-border)",
          borderRadius: "var(--qs-radius-panel)",
          padding: "1.25rem 1.4rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "1rem", flexWrap: "wrap" }}>
          <h1 style={{ fontSize: 20, fontWeight: 800, color: "var(--color-text-primary)", margin: 0 }}>
            گفتگوها
          </h1>
        </div>

        {/* Server-side search over participant + message bodies (M12-001). The design
            only defined the disabled state; the enabled placeholder is recorded in
            docs/design/sync/M12-001-inbox-search.md. */}
        <input
          type="search"
          value={searchInput}
          onChange={(event) => setSearchInput(event.target.value)}
          placeholder="جستجو در گفتگوها…"
          aria-label="جستجو در گفتگوها"
          style={{
            width: "100%",
            marginTop: ".9rem",
            height: 44,
            background: "var(--qs-canvas)",
            border: "1px solid var(--qs-card-border)",
            borderRadius: "var(--qs-control-radius)",
            padding: "0 .9rem",
            font: "inherit",
            fontSize: 13,
            color: "var(--color-text-secondary)",
          }}
        />

        <div role="tablist" aria-label="فیلتر گفتگوها" style={{ display: "flex", gap: ".5rem", margin: ".9rem 0 1rem" }}>
          {FILTERS.map(({ key, label }) => (
            <button
              key={key}
              role="tab"
              aria-selected={filter === key}
              onClick={() => setFilter(key)}
              style={{
                border: "1px solid var(--qs-card-border)",
                cursor: "pointer",
                borderRadius: 9,
                padding: ".35rem .85rem",
                fontSize: 13,
                fontWeight: filter === key ? 700 : 500,
                background: filter === key ? "var(--qs-accent-soft-final)" : "#ffffff",
                color: filter === key ? "var(--color-brand-accent)" : "var(--color-text-secondary)",
              }}
            >
              {label}
            </button>
          ))}
        </div>

        {state === "loading" ? (
          <p style={{ color: "var(--color-text-secondary)", fontSize: 13 }}>در حال دریافت گفتگوها…</p>
        ) : null}

        {state === "error" ? (
          <div
            role="alert"
            style={{
              background: "var(--qs-status-danger-bg)",
              borderRadius: 10,
              padding: ".75rem 1rem",
              display: "grid",
              gap: ".5rem",
            }}
          >
            <span style={{ color: "var(--qs-status-danger)", fontSize: 13 }}>دریافت گفتگوها ناموفق بود.</span>
            <span>
              <Button variant="outline" size="small" onClick={() => void load()}>تلاش مجدد</Button>
            </span>
          </div>
        ) : null}

        {state === "ready" && items && items.length === 0 ? (
          <p style={{ margin: 0, color: "var(--qs-muted-final)", fontSize: 13 }}>
            {search
              ? "گفتگویی با این عبارت پیدا نشد."
              : "هنوز گفتگویی وجود ندارد. پس از اتصال پیج اینستاگرام، دایرکت‌ها اینجا نمایش داده می‌شوند."}
          </p>
        ) : null}

        {state === "ready" && items && items.length > 0 ? (
          <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: ".55rem" }}>
            {items.map((conversation) => (
              <li key={conversation.id}>
                <a
                  href={`/dashboard/inbox/${conversation.id}`}
                  className="inbox-row"
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: ".8rem",
                    textDecoration: "none",
                    background: "#ffffff",
                    border: "1px solid var(--qs-card-border)",
                    borderRadius: 12,
                    padding: ".75rem .9rem",
                  }}
                >
                  <span
                    aria-hidden
                    style={{
                      width: 40,
                      height: 40,
                      flexShrink: 0,
                      borderRadius: "50%",
                      background: conversation.unreadCount > 0 ? "var(--color-brand-accent)" : "var(--qs-accent-soft-final)",
                      color: conversation.unreadCount > 0 ? "#ffffff" : "var(--color-brand-accent)",
                      fontSize: 14,
                      fontWeight: 800,
                      display: "inline-flex",
                      alignItems: "center",
                      justifyContent: "center",
                    }}
                  >
                    {conversation.participantId.slice(0, 1)}
                  </span>
                  <span style={{ minWidth: 0, flex: 1 }}>
                    <span style={{ display: "flex", alignItems: "center", gap: ".5rem" }}>
                      <strong style={{ fontSize: 14, fontWeight: 700, color: "var(--color-text-primary)" }}>
                        {conversation.participantId}
                      </strong>
                      <span style={{ fontSize: 11, color: statusLabel(conversation.status) === "بایگانی" ? "var(--qs-muted-final)" : "var(--qs-status-success)" }}>
                        {statusLabel(conversation.status)}
                      </span>
                    </span>
                    <span
                      style={{
                        display: "block",
                        fontSize: 12,
                        color: "var(--color-text-secondary)",
                        marginTop: ".2rem",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {conversation.lastMessagePreview ?? "(بدون پیام)"}
                    </span>
                  </span>
                  <span style={{ textAlign: "left", whiteSpace: "nowrap" }}>
                    <span style={{ display: "block", fontSize: 11, color: "var(--qs-muted-final)" }}>
                      {conversation.lastMessageAtUtc && nowMs !== null
                        ? formatRelativeFa(conversation.lastMessageAtUtc, nowMs)
                        : ""}
                    </span>
                    {conversation.unreadCount > 0 ? (
                      <span
                        aria-label={`${conversation.unreadCount} پیام نخوانده`}
                        style={{
                          display: "inline-flex",
                          marginTop: ".3rem",
                          minWidth: 18,
                          height: 18,
                          padding: "0 .3rem",
                          borderRadius: 999,
                          background: "var(--color-brand-accent)",
                          color: "#ffffff",
                          fontSize: 10,
                          fontWeight: 700,
                          alignItems: "center",
                          justifyContent: "center",
                        }}
                      >
                        {conversation.unreadCount}
                      </span>
                    ) : null}
                  </span>
                </a>
              </li>
            ))}
          </ul>
        ) : null}
      </div>
    </main>
  );
}
