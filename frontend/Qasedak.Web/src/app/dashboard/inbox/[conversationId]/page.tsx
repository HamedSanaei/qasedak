"use client";

/*
 * Conversation thread detail + reply composer.
 * Synchronized from the canonical Penpot board "Conversations / Inbox / Desktop"
 * thread panel (c48311ed-e700-80f8-8008-88200ed6b9fc) via
 * docs/design/sync/2026-08-24-qasedak-final-designs.md. Visual layer only; reply
 * validation, sending and reload behavior are unchanged.
 */
import { useCallback, useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { Button } from "../../../../shared/design/ui";
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
      <Link href="/dashboard/inbox" style={{ fontSize: 13, color: "var(--qs-muted-final)", textDecoration: "none" }}>
        ← بازگشت به گفتگوها
      </Link>

      {state === "loading" ? (
        <p style={{ color: "var(--color-text-secondary)", fontSize: 13 }}>در حال دریافت پیام‌ها…</p>
      ) : null}

      {state === "error" ? (
        <div
          role="alert"
          style={{
            background: "#ffffff",
            border: "1px solid var(--qs-card-border)",
            borderRadius: "var(--qs-radius-panel)",
            padding: "1rem 1.25rem",
            marginTop: ".75rem",
            display: "grid",
            gap: ".5rem",
          }}
        >
          <span style={{ color: "var(--qs-status-danger)", fontSize: 13 }}>دریافت گفتگو ناموفق بود.</span>
          <span>
            <Button variant="outline" size="small" onClick={() => void load()}>تلاش مجدد</Button>
          </span>
        </div>
      ) : null}

      {state === "ready" && detail ? (
        <>
          <div style={{ display: "flex", alignItems: "center", gap: ".6rem", margin: ".5rem 0 1rem" }}>
            <span
              aria-hidden
              style={{
                width: 40,
                height: 40,
                borderRadius: "50%",
                background: "var(--qs-accent-soft-final)",
                color: "var(--color-brand-accent)",
                fontSize: 14,
                fontWeight: 800,
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
              }}
            >
              {detail.participantId.slice(0, 1)}
            </span>
            <span>
              <strong style={{ display: "block", fontSize: 15, color: "var(--color-text-primary)" }}>
                {detail.participantId}
              </strong>
              <span style={{ fontSize: 11, color: statusLabel(detail.status) === "بایگانی" ? "var(--qs-muted-final)" : "var(--qs-status-success)" }}>
                گفتگوی {statusLabel(detail.status)}
              </span>
            </span>
          </div>

          <ul style={{ listStyle: "none", padding: 0, margin: "0 0 1rem", display: "grid", gap: ".55rem" }}>
            {detail.messages.map((message: ConversationMessage) => {
              const outgoing = message.direction === "Outgoing";
              return (
                <li key={message.id} style={{ display: "flex", justifyContent: outgoing ? "flex-start" : "flex-end" }}>
                  <div
                    style={{
                      maxWidth: "70%",
                      background: outgoing ? "var(--qs-accent-soft-final)" : "#ffffff",
                      border: outgoing ? "1px solid transparent" : "1px solid var(--qs-card-border)",
                      borderRadius: "var(--qs-radius-panel)",
                      padding: ".65rem .9rem",
                    }}
                  >
                    <div style={{ fontSize: 13, color: "var(--color-text-primary)", whiteSpace: "pre-wrap" }}>
                      {message.body}
                    </div>
                    <div
                      style={{
                        fontSize: 10,
                        color: outgoing ? "var(--color-brand-accent)" : "var(--qs-muted-final)",
                        marginTop: ".25rem",
                      }}
                    >
                      {nowMs !== null ? formatRelativeFa(message.occurredAtUtc, nowMs) : ""}
                    </div>
                  </div>
                </li>
              );
            })}
          </ul>

          {/* Composer area per design: soft canvas wrapping a white composer. */}
          <div
            style={{
              background: "var(--qs-canvas)",
              borderRadius: "var(--qs-radius-panel)",
              padding: ".9rem",
            }}
          >
            <label htmlFor="reply" style={{ position: "absolute", width: 1, height: 1, overflow: "hidden", clip: "rect(0 0 0 0)" }}>
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
                background: "#ffffff",
                border: "1px solid var(--qs-card-border)",
                borderRadius: 12,
                padding: ".625rem .875rem",
                font: "inherit",
                fontSize: 13,
                resize: "vertical",
              }}
              placeholder="پیام خود را بنویسید…"
            />
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: ".55rem", gap: ".5rem", flexWrap: "wrap" }}>
              <span style={{ fontSize: 11, color: "var(--qs-muted-final)" }}>
                {`${draft.length} / ${REPLY_MAX_LENGTH}`}
              </span>
              <Button size="small" disabled={sending} onClick={() => void sendReply()}>
                {sending ? "در حال ارسال…" : "ارسال"}
              </Button>
            </div>
            <p style={{ margin: ".45rem 0 0", fontSize: 11, color: "var(--color-text-secondary)" }}>
              ارسال فقط در بازه مجاز ۲۴ ساعته امکان‌پذیر است؛ پیام‌های پذیرفته‌شده توسط کانال ثبت می‌شوند.
            </p>
            {sendError ? (
              <div role="alert" style={{ marginTop: ".5rem", fontSize: 13, color: "var(--qs-status-danger)" }}>
                {sendError}
              </div>
            ) : null}
          </div>
        </>
      ) : null}
    </main>
  );
}
