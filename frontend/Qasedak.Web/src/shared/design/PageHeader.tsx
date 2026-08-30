import type { ReactNode } from "react";
import styles from "./PageHeader.module.css";

export function PageHeader({ title, description, eyebrow, actions }: { title: string; description?: string; eyebrow?: string; actions?: ReactNode }) {
  return <header className={styles.header}><div className={styles.copy}>{eyebrow ? <p className={styles.eyebrow}>{eyebrow}</p> : null}<h1 className={styles.title}>{title}</h1>{description ? <p className={styles.description}>{description}</p> : null}</div>{actions ? <div className={styles.actions}>{actions}</div> : null}</header>;
}
