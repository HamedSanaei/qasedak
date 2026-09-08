"use client";

import type { KeyboardEvent as ReactKeyboardEvent, ReactNode } from "react";
import { useEffect, useRef, useState } from "react";
import { dashboardNavigation } from "@/shared/navigation/dashboard-navigation.mjs";
import Sidebar from "./Sidebar";
import { Icon } from "./Icons";
import { UserMenu } from "./UserMenu";
import styles from "./DashboardShell.module.css";

export function DashboardShell({ children, email, workspaceLabel, workspaceMeta }: { children: ReactNode; email: string; workspaceLabel: string; workspaceMeta: string }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const menuButtonRef = useRef<HTMLButtonElement>(null);
  const drawerRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") setMenuOpen(false); };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);

  useEffect(() => {
    if (!menuOpen) return;
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : menuButtonRef.current;
    const frame = window.requestAnimationFrame(() => drawerRef.current?.focus());
    return () => {
      window.cancelAnimationFrame(frame);
      opener?.focus();
    };
  }, [menuOpen]);

  function trapDrawerFocus(event: ReactKeyboardEvent<HTMLElement>) {
    if (event.key !== "Tab") return;
    const drawer = drawerRef.current;
    if (!drawer) return;
    const focusable = Array.from(drawer.querySelectorAll<HTMLElement>(
      'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])',
    )).filter((element) => element.getClientRects().length > 0);
    if (focusable.length === 0) {
      event.preventDefault();
      drawer.focus();
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && (document.activeElement === first || document.activeElement === drawer)) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  return (
    <div className={styles.shell}>
      <aside className={`${styles.desktopSidebar} ${sidebarCollapsed ? styles.desktopSidebarCollapsed : ""}`}>
        <Sidebar navItems={dashboardNavigation} workspaceLabel={workspaceLabel} workspaceMeta={workspaceMeta} collapsed={sidebarCollapsed} onToggle={() => setSidebarCollapsed((value) => !value)} />
      </aside>
      <div className={styles.column}>
        <header className={styles.topbar}>
          <div className={styles.mobileTools}>
            <button ref={menuButtonRef} className={styles.menuButton} type="button" onClick={() => setMenuOpen(true)} aria-label="باز کردن منو" aria-expanded={menuOpen}>
              <Icon name="menu" size={24} />
            </button>
            <div className={styles.mobileBrand}><span className={styles.mobileMark}>ق</span><span>قاصدک</span></div>
          </div>
          <span className={styles.workspace}>{workspaceLabel}</span>
          <UserMenu email={email} />
        </header>
        <main className={styles.content}>{children}</main>
      </div>
      {menuOpen ? (
        <div className={styles.mobileLayer}>
          <button className={styles.overlay} type="button" onClick={() => setMenuOpen(false)} aria-label="بستن منو" />
          <aside ref={drawerRef} className={styles.drawer} role="dialog" aria-modal="true" aria-label="منوی اصلی" tabIndex={-1} onKeyDown={trapDrawerFocus}>
            <Sidebar mobile navItems={dashboardNavigation} workspaceLabel={workspaceLabel} workspaceMeta={workspaceMeta} onNavigate={() => setMenuOpen(false)} onClose={() => setMenuOpen(false)} />
          </aside>
        </div>
      ) : null}
    </div>
  );
}
