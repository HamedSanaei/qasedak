"use client";

/*
 * Conversation thread detail + reply composer + live CRM context panel.
 * Thread panel synchronized from the canonical Penpot board "Conversations / Inbox /
 * Desktop" (c48311ed-e700-80f8-8008-88200ed6b9fc). M12-002: the future-CRM placeholder
 * is replaced by the real M07 contacts surface — the panel resolves the conversation's
 * participant to its contact (GET /contacts/by-identity) and edits tags/notes behind the
 * workspace-scoped Contacts APIs. Reply behavior and session handling are unchanged.
 */
import { useCallback, useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { Button } from "../../../../shared/design/ui";
import { Skeleton } from "@/shared/design/Feedback";
import {
  conversationsApi,
  type ConversationDetail,
  type ConversationMessage,
} from "../../../../shared/api/conversations";
import { contactsApi, type ContactDetail } from "../../../../shared/api/contacts";
import { readSession, readWorkspaceId } from "../../../../shared/api/identity";
import { useNowMs } from "../../../../shared/hooks/useNowMs";
import {
  describeReplyFailure,
  formatRelativeFa,
  REPLY_MAX_LENGTH,
  statusLabel,
  validateReplyText,
} from "../../../../features/inbox/presentation";
import {
  CONTACT_PANEL_EMPTY_BODY,
  CONTACT_PANEL_EMPTY_TITLE,
  describeContactFailure,
  validateNoteInput,
  validateTagInput,
} from "../../../../features/contacts/presentation";

type ContactState = "idle" | "ready" | "error";
type Mutation = "tag" | "note" | null;

function errorCode(error: unknown): string | null {
  return error && typeof error === "object" && "code" in error
    ? String((error as { code: unknown }).code)
    : null;
}

export default function ConversationThreadPage() {
  const params = useParams<{ conversationId: string }>();
  const router = useRouter();
  const conversationId = params.conversationId;
  const [detail, setDetail] = useState<ConversationDetail | null>(null);
  const [state, setState] = useState<"loading" | "error" | "ready">("loading");
  const [contact, setContact] = useState<ContactDetail | null>(null);
  const [contactState, setContactState] = useState<ContactState>("idle");
  const [chipError, setChipError] = useState<string | null>(null);
  const [tagInput, setTagInput] = useState("");
  const [noteInput, setNoteInput] = useState("");
  const [mutating, setMutating] = useState<Mutation>(null);
  const [draft, setDraft] = useState("");
  const [sendError, setSendError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const nowMs = useNowMs();

  const creds = useCallback(() => {
    const session = readSession();
    const workspaceId = readWorkspaceId();
    return session && workspaceId ? { token: session.accessToken, workspaceId } : null;
  }, []);

  const refreshContact = useCallback(
    async (token: string, workspaceId: string, target: ConversationDetail) => {
      try {
        const resolved = await contactsApi().getByIdentity(
          token,
          workspaceId,
          target.channel,
          target.participantId,
        );
        setContact(resolved);
        setContactState("ready");
        setChipError(null);
      } catch {
        setContactState("error");
      }
    },
    [],
  );

  const load = useCallback(async () => {
    setState("loading");
    try {
      const c = creds();
      if (!c) {
        router.replace("/login");
        return;
      }
      const d = await conversationsApi().get(c.token, c.workspaceId, conversationId);
      setDetail(d);
      setState("ready");
      await refreshContact(c.token, c.workspaceId, d);
    } catch {
      setState("error");
    }
  }, [conversationId, router, creds, refreshContact]);

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
      const c = creds();
      if (!c) return;
      await conversationsApi().reply(c.token, c.workspaceId, conversationId, draft);
      setDraft("");
      await load();
    } catch (error) {
      setSendError(describeReplyFailure(errorCode(error)));
    } finally {
      setSending(false);
    }
  }

  async function mutateTag(tag: string, add: boolean) {
    const c = creds();
    if (!c || !contact) return;
    setMutating("tag");
    setChipError(null);
    try {
      const api = contactsApi();
      if (add) {
        const invalid = validateTagInput(tag);
        if (invalid) {
          setChipError(describeContactFailure(invalid));
          return;
        }
        await api.addTag(c.token, c.workspaceId, contact.id, tag);
      } else {
        await api.removeTag(c.token, c.workspaceId, contact.id, tag);
      }
      setTagInput("");
      await refreshContact(c.token, c.workspaceId, detail!);
    } catch (error) {
      setChipError(describeContactFailure(errorCode(error)));
    } finally {
      setMutating(null);
    }
  }

  async function addNote() {
    const c = creds();
    if (!c || !contact) return;
    const invalid = validateNoteInput(noteInput);
    if (invalid) {
      setChipError(describeContactFailure(invalid));
      return;
    }
    setMutating("note");
    setChipError(null);
    try {
      await contactsApi().addNote(c.token, c.workspaceId, contact.id, noteInput);
      setNoteInput("");
      await refreshContact(c.token, c.workspaceId, detail!);
    } catch (error) {
      setChipError(describeContactFailure(errorCode(error)));
    } finally {
      setMutating(null);
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

          {/* CRM context panel — «اطلاعات گفتگو» (context panel on the canonical inbox board). */}
          <section
            aria-label="اطلاعات گفتگو"
            style={{
              background: "var(--qs-canvas)",
              borderRadius: "var(--qs-radius-panel)",
              padding: "1rem 1.25rem",
              marginBottom: "1rem",
              display: "grid",
              gap: ".75rem",
            }}
          >
            <strong style={{ fontSize: 14, color: "var(--color-text-primary)" }}>اطلاعات گفتگو</strong>

            {contactState !== "ready" ? (
              contactState === "error" ? (
                <div role="alert">
                  <span style={{ fontSize: 13, color: "var(--qs-status-danger)" }}>دریافت مخاطب ناموفق بود.</span>
                  <span style={{ display: "inline-flex", marginInlineStart: ".5rem" }}>
                    <Button variant="outline" size="small" onClick={() => void load()}>تلاش مجدد</Button>
                  </span>
                </div>
              ) : (
                <div aria-label="در حال بارگذاری اطلاعات مخاطب" aria-busy="true" style={{ display: "grid", gap: ".5rem" }}>
                  <Skeleton width="34%" height={12} />
                  <Skeleton width="62%" height={16} />
                  <Skeleton width="48%" height={12} />
                </div>
              )
            ) : contact === null ? (
              <div style={{ display: "grid", gap: ".25rem" }}>
                <span style={{ fontSize: 13, fontWeight: 600, color: "var(--color-text-primary)" }}>
                  {CONTACT_PANEL_EMPTY_TITLE}
                </span>
                <span style={{ fontSize: 12, color: "var(--qs-muted-final)" }}>{CONTACT_PANEL_EMPTY_BODY}</span>
              </div>
            ) : (
              <>
                <div style={{ display: "grid", gap: ".25rem" }}>
                  <span style={{ fontSize: 11, color: "var(--qs-muted-final)" }}>نام مخاطب</span>
                  <span style={{ fontSize: 14, fontWeight: 600, color: "var(--color-text-primary)" }}>
                    {contact.displayName}
                  </span>
                </div>

                <div style={{ display: "grid", gap: ".4rem" }}>
                  <span style={{ fontSize: 11, color: "var(--qs-muted-final)" }}>برچسب‌ها</span>
                  <div style={{ display: "flex", flexWrap: "wrap", gap: ".4rem" }}>
                    {contact.tags.length === 0 ? (
                      <span style={{ fontSize: 12, color: "var(--qs-muted-final)" }}>هنوز برچسبی ثبت نشده است.</span>
                    ) : (
                      contact.tags.map((tag) => (
                        <span
                          key={tag}
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            gap: ".3rem",
                            background: "var(--qs-accent-soft-final)",
                            color: "var(--color-brand-accent)",
                            borderRadius: "var(--qs-radius-panel)",
                            padding: ".25rem .55rem",
                            fontSize: 12,
                          }}
                        >
                          {tag}
                          <button
                            type="button"
                            aria-label={`حذف برچسب ${tag}`}
                            disabled={mutating !== null}
                            onClick={() => void mutateTag(tag, false)}
                            style={{
                              border: "none",
                              background: "transparent",
                              color: "inherit",
                              cursor: "pointer",
                              fontSize: 12,
                              lineHeight: 1,
                              padding: 0,
                            }}
                          >
                            ✕
                          </button>
                        </span>
                      ))
                    )}
                  </div>
                  <div style={{ display: "flex", gap: ".4rem" }}>
                    <label htmlFor="contact-tag" style={{ position: "absolute", width: 1, height: 1, overflow: "hidden", clip: "rect(0 0 0 0)" }}>
                      برچسب جدید
                    </label>
                    <input
                      id="contact-tag"
                      value={tagInput}
                      maxLength={32}
                      onChange={(event) => setTagInput(event.target.value)}
                      placeholder="برچسب جدید…"
                      style={{
                        flex: 1,
                        background: "#ffffff",
                        border: "1px solid var(--qs-card-border)",
                        borderRadius: 10,
                        padding: ".4rem .6rem",
                        font: "inherit",
                        fontSize: 13,
                      }}
                    />
                    <Button size="small" disabled={mutating !== null} onClick={() => void mutateTag(tagInput, true)}>
                      {mutating === "tag" ? "در حال ثبت…" : "افزودن"}
                    </Button>
                  </div>
                </div>

                <div style={{ display: "grid", gap: ".4rem" }}>
                  <span style={{ fontSize: 11, color: "var(--qs-muted-final)" }}>یادداشت‌ها</span>
                  {contact.notes.length === 0 ? (
                    <span style={{ fontSize: 12, color: "var(--qs-muted-final)" }}>یادداشتی ثبت نشده است.</span>
                  ) : (
                    <ul style={{ listStyle: "none", padding: 0, margin: 0, display: "grid", gap: ".4rem" }}>
                      {contact.notes.map((note) => (
                        <li key={note.id} style={{ background: "#ffffff", border: "1px solid var(--qs-card-border)", borderRadius: 10, padding: ".45rem .6rem" }}>
                          <div style={{ fontSize: 13, color: "var(--color-text-primary)", whiteSpace: "pre-wrap" }}>{note.body}</div>
                          <div style={{ fontSize: 10, color: "var(--qs-muted-final)", marginTop: ".2rem" }}>
                            {nowMs !== null ? formatRelativeFa(note.createdAtUtc, nowMs) : ""}
                          </div>
                        </li>
                      ))}
                    </ul>
                  )}
                  <div style={{ display: "grid", gap: ".4rem" }}>
                    <label htmlFor="contact-note" style={{ position: "absolute", width: 1, height: 1, overflow: "hidden", clip: "rect(0 0 0 0)" }}>
                      یادداشت جدید
                    </label>
                    <textarea
                      id="contact-note"
                      value={noteInput}
                      maxLength={2000}
                      onChange={(event) => setNoteInput(event.target.value)}
                      rows={2}
                      placeholder="یادداشت جدید…"
                      style={{
                        width: "100%",
                        background: "#ffffff",
                        border: "1px solid var(--qs-card-border)",
                        borderRadius: 10,
                        padding: ".45rem .6rem",
                        font: "inherit",
                        fontSize: 13,
                        resize: "vertical",
                      }}
                    />
                    <div style={{ display: "flex", justifyContent: "flex-end" }}>
                      <Button size="small" disabled={mutating !== null} onClick={() => void addNote()}>
                        {mutating === "note" ? "در حال ثبت…" : "ثبت یادداشت"}
                      </Button>
                    </div>
                  </div>
                </div>

                {chipError ? (
                  <div role="alert" style={{ fontSize: 13, color: "var(--qs-status-danger)" }}>{chipError}</div>
                ) : null}
              </>
            )}
          </section>

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
