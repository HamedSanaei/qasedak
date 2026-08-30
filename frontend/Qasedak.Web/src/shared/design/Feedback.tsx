import type { CSSProperties, ReactNode } from "react";
import styles from "./Primitives.module.css";

export function Card({ children, padded = true, className = "" }: { children: ReactNode; padded?: boolean; className?: string }) {
  return <section className={[styles.card, padded ? styles.cardPadded : "", className].filter(Boolean).join(" ")}>{children}</section>;
}

export function StatusAlert({ title, children, tone = "info" }: { title: string; children?: ReactNode; tone?: "info" | "success" | "warning" | "danger" }) {
  const toneClass = { info: styles.alertInfo, success: styles.alertSuccess, warning: styles.alertWarning, danger: styles.alertDanger }[tone];
  return <div className={`${styles.alert} ${toneClass}`} role={tone === "danger" ? "alert" : "status"}><strong>{title}</strong>{children ? <p>{children}</p> : null}</div>;
}

export function StatusBadge({ children, tone = "neutral" }: { children: ReactNode; tone?: "neutral" | "success" | "warning" | "danger" | "info" }) {
  const toneClass = { neutral: styles.badgeNeutral, success: styles.badgeSuccess, warning: styles.badgeWarning, danger: styles.badgeDanger, info: styles.badgeInfo }[tone];
  return <span className={`${styles.badge} ${toneClass}`}>{children}</span>;
}

export function Skeleton({ width = "100%", height = 16 }: { width?: CSSProperties["width"]; height?: number }) {
  return <span className={styles.skeleton} aria-hidden="true" style={{ width, height, display: "block" }} />;
}

export function EmptyState({ icon = "ق", title, description, action }: { icon?: ReactNode; title: string; description: string; action?: ReactNode }) {
  return <div className={styles.emptyState}><div className={styles.emptyStateInner}><div className={styles.emptyStateIcon} aria-hidden="true">{icon}</div><h2>{title}</h2><p>{description}</p>{action}</div></div>;
}
