"use client";

import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import Sidebar, { type SidebarNavItem } from "./Sidebar";
import { Icon } from "./Icons";
import { UserMenu } from "./UserMenu";
import styles from "./DashboardShell.module.css";

const navItems: readonly SidebarNavItem[] = [
  { label: "داشبورد", href: "/dashboard", icon: "dashboard" },
  { label: "صندوق گفتگو", href: "/dashboard/inbox", icon: "inbox" },
  { label: "امکانات", icon: "features", children: [{ label: "پاسخ‌های خودکار", href: "/dashboard/automations" }] },
  { label: "اتصال اینستاگرام", href: "/dashboard/settings/instagram", icon: "instagram" },
  { label: "اشتراک", href: "/dashboard/billing", icon: "billing" },
  { label: "حساب من", href: "/dashboard/accounts", icon: "accounts" },
  { label: "راهنما و پشتیبانی", href: "/dashboard/help", icon: "help" },
];

export function DashboardShell({ children, email, workspaceLabel, workspaceMeta }: { children: ReactNode; email: string; workspaceLabel: string; workspaceMeta: string }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") setMenuOpen(false); };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);
  return (
    <div className={styles.shell}>
      <aside className={`${styles.desktopSidebar} ${sidebarCollapsed ? styles.desktopSidebarCollapsed : ""}`}><Sidebar navItems={navItems} workspaceLabel={workspaceLabel} workspaceMeta={workspaceMeta} collapsed={sidebarCollapsed} onToggle={() => setSidebarCollapsed((value) => !value)} /></aside>
      <div className={styles.column}>
        <header className={styles.topbar}>
          <div className={styles.mobileTools}>
            <button className={styles.menuButton} type="button" onClick={() => setMenuOpen(true)} aria-label="باز کردن منو" aria-expanded={menuOpen}><Icon name="menu" size={24} /></button>
            <div className={styles.mobileBrand}><span className={styles.mobileMark}>ق</span><span>قاصدک</span></div>
          </div>
          <span className={styles.workspace}>{workspaceLabel}</span>
          <UserMenu email={email} />
        </header>
        <main className={styles.content}>{children}</main>
      </div>
      {menuOpen ? <div className={styles.mobileLayer}><button className={styles.overlay} type="button" onClick={() => setMenuOpen(false)} aria-label="بستن منو" /><aside className={styles.drawer} role="dialog" aria-modal="true" aria-label="منوی اصلی"><Sidebar mobile navItems={navItems} workspaceLabel={workspaceLabel} workspaceMeta={workspaceMeta} onNavigate={() => setMenuOpen(false)} onClose={() => setMenuOpen(false)} /></aside></div> : null}
    </div>
  );
}
