"use client";

import { useState } from "react";
import { FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

const DRAFT_KEY = "qasedak.followup.draft";

export default function NewFollowUpPage() {
  const [name, setName] = useState("");
  const [delay, setDelay] = useState("1h");
  const [message, setMessage] = useState("");
  const [enabled, setEnabled] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  function save() {
    if (name.trim().length < 3) {
      setError("نام سناریو حداقل ۳ کاراکتر باشد.");
      return;
    }
    if (message.trim().length === 0) {
      setError("متن پیام پیگیری را وارد کنید.");
      return;
    }
    try {
      window.localStorage.setItem(DRAFT_KEY, JSON.stringify({ name: name.trim(), delay, message, enabled }));
    } catch {
      setError("ذخیره پیش‌نویس در این مرورگر ممکن نشد.");
      return;
    }
    setError(null);
    setSaved(true);
  }

  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "پشتیبان هوشمند", "فالوآپ جدید"]} />
      <h1 className={styles.title}>فالوآپ جدید</h1>

      <section className={styles.formCard} aria-label="فرم فالوآپ">
        <h2 className={styles.formCardTitle}>تنظیمات سناریو</h2>
        <p className={styles.formCardBody}>مشخص کنید چه پیامی، با چه فاصله‌ای پس از تعامل کاربر ارسال شود.</p>
        <div className={styles.fieldRow}>
          <input
            className={styles.textInput}
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="نام سناریو؛ مثال: یادآوری پرداخت"
            aria-label="نام سناریو"
          />
          <select className={styles.selectInput} value={delay} onChange={(event) => setDelay(event.target.value)} aria-label="فاصله ارسال">
            <option value="15m">۱۵ دقیقه بعد</option>
            <option value="1h">۱ ساعت بعد</option>
            <option value="24h">۲۴ ساعت بعد</option>
            <option value="72h">۳ روز بعد</option>
          </select>
        </div>
        <div style={{ marginTop: "0.9rem" }}>
          <label htmlFor="followup-message" className={styles.formCardHint}>متن پیام</label>
          <textarea
            id="followup-message"
            className={styles.textInput}
            style={{ width: "100%", minHeight: 110, marginTop: ".4rem", resize: "vertical" }}
            value={message}
            maxLength={1000}
            onChange={(event) => { setMessage(event.target.value); setSaved(false); }}
            placeholder="متن پیام پیگیری را وارد کنید…"
          />
        </div>
        <div className={styles.toggleRow}>
          <span>سناریو بلافاصله پس از ذخیره فعال باشد.</span>
          <button
            type="button"
            role="switch"
            aria-checked={enabled}
            aria-label="فعال بودن سناریو"
            className={styles.switch}
            data-on={enabled}
            onClick={() => setEnabled((value) => !value)}
          >
            <span className={styles.switchKnob} aria-hidden="true" />
          </button>
        </div>
        {error ? <div role="alert" className={`${styles.notice} ${styles.noticeError}`}>{error}</div> : null}
        {saved ? <div role="status" className={`${styles.notice} ${styles.noticeOk}`}>پیش‌نویس در همین مرورگر ذخیره شد.</div> : null}
        <div className={styles.footerRow}>
          <span className={styles.footerHint}>این پیش‌نویس فقط در همین مرورگر نگه‌داری می‌شود.</span>
          <button type="button" className={styles.saveButton} data-ready={name.trim().length >= 3 && message.trim().length > 0} onClick={save}>
            ثبت
          </button>
        </div>
      </section>
    </main>
  );
}
