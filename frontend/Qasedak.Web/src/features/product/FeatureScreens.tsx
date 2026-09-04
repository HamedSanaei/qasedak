import Link from "next/link";
import styles from "./FeatureScreens.module.css";

export function FeatureCrumb({ trail }: { trail: string[] }) {
  return (
    <nav aria-label="مسیر" className={styles.crumb}>
      {trail.map((part, index) => (
        <span key={part}>
          {index > 0 ? <span aria-hidden="true"> / </span> : null}
          {index === trail.length - 1 ? (
            <span className={styles.crumbCurrent}>{part}</span>
          ) : (
            <span>{part}</span>
          )}
        </span>
      ))}
    </nav>
  );
}

export function EducationBanner({
  title,
  body,
  actionLabel,
  actionHref,
  primary,
}: {
  title: string;
  body: string;
  actionLabel: string;
  actionHref?: string;
  primary?: boolean;
}) {
  return (
    <section className={styles.eduBanner} aria-label={title}>
      <span className={styles.eduChip} aria-hidden="true">؟</span>
      <span className={styles.eduText}>
        <span className={styles.eduTitle}>{title}</span>
        <span className={styles.eduBody}>{body}</span>
      </span>
      {actionHref ? (
        <Link
          className={`${styles.eduButton} ${primary ? styles.eduButtonPrimary : ""}`}
          href={actionHref}
        >
          {actionLabel}
        </Link>
      ) : (
        <button
          type="button"
          className={`${styles.eduButton} ${primary ? styles.eduButtonPrimary : ""}`}
        >
          {actionLabel}
        </button>
      )}
    </section>
  );
}
