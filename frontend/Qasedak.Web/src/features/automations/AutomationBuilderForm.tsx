"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { Button, Card, SelectField, TextAreaField, TextField } from "../../shared/design/ui";
import type {
  AutomationActionDto,
  AutomationDefinitionDto,
  AutomationTriggerKind,
  RevealActionDto,
} from "../../shared/api/automations";
import { capabilitiesApi } from "../../shared/api/capabilities";
import { mediaApi } from "../../shared/api/media";
import type { AccountCapabilities, InstagramCapabilityName } from "../instagram/capabilities";
import { capabilityAvailable, capabilityExplanation, capabilityState } from "../instagram/capabilities";
import type { MediaPickerItem } from "../instagram/media";
import { AccountSelector } from "../instagram/ui/AccountSelector";
import { useInstagramAccountContext } from "../instagram/useInstagramAccountContext";
import {
  actionAllowed,
  allowedActions,
  AUTOMATION_MAX_MESSAGE_LENGTH,
  BUTTON_TITLE_MAX_CHARACTERS,
  describeAutomationFailure,
  GATE_PROMPT_MAX_CHARACTERS,
  isEntitlementDenial,
  legacyActionLabel,
  validateAutomationName,
  validateFollowUp,
  validatePlainText,
  validatePublicReply,
  validateReveal,
  utf8ByteLength,
} from "./presentation";
import styles from "./AutomationBuilder.module.css";
interface EditorReveal {
  gatePromptText: string;
  postbackButtonTitle: string;
  revealText: string;
  followUrl: string;
  followButtonTitle: string;
  followGateMode: "Disabled" | "EnabledWhenSupported";
}

interface EditorAction {
  key: number;
  kind: string;
  messageText: string;
  delayMinutes: number;
  reveal: EditorReveal;
}

const KNOWN_TRIGGERS = new Set(["CommentCreated", "InboundDirectMessage"]);
const KNOWN_ACTIONS = new Set(["SendDirectMessage", "SendPrivateReply", "DirectMessage", "StartRevealFlow", "SendPublicReply", "ScheduleFollowUp"]);

function blankReveal(): EditorReveal {
  return {
    gatePromptText: "برای ادامه روی دکمه زیر بزنید.",
    postbackButtonTitle: "ادامه",
    revealText: "اطلاعات درخواستی شما آماده است.",
    followUrl: "",
    followButtonTitle: "مشاهده پیج",
    followGateMode: "Disabled",
  };
}

function editorAction(action: AutomationActionDto, key: number): EditorAction {
  const reveal = action.extras?.reveal ?? action.reveal ?? null;
  return {
    key,
    kind: action.kind,
    messageText: action.messageText ?? "",
    delayMinutes: action.extras?.delayMinutes ?? action.delayMinutes ?? 60,
    reveal: reveal ? {
      gatePromptText: reveal.gatePromptText,
      postbackButtonTitle: reveal.postbackButtonTitle,
      revealText: reveal.revealText,
      followUrl: reveal.followUrl ?? "",
      followButtonTitle: reveal.followButtonTitle ?? "",
      followGateMode: reveal.followGateMode,
    } : blankReveal(),
  };
}
function requiredCapability(kind: string): InstagramCapabilityName | null {
  if (kind === "SendPrivateReply") return "PrivateReply";
  if (kind === "SendPublicReply") return "PublicReply";
  if (kind === "StartRevealFlow") return "RevealFlow";
  if (kind === "DirectMessage" || kind === "ScheduleFollowUp") return "Messaging";
  if (kind === "SendDirectMessage") return null;
  return null;
}

function actionLabel(kind: string, trigger: string): string {
  if (kind === "SendDirectMessage") return legacyActionLabel(trigger);
  if (kind === "SendPrivateReply") return "پاسخ خصوصی";
  if (kind === "DirectMessage") return "دایرکت";
  if (kind === "SendPublicReply") return "پاسخ عمومی به کامنت";
  if (kind === "StartRevealFlow") return "جریان نمایش مرحله‌ای";
  if (kind === "ScheduleFollowUp") return "پیگیری با تأخیر";
  return `پیکربندی پشتیبانی‌نشده: ${kind}`;
}

export interface AutomationBuilderFormProps {
  initialName?: string;
  initialDefinition?: AutomationDefinitionDto | null;
  initialChannelAccountId?: string | null;
  lockAccount?: boolean;
  submitLabel: string;
  onSubmit: (
    name: string,
    channelAccountId: string,
    definition: AutomationDefinitionDto,
  ) => Promise<{ ok: true } | { ok: false; code: string | null }>;
}

export function AutomationBuilderForm({
  initialName = "",
  initialDefinition = null,
  initialChannelAccountId = null,
  lockAccount = false,
  submitLabel,
  onSubmit,
}: AutomationBuilderFormProps) {
  const accounts = useInstagramAccountContext();
  const nextActionKey = useRef((initialDefinition?.actions.length ?? 0) + 1);
  const initialTrigger = initialDefinition?.triggerKind ?? "CommentCreated";
  const [name, setName] = useState(initialName);
  const [trigger, setTrigger] = useState<string>(initialTrigger);
  const [textMatchMode, setTextMatchMode] = useState(initialDefinition?.textMatchMode ?? (initialDefinition?.keywordFilters?.length ? "Keywords" : "EveryEvent"));
  const [wholeWord, setWholeWord] = useState(initialDefinition?.wholeWord ?? false);
  const [sourceScope, setSourceScope] = useState(initialDefinition?.sourceScope ?? "AnySource");
  const [sourceMediaId, setSourceMediaId] = useState<string | null>(initialDefinition?.sourceMediaId ?? null);
  const [keywordText, setKeywordText] = useState((initialDefinition?.keywordFilters ?? []).join("، "));
  const [actions, setActions] = useState<EditorAction[]>(() =>
    initialDefinition?.actions?.length
      ? initialDefinition.actions.map((action, index) => editorAction(action, index + 1))
      : [{ key: 1, kind: "SendPrivateReply", messageText: "", delayMinutes: 60, reveal: blankReveal() }],
  );
  const [capabilities, setCapabilities] = useState<AccountCapabilities | null>(null);
  const [mediaItems, setMediaItems] = useState<MediaPickerItem[]>([]);
  const [loadingCapabilities, setLoadingCapabilities] = useState(false);
  const [loadingMedia, setLoadingMedia] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [entitlementBlocked, setEntitlementBlocked] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const selectedAccountId = lockAccount ? initialChannelAccountId : accounts.selectedAccountId;
  const selectAccount = accounts.selectAccount;
  const accountContextLoading = accounts.loading;
  const unsupportedDefinition = !KNOWN_TRIGGERS.has(trigger) || actions.some((action) => !KNOWN_ACTIONS.has(action.kind));
  const triggerKind = KNOWN_TRIGGERS.has(trigger) ? trigger as AutomationTriggerKind : null;
  const keywords = keywordText.split(/[،,\n]/).map((value) => value.trim()).filter(Boolean);

  useEffect(() => {
    if (!lockAccount || !initialChannelAccountId || accountContextLoading) return;
    const timer = window.setTimeout(() => selectAccount(initialChannelAccountId), 0);
    return () => window.clearTimeout(timer);
  }, [lockAccount, initialChannelAccountId, accountContextLoading, selectAccount]);

  useEffect(() => {
    let cancelled = false;
    const timer = window.setTimeout(() => {
      setCapabilities(null);
      setMediaItems([]);
      setLoadingCapabilities(false);
      if (!selectedAccountId || !accounts.accessToken || !accounts.workspaceId) return;
      setLoadingCapabilities(true);
      void capabilitiesApi().get(accounts.accessToken, accounts.workspaceId, selectedAccountId)
        .then((value) => { if (!cancelled) setCapabilities(value); })
        .catch(() => { if (!cancelled) setCapabilities(null); })
        .finally(() => { if (!cancelled) setLoadingCapabilities(false); });
    }, 0);
    return () => { cancelled = true; window.clearTimeout(timer); };
  }, [selectedAccountId, accounts.accessToken, accounts.workspaceId]);
  useEffect(() => {
    let cancelled = false;
    const timer = window.setTimeout(() => {
      setMediaItems([]);
      setLoadingMedia(false);
      if (sourceScope !== "SpecificSource" || !selectedAccountId || !accounts.accessToken || !accounts.workspaceId) return;
      if (!capabilityAvailable(capabilities, "MediaCatalog")) return;
      setLoadingMedia(true);
      void mediaApi().listMedia(accounts.accessToken, accounts.workspaceId, selectedAccountId, { limit: 24 })
        .then((page) => { if (!cancelled) setMediaItems(page.items); })
        .catch(() => { if (!cancelled) setMediaItems([]); })
        .finally(() => { if (!cancelled) setLoadingMedia(false); });
    }, 0);
    return () => { cancelled = true; window.clearTimeout(timer); };
  }, [sourceScope, selectedAccountId, accounts.accessToken, accounts.workspaceId, capabilities]);

  useEffect(() => {
    if (lockAccount) return;
    const timer = window.setTimeout(() => setSourceMediaId(null), 0);
    return () => window.clearTimeout(timer);
  }, [selectedAccountId, lockAccount]);

  function changeTrigger(value: string) {
    setTrigger(value);
    if (!KNOWN_TRIGGERS.has(value)) return;
    const next = value as AutomationTriggerKind;
    setSourceScope("AnySource");
    setSourceMediaId(null);
    setActions([{
      key: ++nextActionKey.current,
      kind: next === "CommentCreated" ? "SendPrivateReply" : "DirectMessage",
      messageText: "",
      delayMinutes: 60,
      reveal: blankReveal(),
    }]);
  }

  function patchAction(key: number, patch: Partial<EditorAction>) {
    setActions((current) => current.map((action) => action.key === key ? { ...action, ...patch } : action));
  }

  function patchReveal(key: number, patch: Partial<EditorReveal>) {
    setActions((current) => current.map((action) => action.key === key ? { ...action, reveal: { ...action.reveal, ...patch } } : action));
  }
  function actionCapability(kind: string): InstagramCapabilityName | null {
    if (kind === "SendDirectMessage") return trigger === "CommentCreated" ? "PrivateReply" : "Messaging";
    return requiredCapability(kind);
  }

  function validateForm(): string | null {
    const nameError = validateAutomationName(name);
    if (nameError) return nameError;
    if (!selectedAccountId) return "automation.accountInvalid";
    if (lockAccount && initialChannelAccountId && !accounts.accounts.some((item) => item.accountId === initialChannelAccountId)) return "automation.accountInvalid";
    if (!triggerKind) return "automation.triggerKindInvalid";

    const triggerCapability: InstagramCapabilityName = triggerKind === "CommentCreated" ? "CommentAutomation" : "Messaging";
    if (!capabilityAvailable(capabilities, triggerCapability)) return "automation.capabilityUnavailable";
    if (textMatchMode === "Keywords" && keywords.length === 0) return "automation.keywordRequired";
    if (wholeWord && textMatchMode !== "Keywords") return "automation.wholeWordRequiresKeywords";
    if (triggerKind === "InboundDirectMessage" && sourceScope !== "AnySource") return "automation.dmSourceScopeNotApplicable";
    if (triggerKind === "CommentCreated" && sourceScope === "SpecificSource" && !sourceMediaId) return "automation.sourceMediaIdRequired";
    if (actions.length < 1) return "automation.actionRequired";
    if (actions.length > 5) return "automation.tooManyActions";

    let privateConsumers = 0;
    for (const action of actions) {
      if (!KNOWN_ACTIONS.has(action.kind)) return "automation.actionKindInvalid";
      if (!actionAllowed(triggerKind, action.kind)) return "automation.actionNotAllowedForTrigger";
      const required = actionCapability(action.kind);
      if (required && !capabilityAvailable(capabilities, required)) return "automation.capabilityUnavailable";
      if (triggerKind === "CommentCreated" && ["SendDirectMessage", "SendPrivateReply", "StartRevealFlow"].includes(action.kind)) privateConsumers += 1;

      const error = action.kind === "SendPublicReply"
        ? validatePublicReply(action.messageText)
        : action.kind === "StartRevealFlow"
          ? validateReveal({ ...action.reveal, openingText: action.messageText })
          : action.kind === "ScheduleFollowUp"
            ? validateFollowUp(action.messageText, action.delayMinutes)
            : validatePlainText(action.messageText);
      if (error) return error;
    }
    if (privateConsumers > 1) return "automation.conflictingPrivateReplyEffects";
    return null;
  }
  function buildDefinition(): AutomationDefinitionDto {
    if (!triggerKind) throw new Error("unsupported trigger");
    return {
      triggerKind,
      keywordFilters: textMatchMode === "Keywords" ? keywords : [],
      textMatchMode,
      wholeWord: textMatchMode === "Keywords" ? wholeWord : false,
      sourceScope: triggerKind === "CommentCreated" ? sourceScope : "AnySource",
      sourceMediaId: triggerKind === "CommentCreated" && sourceScope === "SpecificSource" ? sourceMediaId : null,
      conditions: [],
      actions: actions.map((action) => {
        const dto: AutomationActionDto = { kind: action.kind, messageText: action.messageText };
        if (action.kind === "ScheduleFollowUp") dto.delayMinutes = action.delayMinutes;
        if (action.kind === "StartRevealFlow") {
          const reveal: RevealActionDto = {
            gatePromptText: action.reveal.gatePromptText,
            postbackButtonTitle: action.reveal.postbackButtonTitle,
            revealText: action.reveal.revealText,
            followUrl: action.reveal.followUrl.trim() || null,
            followButtonTitle: action.reveal.followButtonTitle,
            followGateMode: action.reveal.followGateMode,
          };
          dto.reveal = reveal;
        }
        return dto;
      }),
    };
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitError(null);
    setEntitlementBlocked(false);
    const error = unsupportedDefinition ? "automation.unsupportedConfiguration" : validateForm();
    setFieldError(error ? describeAutomationFailure(error) : null);
    if (error || !selectedAccountId) return;

    setSubmitting(true);
    try {
      const result = await onSubmit(name.trim(), selectedAccountId, buildDefinition());
      if (!result.ok) {
        setSubmitError(describeAutomationFailure(result.code));
        setEntitlementBlocked(isEntitlementDenial(result.code));
      }
    } finally {
      setSubmitting(false);
    }
  }

  function addAction() {
    if (!triggerKind || actions.length >= 5) return;
    const candidate = allowedActions(triggerKind).find((kind) => {
      const required = requiredCapability(kind);
      return !required || capabilityAvailable(capabilities, required);
    });
    if (!candidate) return;
    setActions((current) => [...current, { key: ++nextActionKey.current, kind: candidate, messageText: "", delayMinutes: 60, reveal: blankReveal() }]);
  }
  return (
    <form className={styles.form} onSubmit={handleSubmit} noValidate>
      <Card className={styles.section}>
        <h2 className={styles.sectionTitle}>مشخصات اتوماسیون</h2>
        <TextField id="automation-name" label="نام اتوماسیون" value={name} onChange={(event) => setName(event.target.value)} maxLength={200} error={null} placeholder="مثلاً ارسال قیمت" />
      </Card>

      <Card className={styles.section}>
        <h2 className={styles.sectionTitle}>حساب اینستاگرام</h2>
        <p className={styles.sectionCopy}>{lockAccount ? "حساب اتوماسیون پس از ساخت تغییر نمی‌کند." : "هر اتوماسیون به یک ConnectedAccountId دقیق متصل می‌شود."}</p>
        <AccountSelector accounts={accounts.accounts} selectedAccountId={selectedAccountId} onChange={lockAccount ? () => undefined : accounts.selectAccount} disabled={lockAccount || accounts.loading} />
        {lockAccount && initialChannelAccountId && !accounts.loading && !accounts.accounts.some((item) => item.accountId === initialChannelAccountId) ? <div className={styles.error} role="alert">حساب متصل این اتوماسیون در ورک‌اسپیس فعلی پیدا نشد؛ ذخیره غیرفعال است.</div> : null}
        {loadingCapabilities ? <div className={styles.hint} role="status">در حال دریافت قابلیت‌های محلی حساب…</div> : null}
      </Card>

      <Card className={styles.section}>
        <h2 className={styles.sectionTitle}>رویداد و منبع</h2>
        <div className={styles.twoCol}>
          <SelectField id="automation-trigger" label="رویداد" value={trigger} onChange={(event) => changeTrigger(event.target.value)} options={[
            { value: "CommentCreated", label: "کامنت جدید" },
            { value: "InboundDirectMessage", label: "دایرکت ورودی" },
            ...(!KNOWN_TRIGGERS.has(trigger) ? [{ value: trigger, label: `پشتیبانی‌نشده: ${trigger}` }] : []),
          ]} />
          <SelectField id="automation-match" label="حالت تطبیق متن" value={textMatchMode} onChange={(event) => { setTextMatchMode(event.target.value); if (event.target.value !== "Keywords") setWholeWord(false); }} options={[
            { value: "EveryEvent", label: "هر رویداد" },
            { value: "Keywords", label: "کلمات کلیدی (ANY)" },
          ]} />
        </div>
        {trigger === "CommentCreated" ? (
          <div className={styles.twoCol}>
            <SelectField id="automation-source" label="منبع کامنت" value={sourceScope} onChange={(event) => { setSourceScope(event.target.value); if (event.target.value !== "SpecificSource") setSourceMediaId(null); }} options={[
              { value: "AnySource", label: "همه رسانه‌ها" },
              { value: "SpecificSource", label: "رسانه مشخص" },
            ]} />
            {textMatchMode === "Keywords" ? <TextField id="automation-keywords" label="کلمات کلیدی" value={keywordText} onChange={(event) => setKeywordText(event.target.value)} placeholder="قیمت، موجودی، سفارش" /> : <div className={styles.hint}>در حالت «هر رویداد»، لیست کلمه ارسال نمی‌شود.</div>}
          </div>
        ) : textMatchMode === "Keywords" ? <TextField id="automation-keywords" label="کلمات کلیدی پیام" value={keywordText} onChange={(event) => setKeywordText(event.target.value)} placeholder="قیمت، موجودی" /> : null}

        {textMatchMode === "Keywords" ? <label className={styles.row}><input type="checkbox" checked={wholeWord} onChange={(event) => setWholeWord(event.target.checked)} /> تطبیق کلمه کامل (WholeWord)؛ در غیر این صورت Substring</label> : null}
        <p className={styles.hint}>ANY keyword matches. Regex و AI matching در قرارداد فعلی وجود ندارند.</p>

        {trigger === "CommentCreated" && sourceScope === "SpecificSource" ? (
          <div className={styles.section}>
            <h3 className={styles.actionTitle}>رسانه مشخص</h3>
            {!capabilityAvailable(capabilities, "MediaCatalog") ? <div className={styles.warning}>{capabilityExplanation(capabilityState(capabilities, "MediaCatalog"))}</div> : null}
            {loadingMedia ? <div className={styles.hint} role="status">در حال دریافت رسانه‌ها…</div> : null}
            {capabilityAvailable(capabilities, "MediaCatalog") && !loadingMedia && mediaItems.length === 0 ? <div className={styles.hint}>رسانه‌ای برای انتخاب موجود نیست.</div> : null}
            <div className={styles.mediaGrid}>{mediaItems.map((item) => <button key={item.mediaId} type="button" className={`${styles.media} ${sourceMediaId === item.mediaId ? styles.mediaSelected : ""}`} aria-pressed={sourceMediaId === item.mediaId} onClick={() => setSourceMediaId(item.mediaId)}><span className={styles.mediaTitle}>{item.caption?.trim() || "بدون کپشن"}</span><span className={styles.mediaMeta}>{item.kind} · {item.timestampUtc ? new Date(item.timestampUtc).toLocaleDateString("fa-IR") : "زمان نامشخص"}</span></button>)}</div>
          </div>
        ) : null}
      </Card>
      <Card className={styles.section}>
        <div className={styles.actionHeader}>
          <div><h2 className={styles.sectionTitle}>عمل‌ها</h2><p className={styles.sectionCopy}>حداکثر پنج عمل؛ فقط ترکیب‌های سازگار با trigger فعلی قابل انتخاب‌اند.</p></div>
          <Button type="button" variant="secondary" size="small" disabled={!triggerKind || actions.length >= 5 || loadingCapabilities} onClick={addAction}>＋ افزودن عمل</Button>
        </div>

        {actions.map((action, index) => {
          const required = actionCapability(action.kind);
          const requiredState = required ? capabilityState(capabilities, required) : "Available";
          const options = triggerKind ? allowedActions(triggerKind) : [];
          const counter = action.kind === "SendPublicReply"
            ? `${action.messageText.length} / 1000 کاراکتر`
            : `${utf8ByteLength(action.messageText)} / ${AUTOMATION_MAX_MESSAGE_LENGTH} بایت UTF-8`;
          return (
            <section key={action.key} className={styles.actionCard} aria-labelledby={`action-${action.key}-heading`}>
              <div className={styles.actionHeader}>
                <h3 id={`action-${action.key}-heading`} className={styles.actionTitle}>عمل {new Intl.NumberFormat("fa-IR").format(index + 1)}</h3>
                <Button type="button" variant="outline" size="small" disabled={actions.length === 1} onClick={() => setActions((current) => current.filter((item) => item.key !== action.key))}>حذف</Button>
              </div>
              <label>
                <span className={styles.actionTitle}>نوع عمل</span>
                <select className={styles.control} value={action.kind} onChange={(event) => patchAction(action.key, { kind: event.target.value })}>
                  {action.kind === "SendDirectMessage" ? <option value="SendDirectMessage" disabled>{legacyActionLabel(trigger)}</option> : null}
                  {!KNOWN_ACTIONS.has(action.kind) ? <option value={action.kind} disabled>پشتیبانی‌نشده: {action.kind}</option> : null}
                  {options.map((kind) => {
                    const cap = requiredCapability(kind);
                    const disabled = Boolean(cap && !capabilityAvailable(capabilities, cap));
                    return <option key={kind} value={kind} disabled={disabled}>{actionLabel(kind, trigger)}{disabled ? " — در دسترس نیست" : ""}</option>;
                  })}
                </select>
              </label>
              {required && requiredState !== "Available" ? <div className={styles.warning}>{capabilityExplanation(requiredState)}</div> : null}
              {action.kind === "SendDirectMessage" ? <div className={styles.warning}>این مقدار legacy است: روی Comment رفتار واقعی آن «Private Reply» و روی Inbound DM رفتار واقعی آن «Direct Message» است. تا وقتی نوع عمل را صریحاً عوض نکنید، مقدار ذخیره‌شده بازنویسی نمی‌شود.</div> : null}
              <TextAreaField
                id={`action-${action.key}-message`}
                label={action.kind === "StartRevealFlow" ? "متن آغازین Private Reply" : action.kind === "SendPublicReply" ? "متن پاسخ عمومی" : action.kind === "ScheduleFollowUp" ? "متن پیگیری" : "متن پیام"}
                value={action.messageText}
                onChange={(event) => patchAction(action.key, { messageText: event.target.value })}
                rows={4}
                maxLength={1000}
                counter={counter}
              />

              {action.kind === "ScheduleFollowUp" ? (
                <>
                  <TextField id={`action-${action.key}-delay`} label="تأخیر (دقیقه)" type="number" min={1} max={10080} value={String(action.delayMinutes)} onChange={(event) => patchAction(action.key, { delayMinutes: Number(event.target.value) })} />
                  <div className={styles.warning}>ارسال تضمین‌شده نیست؛ هنگام اجرا به در دسترس بودن حساب، شرکت‌کننده و پنجره مجاز پیام‌رسانی Meta وابسته است.</div>
                </>
              ) : null}

              {action.kind === "StartRevealFlow" ? (
                <div className={styles.section}>
                  <div className={styles.warning}>جریان واقعی: کامنت → Private Reply متنی → پیام کاربر → پیام دایرکت دروازه → postback → در صورت پشتیبانی بررسی Follow → متن Reveal. دکمه داخل Private Reply اول قرار نمی‌گیرد.</div>
                  <TextAreaField id={`action-${action.key}-gate`} label="متن پیام دروازه" value={action.reveal.gatePromptText} onChange={(event) => patchReveal(action.key, { gatePromptText: event.target.value })} maxLength={GATE_PROMPT_MAX_CHARACTERS} rows={3} counter={`${action.reveal.gatePromptText.length} / ${GATE_PROMPT_MAX_CHARACTERS} کاراکتر`} />
                  <div className={styles.twoCol}>
                    <TextField id={`action-${action.key}-postback-title`} label="عنوان دکمه ادامه" value={action.reveal.postbackButtonTitle} onChange={(event) => patchReveal(action.key, { postbackButtonTitle: event.target.value })} maxLength={BUTTON_TITLE_MAX_CHARACTERS} counter={`${action.reveal.postbackButtonTitle.length} / ${BUTTON_TITLE_MAX_CHARACTERS}`} />
                    <TextField id={`action-${action.key}-follow-title`} label="عنوان دکمه پیج" value={action.reveal.followButtonTitle} onChange={(event) => patchReveal(action.key, { followButtonTitle: event.target.value })} maxLength={BUTTON_TITLE_MAX_CHARACTERS} counter={`${action.reveal.followButtonTitle.length} / ${BUTTON_TITLE_MAX_CHARACTERS}`} />
                  </div>
                  <TextField id={`action-${action.key}-follow-url`} label="URL پیج (اختیاری)" type="url" value={action.reveal.followUrl} onChange={(event) => patchReveal(action.key, { followUrl: event.target.value })} maxLength={2000} placeholder="https://instagram.com/..." />
                  <TextAreaField id={`action-${action.key}-reveal`} label="متن نهایی Reveal" value={action.reveal.revealText} onChange={(event) => patchReveal(action.key, { revealText: event.target.value })} rows={4} maxLength={1000} counter={`${utf8ByteLength(action.reveal.revealText)} / 1000 بایت UTF-8`} />
                  <div className={styles.warning}>
                    Follow Gate: {capabilityExplanation(capabilityState(capabilities, "FollowGate"))}
                    {action.reveal.followGateMode === "EnabledWhenSupported" ? " مقدار ذخیره‌شده قدیمی EnabledWhenSupported بدون بازنویسی حفظ می‌شود؛ کنترل فعال جدید ارائه نمی‌شود." : " برای تعریف جدید غیرفعال است."}
                  </div>
                </div>
              ) : null}
            </section>
          );
        })}
      </Card>

      {unsupportedDefinition ? <div className={styles.warning} role="alert">تعریف شامل enum ناشناخته/آینده است. قاصدک آن را حدس نمی‌زند؛ برای ذخیره باید نوع پشتیبانی‌شده را صریحاً انتخاب کنید.</div> : null}
      {fieldError ? <div className={styles.error} role="alert">{fieldError}</div> : null}
      {submitError ? <div className={styles.error} role="alert">{submitError}{entitlementBlocked ? <Link href="/dashboard/billing" style={{ marginRight: 8 }}>مشاهده اشتراک</Link> : null}</div> : null}
      <div className={styles.footer}>
        <Button type="submit" disabled={submitting || loadingCapabilities || unsupportedDefinition || !selectedAccountId}>{submitting ? "در حال ذخیره…" : submitLabel}</Button>
        <span className={styles.hint}>Private/Direct/Reveal/Follow-up: سقف ۱۰۰۰ بایت UTF-8؛ Public Reply: سقف ۱۰۰۰ کاراکتر.</span>
      </div>
    </form>
  );
}
