import type { InputHTMLAttributes, TextareaHTMLAttributes } from "react";
import styles from "./Primitives.module.css";

export function FormField({ label, hint, error, id, ...props }: InputHTMLAttributes<HTMLInputElement> & { label: string; hint?: string; error?: string; id: string }) {
  const describedBy = [hint ? `${id}-hint` : "", error ? `${id}-error` : ""].filter(Boolean).join(" ") || undefined;
  return (
    <div className={styles.field}>
      <label className={styles.label} htmlFor={id}>{label}</label>
      <input className={styles.input} id={id} aria-invalid={Boolean(error)} aria-describedby={describedBy} {...props} />
      {hint ? <p className={styles.hint} id={`${id}-hint`}>{hint}</p> : null}
      {error ? <p className={styles.error} id={`${id}-error`} role="alert">{error}</p> : null}
    </div>
  );
}

export function TextareaField({ label, hint, error, id, ...props }: TextareaHTMLAttributes<HTMLTextAreaElement> & { label: string; hint?: string; error?: string; id: string }) {
  const describedBy = [hint ? `${id}-hint` : "", error ? `${id}-error` : ""].filter(Boolean).join(" ") || undefined;
  return (
    <div className={styles.field}>
      <label className={styles.label} htmlFor={id}>{label}</label>
      <textarea className={styles.textarea} id={id} aria-invalid={Boolean(error)} aria-describedby={describedBy} {...props} />
      {hint ? <p className={styles.hint} id={`${id}-hint`}>{hint}</p> : null}
      {error ? <p className={styles.error} id={`${id}-error`} role="alert">{error}</p> : null}
    </div>
  );
}
