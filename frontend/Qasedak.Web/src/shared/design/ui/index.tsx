/*
 * Visual primitives synchronized from the canonical Penpot file (M08-001).
 * Presentation only — see ./ui.module.css header and docs/design/PENPOT-SYNC.md.
 */
import type {
  ButtonHTMLAttributes,
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from "react";
import styles from "./ui.module.css";

export type ButtonVariant = "primary" | "secondary" | "outline" | "danger";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: "medium" | "small";
}

export function Button({ variant = "primary", size = "medium", className, ...rest }: ButtonProps) {
  const classes = [styles.button, styles[variant], size === "small" ? styles.small : "", className ?? ""]
    .filter(Boolean)
    .join(" ");
  return <button type={rest.type ?? "button"} className={classes} {...rest} />;
}

export function Card({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={[styles.card, className ?? ""].filter(Boolean).join(" ")}>{children}</div>;
}

interface FieldShellProps {
  id: string;
  label: string;
  error?: string | null;
  counter?: string;
  children: ReactNode;
}

function FieldShell({ id, label, error, counter, children }: FieldShellProps) {
  return (
    <div>
      <label className={styles.fieldLabel} htmlFor={id}>
        {label}
      </label>
      {children}
      {counter ? <div className={styles.counter}>{counter}</div> : null}
      {error ? (
        <div className={styles.fieldError} role="alert">
          {error}
        </div>
      ) : null}
    </div>
  );
}

export interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "id"> {
  id: string;
  label: string;
  error?: string | null;
  counter?: string;
}

export function TextField({ label, error, counter, ...rest }: TextFieldProps) {
  return (
    <FieldShell id={rest.id} label={label} error={error} counter={counter}>
      <input
        className={styles.input}
        aria-invalid={error ? true : undefined}
        {...rest}
      />
    </FieldShell>
  );
}

export interface TextAreaFieldProps extends Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, "id"> {
  id: string;
  label: string;
  error?: string | null;
  counter?: string;
}

export function TextAreaField({ label, error, counter, ...rest }: TextAreaFieldProps) {
  return (
    <FieldShell id={rest.id} label={label} error={error} counter={counter}>
      <textarea
        className={styles.textarea}
        aria-invalid={error ? true : undefined}
        maxLength={2000}
        {...rest}
      />
    </FieldShell>
  );
}

export interface SelectFieldProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, "id"> {
  id: string;
  label: string;
  options: readonly { value: string; label: string }[];
  error?: string | null;
}

export function SelectField({ id, label, options, error, className, ...rest }: SelectFieldProps) {
  return (
    <FieldShell id={id} label={label} error={error}>
      <select id={id} className={[styles.select, className ?? ""].filter(Boolean).join(" ")} {...rest}>
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </FieldShell>
  );
}

export type PillTone = "success" | "warning" | "danger" | "info" | "neutral";

const pillToneClass: Record<PillTone, string> = {
  success: styles.successPill,
  warning: styles.warningPill,
  danger: styles.dangerPill,
  info: styles.infoPill,
  neutral: styles.neutralPill,
};

export function StatusPill({ tone, children }: { tone: PillTone; children: ReactNode }) {
  return <span className={`${styles.pill} ${pillToneClass[tone]}`}>{children}</span>;
}

export function PageHeader({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <header style={{ marginBottom: "1.5rem" }}>
      <h1 className={styles.pageHeaderTitle}>{title}</h1>
      {subtitle ? <p className={styles.pageHeaderSub}>{subtitle}</p> : null}
    </header>
  );
}
