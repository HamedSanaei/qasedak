"use client";

import { useState } from "react";
import { FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const DRAFT_KEY = "qasedak.formmaker.draft";

export default function NewFormMakerPage() {
  const [title, setTitle] = useState("");
  const [questionInput, setQuestionInput] = useState("");
  const [questions, setQuestions] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  function addQuestion() {
    const value = questionInput.trim();
    if (!value) return;
    if (!questions.includes(value)) setQuestions((list) => [...list, value]);
    setQuestionInput("");
    setSaved(false);
  }

  function save() {
    if (title.trim().length < 3) {
      setError("عنوان فرم حداقل ۳ کاراکتر باشد.");
      return;
    }
    if (questions.length === 0) {
      setError("حداقل یک سوال اضافه کنید.");
      return;
    }
    try {
      window.localStorage.setItem(DRAFT_KEY, JSON.stringify({ title: title.trim(), questions }));
    } catch {
      setError("ذخیره پیش‌نویس در این مرورگر ممکن نشد.");
      return;
    }
    setError(null);
    setSaved(true);
  }

  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "فرم‌ساز", "فرم جدید"]} />
      <h1 className={styles.title}>فرم جدید</h1>

      <section className={styles.formCard} aria-label="ساخت فرم">
        <h2 className={styles.formCardTitle}>مشخصات فرم</h2>
        <p className={styles.formCardBody}>عنوان فرم و سوال‌هایی که مرحله‌ای از کاربر پرسیده می‌شود را وارد کنید.</p>
        <div className={styles.fieldRow}>
          <input
            className={styles.textInput}
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            placeholder="عنوان فرم؛ مثال: درخواست مشاوره"
            aria-label="عنوان فرم"
          />
        </div>
        <div className={styles.fieldRow}>
          <button type="button" className={styles.addButton} onClick={addQuestion}>
            <span aria-hidden="true">＋</span> افزودن سوال
          </button>
          <input
            className={styles.textInput}
            value={questionInput}
            onChange={(event) => setQuestionInput(event.target.value)}
            onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); addQuestion(); } }}
            placeholder="متن سوال را وارد کنید"
            aria-label="متن سوال"
          />
        </div>
        {questions.length > 0 ? (
          <div className={styles.chipRow}>
            {questions.map((question, index) => (
              <span key={`${question}-${index}`} className={styles.chip}>
                {index + 1}. {question}
                <button
                  type="button"
                  aria-label={`حذف ${question}`}
                  onClick={() => setQuestions((list) => list.filter((_, i) => i !== index))}
                >
                  ×
                </button>
              </span>
            ))}
          </div>
        ) : null}
        {error ? <div role="alert" className={`${styles.notice} ${styles.noticeError}`}>{error}</div> : null}
        {saved ? <div role="status" className={`${styles.notice} ${styles.noticeOk}`}>پیش‌نویس در همین مرورگر ذخیره شد.</div> : null}
        <div className={styles.footerRow}>
          <span className={styles.footerHint}>این پیش‌نویس فقط در همین مرورگر نگه‌داری می‌شود.</span>
          <button type="button" className={styles.saveButton} data-ready={title.trim().length >= 3 && questions.length > 0} onClick={save}>
            ثبت
          </button>
        </div>
      </section>
    </main>
  );
}
