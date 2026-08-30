import Link from "next/link";
import type { ButtonHTMLAttributes, ReactNode } from "react";
import styles from "./Primitives.module.css";

type ButtonVariant = "primary" | "secondary" | "danger" | "success";
const variantClass: Record<ButtonVariant, string> = {
  primary: styles.buttonPrimary,
  secondary: styles.buttonSecondary,
  danger: styles.buttonDanger,
  success: styles.buttonSuccess,
};

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  fullWidth?: boolean;
  compact?: boolean;
  children: ReactNode;
}

export function Button({ variant = "primary", fullWidth = false, compact = false, className = "", children, ...props }: ButtonProps) {
  const classes = [styles.button, variantClass[variant], fullWidth ? styles.buttonFull : "", compact ? styles.buttonCompact : "", className].filter(Boolean).join(" ");
  return <button className={classes} {...props}>{children}</button>;
}

export function ButtonLink({ href, children, variant = "primary", fullWidth = false, compact = false, className = "" }: { href: string; children: ReactNode; variant?: ButtonVariant; fullWidth?: boolean; compact?: boolean; className?: string }) {
  const classes = [styles.button, variantClass[variant], fullWidth ? styles.buttonFull : "", compact ? styles.buttonCompact : "", className].filter(Boolean).join(" ");
  return <Link href={href} className={classes}>{children}</Link>;
}
