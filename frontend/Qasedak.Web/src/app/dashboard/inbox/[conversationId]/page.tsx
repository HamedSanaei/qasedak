"use client";

/*
 * Conversation thread detail + reply composer.
 * DESIGN STATUS: no thread design exists in the canonical Penpot file — see
 * docs/design/sync/M08-004-conversation-inbox.md (visual sync BLOCKED).
 */
import { useCallback, useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { Button, Card } from "../../../../shared/design/ui";
import {
  conversationsApi,
  type ConversationDetail,
  type ConversationMessage,
} from "../../../../shared/api/conversations";
import { readSession, readWorkspaceId } from "../../../../shared/api/identity";
import { useNowMs } from "../../../../shared/hooks/useNowMs";
import {
  describeReplyFailure,
  formatRelativeFa,
  REPLY_MAX_LENGTH,
  statusLabel,
  validateReplyText,
} from "../../../../features/inbox/presentation";

export default function ConversationThreadPage() {
  const params = useParams<{ conversationId: string }>();
  const router = useRouter();
  const conversationId = params.conversationId;
  const [detail, setDetail] = useState<ConversationDetail | null>(null);
  const [state, setState] = useState<"loading" | "error" | "ready">("loading");
  const [draft, setDraft] = useState("");
  const [sendError, setSendError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
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
      setDetail(await conversationsApi().get(session.accessToken, workspaceId, conversationId));
      setState("ready");
    } catch {
      setState("error");
    }
  }, [conversationId, router]);

  useEffect(() => {
    // Defer so the first setState happens outside the effect body (react-hooks lint).
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  async function sendReply() {
    const invalid = validateReplyText(draft);
    if (invalid) {
      setSendError(describeReplyFailure(invalid));
      return;
    }
    setSending(true);
    setSendError(null);
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) return;
      await conversationsApi().reply(session.accessToken, workspaceId, conversationId, draft);
      setDraft("");
      await load();
    } catch (error) {
      const code =
        error && typeof error === "object" && "code" in error
          ? String((error as { code: unknown }).code)
          : null;
      setSendError(describeReplyFailure(code));
    } finally {
      setSending(false);
    }
  }

  return (
    <main style={{ padding: "1.5rem 2rem", maxWidth: 760 }}>
      <Link href="/dashboard/inbox" style={{ fontSize: 13, color: "#88828E", textDecoration: "none" }}>
        ← بازگشت به اینباکس
      </Link>
      <h1 style={{ fontSize: 24, fontWeight: 800, color: "var(--color-text-primary)", margin: ".5rem 0 1rem" }}>
        گفتگو
      </h1>

      {state === "loading" ? (
        <p style={{ color: "var(--color-text-secondary)", fontSize: 14 }}>در حال دریافت پیام‌ها…</p>
      ) : null}

      {state === "error" ? (
        <Card>
          <div role="alert" style={{ color: "var(--color-status-danger)", fontSize: 14 }}>
            دریافت گفتگو ناموفق بود.
          </div>
          <div style={{ marginTop: ".75rem" }}>
            <Button variant="outline" size="small" onClick={() => void load()}>تلاش مجدد</Button>
          </div>
        </Card>
      ) : null}

      {state === "ready" && detail ? (
        <>
          <p style={{ display: "flex", gap: ".5rem", alignItems: "center", fontSize: 13, color: "#737373" }}>
            <strong style={{ color: "#141414" }}>{detail.participantId}</strong>
            <span>· {statusLabel(detail.status)}</span>
          </p>

          <ul style={{ listStyle: "none", padding: 0, margin: "0 0 1.25rem", display: "grid", gap: ".625rem" }}>
            {detail.messages.map((message: ConversationMessage) => {
              const outgoing = message.direction === "Outgoing";
              return (
                <li key={message.id} style={{ display: "flex", justifyContent: outgoing ? "flex-start" : "flex-end" }}>
                  <div
                    style={{
                      maxWidth: "70%",
                      background: outgoing ? "var(--color-accent-softer)" : "var(--color-surface-subtle)",
                      borderRadius: "var(--radius-card)",
                      padding: ".75rem 1rem",
                    }}
                  >
                    <div style={{ fontSize: 14, color: "var(--color-text-primary)", whiteSpace: "pre-wrap" }}>
                      {message.body}
                    </div>
                    <div style={{ fontSize: 11, color: "var(--color-text-muted)", marginTop: ".25rem" }}>
                      {nowMs !== null ? formatRelativeFa(message.occurredAtUtc, nowMs) : ""}
                    </div>
                  </div>
                </li>
              );
            })}
          </ul>

          <Card>
            <label htmlFor="reply" style={{ fontSize: 13, fontWeight: 700, color: "#141414" }}>
              پاسخ
            </label>
            <textarea
              id="reply"
              value={draft}
              maxLength={2000}
              onChange={(event) => setDraft(event.target.value)}
              rows={3}
              style={{
                width: "100%",
                marginTop: ".5rem",
                border: "1px solid var(--color-border-input)",
                borderRadius: "var(--radius-control)",
                padding: ".625rem .875rem",
                font: "inherit",
                resize: "vertical",
              }}
              placeholder="پاسخ خود را بنویسید…"
            />
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: ".5rem" }}>
              <span style={{ fontSize: 12, color: "var(--color-text-muted)" }}>
                {`${draft.length} / ${REPLY_MAX_LENGTH} مجاز`}
              </span>
              <Button size="small" disabled={sending} onClick={() => void sendReply()}>
                {sending ? "در حال ارسال…" : "ارسال پاسخ"}
              </Button>
            </div>
            {sendError ? (
              <div role="alert" style={{ marginTop: ".5rem", fontSize: 13, color: "var(--color-status-danger)" }}>
                {sendError}
              </div>
            ) : null}
          </Card>
        </>
      ) : null}
    </main>
  );
}
