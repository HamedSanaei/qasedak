"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { Icon } from "./Icons";
import styles from "./UserMenu.module.css";
import { clearSession } from "@/shared/api/identity";

export function UserMenu({ email }: { email: string }) {
  const [open, setOpen] = useState(false);
  const [loggingOut, setLoggingOut] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const router = useRouter();

  useEffect(() => {
    const close = (event: PointerEvent) => { if (!rootRef.current?.contains(event.target as Node)) setOpen(false); };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") setOpen(false); };
    document.addEventListener("pointerdown", close);
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", close); document.removeEventListener("keydown", escape); };
  }, []);

  async function logout() {
    setLoggingOut(true);
    await fetch("/web-api/auth/logout", { method: "POST" }).catch(() => undefined);
    clearSession();
    router.replace("/login");
    router.refresh();
  }

  return (
    <div className={styles.root} ref={rootRef}>
      <button className={styles.trigger} type="button" onClick={() => setOpen((value) => !value)} aria-haspopup="menu" aria-expanded={open}>
        <span className={styles.avatar} aria-hidden="true">ق</span>
        <span className={styles.email}>{email}</span>
      </button>
      {open ? (
        <div className={styles.menu} role="menu">
          <div className={styles.identity}><strong>حساب قاصدک</strong><span>{email}</span></div>
          <Link className={styles.item} href="/dashboard/accounts" role="menuitem" onClick={() => setOpen(false)}><Icon name="accounts" size={18} />حساب من</Link>
          <Link className={styles.item} href="/dashboard/settings/instagram" role="menuitem" onClick={() => setOpen(false)}><Icon name="instagram" size={18} />اتصال اینستاگرام</Link>
          <Link className={styles.item} href="/dashboard/billing" role="menuitem" onClick={() => setOpen(false)}><Icon name="billing" size={18} />اشتراک من</Link>
          <button className={`${styles.item} ${styles.danger}`} type="button" role="menuitem" onClick={logout} disabled={loggingOut}>{loggingOut ? "در حال خروج…" : "خروج امن"}</button>
        </div>
      ) : null}
    </div>
  );
}
