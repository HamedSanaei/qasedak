"use client";

import { useState } from "react";
import { EducationBanner, FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const MATCH_OPTIONS = [
  { value: "equals", label: "برابر" },
  { value: "contains", label: "شامل" },
  { value: "any", label: "هر پاسخ" },
];

const RESPONSE_TYPES = [
  { value: "text", label: "افزودن متن", glyph: "T", ready: true },
  { value: "voice", label: "افزودن صدا", glyph: "◔", ready: false },
  { value: "photo", label: "افزودن عکس", glyph: "▨", ready: false },
  { value: "video", label: "افزودن فیلم", glyph: "▶", ready: false },
  { value: "card", label: "افزودن کارت", glyph: "▤", ready: false },
];

const DRAFT_KEY = "qasedak.smart-answer.draft";

export default function NewSmartAnswerPage() {
  const [match, setMatch] = useState("equals");
  const [keywordInput, setKeywordInput] = useState("");
  const [keywords, setKeywords] = useState<string[]>([]);
  const [likeEnabled, setLikeEnabled] = useState(false);
  const [responseType, setResponseType] = useState("text");
  const [message, setMessage] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  function addKeyword() {
    const value = keywordInput.trim();
    if (!value) return;
    if (!keywords.includes(value)) setKeywords((list) => [...list, value]);
    setKeywordInput("");
    setSaved(false);
  }

  function save() {
    if (keywords.length === 0) {
      setError("برای ثبت، حداقل یک فعال‌کننده اضافه کنید.");
      return;
    }
    if (message.trim().length === 0) {
      setError("متن پاسخ را وارد کنید.");
      return;
    }
    if (message.length > 1000) {
      setError("متن پاسخ حداکثر ۱۰۰۰ کاراکتر است.");
      return;
    }
    try {
      window.localStorage.setItem(
        DRAFT_KEY,
        JSON.stringify({ match, keywords, likeEnabled, responseType, message }),
      );
    } catch {
      setError("ذخیره پیش‌نویس در این مرورگر ممکن نشد.");
      return;
    }
    setError(null);
    setSaved(true);
  }

  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "پاسخ هوشمند", "ایجاد پاسخ هوشمند"]} />

      <EducationBanner
        title="نیاز به دیدن آموزش دارید؟"
        body="با یک آموزش کوتاه، سریع‌تر با این بخش آشنا شوید"
        actionLabel="مشاهده آموزش"
        primary
      />

      <section className={styles.formCard} aria-label="فعال‌کننده‌ها">
        <div style={{ display: "flex", alignItems: "baseline", gap: "1rem" }}>
          <h1 className={styles.formCardTitle}>فعال‌کننده‌ها</h1>
          <span className={styles.formCardHint}>راهنمایی</span>
        </div>
        <p className={styles.formCardBody}>پیام‌هایی که کاربر برای شما ارسال می‌کند و باعث فعال شدن پاسخ تنظیم‌شده می‌شود.</p>
        <div className={styles.fieldRow}>
          <button type="button" className={styles.addButton} onClick={addKeyword}>
            <span aria-hidden="true">＋</span> ثبت
          </button>
          <input
            className={styles.textInput}
            value={keywordInput}
            onChange={(event) => setKeywordInput(event.target.value)}
            onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); addKeyword(); } }}
            placeholder="مثال: یک، سلام، ۱"
            aria-label="متن فعال‌کننده"
          />
          <select
            className={styles.selectInput}
            value={match}
            onChange={(event) => setMatch(event.target.value)}
            aria-label="نوع تطبیق"
          >
            {MATCH_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </div>
        {keywords.length > 0 ? (
          <div className={styles.chipRow}>
            {keywords.map((keyword) => (
              <span key={keyword} className={styles.chip}>
                {keyword}
                <button
                  type="button"
                  aria-label={`حذف ${keyword}`}
                  onClick={() => setKeywords((list) => list.filter((item) => item !== keyword))}
                >
                  ×
                </button>
              </span>
            ))}
          </div>
        ) : null}
        <div className={styles.hintStrip}>
          <strong>↗ فعال‌کننده‌ها</strong>
          پیام‌هایی هستند که کاربر برای شما ارسال می‌کند. برای ثبت چند عبارت، پس از هر مورد Enter بزنید.
        </div>
        <div className={styles.toggleRow}>
          <span>با فعال کردن این گزینه، پیام‌های ارسالی مخاطب شما لایک خواهد شد.</span>
          <button
            type="button"
            role="switch"
            aria-checked={likeEnabled}
            aria-label="لایک پیام مخاطب"
            className={styles.switch}
            data-on={likeEnabled}
            onClick={() => setLikeEnabled((value) => !value)}
          >
            <span className={styles.switchKnob} aria-hidden="true" />
          </button>
        </div>
      </section>

      <div className={styles.builderGrid}>
        <section className={styles.formCard} aria-label="ساخت پاسخ">
          <h2 className={styles.formCardTitle}>ساخت پاسخ</h2>
          <p className={styles.formCardBody}>از دکمه‌های زیر برای ساختن پاسخ خود استفاده کنید.</p>
          <div className={styles.typeRow}>
            {RESPONSE_TYPES.map((type) => (
              <button
                key={type.value}
                type="button"
                className={styles.typeButton}
                data-active={responseType === type.value}
                disabled={!type.ready}
                title={type.ready ? type.label : `${type.label} — به‌زودی`}
                onClick={() => setResponseType(type.value)}
              >
                <span aria-hidden="true">{type.glyph}</span>
                {type.label}
                {type.ready ? null : <span className={styles.typeSoon}>به‌زودی</span>}
              </button>
            ))}
          </div>
          <div style={{ marginTop: "0.9rem" }}>
            <label htmlFor="smart-answer-message" className={styles.formCardHint}>متن پاسخ</label>
            <textarea
              id="smart-answer-message"
              className={styles.textInput}
              style={{ width: "100%", minHeight: 120, marginTop: ".4rem", resize: "vertical" }}
              value={message}
              maxLength={1000}
              onChange={(event) => { setMessage(event.target.value); setSaved(false); }}
              placeholder="متن پاسخ را وارد کنید…"
            />
            <div className={styles.footerHint}>{message.length} / ۱۰۰۰</div>
          </div>
          {error ? <div role="alert" className={`${styles.notice} ${styles.noticeError}`}>{error}</div> : null}
          {saved ? <div role="status" className={`${styles.notice} ${styles.noticeOk}`}>پیش‌نویس در همین مرورگر ذخیره شد.</div> : null}
          <div className={styles.footerRow}>
            <span className={styles.footerHint}>ابتدا حداقل یک فعال‌کننده و پاسخ اضافه کنید.</span>
            <button type="button" className={styles.saveButton} data-ready={keywords.length > 0 && message.trim().length > 0} onClick={save}>
              ثبت
            </button>
          </div>
        </section>

        <aside className={styles.phoneCard} aria-label="پیش‌نمایش">
          <h2 className={styles.phoneTitle}>پیش‌نمایش</h2>
          <div className={styles.phone}>
            <span className={styles.phoneNotch} aria-hidden="true" />
            <div className={styles.phoneBubble}><strong>Directam</strong></div>
            {message.trim() ? (
              <div className={styles.phoneBubble}>{message.trim()}</div>
            ) : (
              <div className={`${styles.phoneBubble} ${styles.phoneBubbleMuted}`}>پاسخ‌های شما اینجا نمایش داده می‌شوند</div>
            )}
          </div>
        </aside>
      </div>
    </main>
  );
}
