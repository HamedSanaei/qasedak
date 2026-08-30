"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useMemo, useState } from "react";
import { routeIsActive } from "@/shared/navigation/route-policy.mjs";
import { Icon } from "./Icons";
import styles from "./Sidebar.module.css";

export interface SidebarNavItem {
  label: string;
  href?: string;
  icon: "dashboard" | "features" | "inbox" | "instagram" | "billing" | "accounts" | "help";
  children?: readonly { label: string; href: string }[];
}

export interface SidebarProps {
  navItems: readonly SidebarNavItem[];
  workspaceLabel: string;
  workspaceMeta: string;
  onNavigate?: () => void;
  onClose?: () => void;
  onToggle?: () => void;
  collapsed?: boolean;
  mobile?: boolean;
}

export default function Sidebar({ navItems, workspaceLabel, workspaceMeta, onNavigate, onClose, onToggle, collapsed = false, mobile = false }: SidebarProps) {
  const pathname = usePathname();
  const groupInitiallyOpen = useMemo(() => navItems.some((item) => item.children?.some((child) => routeIsActive(pathname, child.href))), [navItems, pathname]);
  const [expanded, setExpanded] = useState(groupInitiallyOpen);

  return (
    <nav className={`${styles.sidebar} ${collapsed ? styles.sidebarCollapsed : ""}`} aria-label="ناوبری اصلی">
      <div className={styles.brandRow}>
        <Link href="/dashboard" className={styles.brand} onClick={onNavigate} aria-label="قاصدک؛ رفتن به داشبورد">
          <span className={styles.brandName}>قاصدک</span>
          <span className={styles.brandMark}>ق</span>
        </Link>
        {mobile ? <button type="button" className={styles.closeButton} onClick={onClose} aria-label="بستن منو"><Icon name="close" size={22} /></button> : null}
        {!mobile ? <button type="button" className={styles.toggleButton} onClick={onToggle} aria-label={collapsed ? "باز کردن منو" : "جمع کردن منو"} aria-expanded={!collapsed}><Icon name="toggle" size={22} /></button> : null}
      </div>

      <ul className={styles.navList}>
        {navItems.map((item) => {
          const groupActive = item.children?.some((child) => routeIsActive(pathname, child.href)) ?? false;
          if (item.children) {
            return (
              <li key={item.label}>
                <button type="button" title={collapsed ? item.label : undefined} className={`${styles.navItem} ${groupActive ? styles.navItemActive : ""}`} onClick={() => collapsed ? onToggle?.() : setExpanded((value) => !value)} aria-expanded={collapsed ? false : expanded}>
                  <Icon name={item.icon} size={20} />
                  <span>{item.label}</span>
                  <Icon name="caret" size={18} className={`${styles.caret} ${expanded ? styles.caretOpen : ""}`} />
                </button>
                {expanded && !collapsed ? (
                  <ul className={styles.subList}>
                    {item.children.map((child) => {
                      const active = routeIsActive(pathname, child.href);
                      return <li key={child.href}><Link href={child.href} onClick={onNavigate} className={`${styles.subItem} ${active ? styles.subItemActive : ""}`} aria-current={active ? "page" : undefined}><span className={styles.subDot} aria-hidden="true" />{child.label}</Link></li>;
                    })}
                  </ul>
                ) : null}
              </li>
            );
          }
          const active = item.href ? routeIsActive(pathname, item.href) : false;
          return (
            <li key={item.href ?? item.label}>
              <Link href={item.href ?? "/dashboard"} title={collapsed ? item.label : undefined} onClick={onNavigate} className={`${styles.navItem} ${active ? styles.navItemActive : ""}`} aria-current={active ? "page" : undefined}>
                <Icon name={item.icon} size={20} />
                <span>{item.label}</span>
              </Link>
            </li>
          );
        })}
      </ul>

      <div className={styles.footerCard}>
        <div className={styles.footerPlan}>{workspaceLabel}</div>
        <div className={styles.footerTime}>{workspaceMeta}</div>
      </div>
    </nav>
  );
}
