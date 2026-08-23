/*
 * Sidebar — synchronized from Penpot board "Navigation / Sidebar"
 * (page "Directam — Global Navigation Components", board
 * f5bf3c2c-b970-8002-8008-8752c6768b24, component
 * f5bf3c2c-b970-8002-8008-8752c87448ee).
 *
 * Visual layer only (Penpot-owned per docs/design/PENPOT-SYNC.md): colors, typography,
 * spacing and geometry mirror the inspected design. Navigation targets and active-item
 * state are application-owned props — re-sync must never overwrite them.
 */
import Link from "next/link";
import styles from "./Sidebar.module.css";

export interface SidebarNavItem {
  /** Visible label, verbatim from the Penpot design. */
  label: string;
  /** Application-owned route target. */
  href: string;
}

export interface SidebarSubItem {
  label: string;
  href: string;
}

export interface SidebarProps {
  navItems: readonly SidebarNavItem[];
  /** Sub-items rendered beneath their parent nav item's group. */
  subItems?: readonly SidebarSubItem[];
  /** Route of the currently active nav entry; visual state only. */
  activeHref?: string;
  planLabel: string;
  planTimeLabel: string;
}

export default function Sidebar({ navItems, subItems = [], activeHref, planLabel, planTimeLabel }: SidebarProps) {
  return (
    <nav className={styles.sidebar} aria-label="ناوبری اصلی">
      <div className={styles.brand}>
        <span className={styles.brandName}>دایرکتم</span>
        <span className={styles.brandMark}>DM</span>
      </div>

      <ul className={styles.navList}>
        {navItems.map((item) => (
          <li key={item.href}>
            <Link
              href={item.href}
              className={`${styles.navItem} ${activeHref === item.href ? styles.navItemActive : ""}`}
              aria-current={activeHref === item.href ? "page" : undefined}
            >
              {item.label}
            </Link>
            {subItems.length > 0 && activeHref === item.href ? (
              <ul className={styles.subList}>
                {subItems.map((sub) => (
                  <li key={sub.href}>
                    <Link href={sub.href} className={styles.subItem}>
                      {sub.label}
                    </Link>
                  </li>
                ))}
              </ul>
            ) : null}
          </li>
        ))}
      </ul>

      <div className={styles.footerCard}>
        <div className={styles.footerPlan}>{planLabel}</div>
        <div className={styles.footerTime}>{planTimeLabel}</div>
      </div>
    </nav>
  );
}
