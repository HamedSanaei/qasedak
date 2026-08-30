"use client";

/*
 * Automation builder form shared by the New and Edit routes.
 * Synchronized from canonical boards "Comment Automation — New"
 * (f5bf3c2c-b970-8002-8008-874ec2cb62fb) and "Smart Answering — Component States"
 * (f5bf3c2c-b970-8002-8008-8747843b4ad6): match-type dropdown with inline hint panels
 * (برابر/شامل/هر ریپلایی), keyword chips («مثال: قیمت، خرید، لینک»), reply composer with
 * ۰/حداکثر counter, live preview bubble, action row «دایرکت», submit «ثبت».
 * Divergence note: the design shows a ۰/۲۰۰۰ counter; the backend domain caps action
 * text at 1000 chars — the backend limit wins (documented in the design sync evidence).
 */
import { useState } from "react";
import Link from "next/link";
import { Button, Card, SelectField, TextField } from "../../shared/design/ui";
import type { AutomationDefinitionDto } from "../../shared/api/automations";
import {
  AUTOMATION_MAX_MESSAGE_LENGTH,
  describeAutomationFailure,
  isEntitlementDenial,
  MATCH_MODE_OPTIONS,
  validateAutomationName,
  validateDefinition,
  type MatchMode,
} from "./presentation";

export interface AutomationBuilderFormProps {
  initialName?: string;
  initialDefinition?: AutomationDefinitionDto | null;
  submitLabel: string;
  onSubmit: (
    name: string,
    definition: AutomationDefinitionDto,
  ) => Promise<{ ok: true } | { ok: false; code: string | null }>;
}

export function AutomationBuilderForm({
  initialName = "",
  initialDefinition = null,
  submitLabel,
  onSubmit,
}: AutomationBuilderFormProps) {
  const firstCondition = initialDefinition?.conditions?.[0];
  const initialMode: MatchMode =
    !firstCondition ? "anyReply" : firstCondition.operator === "Equals" ? "equals" : "contains";

  const [name, setName] = useState(initialName);
  const [matchMode, setMatchMode] = useState<MatchMode>(initialMode);
  const [keywordText, setKeywordText] = useState((initialDefinition?.keywordFilters ?? []).join("، "));
  const [messageText, setMessageText] = useState(initialDefinition?.actions?.[0]?.messageText ?? "");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string | null>>({});
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [entitlementBlocked, setEntitlementBlocked] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const keywords = keywordText
    .split(/[،,]/)
    .map((k) => k.trim())
    .filter(Boolean);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitError(null);
    setEntitlementBlocked(false);

    const errors: Record<string, string | null> = {
      name: describeAutomationFailure(validateAutomationName(name)),
      definition: describeAutomationFailure(validateDefinition(matchMode, keywords, messageText)),
    };
    setFieldErrors(errors);
    if (errors.name || errors.definition) return;

    const definition: AutomationDefinitionDto = {
      triggerKind: "CommentCreated",
      keywordFilters: matchMode === "anyReply" ? [] : keywords,
      conditions:
        matchMode === "anyReply"
          ? []
          : [
              {
                field: "CommentText",
                operator: matchMode === "equals" ? "Equals" : "Contains",
                expectedValue: keywords[0] ?? "",
              },
            ],
      actions: [{ kind: "SendDirectMessage", messageText }],
    };

    setSubmitting(true);
    try {
      const result = await onSubmit(name.trim(), definition);
      if (!result.ok) {
        setSubmitError(describeAutomationFailure(result.code));
        setEntitlementBlocked(isEntitlementDenial(result.code));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} noValidate style={{ display: "grid", gap: "1.25rem", maxWidth: 720 }}>
      <Card>
        <TextField
          id="automation-name"
          label="نام دستور"
          value={name}
          onChange={(e) => setName(e.target.value)}
          error={fieldErrors.name}
          placeholder="مثلاً ارسال قیمت"
        />
      </Card>

      <Card>
        <h2 style={{ fontSize: 16, fontWeight: 700, color: "#141414", margin: "0 0 .75rem" }}>
          اگر کاربر دستورهای زیر را در کامنت وارد کرد…
        </h2>

        <div style={{ display: "flex", alignItems: "center", gap: ".5rem", fontSize: 13, color: "#514D5E" }}>
          <span>پست</span>
          <select
            disabled
            aria-label="محدود به پست (در نسخه فعلی همه پست‌ها)"
            title="در نسخه فعلی روی همه پست‌ها اعمال می‌شود"
            style={{
              border: "1px solid var(--color-border-input)",
              borderRadius: "var(--radius-chip)",
              padding: ".25rem .75rem",
              font: "inherit",
              background: "var(--color-surface-subtle)",
            }}
          >
            <option>همه پست‌ها ⌄</option>
          </select>
        </div>

        <div style={{ marginTop: ".9rem", display: "grid", gap: ".75rem" }}>
          <TextField
            id="trigger-keywords"
            label="واژه‌های فعال‌کننده"
            placeholder="مثال: قیمت، خرید، لینک"
            value={keywordText}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setKeywordText(e.target.value)}
            error={null}
            disabled={matchMode === "anyReply"}
          />
          <SelectField
            id="match-mode"
            label="نوع تطبیق"
            value={matchMode}
            onChange={(e: React.ChangeEvent<HTMLSelectElement>) => setMatchMode(e.target.value as MatchMode)}
            options={MATCH_MODE_OPTIONS.map((option) => ({
              value: option.value,
              label: `${option.label} — ${option.hint}`,
            }))}
          />
        </div>
      </Card>

      <Card>
        <h2 style={{ fontSize: 16, fontWeight: 700, color: "#141414", margin: "0 0 .75rem" }}>
          این پاسخ را می‌فرستیم
        </h2>
        <label htmlFor="reply-text" style={{ fontSize: 13, fontWeight: 700, color: "#141414" }}>
          متن پاسخ دایرکت
        </label>
        <textarea
          id="reply-text"
          rows={3}
          maxLength={2000}
          value={messageText}
          onChange={(event) => setMessageText(event.target.value)}
          placeholder="سلام 👋 اطلاعات کامل برات ارسال شد."
          style={{
            width: "100%",
            marginTop: ".5rem",
            border: "1px solid var(--color-border-input)",
            borderRadius: "var(--radius-control)",
            padding: ".625rem .875rem",
            font: "inherit",
            resize: "vertical",
          }}
        />
        <div style={{ display: "flex", justifyContent: "space-between", marginTop: ".35rem", fontSize: 11, color: "var(--color-text-muted)" }}>
          <span>{`${messageText.length} / ${AUTOMATION_MAX_MESSAGE_LENGTH}`}</span>
          <span>اقدام: دایرکت</span>
        </div>
        {fieldErrors.definition ? (
          <div role="alert" style={{ marginTop: ".5rem", fontSize: 13, color: "var(--color-status-danger)" }}>
            {fieldErrors.definition}
          </div>
        ) : null}
      </Card>

      <Card>
        <p style={{ margin: 0, fontSize: 12, fontWeight: 600, color: "#737373" }}>پیش‌نمایش</p>
        <div style={{ display: "flex", justifyContent: "flex-start", marginTop: ".6rem" }}>
          <div
            style={{
              maxWidth: "80%",
              background: "var(--color-accent-softer)",
              borderRadius: "var(--radius-card)",
              padding: ".75rem 1rem",
              fontSize: 13,
              fontWeight: 500,
              color: "#141414",
              whiteSpace: "pre-wrap",
            }}
          >
            {messageText.trim().length > 0 ? messageText : "متن پاسخ اینجا نمایش داده می‌شود…"}
          </div>
        </div>
        <p style={{ margin: ".35rem 0 0", fontSize: 10, color: "#737373" }}>پاسخ دایرکت</p>
      </Card>

      {submitError ? (
        <div role="alert" style={{ fontSize: 13, color: "var(--color-status-danger)" }}>
          {submitError}
          {entitlementBlocked ? (
            <Link href="/dashboard/billing" style={{ marginRight: ".5rem", color: "var(--color-brand-accent)", fontWeight: 700 }}>
              مشاهده اشتراک
            </Link>
          ) : null}
        </div>
      ) : null}

      <div>
        <Button type="submit" disabled={submitting}>
          {submitting ? "در حال ثبت…" : submitLabel}
        </Button>
      </div>
    </form>
  );
}
