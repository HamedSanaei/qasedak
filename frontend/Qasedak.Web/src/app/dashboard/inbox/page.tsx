"use client";

/*
 * Workspace inbox — conversation list.
 * DESIGN STATUS: no inbox/conversation design exists in the canonical Penpot file
 * (all 24 pages swept during M08-004). Layout composes approved foundation primitives
 * and tokens only; visual sync BLOCKED pending a human-approved design.
 */
import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Button, Card, StatusPill } from "../../../shared/design/ui";
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
  const nowMs = useNowMs();

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
      });
      setItems(result.items);
      setState("ready");
    } catch {
      setState("error");
    }
  }, [filter, router]);

  useEffect(() => {
    // Defer so the first setState happens outside the effect body (react-hooks lint).
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  return (
    <main style={{ padding: "1.5rem 2rem", maxWidth: 900 }}>
      <h1 style={{ fontSize: 24, fontWeight: 800, color: "var(--color-text-primary)", margin: "0 0 1rem" }}>
        اینباکس دایرکت
      </h1>

      <div role="tablist" aria-label="فیلتر گفتگوها" style={{ display: "flex", gap: ".5rem", marginBottom: "1rem" }}>
        {FILTERS.map(({ key, label }) => (
          <button
            key={key}
            role="tab"
            aria-selected={filter === key}
            onClick={() => setFilter(key)}
            style={{
              border: "none",
              cursor: "pointer",
              borderRadius: "var(--radius-chip)",
              padding: ".375rem .875rem",
              fontSize: 13,
              fontWeight: filter === key ? 800 : 500,
              background: filter === key ? "var(--color-accent-soft)" : "transparent",
              color: filter === key ? "var(--color-brand-accent)" : "var(--color-text-secondary)",
            }}
          >
            {label}
          </button>
        ))}
      </div>

      {state === "loading" ? (
        <p style={{ color: "var(--color-text-secondary)", fontSize: 14 }}>در حال دریافت گفتگوها…</p>
      ) : null}

      {state === "error" ? (
        <Card>
          <div role="alert" style={{ color: "var(--color-status-danger)", fontSize: 14 }}>
            دریافت گفتگوها ناموفق بود.
          </div>
          <div style={{ marginTop: ".75rem" }}>
            <Button variant="outline" size="small" onClick={() => void load()}>تلاش مجدد</Button>
          </div>
        </Card>
      ) : null}

      {state === "ready" && items && items.length === 0 ? (
        <Card>
          <p style={{ margin: 0, color: "var(--color-text-secondary)", fontSize: 14 }}>
            هنوز گفتگویی وجود ندارد. پس از اتصال پیج اینستاگرام، دایرکت‌ها اینجا نمایش داده می‌شوند.
          </p>
        </Card>
      ) : null}

      {state === "ready" && items && items.length > 0 ? (
        <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: ".625rem" }}>
          {items.map((conversation) => (
            <li key={conversation.id}>
              <a
                href={`/dashboard/inbox/${conversation.id}`}
                className="inbox-row"
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: "1rem",
                  textDecoration: "none",
                  background: "var(--color-surface-subtle)",
                  borderRadius: "var(--radius-card)",
                  padding: ".875rem 1rem",
                }}
              >
                <span style={{ minWidth: 0 }}>
                  <span style={{ display: "flex", alignItems: "center", gap: ".5rem" }}>
                    <strong style={{ fontSize: 14, fontWeight: 700, color: "#141414" }}>
                      {conversation.participantId}
                    </strong>
                    <StatusPill tone={conversation.status === "archived" ? "neutral" : "info"}>
                      {statusLabel(conversation.status)}
                    </StatusPill>
                    {conversation.unreadCount > 0 ? (
                      <StatusPill tone="danger">{`${conversation.unreadCount} نخوانده`}</StatusPill>
                    ) : null}
                  </span>
                  <span
                    style={{
                      display: "block",
                      fontSize: 13,
                      color: "#737373",
                      marginTop: ".25rem",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {conversation.lastMessagePreview ?? "(بدون پیام)"}
                  </span>
                </span>
                <span style={{ fontSize: 12, color: "var(--color-text-muted)", whiteSpace: "nowrap" }}>
                  {conversation.lastMessageAtUtc && nowMs !== null
                    ? formatRelativeFa(conversation.lastMessageAtUtc, nowMs)
                    : ""}
                </span>
              </a>
            </li>
          ))}
        </ul>
      ) : null}
    </main>
  );
}
