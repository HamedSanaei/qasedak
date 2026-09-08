"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import styles from "./InstagramSurface.module.css";

const routes = [
  ["حساب‌ها و اتصال", "/dashboard/settings/instagram"],
  ["آمار و اینسایت", "/dashboard/instagram/insights"],
  ["رسانه‌ها", "/dashboard/instagram/media"],
  ["تاریخچه گفتگو", "/dashboard/instagram/history"],
] as const;

export function InstagramSurfaceNav({ accountId }: { accountId?: string | null }) {
  const pathname = usePathname();
  return (
    <nav className={styles.tabs} aria-label="بخش‌های اینستاگرام">
      {routes.map(([label, href]) => {
        const active = pathname === href;
        const target = accountId && href !== "/dashboard/settings/instagram" ? `${href}?accountId=${encodeURIComponent(accountId)}` : href;
        return <Link key={href} href={target} className={`${styles.tab} ${active ? styles.tabActive : ""}`} aria-current={active ? "page" : undefined}>{label}</Link>;
      })}
    </nav>
  );
}
