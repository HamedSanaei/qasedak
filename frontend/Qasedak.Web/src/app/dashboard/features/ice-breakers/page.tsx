"use client";

import { useState } from "react";
import { EducationBanner, FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const DRAFT_KEY = "qasedak.icebreakers.draft";
const MAX_TRIGGERS = 4;

export default function IceBreakersPage() {
  const [phraseInput, setPhraseInput] = useState("");
  const [phrases, setPhrases] = useState<string[]>(["سلام، خوش آمدید", "برای مشاوره اینجا بزن"]);
  const [notice, setNotice] = useState<{ kind: "ok" | "error"; text: string } | null>(null);

  function addPhrase() {
    const value = phraseInput.trim();
    if (!value) return;
    if (phrases.length >= MAX_TRIGGERS) {
      setNotice({ kind: "error", text: `حداکثر ${MAX_TRIGGERS} مورد.` });
      return;
    }
    if (!phrases.includes(value)) setPhrases((list) => [...list, value]);
    setPhraseInput("");
    setNotice(null);
  }

  function save() {
    try {
      window.localStorage.setItem(DRAFT_KEY, JSON.stringify({ phrases }));
    } catch {
      setNotice({ kind: "error", text: "ذخیره پیش‌نویس در این مرورگر ممکن نشد." });
      return;
    }
    setNotice({ kind: "ok", text: "پیش‌نویس در همین مرورگر ذخیره شد." });
  }

  function clear() {
    setPhrases([]);
    try {
      window.localStorage.removeItem(DRAFT_KEY);
    } catch {
      /* نگه‌داری محلی در دسترس نیست؛ وضعیت صفحه همان است */
    }
    setNotice(null);
  }

  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "پیام خوش‌آمدگویی"]} />
      <h1 className={styles.title}>پیام خوش‌آمدگویی</h1>

      <EducationBanner
        title="آموزش پیام خوش‌آمدگویی"
        body="عبارت‌هایی که اولین گفت‌وگوی کاربر با پیج شما را شروع می‌کنند."
        actionLabel="مشاهده آموزش"
      />

      <section className={styles.formCard} aria-label="ویرایش پیام خوش‌آمدگویی" style={{ maxWidth: 860 }}>
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <span className={`${styles.thumb} ${styles.thumbPink}`} aria-hidden="true">✦</span>
          <h2 className={styles.formCardTitle}>پیام خوش‌آمدگویی</h2>
        </div>
        <p className={styles.formCardBody}>وقتی کاربر برای اولین بار به شما پیام می‌دهد، این پیام‌ها برایش نمایش داده می‌شود.</p>

        <div style={{ display: "flex", alignItems: "baseline", gap: "1rem", marginTop: "1rem" }}>
          <h3 className={styles.formCardTitle} style={{ fontSize: 15 }}>فعال‌کننده‌ها</h3>
          <span className={styles.formCardHint}>راهنمایی</span>
          <span className={styles.scopePill} style={{ marginRight: "auto" }}>{phrases.length} از {MAX_TRIGGERS}</span>
        </div>
        <p className={styles.formCardBody}>عبارتی که کاربر می‌فرستد را وارد کنید تا پیام خوش‌آمدگویی فعال شود. حداکثر ۴ مورد.</p>

        <div className={styles.fieldRow}>
          <button type="button" className={styles.addButton} onClick={addPhrase}>
            <span aria-hidden="true">＋</span> افزودن
          </button>
          <input
            className={styles.textInput}
            value={phraseInput}
            onChange={(event) => setPhraseInput(event.target.value)}
            onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); addPhrase(); } }}
            placeholder="مثال: سلام، خوش آمدید"
            aria-label="عبارت خوش‌آمدگویی"
          />
        </div>

        <div className={styles.chipRow}>
          {phrases.map((phrase) => (
            <span key={phrase} className={styles.chip}>
              {phrase}
              <button type="button" aria-label={`حذف ${phrase}`} onClick={() => setPhrases((list) => list.filter((item) => item !== phrase))}>
                ×
              </button>
            </span>
          ))}
        </div>

        <div className={styles.emptyCanvas} style={{ borderStyle: "dashed", minHeight: 90 }}>
          <span className={styles.emptyCanvasBody}>برای افزودن فعال‌کننده بعدی، عبارت را در کادر بالا بنویسید.</span>
        </div>

        <div className={`${styles.notice} ${styles.noticeInfo}`} role="note">
          تا وقتی دکمه ذخیره را نزنید، تغییرات روی اینستاگرام اعمال نمی‌شود.
        </div>

        {notice ? (
          <div role={notice.kind === "error" ? "alert" : "status"} className={`${styles.notice} ${notice.kind === "error" ? styles.noticeError : styles.noticeOk}`}>
            {notice.text}
          </div>
        ) : null}

        <div className={styles.footerRow}>
          <button
            type="button"
            className={styles.deleteButton}
            style={{ flex: "none", paddingInline: "1.4rem", background: "#ffffff", border: "1px solid var(--color-status-danger)" }}
            onClick={clear}
          >
            ⌫ پاکسازی آیس بریکرها
          </button>
          <button type="button" className={styles.saveButton} data-ready={phrases.length > 0} onClick={save}>
            ▣ ذخیره و اعمال
          </button>
        </div>
      </section>
    </main>
  );
}
