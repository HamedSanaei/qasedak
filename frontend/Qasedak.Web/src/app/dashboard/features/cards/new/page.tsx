"use client";

import { useState } from "react";
import { FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const RESPONSE_TYPES = [
  { value: "card", label: "افزودن کارت", glyph: "▦", ready: true },
  { value: "voice", label: "افزودن صدا", glyph: "◔", ready: false },
  { value: "video", label: "افزودن فیلم", glyph: "▶", ready: false },
  { value: "photo", label: "افزودن عکس", glyph: "▨", ready: false },
  { value: "text", label: "افزودن متن", glyph: "T", ready: false },
];

const DRAFT_KEY = "qasedak.cards.draft";

export default function NewCardPage() {
  const [keywordInput, setKeywordInput] = useState("");
  const [keywords, setKeywords] = useState<string[]>([]);
  const [cards, setCards] = useState<string[]>([]);
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
      setError("پیامی که کاربر می‌فرستد و باعث نمایش ویترین می‌شود را وارد کنید.");
      return;
    }
    if (cards.length === 0) {
      setError("برای شروع «افزودن کارت» را انتخاب کنید.");
      return;
    }
    try {
      window.localStorage.setItem(DRAFT_KEY, JSON.stringify({ keywords, cards }));
    } catch {
      setError("ذخیره پیش‌نویس در این مرورگر ممکن نشد.");
      return;
    }
    setError(null);
    setSaved(true);
  }

  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "ویترین‌ساز", "ایجاد ویترین‌ساز"]} />
      <h1 className={styles.title}>ایجاد ویترین‌ساز</h1>

      <div className={styles.builderGrid}>
        <div style={{ display: "flex", flexDirection: "column", gap: "0.875rem" }}>
          <section className={styles.formCard} aria-label="فعال‌کننده‌ها">
            <div style={{ display: "flex", alignItems: "baseline", gap: "1rem" }}>
              <h2 className={styles.formCardTitle}>فعال‌کننده‌ها</h2>
              <span className={styles.formCardHint}>راهنمایی</span>
            </div>
            <p className={styles.formCardBody}>پیامی که کاربر می‌فرستد و باعث نمایش ویترین می‌شود.</p>
            <div className={styles.fieldRow}>
              <button type="button" className={styles.addButton} onClick={addKeyword}>ثبت</button>
              <input
                className={styles.textInput}
                value={keywordInput}
                onChange={(event) => setKeywordInput(event.target.value)}
                onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); addKeyword(); } }}
                placeholder="مثال: محصولات، قیمت، کاتالوگ"
                aria-label="متن فعال‌کننده ویترین"
              />
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
          </section>

          <section className={styles.formCard} aria-label="پاسخ‌ساز">
            <h2 className={styles.formCardTitle}>پاسخ‌ساز</h2>
            <p className={styles.formCardBody}>اجزای پیام ویترین را اضافه و مرتب کنید.</p>
            <div className={styles.typeRow}>
              {RESPONSE_TYPES.map((type) => (
                <button
                  key={type.value}
                  type="button"
                  className={styles.typeButton}
                  data-active={cards.length > 0 && type.value === "card"}
                  disabled={!type.ready}
                  title={type.ready ? type.label : `${type.label} — به‌زودی`}
                  onClick={() => { setCards((list) => [...list, `کارت ${list.length + 1}`]); setSaved(false); }}
                >
                  <span aria-hidden="true">{type.glyph}</span>
                  {type.label}
                  {type.ready ? null : <span className={styles.typeSoon}>به‌زودی</span>}
                </button>
              ))}
            </div>
            <div className={styles.emptyCanvas}>
              {cards.length === 0 ? (
                <>
                  <span aria-hidden="true" style={{ fontSize: 22, color: "var(--color-brand-accent)" }}>＋</span>
                  <span className={styles.emptyCanvasTitle}>برای شروع «افزودن کارت» را انتخاب کنید</span>
                  <span className={styles.emptyCanvasBody}>هر کارت می‌تواند تصویر، تیتر و دکمه داشته باشد.</span>
                </>
              ) : (
                <div className={styles.chipRow} style={{ marginTop: 0 }}>
                  {cards.map((card, index) => (
                    <span key={`${card}-${index}`} className={styles.chip}>
                      {card}
                      <button
                        type="button"
                        aria-label={`حذف ${card}`}
                        onClick={() => setCards((list) => list.filter((_, i) => i !== index))}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
              )}
            </div>
            {error ? <div role="alert" className={`${styles.notice} ${styles.noticeError}`}>{error}</div> : null}
            {saved ? <div role="status" className={`${styles.notice} ${styles.noticeOk}`}>پیش‌نویس در همین مرورگر ذخیره شد.</div> : null}
            <div className={styles.footerRow}>
              <span className={styles.footerHint}>ابتدا حداقل یک فعال‌کننده و پاسخ اضافه کنید.</span>
              <button type="button" className={styles.saveButton} data-ready={keywords.length > 0 && cards.length > 0} onClick={save}>
                ثبت
              </button>
            </div>
          </section>
        </div>

        <aside className={styles.phoneCard} aria-label="پیش‌نمایش">
          <h2 className={styles.phoneTitle}>پیش‌نمایش</h2>
          <div className={styles.phone}>
            <span className={styles.phoneNotch} aria-hidden="true" />
            <div className={styles.phoneBubble}><span aria-hidden="true">👋</span> ویترین محصولات ما</div>
            <div className={styles.phoneBubble}>
              <span aria-hidden="true" style={{ fontSize: 26 }}>▦</span>
              <div className={styles.footerHint}>{cards.length === 0 ? "هنوز کارتی اضافه نشده" : `${cards.length} کارت اضافه شده است`}</div>
            </div>
          </div>
        </aside>
      </div>
    </main>
  );
}
